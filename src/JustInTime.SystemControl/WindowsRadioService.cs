using Windows.Devices.Radios;

namespace JustInTime.SystemControl;

/// <summary>
/// Переключает Bluetooth и Wi-Fi через Windows.Devices.Radios (официальный API Windows 10 и 11).
/// Порядок из документации Microsoft: найти адаптеры, запросить доступ, изменить состояние,
/// затем перечитать состояние, потому что запрос только ставится в очередь, а аппаратный переключатель
/// или политика могут его отменить.
/// </summary>
public sealed class WindowsRadioService : IRadioService
{
    private static readonly TimeSpan ConfirmTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    public async Task<RadioSwitchResult> SetAsync(
        SystemRadio radio, bool enable, CancellationToken cancellationToken)
    {
        var name = radio == SystemRadio.Bluetooth ? "Bluetooth" : "Wi-Fi";
        var target = enable ? RadioState.On : RadioState.Off;

        try
        {
            var adapters = await FindAsync(radio);

            if (adapters.Count == 0)
            {
                return RadioSwitchResult.Error(
                    RadioSwitchStatus.NoAdapter, $"В компьютере не найден адаптер {name}.");
            }

            if (adapters.All(a => IsSatisfied(a.State, target)))
            {
                return RadioSwitchResult.Done($"{name} уже в нужном состоянии.");
            }

            if (enable && adapters.Any(a => a.State == RadioState.Disabled))
            {
                return RadioSwitchResult.Error(
                    RadioSwitchStatus.Blocked,
                    $"Адаптер {name} отключён системой (аппаратный переключатель, авиарежим или политика), включить его программно нельзя.");
            }

            cancellationToken.ThrowIfCancellationRequested();

            var access = await Radio.RequestAccessAsync();

            if (access != RadioAccessStatus.Allowed)
            {
                return RadioSwitchResult.Error(
                    RadioSwitchStatus.AccessDenied,
                    $"Windows не разрешила приложению менять {name} ({access}). " +
                    "Проверьте Параметры Windows, Конфиденциальность и безопасность, Радиомодули, и политики организации.");
            }

            foreach (var adapter in adapters.Where(a => !IsSatisfied(a.State, target)))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var status = await adapter.SetStateAsync(target);

                if (status != RadioAccessStatus.Allowed)
                {
                    return RadioSwitchResult.Error(
                        RadioSwitchStatus.AccessDenied,
                        $"Windows отклонила изменение {name} ({status}).");
                }
            }

            return await WaitForStateAsync(radio, target, name, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return RadioSwitchResult.Error(RadioSwitchStatus.Failed, $"Не удалось переключить {name}: {ex.Message}");
        }
    }

    private static async Task<RadioSwitchResult> WaitForStateAsync(
        SystemRadio radio, RadioState target, string name, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + ConfirmTimeout;

        while (true)
        {
            var current = await FindAsync(radio);

            if (current.Count > 0 && current.All(a => IsSatisfied(a.State, target)))
            {
                return RadioSwitchResult.Done();
            }

            if (DateTime.UtcNow >= deadline)
            {
                return RadioSwitchResult.Error(
                    RadioSwitchStatus.NotConfirmed,
                    $"Запрос на изменение {name} принят, но состояние не изменилось за {ConfirmTimeout.TotalSeconds:0} с. " +
                    "Возможно, переключение блокирует аппаратный переключатель или политика.");
            }

            await Task.Delay(PollInterval, cancellationToken);
        }
    }

    private static async Task<IReadOnlyList<Radio>> FindAsync(SystemRadio radio)
    {
        var kind = radio == SystemRadio.Bluetooth ? RadioKind.Bluetooth : RadioKind.WiFi;
        var all = await Radio.GetRadiosAsync();
        return all.Where(r => r.Kind == kind).ToList();
    }

    /// <summary>Состояние Disabled выставляет сама система. Для задачи «выключить» оно уже означает, что адаптер выключен.</summary>
    private static bool IsSatisfied(RadioState state, RadioState target) =>
        state == target || (target == RadioState.Off && state == RadioState.Disabled);
}

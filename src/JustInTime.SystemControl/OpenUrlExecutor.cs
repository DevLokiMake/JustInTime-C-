using System.ComponentModel;
using System.Diagnostics;
using JustInTime.Core.Models;
using JustInTime.Core.Services;

namespace JustInTime.SystemControl;

/// <summary>Открывает ссылку в браузере по умолчанию через оболочку Windows.</summary>
public sealed class OpenUrlExecutor : ActionExecutor<OpenUrlPayload>
{
    public override ActionType ActionType => ActionType.OpenUrl;

    protected override Task<ExecutionOutcome> ExecuteAsync(OpenUrlPayload payload, CancellationToken cancellationToken)
    {
        // Повторная проверка схемы: данные из БД нельзя считать безопасными только потому, что их проверили при создании.
        if (!Uri.TryCreate(payload.Url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return Task.FromResult(ExecutionOutcome.Failed("Разрешены только ссылки http и https."));
        }

        try
        {
            // Оболочка передаёт ссылку браузеру по умолчанию. Процесс не ждём: браузер может жить сколько угодно.
            using var process = Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });

            // Для ссылок Windows может вернуть null, если запрос передан уже запущенному браузеру. Это не ошибка.
            return Task.FromResult(ExecutionOutcome.Success());
        }
        catch (Win32Exception ex)
        {
            return Task.FromResult(ExecutionOutcome.Failed(
                $"Windows не смогла открыть ссылку (возможно, не настроен браузер по умолчанию): {ex.Message}"));
        }
    }
}

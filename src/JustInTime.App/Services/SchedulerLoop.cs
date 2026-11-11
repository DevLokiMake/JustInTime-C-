using JustInTime.Core.Models;
using JustInTime.Core.Services;
using Microsoft.Extensions.Logging;

namespace JustInTime.App.Services;

/// <summary>
/// Фоновый цикл внутри открытого приложения: раз в 30 секунд выполняет наступившие задачи.
/// Это запасной механизм. Главный это Планировщик заданий Windows, который запускает приложение
/// с ключом --run-due, даже когда окно закрыто. Оба безопасно работают одновременно:
/// каждую задачу атомарно берёт только один процесс.
/// </summary>
public sealed class SchedulerLoop(SchedulerEngine engine, RunNotifier notifier, ILogger<SchedulerLoop> logger)
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(15);

    private CancellationTokenSource? _cts;
    private Task? _loop;

    public void Start()
    {
        if (_loop is not null)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => RunAsync(_cts.Token));
    }

    public async Task StopAsync()
    {
        if (_cts is null || _loop is null)
        {
            return;
        }

        await _cts.CancelAsync().ConfigureAwait(false);

        try
        {
            await _loop.WaitAsync(StopTimeout).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            logger.LogWarning("Цикл планировщика не остановился за {Seconds} с", StopTimeout.TotalSeconds);
        }
        catch (OperationCanceledException)
        {
            // Остановка по запросу, это штатно.
        }

        _cts.Dispose();
        _cts = null;
        _loop = null;
    }

    /// <summary>Пишет в журнал, что сделал прогон. Общий для цикла и для режима --run-due.</summary>
    public static void LogReport(ILogger logger, RunReport report)
    {
        if (report.AutomationDisabled)
        {
            logger.LogInformation("Автоматизация выключена в настройках: задачи не выполняются");
            return;
        }

        if (report.RecoveredStuck > 0)
        {
            logger.LogWarning(
                "Найдено зависших задач: {Count}. Они переведены в «Требуется действие», результат неизвестен",
                report.RecoveredStuck);
        }

        foreach (var entry in report.Entries)
        {
            logger.LogInformation(
                "Задача {TaskId} «{Title}»: {Result}, статус {Status}. {Message}",
                entry.TaskId, entry.Title, entry.Result, entry.FinalStatus, entry.Message);
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Приводим системный планировщик в соответствие с БД: после перезагрузки, обновления или ручной
            // правки запуски могли пропасть или остаться лишними.
            var reconcile = await engine.ReconcileAsync(removeStale: true, cancellationToken);

            logger.LogInformation(
                "Сверка с Планировщиком Windows: зарегистрировано {Registered}, удалено лишних {Removed}",
                reconcile.RegisteredIds.Count, reconcile.Removed);

            foreach (var error in reconcile.Errors)
            {
                logger.LogWarning("Планировщик Windows: {Error}", error);
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Не удалось сверить задачи с Планировщиком Windows");
        }

        using var timer = new PeriodicTimer(Interval);

        try
        {
            do
            {
                await RunOnceSafelyAsync(cancellationToken);
            }
            while (await timer.WaitForNextTickAsync(cancellationToken));
        }
        catch (OperationCanceledException)
        {
            // Приложение закрывается.
        }
    }

    private async Task RunOnceSafelyAsync(CancellationToken cancellationToken)
    {
        try
        {
            var report = await engine.RunDueAsync(cancellationToken);
            LogReport(logger, report);
            await notifier.NotifyAsync(report, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Один сбой (например, PostgreSQL на секунду недоступен) не должен убивать весь цикл.
            logger.LogError(ex, "Сбой прогона планировщика, повтор через {Seconds} с", Interval.TotalSeconds);
        }
    }
}

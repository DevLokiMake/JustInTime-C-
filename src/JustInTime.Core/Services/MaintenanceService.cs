using JustInTime.Core.Interfaces;
using JustInTime.Core.Models;

namespace JustInTime.Core.Services;

public sealed class MaintenanceService(ITaskService tasks, IExecutionHistoryRepository history) : IMaintenanceService
{
    public async Task<int> CountCancellableAsync(CancellationToken cancellationToken = default) =>
        (await tasks.GetListAsync(ScheduledTaskStatuses.Editable, cancellationToken)).Count;

    public async Task<BulkResult> CancelAllAsync(CancellationToken cancellationToken = default)
    {
        var list = await tasks.GetListAsync(ScheduledTaskStatuses.Editable, cancellationToken);
        var done = 0;
        var failed = 0;
        var problems = new List<string>();

        foreach (var task in list)
        {
            var result = await tasks.CancelAsync(task.Id, cancellationToken);

            if (result.Succeeded)
            {
                done++;
                problems.AddRange(result.Warnings);
            }
            else
            {
                // Задача могла за это время начать выполняться или завершиться: это не ошибка всей операции.
                failed++;
                problems.Add($"{task.Title}: {result.ErrorText}");
            }
        }

        return new BulkResult(done, failed, problems);
    }

    public Task<int> CountFinishedHistoryAsync(CancellationToken cancellationToken = default) =>
        history.CountFinishedAsync(cancellationToken);

    public Task<int> ClearHistoryAsync(CancellationToken cancellationToken = default) =>
        history.DeleteFinishedAsync(cancellationToken);
}

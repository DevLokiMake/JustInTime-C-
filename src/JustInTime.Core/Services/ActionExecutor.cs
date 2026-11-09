using JustInTime.Core.Interfaces;
using JustInTime.Core.Models;

namespace JustInTime.Core.Services;

/// <summary>Основа исполнителя с типизированными параметрами: приведение типа сделано один раз здесь.</summary>
public abstract class ActionExecutor<TPayload> : IActionExecutor where TPayload : TaskPayload
{
    public abstract ActionType ActionType { get; }

    public Task<ExecutionOutcome> ExecuteAsync(TaskPayload payload, CancellationToken cancellationToken) =>
        payload is TPayload typed
            ? ExecuteAsync(typed, cancellationToken)
            : Task.FromResult(ExecutionOutcome.Failed("Параметры задачи не подходят к этому действию."));

    protected abstract Task<ExecutionOutcome> ExecuteAsync(TPayload payload, CancellationToken cancellationToken);
}

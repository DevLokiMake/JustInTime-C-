using JustInTime.Core.Interfaces;
using JustInTime.Core.Models;
using JustInTime.Core.Localization;

namespace JustInTime.Core.Services;

/// <summary>Основа исполнителя с типизированными параметрами: приведение типа сделано один раз здесь.</summary>
public abstract class ActionExecutor<TPayload> : IActionExecutor where TPayload : TaskPayload
{
    public abstract ActionType ActionType { get; }

    /// <summary>Исполнители, управляющие окнами программ, переопределяют это значение.</summary>
    public virtual bool RequiresInteractiveDesktop => false;

    public virtual TimeSpan? Timeout => null;

    public Task<ExecutionOutcome> ExecuteAsync(TaskPayload payload, CancellationToken cancellationToken) =>
        payload is TPayload typed
            ? ExecuteAsync(typed, cancellationToken)
            : Task.FromResult(ExecutionOutcome.Failed(Loc.T("exe_payload_mismatch")));

    protected abstract Task<ExecutionOutcome> ExecuteAsync(TPayload payload, CancellationToken cancellationToken);
}

using JustInTime.Core.Models;

namespace JustInTime.Core.Interfaces;

/// <summary>Исполнитель одного типа действия. На каждый ActionType не больше одного исполнителя.</summary>
public interface IActionExecutor
{
    ActionType ActionType { get; }

    /// <summary>
    /// true, если действие работает через интерфейс программ (UI Automation) и требует разблокированного рабочего стола.
    /// Такие задачи движок не запускает на экране блокировки, а переводит в WaitingForUnlock.
    /// </summary>
    bool RequiresInteractiveDesktop => false;

    /// <summary>
    /// Выполняет действие и честно сообщает итог. Неожиданное исключение движок считает неизвестным результатом,
    /// поэтому ожидаемые сбои исполнитель должен возвращать как ExecutionOutcome.Failed.
    /// </summary>
    Task<ExecutionOutcome> ExecuteAsync(TaskPayload payload, CancellationToken cancellationToken);
}

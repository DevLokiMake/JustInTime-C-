using JustInTime.Core.Interfaces;
using JustInTime.Core.Models;

namespace JustInTime.Tests;

/// <summary>Исполнитель для тестов движка: возвращает заданный итог, бросает исключение или «зависает».</summary>
internal sealed class FakeExecutor(ActionType type) : IActionExecutor
{
    public ActionType ActionType => type;

    public int Calls { get; private set; }

    public Func<TaskPayload, ExecutionOutcome> Behavior { get; set; } = _ => ExecutionOutcome.Success();

    public bool Hang { get; set; }

    public async Task<ExecutionOutcome> ExecuteAsync(TaskPayload payload, CancellationToken cancellationToken)
    {
        Calls++;

        if (Hang)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        return Behavior(payload);
    }
}

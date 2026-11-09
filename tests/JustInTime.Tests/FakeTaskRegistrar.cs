using JustInTime.Core.Interfaces;
using JustInTime.Core.Models;

namespace JustInTime.Tests;

/// <summary>Системный планировщик для тестов: запоминает вызовы и умеет изображать сбой.</summary>
internal sealed class FakeTaskRegistrar : ITaskRegistrar
{
    public HashSet<Guid> Registered { get; } = new();

    public ScheduledTask? LastRegistered { get; private set; }

    public bool FailRegister { get; set; }

    public bool FailUnregister { get; set; }

    public int ReconcileCalls { get; private set; }

    public IReadOnlyList<Guid> LastToRegister { get; private set; } = Array.Empty<Guid>();

    public IReadOnlyList<Guid> LastKeep { get; private set; } = Array.Empty<Guid>();

    public bool LastRemoveStale { get; private set; }

    public Task<RegistrationResult> RegisterAsync(ScheduledTask task, CancellationToken cancellationToken = default)
    {
        if (FailRegister)
        {
            return Task.FromResult(RegistrationResult.Fail("Отказано в доступе."));
        }

        Registered.Add(task.Id);
        LastRegistered = task;
        return Task.FromResult(RegistrationResult.Ok());
    }

    public Task<RegistrationResult> UnregisterAsync(Guid taskId, CancellationToken cancellationToken = default)
    {
        if (FailUnregister)
        {
            return Task.FromResult(RegistrationResult.Fail("Отказано в доступе."));
        }

        Registered.Remove(taskId);
        return Task.FromResult(RegistrationResult.Ok());
    }

    public Task<ReconcileResult> ReconcileAsync(
        IReadOnlyCollection<ScheduledTask> toRegister,
        IReadOnlyCollection<Guid> keepIds,
        bool removeStale,
        CancellationToken cancellationToken = default)
    {
        ReconcileCalls++;
        LastToRegister = toRegister.Select(t => t.Id).ToList();
        LastKeep = keepIds.ToList();
        LastRemoveStale = removeStale;
        return Task.FromResult(new ReconcileResult(LastToRegister, 0, Array.Empty<string>()));
    }
}

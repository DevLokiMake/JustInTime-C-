namespace JustInTime.Core.Models;

/// <summary>Итог регистрации или снятия одного системного запуска.</summary>
public sealed record RegistrationResult(bool Succeeded, string? Error)
{
    public static RegistrationResult Ok() => new(true, null);

    public static RegistrationResult Fail(string error) => new(false, error);
}

/// <summary>Итог сверки задач в БД с запусками в системном планировщике.</summary>
public sealed record ReconcileResult(
    IReadOnlyList<Guid> RegisteredIds,
    int Removed,
    IReadOnlyList<string> Errors)
{
    public static ReconcileResult Empty { get; } =
        new(Array.Empty<Guid>(), 0, Array.Empty<string>());
}

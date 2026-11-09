namespace JustInTime.Core.Services;

/// <summary>
/// Итог операции сервиса. Ошибки это понятные пользователю сообщения.
/// Предупреждения не отменяют успех: операция выполнена, но есть то, о чём нужно сказать пользователю.
/// </summary>
public sealed class ServiceResult
{
    internal const string UnknownError = "Неизвестная ошибка.";

    private ServiceResult(IReadOnlyList<string> errors, IReadOnlyList<string> warnings)
    {
        Errors = errors;
        Warnings = warnings;
    }

    public IReadOnlyList<string> Errors { get; }

    public IReadOnlyList<string> Warnings { get; }

    public bool Succeeded => Errors.Count == 0;

    public string ErrorText => string.Join(Environment.NewLine, Errors);

    public string WarningText => string.Join(Environment.NewLine, Warnings);

    public static ServiceResult Ok() => new(Array.Empty<string>(), Array.Empty<string>());

    public static ServiceResult OkWithWarnings(IEnumerable<string> warnings) =>
        new(Array.Empty<string>(), warnings.ToList());

    public static ServiceResult Fail(params string[] errors) =>
        new(errors.Length > 0 ? errors : new[] { UnknownError }, Array.Empty<string>());

    public static ServiceResult Fail(IEnumerable<string> errors)
    {
        var list = errors.ToList();
        return new(list.Count > 0 ? list : new List<string> { UnknownError }, Array.Empty<string>());
    }
}

public sealed class ServiceResult<T>
{
    private ServiceResult(T? value, IReadOnlyList<string> errors, IReadOnlyList<string> warnings)
    {
        Value = value;
        Errors = errors;
        Warnings = warnings;
    }

    public T? Value { get; }

    public IReadOnlyList<string> Errors { get; }

    public IReadOnlyList<string> Warnings { get; }

    public bool Succeeded => Errors.Count == 0;

    public string ErrorText => string.Join(Environment.NewLine, Errors);

    public string WarningText => string.Join(Environment.NewLine, Warnings);

    public static ServiceResult<T> Ok(T value) => new(value, Array.Empty<string>(), Array.Empty<string>());

    public static ServiceResult<T> OkWithWarnings(T value, IEnumerable<string> warnings) =>
        new(value, Array.Empty<string>(), warnings.ToList());

    public static ServiceResult<T> Fail(params string[] errors) =>
        new(default, errors.Length > 0 ? errors : new[] { ServiceResult.UnknownError }, Array.Empty<string>());

    public static ServiceResult<T> Fail(IEnumerable<string> errors)
    {
        var list = errors.ToList();
        return new(default, list.Count > 0 ? list : new List<string> { ServiceResult.UnknownError }, Array.Empty<string>());
    }

    public ServiceResult ToResult() =>
        Succeeded ? ServiceResult.OkWithWarnings(Warnings) : ServiceResult.Fail(Errors);
}

namespace JustInTime.Core.Services;

/// <summary>Итог операции сервиса. Ошибки это понятные пользователю сообщения.</summary>
public sealed class ServiceResult
{
    internal const string UnknownError = "Неизвестная ошибка.";

    private ServiceResult(IReadOnlyList<string> errors) => Errors = errors;

    public IReadOnlyList<string> Errors { get; }

    public bool Succeeded => Errors.Count == 0;

    public string ErrorText => string.Join(Environment.NewLine, Errors);

    public static ServiceResult Ok() => new(Array.Empty<string>());

    public static ServiceResult Fail(params string[] errors) =>
        new(errors.Length > 0 ? errors : new[] { UnknownError });

    public static ServiceResult Fail(IEnumerable<string> errors)
    {
        var list = errors.ToList();
        return new(list.Count > 0 ? list : new List<string> { UnknownError });
    }
}

public sealed class ServiceResult<T>
{
    private ServiceResult(T? value, IReadOnlyList<string> errors)
    {
        Value = value;
        Errors = errors;
    }

    public T? Value { get; }

    public IReadOnlyList<string> Errors { get; }

    public bool Succeeded => Errors.Count == 0;

    public string ErrorText => string.Join(Environment.NewLine, Errors);

    public static ServiceResult<T> Ok(T value) => new(value, Array.Empty<string>());

    public static ServiceResult<T> Fail(params string[] errors) =>
        new(default, errors.Length > 0 ? errors : new[] { ServiceResult.UnknownError });

    public static ServiceResult<T> Fail(IEnumerable<string> errors)
    {
        var list = errors.ToList();
        return new(default, list.Count > 0 ? list : new List<string> { ServiceResult.UnknownError });
    }

    public ServiceResult ToResult() => Succeeded ? ServiceResult.Ok() : ServiceResult.Fail(Errors);
}

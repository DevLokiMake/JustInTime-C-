namespace JustInTime.Core.Models;

/// <summary>Что произошло при выполнении действия, по версии самого исполнителя.</summary>
public enum OutcomeKind
{
    /// <summary>Действие выполнено и это подтверждено доступными признаками.</summary>
    Success,

    /// <summary>Действие точно не выполнено. Повтор безопасен.</summary>
    Failed,

    /// <summary>
    /// Неясно, выполнено ли действие. Автоматический повтор запрещён, нужно решение пользователя,
    /// иначе возможен дубль (например, второе сообщение получателю).
    /// </summary>
    Unknown,

    /// <summary>Нужен разблокированный рабочий стол. Задача подождёт и будет запущена снова.</summary>
    WaitForUnlock,

    /// <summary>Нужен интернет. Задача подождёт и будет запущена снова.</summary>
    WaitForNetwork
}

public sealed record ExecutionOutcome(OutcomeKind Kind, string? Message)
{
    public static ExecutionOutcome Success() => new(OutcomeKind.Success, null);

    public static ExecutionOutcome Failed(string message) => new(OutcomeKind.Failed, message);

    public static ExecutionOutcome Unknown(string message) => new(OutcomeKind.Unknown, message);

    public static ExecutionOutcome WaitForUnlock(string message) => new(OutcomeKind.WaitForUnlock, message);

    public static ExecutionOutcome WaitForNetwork(string message) => new(OutcomeKind.WaitForNetwork, message);
}

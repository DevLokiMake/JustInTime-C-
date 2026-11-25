using JustInTime.Core.Localization;

namespace JustInTime.Core.Models;

public enum DangerKind
{
    /// <summary>Массовая операция: отмена нескольких задач сразу.</summary>
    CancelMany,

    /// <summary>Необратимая операция: удаление записей истории.</summary>
    ClearHistory,

    /// <summary>Необратимое действие задачи: запуск программы.</summary>
    LaunchProgram,

    /// <summary>Необратимое действие, которое повторяется: отправка сообщения по расписанию.</summary>
    RecurringMessage
}

/// <summary>Почему операция считается опасной. Count нужен массовым операциям (сколько объектов затронуто).</summary>
public sealed record DangerReason(DangerKind Kind, int Count = 0)
{
    public string Text => Kind switch
    {
        DangerKind.CancelMany => Loc.T("danger_cancel_many", Count),
        DangerKind.ClearHistory => Loc.T("danger_clear_history", Count),
        DangerKind.LaunchProgram => Loc.T("danger_launch_program"),
        DangerKind.RecurringMessage => Loc.T("danger_recurring_message"),
        _ => Kind.ToString()
    };
}

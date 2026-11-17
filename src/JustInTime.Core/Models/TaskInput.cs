namespace JustInTime.Core.Models;

/// <summary>
/// Всё, что пользователь вводит при создании или редактировании задачи.
/// Recurrence: если задано, Schedule определяет дату первого запуска, а дальше задача повторяется по правилу.
/// </summary>
public sealed record TaskInput(string Title, TaskPayload Payload, TaskSchedule Schedule, RecurrenceRule? Recurrence = null);

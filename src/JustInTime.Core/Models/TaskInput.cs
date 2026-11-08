namespace JustInTime.Core.Models;

/// <summary>Всё, что пользователь вводит при создании или редактировании задачи.</summary>
public sealed record TaskInput(string Title, TaskPayload Payload, TaskSchedule Schedule);

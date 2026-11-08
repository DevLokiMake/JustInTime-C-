namespace JustInTime.App.ViewModels;

/// <summary>Пункт выбора в интерфейсе: значение и подпись.</summary>
public sealed record Option<T>(T Value, string Title);

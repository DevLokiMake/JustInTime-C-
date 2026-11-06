namespace JustInTime.App.ViewModels;

/// <summary>
/// ViewModel, которой нужна асинхронная загрузка данных при открытии страницы.
/// Реализация обязана сама ловить исключения: результат вызова никто не ожидает.
/// </summary>
public interface IInitializableViewModel
{
    Task InitializeAsync();
}

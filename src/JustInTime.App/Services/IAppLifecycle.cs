namespace JustInTime.App.Services;

/// <summary>Жизненный цикл окна: показ, сворачивание в трей, выход.</summary>
public interface IAppLifecycle
{
    /// <summary>Закрытие окна сворачивает приложение в трей. Если значка в трее нет, значение игнорируется и окно закрывает приложение.</summary>
    bool MinimizeToTray { get; set; }

    void ShowMainWindow();

    /// <summary>Открыть окно сразу на создании задачи.</summary>
    void ShowCreateTask();

    void ExitApplication();
}

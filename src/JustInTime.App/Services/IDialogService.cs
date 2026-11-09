namespace JustInTime.App.Services;

/// <summary>Диалоги подтверждения. Вынесены в интерфейс, чтобы ViewModels не зависели от MessageBox.</summary>
public interface IDialogService
{
    bool Confirm(string title, string message);

    /// <summary>Сообщение с одной кнопкой «OK»: предупреждение, которое нужно прочитать.</summary>
    void Warn(string title, string message);
}

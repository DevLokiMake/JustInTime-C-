namespace JustInTime.App.Services;

/// <summary>Диалоги подтверждения. Вынесены в интерфейс, чтобы ViewModels не зависели от MessageBox.</summary>
public interface IDialogService
{
    bool Confirm(string title, string message);
}

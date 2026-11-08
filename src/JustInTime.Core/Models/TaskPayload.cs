namespace JustInTime.Core.Models;

/// <summary>
/// Параметры действия задачи. Хранятся в Tasks.Payload как JSON.
/// Тип параметров однозначно определяет тип действия, поэтому рассинхрона между ними быть не может.
/// </summary>
public abstract record TaskPayload
{
    // Методы, а не свойства: так они гарантированно не попадают в JSON.
    public abstract ActionType GetActionType();

    /// <summary>Копия с обрезанными пробелами в текстовых полях.</summary>
    public abstract TaskPayload Normalize();
}

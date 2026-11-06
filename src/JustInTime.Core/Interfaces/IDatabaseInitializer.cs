namespace JustInTime.Core.Interfaces;

public interface IDatabaseInitializer
{
    /// <summary>Применяет миграции. Создаёт базу, если её ещё нет.</summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);
}

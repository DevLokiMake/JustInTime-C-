namespace JustInTime.Core.Interfaces;

/// <summary>Итог массовой операции: сколько выполнено и какие были проблемы.</summary>
public sealed record BulkResult(int Done, int Failed, IReadOnlyList<string> Problems);

/// <summary>Массовые операции: отмена всех активных задач и очистка истории.</summary>
public interface IMaintenanceService
{
    /// <summary>Сколько задач сейчас можно отменить (не завершены и не выполняются).</summary>
    Task<int> CountCancellableAsync(CancellationToken cancellationToken = default);

    /// <summary>Отменяет все задачи, которые можно отменить. Выполняющиеся не трогает.</summary>
    Task<BulkResult> CancelAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Сколько завершённых записей истории можно удалить.</summary>
    Task<int> CountFinishedHistoryAsync(CancellationToken cancellationToken = default);

    /// <summary>Удаляет завершённые записи истории. Записи идущих попыток остаются. Возвращает количество удалённых.</summary>
    Task<int> ClearHistoryAsync(CancellationToken cancellationToken = default);
}

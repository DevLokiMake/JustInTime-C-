namespace JustInTime.Automation;

public enum UiSearchStatus
{
    Found,
    NotFound,
    /// <summary>Подходит больше одного элемента. Автоматизация обязана остановиться, а не угадывать.</summary>
    Ambiguous
}

public sealed record UiSearchResult(UiSearchStatus Status, IUiElement? Element, int Count, string Description)
{
    public bool IsFound => Status == UiSearchStatus.Found;
}

/// <summary>Поиск по дереву элементов в ширину с ограничением глубины и числа узлов.</summary>
public static class UiSearch
{
    public const int DefaultMaxDepth = 15;
    public const int DefaultMaxNodes = 5000;

    /// <summary>Потомки корня (сам корень не входит) в порядке обхода в ширину.</summary>
    public static IEnumerable<IUiElement> Descendants(
        IUiElement root, int maxDepth = DefaultMaxDepth, int maxNodes = DefaultMaxNodes)
    {
        ArgumentNullException.ThrowIfNull(root);

        var queue = new Queue<(IUiElement Element, int Depth)>();
        queue.Enqueue((root, 0));
        var visited = 0;

        while (queue.Count > 0)
        {
            var (current, depth) = queue.Dequeue();

            if (depth >= maxDepth)
            {
                continue;
            }

            IReadOnlyList<IUiElement> children;

            try
            {
                children = current.GetChildren();
            }
            catch (UiAutomationException)
            {
                // Элемент исчез во время обхода (интерфейс меняется): пропускаем ветку, остальное ищем дальше.
                continue;
            }

            foreach (var child in children)
            {
                if (++visited > maxNodes)
                {
                    yield break;
                }

                yield return child;
                queue.Enqueue((child, depth + 1));
            }
        }
    }

    public static IReadOnlyList<IUiElement> FindAll(
        IUiElement root, ElementQuery query, int maxDepth = DefaultMaxDepth, int maxNodes = DefaultMaxNodes)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.IsEmpty)
        {
            throw new ArgumentException("Условие поиска пустое: так можно найти что угодно.", nameof(query));
        }

        return Descendants(root, maxDepth, maxNodes).Where(query.Matches).ToList();
    }

    /// <summary>Ищет ровно один элемент. Если подходит несколько, возвращает Ambiguous.</summary>
    public static UiSearchResult FindSingle(
        IUiElement root, ElementQuery query, int maxDepth = DefaultMaxDepth, int maxNodes = DefaultMaxNodes)
    {
        var all = FindAll(root, query, maxDepth, maxNodes);

        return all.Count switch
        {
            0 => new UiSearchResult(UiSearchStatus.NotFound, null, 0, $"Элемент не найден: {query}."),
            1 => new UiSearchResult(UiSearchStatus.Found, all[0], 1, $"Найден элемент: {query}."),
            _ => new UiSearchResult(
                UiSearchStatus.Ambiguous, null, all.Count, $"Найдено элементов: {all.Count}. Условие неоднозначно: {query}.")
        };
    }

    /// <summary>
    /// Ждёт появления единственного элемента. Ambiguous возвращается сразу, NotFound после тайм-аута.
    /// Отмена токена бросает OperationCanceledException.
    /// </summary>
    public static async Task<UiSearchResult> WaitForSingleAsync(
        IUiElement root,
        ElementQuery query,
        TimeSpan timeout,
        TimeSpan pollInterval,
        CancellationToken cancellationToken,
        TimeProvider? time = null)
    {
        time ??= TimeProvider.System;
        var deadline = time.GetUtcNow() + timeout;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result = FindSingle(root, query);

            if (result.Status != UiSearchStatus.NotFound)
            {
                return result;
            }

            if (time.GetUtcNow() >= deadline)
            {
                return new UiSearchResult(
                    UiSearchStatus.NotFound, null, 0,
                    $"Элемент не появился за {(int)timeout.TotalSeconds} с: {query}.");
            }

            await Task.Delay(pollInterval, time, cancellationToken);
        }
    }
}

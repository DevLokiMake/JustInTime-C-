namespace JustInTime.Automation;

/// <summary>
/// Что искать в дереве элементов. Все заданные условия должны совпасть. Координаты экрана намеренно не поддерживаются:
/// элементы ищутся по имени, AutomationId, типу и классу.
/// </summary>
public sealed record ElementQuery(
    string? Name = null,
    string? NameContains = null,
    string? AutomationId = null,
    string? ControlType = null,
    string? ClassName = null)
{
    public bool IsEmpty =>
        Name is null && NameContains is null && AutomationId is null && ControlType is null && ClassName is null;

    public bool Matches(IUiElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        if (IsEmpty)
        {
            return false;
        }

        return Eq(Name, element.Name)
               && (NameContains is null || element.Name.Contains(NameContains, StringComparison.OrdinalIgnoreCase))
               && Eq(AutomationId, element.AutomationId)
               && Eq(ControlType, element.ControlType)
               && Eq(ClassName, element.ClassName);
    }

    public override string ToString()
    {
        var parts = new List<string>();
        if (Name is not null) parts.Add($"Name=\"{Name}\"");
        if (NameContains is not null) parts.Add($"Name содержит \"{NameContains}\"");
        if (AutomationId is not null) parts.Add($"AutomationId=\"{AutomationId}\"");
        if (ControlType is not null) parts.Add($"Тип={ControlType}");
        if (ClassName is not null) parts.Add($"Класс={ClassName}");
        return parts.Count == 0 ? "(пустое условие)" : string.Join(", ", parts);
    }

    private static bool Eq(string? expected, string actual) =>
        expected is null || string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase);
}

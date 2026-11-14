using System.Text;

namespace JustInTime.Automation;

/// <summary>InteractiveOnly: печатать только кнопки, поля ввода и другие элементы управления (без списков чатов и сообщений).</summary>
public sealed record DumpOptions(
    int MaxDepth = 14, int MaxNodes = 3000, int MaxNameLength = 60, bool InteractiveOnly = false);

/// <summary>
/// Текстовый снимок дерева элементов окна. Нужен, чтобы адаптеры мессенджеров писались по реальной структуре окна,
/// а не по догадкам. Значения полей ввода (Value) не читаются, имена обрезаются: в снимок не должны попадать переписки.
/// </summary>
public static class UiTreeDumper
{
    private static readonly HashSet<string> InteractiveTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Button", "Edit", "CheckBox", "RadioButton", "ComboBox", "Hyperlink", "MenuItem", "TabItem", "Slider", "SplitButton"
    };

    public static string Dump(IUiElement root, DumpOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        options ??= new DumpOptions();

        var sb = new StringBuilder();
        var count = 0;
        var truncated = false;

        void Visit(IUiElement element, int depth)
        {
            if (truncated)
            {
                return;
            }

            if (++count > options.MaxNodes)
            {
                truncated = true;
                return;
            }

            if (!options.InteractiveOnly || InteractiveTypes.Contains(element.ControlType))
            {
                sb.Append(' ', depth * 2).AppendLine(Describe(element, options));
            }

            if (depth + 1 >= options.MaxDepth)
            {
                return;
            }

            IReadOnlyList<IUiElement> children;

            try
            {
                children = element.GetChildren();
            }
            catch (UiAutomationException ex)
            {
                sb.Append(' ', (depth + 1) * 2).AppendLine($"(потомки недоступны: {ex.Message})");
                return;
            }

            foreach (var child in children)
            {
                Visit(child, depth + 1);
            }
        }

        Visit(root, 0);

        if (truncated)
        {
            sb.AppendLine($"... снимок обрезан: достигнут предел {options.MaxNodes} элементов.");
        }

        return sb.ToString();
    }

    public static string Describe(IUiElement element, DumpOptions options)
    {
        var sb = new StringBuilder();
        sb.Append('[').Append(element.ControlType).Append(']');
        sb.Append(" name=\"").Append(Shorten(element.Name, options.MaxNameLength)).Append('"');

        if (element.AutomationId.Length > 0)
        {
            sb.Append(" id=\"").Append(Shorten(element.AutomationId, options.MaxNameLength)).Append('"');
        }

        if (element.ClassName.Length > 0)
        {
            sb.Append(" class=\"").Append(element.ClassName).Append('"');
        }

        if (element.Patterns.Count > 0)
        {
            sb.Append(" patterns=").Append(string.Join('+', element.Patterns));
        }

        if (!element.IsEnabled)
        {
            sb.Append(" disabled");
        }

        if (element.IsOffscreen)
        {
            sb.Append(" offscreen");
        }

        return sb.ToString();
    }

    private static string Shorten(string text, int max)
    {
        var flat = text.Replace('\r', ' ').Replace('\n', ' ').Replace('"', '\'');
        return flat.Length <= max ? flat : flat[..max] + "…";
    }
}

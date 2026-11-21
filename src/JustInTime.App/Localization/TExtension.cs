using System.Windows.Data;
using System.Windows.Markup;

namespace JustInTime.App.Localization;

/// <summary>Использование в XAML: Text="{loc:T ключ}". Текст берётся из таблицы выбранного языка и обновляется при его смене.</summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TExtension : MarkupExtension
{
    public TExtension(string key)
    {
        Key = key;
    }

    [ConstructorArgument("key")]
    public string Key { get; set; }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new Binding($"[{Key}]")
        {
            Source = LocSource.Instance,
            Mode = BindingMode.OneWay
        };

        return binding.ProvideValue(serviceProvider);
    }
}

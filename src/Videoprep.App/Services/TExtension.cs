using Avalonia.Data;
using Avalonia.Markup.Xaml;

namespace Videoprep.App.Services;

/// <summary>
/// XAML shortcut for a translated text that follows the language switch: <c>Text="{s:T btn_send}"</c>.
/// Works anywhere (also inside item templates whose data is not the main view model).
/// </summary>
public sealed class TExtension(string key) : MarkupExtension
{
    public string Key { get; } = key;

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new ReflectionBinding($"[{Key}]") { Source = Loc.Instance, Mode = BindingMode.OneWay };
}

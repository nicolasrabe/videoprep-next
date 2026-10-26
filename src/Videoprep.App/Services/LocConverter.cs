using System.Globalization;
using Avalonia.Data.Converters;

namespace Videoprep.App.Services;

/// <summary>Turns an i18n key held in data (e.g. a status) into its text in the current language.</summary>
public sealed class LocConverter : IValueConverter
{
    public static LocConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string key ? Loc.Instance[key] : value;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

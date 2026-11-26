using System.Globalization;
using Avalonia.Data.Converters;

namespace ScrollKeeper.Presentation.Converters;

/// <summary>Negates a boolean binding (both directions).</summary>
public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not true;
}

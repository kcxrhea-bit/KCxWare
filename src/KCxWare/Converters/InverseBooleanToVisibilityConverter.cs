using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace KCxWare.Converters;

/// <summary>Used by the loading overlay to show the determinate percentage only while progress is not indeterminate.</summary>
public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

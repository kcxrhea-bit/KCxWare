using System.Globalization;
using System.Windows.Data;

namespace KCxWare.Converters;

/// <summary>Used to disable action buttons while MainViewModel.IsBusy is true (double-submission guard).</summary>
public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not true;
}

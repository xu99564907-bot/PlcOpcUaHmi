using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PlcOpcUaHmi.Converters;

public sealed class NavigationGroupVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var currentGroup = values.Length > 0 ? values[0] as string : string.Empty;
        var title = values.Length > 1 ? values[1] as string : string.Empty;
        return string.Equals(currentGroup, title, StringComparison.Ordinal) ? Visibility.Visible : Visibility.Collapsed;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => Array.Empty<object>();
}

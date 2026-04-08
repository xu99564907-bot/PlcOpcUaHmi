using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PlcOpcUaHmi.Converters;

public sealed class CurrentSectionToNavigationWidthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var currentSection = value as string;
        return string.Equals(currentSection, "主界面", StringComparison.Ordinal)
            ? new GridLength(0)
            : new GridLength(260);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => DependencyProperty.UnsetValue;
}

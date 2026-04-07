using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace PlcOpcUaHmi.Converters;

public class StringToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        try
        {
            var color = value?.ToString();
            if (string.IsNullOrWhiteSpace(color))
            {
                return Brushes.LightGray;
            }

            return (SolidColorBrush)new BrushConverter().ConvertFromString(color)!;
        }
        catch
        {
            return Brushes.LightGray;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value?.ToString() ?? "#D1D5DB";
    }
}

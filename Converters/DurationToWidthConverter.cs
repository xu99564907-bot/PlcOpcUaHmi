using System;
using System.Globalization;
using System.Windows.Data;

namespace PlcOpcUaHmi.Converters;

public class DurationToWidthConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var duration = 0.0;
        if (value is double widthValue)
        {
            duration = widthValue;
        }
        else if (value is string text && double.TryParse(text, out var parsed))
        {
            duration = parsed;
        }

        var scale = 48.0;
        if (parameter is string parameterText && double.TryParse(parameterText, out var parameterScale))
        {
            scale = parameterScale;
        }

        var width = Math.Max(72.0, duration * scale);
        return Math.Min(width, 520.0);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}

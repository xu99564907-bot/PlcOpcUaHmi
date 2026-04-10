using System;
using System.Globalization;
using System.Windows.Data;

namespace PlcOpcUaHmi.Converters
{
    public class ViewportToCylinderItemWidthConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (!(value is double viewportWidth) || double.IsNaN(viewportWidth) || viewportWidth <= 0)
                return 292.0;

            // approximate padding/margins around the items area
            const double outerPadding = 40.0;
            var available = Math.Max(220.0, viewportWidth - outerPadding);

            // decide target columns based on a preferred item baseline (approx 320)
            var baseline = 320.0;
            int columns = (int)Math.Floor(available / baseline);
            if (columns < 1) columns = 1;

            // spacing between items (WrapPanel margin approx 10)
            double spacingTotal = Math.Max(0, (columns - 1) * 10.0);
            double itemWidth = Math.Floor((available - spacingTotal) / columns);

            // clamp to reasonable bounds
            if (itemWidth < 220.0) itemWidth = 220.0;
            if (itemWidth > 420.0) itemWidth = 420.0;

            return itemWidth;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}

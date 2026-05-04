using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Grimly.Converters;

public sealed class ReadabilityScoreToBrushConverter : IValueConverter
{
    // Green gradient: higher score = more green, lower = more red
    // 60+ = bright green (very easy)
    // 40-59 = yellow-green (readable)
    // 30-39 = yellow (borderline)
    // 20-29 = orange (hard)
    // 0-19 = red (very hard)

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not double score) return Brushes.Gray;

        return score switch
        {
            >= 60 => new SolidColorBrush(Color.FromRgb(80, 210, 90)),    // bright green
            >= 50 => new SolidColorBrush(Color.FromRgb(140, 200, 80)),   // yellow-green
            >= 40 => new SolidColorBrush(Color.FromRgb(180, 190, 70)),   // green-yellow
            >= 30 => new SolidColorBrush(Color.FromRgb(220, 180, 50)),   // yellow
            >= 20 => new SolidColorBrush(Color.FromRgb(230, 140, 50)),   // orange
            _ => new SolidColorBrush(Color.FromRgb(220, 80, 80)),        // red
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

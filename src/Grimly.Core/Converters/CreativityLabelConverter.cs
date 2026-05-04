using System.Globalization;
using System.Windows.Data;

namespace Grimly.Converters;

public sealed class CreativityLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double d)
        {
            return d switch
            {
                <= 0.1 => "(very precise)",
                <= 0.3 => "(precise)",
                <= 0.6 => "(balanced)",
                <= 0.8 => "(varied)",
                _ => "(very varied)"
            };
        }
        return "";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

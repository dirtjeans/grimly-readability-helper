using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Grimly.Services;

namespace Grimly.Converters;

public sealed class ConnectionStatusToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Green = new(Color.FromRgb(80, 210, 90));
    private static readonly SolidColorBrush Yellow = new(Color.FromRgb(230, 180, 50));
    private static readonly SolidColorBrush Red = new(Color.FromRgb(220, 80, 80));
    private static readonly SolidColorBrush DarkRed = new(Color.FromRgb(160, 50, 50));
    private static readonly SolidColorBrush Gray = new(Color.FromRgb(130, 130, 130));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is ConnectionStatus status)
        {
            return status switch
            {
                ConnectionStatus.Connected => Green,
                ConnectionStatus.ModelNotLoaded => Yellow,
                ConnectionStatus.ServiceNotRunning => Red,
                ConnectionStatus.NotInstalled => DarkRed,
                ConnectionStatus.Error => Red,
                _ => Gray
            };
        }
        return Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

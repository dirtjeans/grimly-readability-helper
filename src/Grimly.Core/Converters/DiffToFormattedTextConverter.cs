using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using Grimly.Models;

namespace Grimly.Converters;

public sealed class DiffToFormattedTextConverter : IValueConverter
{
    private static readonly SolidColorBrush AddedBackground = new(Color.FromArgb(60, 0, 180, 0));
    private static readonly SolidColorBrush AddedForeground = new(Color.FromRgb(80, 220, 80));
    private static readonly SolidColorBrush RemovedForeground = new(Color.FromRgb(220, 80, 80));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not IEnumerable<TextDiff> diffs)
            return new Span();

        var span = new Span();
        foreach (var diff in diffs)
        {
            var run = new Run(diff.Text);
            switch (diff.Type)
            {
                case DiffType.Added:
                    run.Background = AddedBackground;
                    run.Foreground = AddedForeground;
                    break;
                case DiffType.Removed:
                    run.Foreground = RemovedForeground;
                    run.TextDecorations = TextDecorations.Strikethrough;
                    break;
            }
            span.Inlines.Add(run);
        }
        return span;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

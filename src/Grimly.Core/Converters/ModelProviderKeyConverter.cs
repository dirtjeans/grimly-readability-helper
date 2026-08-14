using System.Globalization;
using System.Windows.Data;
using Grimly.Models;
using Grimly.Services;

namespace Grimly.Converters;

/// <summary>
/// Maps a model id from the picker list to a provider key the XAML icon
/// template switches on: "windows-ai", "ollama", "lmstudio", "geniex", or
/// "foundry" (the unprefixed default). Accepts either a plain id string
/// (Settings quick picker) or a CatalogModelInfo (model browser), so both
/// lists can share the same icon template. Kept as strings so the
/// template's DataTriggers stay readable and adding a provider is a
/// one-line change here plus one trigger there.
/// </summary>
public sealed class ModelProviderKeyConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var id = value switch
        {
            string s => s,
            CatalogModelInfo c => c.Id,
            _ => "",
        };
        if (string.Equals(id, IWindowsAiClient.ModelId, StringComparison.OrdinalIgnoreCase))
            return "windows-ai";
        if (id.StartsWith("ollama:", StringComparison.OrdinalIgnoreCase)) return "ollama";
        if (id.StartsWith("lmstudio:", StringComparison.OrdinalIgnoreCase)) return "lmstudio";
        if (id.StartsWith("geniex:", StringComparison.OrdinalIgnoreCase)) return "geniex";
        return "foundry";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

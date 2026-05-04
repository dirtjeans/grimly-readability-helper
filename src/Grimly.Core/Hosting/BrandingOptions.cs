using System.Windows.Media;

namespace Grimly.Hosting;

/// <summary>
/// Per-app branding the shared <see cref="ApplicationHost"/> needs at startup.
/// </summary>
public sealed class BrandingOptions
{
    /// <summary>App name shown in tray tooltip, balloon notifications, and dialog titles.</summary>
    public required string AppDisplayName { get; init; }

    /// <summary>Subdirectory under %APPDATA% where this app stores its settings.json.</summary>
    public required string SettingsFolderName { get; init; }

    /// <summary>Default modifier string used when no settings file exists yet (e.g. "Ctrl+Alt").</summary>
    public string DefaultHotkeyModifiers { get; init; } = "Ctrl+Alt";

    /// <summary>Default key used when no settings file exists yet (e.g. "G").</summary>
    public string DefaultHotkeyKey { get; init; } = "G";

    /// <summary>Single character drawn as a fallback tray icon if Resources/TrayIcon.ico is missing.</summary>
    public string FallbackIconLetter { get; init; } = "?";

    /// <summary>Background color of the fallback tray icon.</summary>
    public Color FallbackIconBackground { get; init; } = Color.FromRgb(30, 20, 50);

    /// <summary>Foreground (letter) color of the fallback tray icon.</summary>
    public Color FallbackIconForeground { get; init; } = Colors.Gold;
}

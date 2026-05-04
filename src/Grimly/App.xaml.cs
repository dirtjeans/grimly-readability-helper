using System.Windows;
using System.Windows.Media;
using Grimly.Hosting;

namespace Grimly;

public partial class App : Application
{
    private ApplicationHost? _host;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _host = new ApplicationHost(this, new BrandingOptions
        {
            AppDisplayName = "Grimly",
            SettingsFolderName = "Grimly",
            DefaultHotkeyModifiers = "Ctrl+Alt",
            DefaultHotkeyKey = "G",
            FallbackIconLetter = "G",
            FallbackIconBackground = Color.FromRgb(30, 20, 50),
            FallbackIconForeground = Colors.Gold,
        });
        _host.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _host?.Dispose();
        base.OnExit(e);
    }
}

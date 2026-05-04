using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Hardcodet.Wpf.TaskbarNotification;
using Microsoft.Extensions.DependencyInjection;
using Grimly.Models;
using Grimly.Native;
using Grimly.Services;
using Grimly.ViewModels;
using Grimly.Views;

namespace Grimly.Hosting;

/// <summary>
/// Shared application lifecycle for both Grimly and Style Helper. Owns DI,
/// tray icon, global hotkey, selection watcher, popup orchestration, and the
/// Foundry Local startup flow. The thin per-app App.xaml.cs just constructs
/// one of these with branding and calls Start / Dispose.
/// </summary>
public sealed class ApplicationHost : IDisposable
{
    private readonly Application _app;
    private readonly BrandingOptions _branding;

    private ServiceProvider? _serviceProvider;
    private HotKeyHelper? _hotKeyHelper;
    private int _hotkeyId;
    private TaskbarIcon? _trayIcon;
    private TrayIconViewModel? _trayVm;
    private SelectionWatcherService? _selectionWatcher;
    private FloatingIconWindow? _floatingIcon;
    private IntPtr _pendingSelectionWindow;
    private Point? _animateFromPoint;
    private Task<string?>? _prefetchSelectionTask;
    private Mutex? _singleInstanceMutex;

    public ApplicationHost(Application app, BrandingOptions branding)
    {
        _app = app;
        _branding = branding;
    }

    public void Start()
    {
        StartupLog.Initialize(_branding.SettingsFolderName);
        StartupLog.Write($"Start() — app={_branding.AppDisplayName}");

        // Single-instance guard. Each app (Grimly, StyleHelper) gets its own
        // mutex keyed on the settings folder so both can run side-by-side,
        // but a second instance of the SAME app is blocked. User-scoped
        // ("Local\") so multiple Windows users on one machine can each have
        // their own running instance.
        var mutexName = $"Local\\Grimly.{_branding.SettingsFolderName}.SingleInstance";
        _singleInstanceMutex = new Mutex(initiallyOwned: true, mutexName, out bool createdNew);
        if (!createdNew)
        {
            StartupLog.Write("Another instance already running — exiting.");
            MessageBox.Show(
                $"{_branding.AppDisplayName} is already running.\n\n" +
                $"Check your system tray for the {_branding.AppDisplayName} icon. " +
                $"Right-click it to access settings or exit.",
                _branding.AppDisplayName,
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            // Release/dispose the un-owned mutex handle and tear down the app
            // cleanly. Don't go through ServiceProvider disposal since we
            // never built one.
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
            _app.Shutdown();
            return;
        }

        // Expose the app name as a resource so XAML can pick it up via
        // {DynamicResource AppDisplayName}. Used for visible window text.
        _app.Resources["AppDisplayName"] = _branding.AppDisplayName;

        var services = new ServiceCollection();
        services.AddSingleton(_branding);
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IClipboardService, ClipboardService>();
        services.AddSingleton<ITextDiffService, TextDiffService>();
        services.AddSingleton<IFoundryManager, FoundryManager>();
        services.AddSingleton<IReadabilityService, ReadabilityService>();
        // Word-backed readability (Windows only). Self-reports unavailability
        // if Word isn't installed; kept as a singleton because the underlying
        // COM instance is expensive to spin up and benefits from reuse.
        services.AddSingleton<IWordReadabilityService, WordReadabilityService>();
        services.AddSingleton<IFoundryInstallerService, FoundryInstallerService>();
        // Spell checker needs to be a singleton — it loads the en_US Hunspell
        // dictionary on construction (~12 MB / ~100 ms), and we don't want to
        // pay that cost on every invocation.
        services.AddSingleton<ISpellCheckerService, SpellCheckerService>();
        services.AddSingleton<IGrammarChecker, GrammarChecker>();
        services.AddHttpClient<IFoundryLocalClient, FoundryLocalClient>();
        _serviceProvider = services.BuildServiceProvider();

        var settings = _serviceProvider.GetRequiredService<ISettingsService>().Load();

        SetupTrayIcon();
        SetupHotkey(settings);

        try { SetupSelectionWatcher(settings); }
        catch { /* floating icon is optional — don't prevent startup */ }

        InitializeFoundryAsync();
    }

    private async void InitializeFoundryAsync()
    {
        StartupLog.Write("InitializeFoundryAsync — entered");
        var manager = _serviceProvider!.GetRequiredService<IFoundryManager>();

        if (!manager.IsFoundryInstalled())
        {
            StartupLog.Write("IsFoundryInstalled=false → PromptToInstallFoundry");
            PromptToInstallFoundry(manager);
            return;
        }
        StartupLog.Write("IsFoundryInstalled=true");

        // One persistent toast that updates through every startup stage. The
        // user sees something reassuring from the moment the app starts,
        // watches the message evolve ("Getting ready…" → "Starting service…"
        // → "Warming up model…"), and finally sees it settle on "Ready!".
        // Cold NPU starts can take ~30s; this keeps the user oriented.
        var toast = ToastWindow.Show(
            _branding.AppDisplayName,
            $"Getting {_branding.AppDisplayName} ready…",
            autoDismiss: false);

        // Wrap the whole async startup dance in a try/catch so that if any
        // awaited Foundry call hangs or throws, we still dismiss the toast
        // instead of leaving it stuck on "Warming up the local model…"
        // forever — which was the user-reported failure mode.
        try
        {

        var (running, endpoint) = await manager.CheckServiceStatusAsync();
        StartupLog.Write($"CheckServiceStatusAsync — running={running}, endpoint={endpoint ?? "null"}");

        if (running && endpoint != null)
        {
            // Fast path — Foundry service is up. Keep the "Getting ready…"
            // message until the model is actually confirmed responsive.
            var settings = _serviceProvider!.GetRequiredService<ISettingsService>().Load();
            if (settings.FoundryEndpoint != endpoint)
            {
                settings.FoundryEndpoint = endpoint;
                _serviceProvider!.GetRequiredService<ISettingsService>().Save(settings);
            }
            toast?.UpdateMessage("Warming up the local model…");
        }
        else
        {
            toast?.UpdateMessage("Starting Foundry Local service…");
        }

        var (success, finalEndpoint, modelId) = await manager.EnsureRunningAsync();
        StartupLog.Write($"EnsureRunningAsync — success={success}, endpoint={finalEndpoint ?? "null"}, modelId={modelId ?? "null"}");

        if (success)
        {
            // EnsureRunningAsync already verifies (a) the service is running
            // and (b) a model is loaded, which is authoritative per Foundry's
            // own view. A separate health-check inference round-trip used to
            // live here as belt-and-suspenders, but it produced false
            // negatives on cold NPU starts (first inference takes 30s+ to
            // compile kernels) which then triggered a needless service
            // restart — and the "Restarting Foundry…" toast got stuck while
            // the user was already successfully using the app. Per-request
            // timeouts in FoundryLocalClient still catch genuine model
            // failures at the point of use.
            var readySettings = _serviceProvider!.GetRequiredService<ISettingsService>().Load();
            var hotkeyDesc = $"{_branding.DefaultHotkeyModifiers}+{_branding.DefaultHotkeyKey}";
            var readyMessage = readySettings.ShowFloatingIcon
                ? $"{_branding.AppDisplayName} is ready! Select any text to get started, or press {hotkeyDesc}."
                : $"{_branding.AppDisplayName} is ready! Press {hotkeyDesc} on selected text to get started.";
            StartupLog.Write($"Ready branch reached. Updating toast to final message.");
            toast?.UpdateAndDismiss(readyMessage);
        }
        else if (finalEndpoint == null)
        {
            // Service genuinely isn't running — this is the only case that
            // warrants a blocking modal. User needs to take action (start
            // Foundry manually, or let us install it).
            StartupLog.Write($"EnsureRunningAsync returned false AND endpoint=null → setup-required modal");
            toast?.DismissNow();

            var message =
                $"{_branding.AppDisplayName} could not start Foundry Local automatically.\n\n" +
                "Open a terminal and run:\n\n" +
                "    foundry service start\n" +
                "    foundry model run qwen2.5-7b\n\n" +
                $"{_branding.AppDisplayName} will keep running — it will work once Foundry is ready.\n\n" +
                "Copy commands to clipboard?";
            var clipboardText = "foundry service start\r\nfoundry model run qwen2.5-7b";

            var result = MessageBox.Show(message,
                $"{_branding.AppDisplayName} - Setup Required",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                try { Clipboard.SetText(clipboardText); } catch { }
            }
        }
        else
        {
            // Service is up but we couldn't confirm a loaded model during
            // startup. This used to fire a blocking "Setup Required" modal,
            // but in practice the model usually loads on first inference
            // anyway (and the user's actual experience proves the service is
            // working). Just show a soft toast and let them proceed —
            // FoundryLocalClient will surface real failures at point of use.
            StartupLog.Write($"EnsureRunningAsync: service up but no model confirmed — soft warning toast");
            toast?.UpdateAndDismiss(
                $"{_branding.AppDisplayName} is ready. Model loading in background — first revision may take a few seconds.");
        }

        } // end try
        catch (Exception ex)
        {
            StartupLog.Write($"InitializeFoundryAsync threw: {ex.GetType().Name}: {ex.Message}");
            toast?.UpdateAndDismiss($"Startup error — {ex.Message}. Check startup.log for details.");
        }
    }

    private void PromptToInstallFoundry(IFoundryManager manager)
    {
        bool hasWinget = manager.IsWingetInstalled();

        if (!hasWinget)
        {
            var result = MessageBox.Show(
                $"{_branding.AppDisplayName} uses Microsoft Foundry Local to run AI models on your PC.\n\n" +
                "Foundry Local is normally installed via winget (the Windows package manager), " +
                "but winget is not available on this system.\n\n" +
                $"You can install winget (\"App Installer\") from the Microsoft Store, then restart {_branding.AppDisplayName}.\n\n" +
                "Yes = Open the Microsoft Store page for App Installer\n" +
                "No = Skip for now",
                $"{_branding.AppDisplayName} - winget Required",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "ms-windows-store://pdp/?productid=9NBLGGH4NNS1",
                        UseShellExecute = true
                    });
                }
                catch { }
            }
            return;
        }

        var prompt = MessageBox.Show(
            $"{_branding.AppDisplayName} uses Microsoft Foundry Local to run AI models privately on your PC.\n\n" +
            $"{_branding.AppDisplayName} can install it for you now. This will:\n\n" +
            "  1. Download and install Foundry Local (~500 MB)\n" +
            "  2. Start the Foundry service\n" +
            "  3. Download the qwen2.5-7b model (~5 GB)\n\n" +
            "The whole process takes 5-15 minutes depending on your connection.\n\n" +
            "Install now?",
            $"{_branding.AppDisplayName} - Install Foundry Local",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (prompt == MessageBoxResult.Yes)
        {
            ShowInstallProgressWindow();
        }
    }

    private void ShowInstallProgressWindow()
    {
        var installer = _serviceProvider!.GetRequiredService<IFoundryInstallerService>();
        var vm = new InstallProgressViewModel(installer);
        var window = new InstallProgressWindow
        {
            DataContext = vm,
            Title = $"{_branding.AppDisplayName} - Installing Foundry Local"
        };

        vm.InstallCompleted += success =>
        {
            if (success)
            {
                _ = Task.Run(async () =>
                {
                    var manager = _serviceProvider!.GetRequiredService<IFoundryManager>();
                    await manager.EnsureRunningAsync();
                    await _app.Dispatcher.InvokeAsync(() =>
                    {
                        var readySettings = _serviceProvider!.GetRequiredService<ISettingsService>().Load();
                        var hotkeyDesc = $"{_branding.DefaultHotkeyModifiers}+{_branding.DefaultHotkeyKey}";
                        var readyMessage = readySettings.ShowFloatingIcon
                            ? $"{_branding.AppDisplayName} is ready! Select any text to get started, or press {hotkeyDesc}."
                            : $"{_branding.AppDisplayName} is ready! Press {hotkeyDesc} on selected text to get started.";
                        ToastWindow.Show(_branding.AppDisplayName, readyMessage);
                    });
                });
            }
        };

        _ = vm.StartAsync();
        window.Show();
    }

    private void SetupTrayIcon()
    {
        _trayVm = new TrayIconViewModel();
        _trayVm.SettingsRequested += ShowSettings;
        _trayVm.ExitRequested += () => _app.Shutdown();

        _trayIcon = new TaskbarIcon
        {
            ToolTipText = $"{_branding.AppDisplayName} ({_branding.DefaultHotkeyModifiers}+{_branding.DefaultHotkeyKey})",
            Icon = CreateDefaultIcon(),
            ContextMenu = CreateTrayMenu()
        };

        _trayIcon.TrayLeftMouseDown += (_, _) => ShowSettings();
    }

    private System.Drawing.Icon CreateDefaultIcon()
    {
        // Both apps ship a Resources/TrayIcon.ico in their own assembly. The
        // pack URI without an assembly name resolves against the executing
        // (entry) assembly, which is the right thing here.
        try
        {
            var uri = new Uri("pack://application:,,,/Resources/TrayIcon.ico");
            var stream = Application.GetResourceStream(uri)?.Stream;
            if (stream != null)
                return new System.Drawing.Icon(stream);
        }
        catch { /* fall through to drawn fallback */ }

        // Fallback: draw the branding letter on the branding background color.
        using var bmp = new System.Drawing.Bitmap(32, 32);
        using var g = System.Drawing.Graphics.FromImage(bmp);
        g.Clear(System.Drawing.Color.FromArgb(
            _branding.FallbackIconBackground.R,
            _branding.FallbackIconBackground.G,
            _branding.FallbackIconBackground.B));
        using var font = new System.Drawing.Font("Segoe UI", 16, System.Drawing.FontStyle.Bold);
        var size = g.MeasureString(_branding.FallbackIconLetter, font);
        using var brush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(
            _branding.FallbackIconForeground.R,
            _branding.FallbackIconForeground.G,
            _branding.FallbackIconForeground.B));
        g.DrawString(_branding.FallbackIconLetter, font, brush,
            (32 - size.Width) / 2, (32 - size.Height) / 2);
        return System.Drawing.Icon.FromHandle(bmp.GetHicon());
    }

    private System.Windows.Controls.ContextMenu CreateTrayMenu()
    {
        var menu = new System.Windows.Controls.ContextMenu();

        var settingsItem = new System.Windows.Controls.MenuItem { Header = "Settings..." };
        settingsItem.Click += (_, _) => ShowSettings();
        menu.Items.Add(settingsItem);

        menu.Items.Add(new System.Windows.Controls.Separator());

        var exitItem = new System.Windows.Controls.MenuItem { Header = "Exit" };
        exitItem.Click += (_, _) => _app.Shutdown();
        menu.Items.Add(exitItem);

        return menu;
    }

    private void SetupHotkey(AppSettings settings)
    {
        _hotKeyHelper = new HotKeyHelper();

        var modifiers = ModifierKeys.None;
        foreach (var mod in settings.HotkeyModifiers.Split('+', StringSplitOptions.TrimEntries))
        {
            modifiers |= mod.ToLowerInvariant() switch
            {
                "ctrl" or "control" => ModifierKeys.Control,
                "alt" => ModifierKeys.Alt,
                "shift" => ModifierKeys.Shift,
                _ => ModifierKeys.None
            };
        }

        var defaultKey = Enum.TryParse<Key>(_branding.DefaultHotkeyKey, true, out var dk) ? dk : Key.G;
        var key = Enum.TryParse<Key>(settings.HotkeyKey, true, out var k) ? k : defaultKey;

        try
        {
            _hotkeyId = _hotKeyHelper.Register(modifiers, key, OnHotkeyPressed);
        }
        catch (Exception ex)
        {
            ToastWindow.Show(_branding.AppDisplayName, $"Failed to register hotkey: {ex.Message}");
        }
    }

    private void SetupSelectionWatcher(AppSettings settings)
    {
        if (!settings.ShowFloatingIcon) return;

        _selectionWatcher = new SelectionWatcherService();
        _selectionWatcher.DragSelectionDetected += OnDragSelectionDetected;
        _selectionWatcher.SelectionCleared += () =>
        {
            _floatingIcon?.DismissNow();
            _prefetchSelectionTask = null;
        };
        _selectionWatcher.Start();
    }

    private void OnDragSelectionDetected(Point position, IntPtr sourceWindow)
    {
        try
        {
            _pendingSelectionWindow = sourceWindow;

            _floatingIcon ??= new FloatingIconWindow();
            _floatingIcon.IconClicked -= OnFloatingIconClicked;
            _floatingIcon.IconClicked += OnFloatingIconClicked;
            _floatingIcon.ShowAt(position);

            // Pre-fetch the selected text so it's ready by the time the user clicks
            // the icon. The clipboard read must run on the dispatcher (STA) thread —
            // we don't await here, so the call returns immediately and the task
            // completes in the background.
            try
            {
                var clipboardService = _serviceProvider!.GetRequiredService<IClipboardService>();
                _prefetchSelectionTask = clipboardService.GetSelectedTextAsync(sourceWindow);
            }
            catch { _prefetchSelectionTask = null; }
        }
        catch { }
    }

    private async void OnFloatingIconClicked()
    {
        try
        {
            var iconCenter = _floatingIcon?.IconCenter ?? new Point(0, 0);
            var settings = _serviceProvider!.GetRequiredService<ISettingsService>().Load();
            var clipboardService = _serviceProvider!.GetRequiredService<IClipboardService>();

            Task<string?> captureTask;
            if (_prefetchSelectionTask is { } prefetch)
            {
                captureTask = prefetch;
                _prefetchSelectionTask = null;
            }
            else
            {
                if (_pendingSelectionWindow != IntPtr.Zero)
                    NativeMethods.SetForegroundWindow(_pendingSelectionWindow);
                captureTask = clipboardService.GetSelectedTextAsync(_pendingSelectionWindow);
            }

            var vm = new EditorPopupViewModel(
                _serviceProvider!.GetRequiredService<IFoundryLocalClient>(),
                clipboardService,
                _serviceProvider!.GetRequiredService<ITextDiffService>(),
                _serviceProvider!.GetRequiredService<IFoundryManager>(),
                _serviceProvider!.GetRequiredService<IReadabilityService>(),
                
                _serviceProvider!.GetRequiredService<IGrammarChecker>(),
                _serviceProvider!.GetRequiredService<ISettingsService>(),
                _branding,
                _serviceProvider!.GetService<IWordReadabilityService>())
            {
                SelectedMode = settings.DefaultMode,
                PreviousForegroundWindow = _pendingSelectionWindow
            };

            var popup = new EditorPopupWindow
            {
                DataContext = vm,
                Opacity = settings.PopupOpacity
            };

            popup.Show();
            ApplySpringAnimation(popup, iconCenter);

            var selectedText = await captureTask;

            if (!string.IsNullOrWhiteSpace(selectedText))
            {
                vm.SetCapturedText(selectedText);
            }
            else
            {
                // UIA couldn't read the selection (Electron app, custom text
                // surface, permission denied, etc.). Keep the popup open with
                // an empty internal buffer, show the manual-paste hint, and
                // focus the input so the user can paste their own text with
                // Ctrl+V and proceed normally.
                vm.IsManualPasteMode = true;
                popup.FocusWorkingTextInput();
            }
            _ = vm.CheckConnectionAsync();
        }
        catch { }
    }

    private async void OnHotkeyPressed()
    {
        try
        {
            _floatingIcon?.DismissNow();

            var foregroundWindow = NativeMethods.GetForegroundWindow();
            var clipboardService = _serviceProvider!.GetRequiredService<IClipboardService>();
            var selectedText = await clipboardService.GetSelectedTextAsync(foregroundWindow);

            var settings = _serviceProvider!.GetRequiredService<ISettingsService>().Load();

            var vm = new EditorPopupViewModel(
                _serviceProvider!.GetRequiredService<IFoundryLocalClient>(),
                clipboardService,
                _serviceProvider!.GetRequiredService<ITextDiffService>(),
                _serviceProvider!.GetRequiredService<IFoundryManager>(),
                _serviceProvider!.GetRequiredService<IReadabilityService>(),
                
                _serviceProvider!.GetRequiredService<IGrammarChecker>(),
                _serviceProvider!.GetRequiredService<ISettingsService>(),
                _branding,
                _serviceProvider!.GetService<IWordReadabilityService>())
            {
                SelectedMode = settings.DefaultMode,
                PreviousForegroundWindow = foregroundWindow
            };

            bool hasSelection = !string.IsNullOrWhiteSpace(selectedText);
            if (hasSelection) vm.SetCapturedText(selectedText!);
            else vm.IsManualPasteMode = true;

            var popup = new EditorPopupWindow
            {
                DataContext = vm,
                Opacity = settings.PopupOpacity
            };

            popup.Show();

            if (_animateFromPoint is { } fromPoint)
            {
                _animateFromPoint = null;
                ApplySpringAnimation(popup, fromPoint);
            }

            // If UIA couldn't grab a selection (Electron apps, custom
            // renderers, no selection active), keep the popup open anyway
            // with an empty buffer and focus the input so the user can paste
            // their text manually (Ctrl+V).
            if (!hasSelection) popup.FocusWorkingTextInput();

            _ = vm.CheckConnectionAsync();
        }
        catch { }
    }

    private static void ApplySpringAnimation(Window popup, Point fromPoint)
    {
        try
        {
            if (popup.Content is not FrameworkElement content) return;

            double originX = (fromPoint.X - popup.Left) / Math.Max(popup.ActualWidth, 1);
            double originY = (fromPoint.Y - popup.Top) / Math.Max(popup.ActualHeight, 1);
            originX = Math.Clamp(originX, 0, 1);
            originY = Math.Clamp(originY, 0, 1);

            var scaleTransform = new ScaleTransform(0.03, 0.03);
            content.RenderTransform = scaleTransform;
            content.RenderTransformOrigin = new Point(originX, originY);
            popup.Opacity = 0;

            // Damped spring physics. zeta lower = bouncier, omega higher = snappier.
            double zeta = 0.7;
            double omega = 25.0;
            double omegaD = omega * Math.Sqrt(1 - zeta * zeta);
            double duration = 0.35;

            var sw = System.Diagnostics.Stopwatch.StartNew();
            EventHandler? handler = null;

            handler = (_, _) =>
            {
                double t = sw.Elapsed.TotalSeconds;

                if (t >= duration)
                {
                    CompositionTarget.Rendering -= handler;
                    scaleTransform.ScaleX = 1;
                    scaleTransform.ScaleY = 1;
                    popup.Opacity = 1;
                    content.RenderTransform = Transform.Identity;
                    return;
                }

                double spring = 1.0 - Math.Exp(-zeta * omega * t) * Math.Cos(omegaD * t);
                double scale = 0.03 + 0.97 * spring;

                scaleTransform.ScaleX = scale;
                scaleTransform.ScaleY = scale;
                popup.Opacity = Math.Min(1, t / 0.08);
            };

            CompositionTarget.Rendering += handler;
        }
        catch
        {
            popup.Opacity = 1;
        }
    }

    private void ShowSettings()
    {
        var vm = new SettingsViewModel(
            _serviceProvider!.GetRequiredService<ISettingsService>(),
            _serviceProvider!.GetRequiredService<IFoundryManager>());
        var window = new SettingsWindow
        {
            DataContext = vm,
            Title = $"{_branding.AppDisplayName} Settings"
        };

        vm.RequestClose += saved =>
        {
            if (saved)
            {
                if (_hotKeyHelper != null && _hotkeyId > 0)
                    _hotKeyHelper.Unregister(_hotkeyId);

                var newSettings = _serviceProvider!.GetRequiredService<ISettingsService>().Load();
                SetupHotkey(newSettings);

                // If the model name changed, nudge Foundry to load the new
                // model now so it's ready for the next request. Without this,
                // the user's first action after switching models hits a cold
                // lazy-load and often fails with a timeout.
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var manager = _serviceProvider!.GetRequiredService<IFoundryManager>();
                        await manager.EnsureRunningAsync();
                    }
                    catch { /* startup nudge is best-effort */ }
                });
            }
        };

        window.ShowDialog();
    }

    public void Dispose()
    {
        if (_hotKeyHelper != null)
        {
            if (_hotkeyId > 0)
                _hotKeyHelper.Unregister(_hotkeyId);
            _hotKeyHelper.Dispose();
        }

        _selectionWatcher?.Dispose();
        _floatingIcon?.Close();
        _trayIcon?.Dispose();
        _serviceProvider?.Dispose();

        // Release the single-instance mutex last. Wrapping in try/catch
        // because ReleaseMutex throws if the mutex wasn't owned (can happen
        // if Dispose is called on a host that exited early in Start()).
        if (_singleInstanceMutex != null)
        {
            try { _singleInstanceMutex.ReleaseMutex(); } catch { }
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
        }
    }
}

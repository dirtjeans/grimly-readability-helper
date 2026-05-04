using System.IO;
using System.Media;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Grimly.Native;

namespace Grimly.Views;

/// <summary>
/// A self-contained notification popup, shown at the top-center of the
/// monitor the user is currently working on.
///
/// Two modes:
/// - <b>Auto-dismiss</b> (default): fades in, sits for <see cref="DisplayDuration"/>, fades out.
/// - <b>Persistent</b>: stays until <see cref="UpdateAndDismiss"/> or <see cref="DismissNow"/>
///   is called. Use for long-running operations where you want a single
///   message that updates through multiple stages.
///
/// Never takes focus from the user's current app.
/// </summary>
public partial class ToastWindow : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    /// <summary>How long the toast stays fully visible after it's been told to dismiss.</summary>
    private static readonly TimeSpan DisplayDuration = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Safety cap for persistent toasts. If something goes wrong and the
    /// caller forgets to dismiss, the toast auto-dismisses anyway after this
    /// long so it doesn't stay on screen forever. 90 s is plenty for any
    /// reasonable startup — the Foundry-command timeout is 2 min, but our
    /// per-command timeout is stricter on the first /v1/models call.
    /// </summary>
    private static readonly TimeSpan PersistentSafetyTimeout = TimeSpan.FromSeconds(90);

    private static readonly Duration FadeIn = new(TimeSpan.FromMilliseconds(220));
    private static readonly Duration FadeOut = new(TimeSpan.FromMilliseconds(400));

    private readonly DispatcherTimer _autoDismissTimer;
    private bool _closing;
    private bool _autoDismiss;

    public ToastWindow()
    {
        InitializeComponent();
        Opacity = 0;

        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
        };

        Loaded += OnLoadedInitialize;

        _autoDismissTimer = new DispatcherTimer();
        _autoDismissTimer.Tick += (_, _) => StartFadeOut();
    }

    /// <summary>
    /// Show a toast on the cursor's current monitor. Safe to call from any
    /// thread. Returns the toast instance so the caller can call
    /// <see cref="UpdateAndDismiss"/> or <see cref="DismissNow"/> later —
    /// useful for long-running operations where the message evolves through
    /// stages ("Getting ready..." → "Ready!").
    /// </summary>
    /// <param name="autoDismiss">
    /// If true (default), the toast fades out on its own after a short delay.
    /// If false, the toast stays visible until a follow-up call updates or
    /// dismisses it (with a 3-minute safety cap in case the caller forgets).
    /// </param>
    public static ToastWindow? Show(string title, string message, bool autoDismiss = true)
    {
        var app = Application.Current;
        if (app == null) return null;

        ToastWindow? result = null;
        void Create()
        {
            result = new ToastWindow();
            result.TitleText.Text = title;
            result.MessageText.Text = message;
            result._autoDismiss = autoDismiss;
            result._autoDismissTimer.Interval = autoDismiss ? DisplayDuration : PersistentSafetyTimeout;
            result.Show();
            PlaySoftChime();
        }

        if (app.Dispatcher.CheckAccess()) Create();
        else app.Dispatcher.Invoke(Create);
        return result;
    }

    /// <summary>
    /// Replace the toast's message in place without dismissing it. Use during
    /// long operations to reflect progress ("Getting ready…" → "Starting
    /// Foundry Local service…") while keeping the toast on screen.
    /// </summary>
    public void UpdateMessage(string newMessage)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(() => UpdateMessage(newMessage)); return; }
        if (_closing) return;
        MessageText.Text = newMessage;
    }

    /// <summary>
    /// Replace the message and start the auto-dismiss countdown. Use for the
    /// final "done!" (or final error) stage of a multi-step operation.
    /// </summary>
    public void UpdateAndDismiss(string newMessage)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(() => UpdateAndDismiss(newMessage)); return; }
        if (_closing) return;
        MessageText.Text = newMessage;
        _autoDismissTimer.Stop();
        _autoDismissTimer.Interval = DisplayDuration;
        _autoDismissTimer.Start();
    }

    /// <summary>Immediately fade out and close.</summary>
    public void DismissNow()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(DismissNow); return; }
        StartFadeOut();
    }

    // Candidate notification WAVs in preferred order. "System Generic" is
    // Microsoft's purpose-built app-notification sound — a short, gentle
    // ping that users don't associate with errors. The rest are fallbacks
    // for older Windows installs. If none resolve, we play nothing.
    private static readonly string[] ChimeCandidates = new[]
    {
        @"C:\Windows\Media\Windows Notify System Generic.wav",
        @"C:\Windows\Media\Windows Notify.wav",
        @"C:\Windows\Media\notify.wav",
        @"C:\Windows\Media\chimes.wav",
    };

    private static string? _cachedChimePath;
    private static bool _chimeLookupDone;

    private static void PlaySoftChime()
    {
        try
        {
            if (!_chimeLookupDone)
            {
                foreach (var path in ChimeCandidates)
                {
                    if (File.Exists(path)) { _cachedChimePath = path; break; }
                }
                _chimeLookupDone = true;
            }

            if (_cachedChimePath == null) return;

            // SoundPlayer.Play() is fire-and-forget on a background thread,
            // so we don't block the UI on audio dispatch. A new instance per
            // play is fine — SoundPlayer is lightweight.
            var player = new SoundPlayer(_cachedChimePath);
            player.Play();
        }
        catch { /* audio is a nice-to-have; never throw */ }
    }

    private void OnLoadedInitialize(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoadedInitialize;
        PositionOnActiveMonitor();
        BeginPopIn();
        _autoDismissTimer.Start();
    }

    private void PositionOnActiveMonitor()
    {
        double workLeft, workTop, workWidth;
        double dpiScale = 1.0;

        try
        {
            if (NativeMethods.GetCursorPos(out var cursor))
            {
                var hMon = NativeMethods.MonitorFromPoint(cursor, NativeMethods.MONITOR_DEFAULTTONEAREST);
                var info = new NativeMethods.MONITORINFO
                {
                    cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>()
                };
                if (NativeMethods.GetMonitorInfo(hMon, ref info))
                {
                    var source = PresentationSource.FromVisual(this);
                    dpiScale = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
                    if (dpiScale <= 0) dpiScale = 1.0;

                    workLeft = info.rcWork.Left / dpiScale;
                    workTop = info.rcWork.Top / dpiScale;
                    workWidth = info.rcWork.Width / dpiScale;
                    PositionAt(workLeft, workTop, workWidth);
                    return;
                }
            }
        }
        catch { /* fall through */ }

        var primary = SystemParameters.WorkArea;
        PositionAt(primary.Left, primary.Top, primary.Width);
    }

    private void PositionAt(double workLeft, double workTop, double workWidth)
    {
        double w = ActualWidth > 0 ? ActualWidth : Width;
        Left = workLeft + (workWidth - w) / 2;
        Top = workTop + 80;
    }

    private void BeginPopIn()
    {
        var fade = new DoubleAnimation(0, 1, FadeIn)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        BeginAnimation(OpacityProperty, fade);

        var scaleEasing = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 };
        var scaleAnim = new DoubleAnimation(0.92, 1.0, FadeIn) { EasingFunction = scaleEasing };
        RootScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
        RootScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);
    }

    private void StartFadeOut()
    {
        if (_closing) return;
        _closing = true;
        _autoDismissTimer.Stop();

        var fadeOut = new DoubleAnimation(Opacity, 0, FadeOut);
        fadeOut.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, fadeOut);
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        StartFadeOut();
        e.Handled = true;
    }
}

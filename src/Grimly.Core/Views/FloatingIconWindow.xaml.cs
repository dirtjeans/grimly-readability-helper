using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Grimly.Views;

public partial class FloatingIconWindow : Window
{
    private readonly DispatcherTimer _autoDismissTimer;

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    // NOACTIVATE prevents the window from taking focus on click or on show —
    // crucial so the user's Ctrl+C in the underlying app isn't stolen by us.
    private const int WS_EX_NOACTIVATE = 0x08000000;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    public event Action? IconClicked;
    public Point IconCenter => new(Left + Width / 2, Top + Height / 2);

    public FloatingIconWindow()
    {
        InitializeComponent();

        // - WS_EX_TOOLWINDOW: suppress taskbar/alt-tab presence.
        // - WS_EX_NOACTIVATE: don't take focus when shown or clicked. Without
        //   this the icon steals the user's Ctrl+C / Ctrl+V keystroke, which
        //   then lands on our unhandled window and Windows chimes.
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
        };

        _autoDismissTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(5)
        };
        _autoDismissTimer.Tick += (_, _) => HideWithFade();
    }

    public void ShowAt(Point screenPosition)
    {
        // screenPosition is in physical pixels (from GetCursorPos).
        // WPF Left/Top use device-independent pixels (DIPs).
        // Convert physical → DIPs to handle non-100% DPI scaling.
        var dpiScale = GetDpiScale();
        var dipPos = new Point(screenPosition.X / dpiScale, screenPosition.Y / dpiScale);

        Left = dipPos.X + 8;
        Top = dipPos.Y + 8;

        var workArea = SystemParameters.WorkArea;
        if (Left + Width > workArea.Right) Left = workArea.Right - Width - 8;
        if (Top + Height > workArea.Bottom) Top = dipPos.Y - Height - 8;

        Opacity = 0;
        Show();

        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150));
        BeginAnimation(OpacityProperty, fadeIn);

        _autoDismissTimer.Stop();
        _autoDismissTimer.Start();
    }

    public void HideWithFade()
    {
        _autoDismissTimer.Stop();
        var fadeOut = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(200));
        fadeOut.Completed += (_, _) => Hide();
        BeginAnimation(OpacityProperty, fadeOut);
    }

    public void DismissNow()
    {
        _autoDismissTimer.Stop();
        Hide();
    }

    private double GetDpiScale()
    {
        // Try WPF's PresentationSource (works if the window has been shown before)
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget != null)
            return source.CompositionTarget.TransformToDevice.M11;

        // Fallback: use the main window or any active window
        foreach (Window w in Application.Current.Windows)
        {
            var s = PresentationSource.FromVisual(w);
            if (s?.CompositionTarget != null)
                return s.CompositionTarget.TransformToDevice.M11;
        }

        // Last resort: query via Win32 GetDpiForSystem (available on Win10 1607+)
        try
        {
            return Native.NativeMethods.GetDpiForSystem() / 96.0;
        }
        catch
        {
            return 1.0;
        }
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _autoDismissTimer.Stop();
        Hide();
        IconClicked?.Invoke();
        e.Handled = true;
    }
}

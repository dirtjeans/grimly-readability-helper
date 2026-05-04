using System.Windows;
using System.Windows.Threading;
using Grimly.Native;

namespace Grimly.Services;

public interface ISelectionWatcherService
{
    event Action<Point, IntPtr>? DragSelectionDetected; // screen position, source window
    event Action? SelectionCleared; // fired on click without drag (deselection)
    void Start();
    void Stop();
}

public sealed class SelectionWatcherService : ISelectionWatcherService, IDisposable
{
    public event Action<Point, IntPtr>? DragSelectionDetected;
    public event Action? SelectionCleared;

    private IntPtr _hookId;
    private NativeMethods.LowLevelMouseProc? _hookProc;
    private Point _mouseDownPosition;
    private bool _isMouseDown;

    private const int WH_MOUSE_LL = 14;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_LBUTTONUP = 0x0202;
    private const double MIN_DRAG_DISTANCE = 15;

    public void Start()
    {
        if (_hookId != IntPtr.Zero) return;

        _hookProc = HookCallback;
        _hookId = NativeMethods.SetWindowsHookEx(WH_MOUSE_LL, _hookProc,
            NativeMethods.GetModuleHandle(null), 0);
    }

    public void Stop()
    {
        if (_hookId != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (nCode >= 0)
            {
                int msg = wParam.ToInt32();
                if (msg == WM_LBUTTONDOWN)
                {
                    _isMouseDown = true;
                    if (NativeMethods.GetCursorPos(out var pt))
                        _mouseDownPosition = new Point(pt.X, pt.Y);
                }
                else if (msg == WM_LBUTTONUP && _isMouseDown)
                {
                    _isMouseDown = false;

                    if (NativeMethods.GetCursorPos(out var pt))
                    {
                        var mouseUp = new Point(pt.X, pt.Y);
                        double dx = mouseUp.X - _mouseDownPosition.X;
                        double dy = mouseUp.Y - _mouseDownPosition.Y;
                        double dist = Math.Sqrt(dx * dx + dy * dy);

                        if (dist >= MIN_DRAG_DISTANCE)
                        {
                            var sourceWindow = NativeMethods.GetForegroundWindow();
                            Application.Current?.Dispatcher.BeginInvoke(() =>
                            {
                                DragSelectionDetected?.Invoke(mouseUp, sourceWindow);
                            });
                        }
                        else
                        {
                            // Simple click (no drag) = user clicked somewhere, selection likely cleared
                            Application.Current?.Dispatcher.BeginInvoke(() =>
                            {
                                SelectionCleared?.Invoke();
                            });
                        }
                    }
                }
            }
        }
        catch { }

        return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        Stop();
    }
}

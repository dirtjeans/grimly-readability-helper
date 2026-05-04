using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace Grimly.Native;

public sealed class HotKeyHelper : IDisposable
{
    private readonly Window _hiddenWindow;
    private readonly IntPtr _handle;
    private readonly HwndSource _source;
    private int _nextId = 1;
    private readonly Dictionary<int, Action> _handlers = new();

    public HotKeyHelper()
    {
        // Show window off-screen then hide — WPF needs Show() to pump messages
        _hiddenWindow = new Window
        {
            Width = 1,
            Height = 1,
            Left = -9999,
            Top = -9999,
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false,
            ShowActivated = false
        };
        _hiddenWindow.Show();
        _hiddenWindow.Hide();

        var interop = new WindowInteropHelper(_hiddenWindow);
        _handle = interop.Handle;
        _source = HwndSource.FromHwnd(_handle)!;
        _source.AddHook(WndProc);
    }

    public int Register(ModifierKeys modifiers, Key key, Action handler)
    {
        int id = _nextId++;
        uint fsModifiers = 0;
        if (modifiers.HasFlag(ModifierKeys.Control)) fsModifiers |= NativeMethods.MOD_CONTROL;
        if (modifiers.HasFlag(ModifierKeys.Alt)) fsModifiers |= NativeMethods.MOD_ALT;
        if (modifiers.HasFlag(ModifierKeys.Shift)) fsModifiers |= NativeMethods.MOD_SHIFT;

        uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);

        if (!NativeMethods.RegisterHotKey(_handle, id, fsModifiers, vk))
        {
            int error = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
            throw new InvalidOperationException(
                $"Failed to register hotkey (id={id}, error={error}). " +
                "Another application may have registered this hotkey.");
        }

        _handlers[id] = handler;
        return id;
    }

    public void Unregister(int id)
    {
        NativeMethods.UnregisterHotKey(_handle, id);
        _handlers.Remove(id);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY)
        {
            int id = wParam.ToInt32();
            if (_handlers.TryGetValue(id, out var handler))
            {
                handler();
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        foreach (var id in _handlers.Keys.ToList())
            Unregister(id);

        _source.RemoveHook(WndProc);
        _hiddenWindow.Close();
    }
}

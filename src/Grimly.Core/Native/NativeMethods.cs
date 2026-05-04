using System.Runtime.InteropServices;

namespace Grimly.Native;

public static partial class NativeMethods
{
    public const int WM_HOTKEY = 0x0312;
    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_NOREPEAT = 0x4000;

    public const ushort VK_CONTROL = 0x11;
    public const ushort VK_MENU = 0x12;    // Alt key
    public const ushort VK_SHIFT = 0x10;
    public const ushort VK_LWIN = 0x5B;
    public const ushort VK_C = 0x43;
    public const ushort VK_V = 0x56;

    public const uint KEYEVENTF_KEYUP = 0x0002;
    public const uint INPUT_KEYBOARD = 1;

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnregisterHotKey(IntPtr hWnd, int id);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetCursorPos(out POINT lpPoint);

    // Monitor-from-cursor helpers so toasts/popups render on the display the
    // user is actually looking at, not always the primary monitor.
    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    public const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public struct MONITORINFO
    {
        public int cbSize;
        public MonitorRect rcMonitor;
        public MonitorRect rcWork;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MonitorRect
    {
        public int Left, Top, Right, Bottom;
        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    [LibraryImport("user32.dll")]
    public static partial IntPtr GetForegroundWindow();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [LibraryImport("user32.dll")]
    private static partial short GetAsyncKeyState(int vKey);

    [LibraryImport("user32.dll")]
    private static partial uint MapVirtualKeyW(uint uCode, uint uMapType);

    private const uint MAPVK_VK_TO_VSC = 0;

    // For the WM_COPY / WM_PASTE fallback: find the currently focused child
    // window inside the target process so we can message-post directly to it,
    // bypassing SendInput entirely. Word, WordPad, and any standard Edit/Rich
    // Edit control respond to these messages.
    //
    // We use GetGUIThreadInfo rather than AttachThreadInput + GetFocus.
    // Microsoft explicitly warns against AttachThreadInput on Vista+ — it
    // merges input state between threads and can leave side effects even
    // after detaching, which manifested as Chrome playing a chime when the
    // user pressed Ctrl+C shortly after we attached. GetGUIThreadInfo reads
    // the focused-window info without touching input state.
    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [LibraryImport("user32.dll", EntryPoint = "SendMessageTimeoutW")]
    private static partial IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam, uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

    [LibraryImport("user32.dll", EntryPoint = "GetGUIThreadInfo")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO lpgui);

    [StructLayout(LayoutKind.Sequential)]
    private struct GUITHREADINFO
    {
        public uint cbSize;
        public uint flags;
        public IntPtr hwndActive;
        public IntPtr hwndFocus;
        public IntPtr hwndCapture;
        public IntPtr hwndMenuOwner;
        public IntPtr hwndMoveSize;
        public IntPtr hwndCaret;
        public RECT rcCaret;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    private const uint WM_COPY = 0x0301;
    private const uint WM_PASTE = 0x0302;
    private const uint SMTO_ABORTIFHUNG = 0x0002;

    /// <summary>True if the given virtual-key is currently physically held down.</summary>
    public static bool IsKeyDown(ushort vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    // Note: WM_COPY capture was removed when ClipboardService switched to a
    // strict "capture never touches the system clipboard" policy — UIA is
    // now the only capture path. WM_PASTE is kept because Paste is the
    // app's one sanctioned moment to write the clipboard.

    /// <summary>Post WM_PASTE to whichever child window currently has keyboard focus.</summary>
    public static bool TrySendPasteMessage(IntPtr targetTopLevel)
    {
        if (targetTopLevel == IntPtr.Zero) return false;
        return TrySendClipboardMessage(targetTopLevel, WM_PASTE);
    }

    private static bool TrySendClipboardMessage(IntPtr targetTopLevel, uint msg)
    {
        uint targetThread = GetWindowThreadProcessId(targetTopLevel, out _);
        if (targetThread == 0) return false;

        var info = new GUITHREADINFO();
        info.cbSize = (uint)Marshal.SizeOf<GUITHREADINFO>();
        if (!GetGUIThreadInfo(targetThread, ref info)) return false;

        var focused = info.hwndFocus;
        if (focused == IntPtr.Zero) focused = targetTopLevel;

        // 200 ms is generous — WM_COPY is synchronous inside the target and
        // should return in microseconds for any cooperating control.
        var result = SendMessageTimeout(focused, msg, IntPtr.Zero, IntPtr.Zero,
            SMTO_ABORTIFHUNG, 200, out _);
        return result != IntPtr.Zero;
    }

    // Mouse hook
    public delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    public static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern IntPtr LoadLibrary(string lpFileName);

    [DllImport("user32.dll")]
    public static extern uint GetDpiForSystem();

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT
    {
        public uint Type;
        public INPUTUNION U;
    }

    // Union must be sized to the largest member (MOUSEINPUT = 32 bytes on 64-bit)
    [StructLayout(LayoutKind.Explicit)]
    public struct INPUTUNION
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    /// <summary>
    /// Send Ctrl+{keyVk} to the foreground window.
    ///
    /// The hazard: when the user is still physically holding a hotkey
    /// (Ctrl+Alt+G) at the moment we fire, the hardware Ctrl-up can arrive
    /// mid-combo and the target app sees a bare letter ("c" or "v"), which
    /// overwrites the user's selection.
    ///
    /// Fix: poll GetAsyncKeyState and wait for physical release of Ctrl, Alt,
    /// and Shift before sending anything. Then inject synthetic up events
    /// (in case Windows's tracked state drifted), settle, and finally send
    /// the Ctrl+key combo as an atomic SendInput batch.
    /// </summary>
    public static async Task SendCtrlComboAsync(ushort keyVk)
    {
        // Phase 1: wait for any physically-held modifiers to be released.
        // Hard cap at 500 ms in case the user is really leaning on the hotkey,
        // but in practice users let go within 50–100 ms of the popup appearing.
        var deadline = DateTime.UtcNow.AddMilliseconds(500);
        while (DateTime.UtcNow < deadline)
        {
            if (!IsKeyDown(VK_CONTROL) && !IsKeyDown(VK_MENU) && !IsKeyDown(VK_SHIFT))
                break;
            await Task.Delay(10);
        }

        int size = Marshal.SizeOf<INPUT>();

        // Phase 2: inject synthetic up events as a safety net. If Windows's
        // tracked modifier state is out of sync with the physical keyboard
        // (can happen after our synthetic injections or UAC prompts), this
        // forces it back to "up".
        var release = new INPUT[]
        {
            MakeKeyInput(VK_CONTROL, KEYEVENTF_KEYUP),
            MakeKeyInput(VK_MENU, KEYEVENTF_KEYUP),
            MakeKeyInput(VK_SHIFT, KEYEVENTF_KEYUP),
        };
        SendInput((uint)release.Length, release, size);

        // Small settle so the release propagates through the input queue.
        await Task.Delay(20);

        // Phase 3: Ctrl-down first, small settle, then the key press.
        // In atomic-batch form some apps were still seeing a bare letter when
        // the target's input loop polled after the letter event but before
        // processing Ctrl-down. A 15 ms gap between Ctrl-down and the letter
        // reliably fixes this on Chromium-based apps and Office targets.
        SendInput(1, new[] { MakeKeyInput(VK_CONTROL, 0) }, size);
        await Task.Delay(15);

        var letter = new INPUT[]
        {
            MakeKeyInput(keyVk, 0),
            MakeKeyInput(keyVk, KEYEVENTF_KEYUP),
        };
        SendInput((uint)letter.Length, letter, size);
        await Task.Delay(15);

        SendInput(1, new[] { MakeKeyInput(VK_CONTROL, KEYEVENTF_KEYUP) }, size);
    }

    // Include the scan code alongside the virtual key. Some apps (Electron,
    // Chromium-based browsers, games with low-level keyboard hooks) look at
    // the scan code field and may not register injected keys without it.
    private static INPUT MakeKeyInput(ushort vk, uint flags) => new()
    {
        Type = INPUT_KEYBOARD,
        U = new INPUTUNION
        {
            ki = new KEYBDINPUT
            {
                wVk = vk,
                wScan = (ushort)MapVirtualKeyW(vk, MAPVK_VK_TO_VSC),
                dwFlags = flags
            }
        }
    };

}

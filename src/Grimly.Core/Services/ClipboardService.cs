using System.Windows;
using System.Windows.Automation;
using Grimly.Native;

namespace Grimly.Services;

/// <summary>
/// Capture-to-internal / output-to-system workflow.
///
/// <para>
/// The app never writes captured or processed text to the system clipboard
/// during the capture and revision phases. All intermediate state lives in
/// the view-model's <c>WorkingText</c> (the internal buffer). The ONLY place
/// we write to the system clipboard is <see cref="PasteTextAsync"/>, which
/// runs exclusively when the user explicitly hits the Paste button — and at
/// that moment we both write to the clipboard and fire Ctrl+V so the target
/// app ingests the text.
/// </para>
/// </summary>
public interface IClipboardService
{
    /// <summary>
    /// Capture the target app's current text selection into an internal
    /// string — the caller stores it in the view-model's buffer. Does NOT
    /// write to, read from, or in any way disturb the system clipboard.
    ///
    /// <para>
    /// Works by reading the selection directly from the target app's
    /// accessibility tree (UIA <c>TextPattern</c>). Apps that don't expose
    /// UIA selection (some Chromium/Electron builds, custom text surfaces)
    /// return null — the caller then shows the user a friendly "can't read
    /// selection here" message.
    /// </para>
    /// </summary>
    Task<string?> GetSelectedTextAsync(IntPtr targetWindow);

    /// <summary>
    /// Export the internal buffer to the system clipboard and paste it into
    /// the target app. The ONLY clipboard-writing operation in this service.
    /// </summary>
    Task PasteTextAsync(string text, IntPtr targetWindow);
}

public sealed class ClipboardService : IClipboardService
{
    // ─────────────────────────────────────────────────────────────────────
    // CAPTURE PHASE — never touches the system clipboard
    // ─────────────────────────────────────────────────────────────────────

    public async Task<string?> GetSelectedTextAsync(IntPtr targetWindow)
    {
        // Make sure the target app has focus before we ask UIA about its
        // selection: AutomationElement.FocusedElement reports the system-wide
        // focused element, so we need the target to actually have it.
        var currentForeground = NativeMethods.GetForegroundWindow();
        if (targetWindow != IntPtr.Zero && targetWindow != currentForeground)
        {
            NativeMethods.SetForegroundWindow(targetWindow);
            await Task.Delay(80);
        }

        // UIA TextPattern runs on a background thread because the COM calls
        // can block for tens of ms. Returns the selected text as a plain
        // string — no clipboard involved, and no chance of leaving a "c" in
        // the user's document from a keystroke race.
        return await Task.Run(TryGetSelectedTextViaUIA);
    }

    /// <summary>
    /// Query the target app's accessibility tree for its current text
    /// selection. Returns null if UIA isn't usable for this focused element
    /// (no TextPattern) or the selection is empty.
    /// </summary>
    private static string? TryGetSelectedTextViaUIA()
    {
        try
        {
            var focused = AutomationElement.FocusedElement;
            if (focused == null) return null;

            if (!focused.TryGetCurrentPattern(TextPattern.Pattern, out var patternObj))
                return null;

            var textPattern = (TextPattern)patternObj;
            var selections = textPattern.GetSelection();
            if (selections == null || selections.Length == 0) return null;

            // -1 means "give me the whole range" — full text of the first selection.
            var text = selections[0].GetText(-1);
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch
        {
            // AutomationElement can throw ElementNotAvailable, COM timeouts,
            // or unauthorized-access exceptions on apps with restricted UIA.
            return null;
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // OUTPUT PHASE — the only place we write to the system clipboard
    // ─────────────────────────────────────────────────────────────────────

    public async Task PasteTextAsync(string text, IntPtr targetWindow)
    {
        try { Clipboard.SetText(text); } catch { }

        if (targetWindow != IntPtr.Zero)
        {
            NativeMethods.SetForegroundWindow(targetWindow);
            await Task.Delay(100);
        }

        // WM_PASTE first — fires directly at the target's focused control,
        // no keyboard event queue, no modifier-timing races. Falls back to a
        // simulated Ctrl+V for apps that don't implement WM_PASTE (Chromium,
        // custom surfaces).
        bool pasted = false;
        if (targetWindow != IntPtr.Zero)
            pasted = NativeMethods.TrySendPasteMessage(targetWindow);

        if (!pasted)
            await NativeMethods.SendCtrlComboAsync(NativeMethods.VK_V);
    }
}

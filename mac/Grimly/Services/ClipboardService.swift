import AppKit
import ApplicationServices
import Carbon.HIToolbox

/// Capture-to-internal / output-to-system workflow.
///
/// The app never writes captured or processed text to the system pasteboard
/// during the capture and revision phases. All intermediate state lives in
/// the view-model's `workingText` (the internal buffer). The ONLY place we
/// write to the pasteboard is `pasteText(_:previousApp:)`, which runs
/// exclusively when the user explicitly hits the Paste button — at that
/// moment we both write to the pasteboard and fire Cmd+V so the target app
/// ingests the text.
class ClipboardService {

    // MARK: - Capture phase — never touches the pasteboard

    /// Capture the target app's current text selection via the Accessibility
    /// API. Returns nil if AX can't extract a selection (target app doesn't
    /// expose accessibility, or nothing is selected).
    ///
    /// The old implementation sent Cmd+C, read the pasteboard, and restored
    /// the original pasteboard contents. That worked but briefly disturbed
    /// the user's clipboard. The AX path reads the selection directly from
    /// the target app's accessibility tree — no pasteboard involvement.
    func getSelectedText(previousApp: NSRunningApplication?) async -> String? {
        // The target needs to be frontmost for AX queries to reliably find
        // its focused element. Re-activate if we know which app to return to.
        if let app = previousApp {
            app.activate()
            try? await Task.sleep(nanoseconds: 100_000_000)
        }

        let pid: pid_t
        if let app = previousApp {
            pid = app.processIdentifier
        } else if let frontmost = NSWorkspace.shared.frontmostApplication {
            pid = frontmost.processIdentifier
        } else {
            return nil
        }

        return readSelectedText(forPID: pid)
    }

    /// Read `kAXSelectedTextAttribute` from the focused UI element of the
    /// given process. Safe to call from any thread; all the AX calls here are
    /// synchronous CF operations.
    private func readSelectedText(forPID pid: pid_t) -> String? {
        let appElement = AXUIElementCreateApplication(pid)

        var focusedValue: AnyObject?
        let focusResult = AXUIElementCopyAttributeValue(
            appElement,
            kAXFocusedUIElementAttribute as CFString,
            &focusedValue
        )
        guard focusResult == .success, let focusedElement = focusedValue else {
            return nil
        }

        // `focusedValue` comes back as AnyObject but is really an AXUIElement.
        // Unsafe bit-cast through CFTypeRef is the idiomatic way across this
        // API surface — AXUIElement isn't directly bridgeable.
        let focusedAX = focusedElement as! AXUIElement

        var selectedValue: AnyObject?
        let selResult = AXUIElementCopyAttributeValue(
            focusedAX,
            kAXSelectedTextAttribute as CFString,
            &selectedValue
        )
        guard selResult == .success, let text = selectedValue as? String else {
            return nil
        }

        let trimmed = text.trimmingCharacters(in: .whitespacesAndNewlines)
        return trimmed.isEmpty ? nil : text
    }

    // MARK: - Output phase — the only place we write to the pasteboard

    /// Write the internal buffer to the system pasteboard and paste it into
    /// the target app. The ONLY pasteboard-writing operation in this service.
    func pasteText(_ text: String, previousApp: NSRunningApplication?) async {
        let pasteboard = NSPasteboard.general
        pasteboard.clearContents()
        pasteboard.setString(text, forType: .string)

        if let app = previousApp {
            app.activate()
            try? await Task.sleep(nanoseconds: 100_000_000)
        }

        // Release any physically-held modifier keys before injecting Cmd+V so
        // the user's earlier hotkey doesn't taint the combo.
        releaseModifiers()
        sendKeyCombo(keyCode: UInt16(kVK_ANSI_V), flags: .maskCommand)
    }

    // MARK: - Keyboard helpers (used only by the Output phase)

    private func releaseModifiers() {
        let modifierKeys: [UInt16] = [
            UInt16(kVK_Command),
            UInt16(kVK_Option),
            UInt16(kVK_Shift),
            UInt16(kVK_Control),
        ]

        let source = CGEventSource(stateID: .combinedSessionState)

        for keyCode in modifierKeys {
            if let event = CGEvent(keyboardEventSource: source, virtualKey: keyCode, keyDown: false) {
                event.flags = []
                event.post(tap: .cghidEventTap)
            }
        }

        // Brief pause to let the key-up events register.
        usleep(50_000)
    }

    private func sendKeyCombo(keyCode: UInt16, flags: CGEventFlags) {
        let source = CGEventSource(stateID: .combinedSessionState)

        guard let keyDown = CGEvent(keyboardEventSource: source, virtualKey: keyCode, keyDown: true),
              let keyUp = CGEvent(keyboardEventSource: source, virtualKey: keyCode, keyDown: false) else {
            return
        }

        keyDown.flags = flags
        keyUp.flags = flags

        keyDown.post(tap: .cghidEventTap)
        keyUp.post(tap: .cghidEventTap)
    }
}

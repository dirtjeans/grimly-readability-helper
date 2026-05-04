import Cocoa
import Carbon.HIToolbox

class HotKeyService {
    private var globalMonitor: Any?
    private var localMonitor: Any?
    private var registeredKeyCode: UInt16 = 0
    private var registeredModifiers: NSEvent.ModifierFlags = []
    private var handler: (() -> Void)?

    // The modifier flags we check (strip device-dependent bits)
    private static let relevantModifiers: NSEvent.ModifierFlags = [.command, .option, .shift, .control]

    func register(keyCode: UInt16, modifiers: CGEventFlags, handler: @escaping () -> Void) -> Bool {
        self.registeredKeyCode = keyCode
        self.handler = handler

        // Convert CGEventFlags to NSEvent.ModifierFlags
        var nsMods: NSEvent.ModifierFlags = []
        if modifiers.contains(.maskCommand) { nsMods.insert(.command) }
        if modifiers.contains(.maskAlternate) { nsMods.insert(.option) }
        if modifiers.contains(.maskShift) { nsMods.insert(.shift) }
        if modifiers.contains(.maskControl) { nsMods.insert(.control) }
        self.registeredModifiers = nsMods

        // Global monitor catches keyDown events in other apps
        globalMonitor = NSEvent.addGlobalMonitorForEvents(matching: .keyDown) { [weak self] event in
            self?.handleEvent(event)
        }

        // Local monitor catches keyDown events when Grimly itself is active
        localMonitor = NSEvent.addLocalMonitorForEvents(matching: .keyDown) { [weak self] event in
            if self?.handleEvent(event) == true {
                return nil // Swallow the event
            }
            return event
        }

        return globalMonitor != nil
    }

    @discardableResult
    private func handleEvent(_ event: NSEvent) -> Bool {
        let flags = event.modifierFlags.intersection(Self.relevantModifiers)

        if event.keyCode == registeredKeyCode && flags == registeredModifiers {
            NSLog("[Grimly HotKey] Matched! keyCode=\(event.keyCode)")
            handler?()
            return true
        }
        return false
    }

    func unregister() {
        if let monitor = globalMonitor {
            NSEvent.removeMonitor(monitor)
            globalMonitor = nil
        }
        if let monitor = localMonitor {
            NSEvent.removeMonitor(monitor)
            localMonitor = nil
        }
        handler = nil
    }

    deinit {
        unregister()
    }
}

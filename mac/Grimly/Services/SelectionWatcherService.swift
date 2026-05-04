import AppKit
import ApplicationServices

protocol SelectionWatcherDelegate: AnyObject {
    func selectionChanged(text: String, screenPoint: NSPoint, app: NSRunningApplication)
    func selectionCleared()
}

class SelectionWatcherService {
    weak var delegate: SelectionWatcherDelegate?

    private var isRunning = false
    private var currentObserver: AXObserver?
    private var currentPid: pid_t = 0
    private var currentApp: AXUIElement?
    private var currentFocusedElement: AXUIElement?
    private var debounceWork: DispatchWorkItem?
    private var appActivationObserver: NSObjectProtocol?

    private let debounceInterval: TimeInterval = 0.4

    func start() {
        guard !isRunning else { return }
        isRunning = true

        appActivationObserver = NSWorkspace.shared.notificationCenter.addObserver(
            forName: NSWorkspace.didActivateApplicationNotification,
            object: nil,
            queue: .main
        ) { [weak self] notification in
            guard let app = notification.userInfo?[NSWorkspace.applicationUserInfoKey] as? NSRunningApplication else { return }
            self?.attachToApp(app)
        }

        if let frontApp = NSWorkspace.shared.frontmostApplication {
            attachToApp(frontApp)
        }
    }

    func stop() {
        guard isRunning else { return }
        isRunning = false
        debounceWork?.cancel()
        debounceWork = nil
        detachCurrentObserver()

        if let obs = appActivationObserver {
            NSWorkspace.shared.notificationCenter.removeObserver(obs)
            appActivationObserver = nil
        }
    }

    // MARK: - Attach / Detach

    private func attachToApp(_ app: NSRunningApplication) {
        let pid = app.processIdentifier
        if pid == currentPid { return }
        if pid == ProcessInfo.processInfo.processIdentifier { return }

        detachCurrentObserver()
        currentPid = pid

        NSLog("[Grimly Watcher] Attaching to app: \(app.localizedName ?? "?") (pid=\(pid))")

        var observer: AXObserver?
        let result = AXObserverCreate(pid, axCallback, &observer)
        guard result == .success, let observer else {
            NSLog("[Grimly Watcher] Failed to create AXObserver: \(result.rawValue)")
            return
        }
        currentObserver = observer

        let appElement = AXUIElementCreateApplication(pid)
        currentApp = appElement

        // Register for focus changes on the app element (this always works)
        let refcon = Unmanaged.passUnretained(self).toOpaque()
        let r1 = AXObserverAddNotification(observer, appElement, kAXFocusedUIElementChangedNotification as CFString, refcon)
        NSLog("[Grimly Watcher] App-level focusedUI registration: \(r1.rawValue)")

        CFRunLoopAddSource(CFRunLoopGetMain(), AXObserverGetRunLoopSource(observer), .defaultMode)

        // Now find the currently focused element and register on it
        attachToFocusedElement()
    }

    /// Find the focused UI element in the current app and register
    /// kAXSelectedTextChangedNotification on it directly.
    private func attachToFocusedElement() {
        guard let observer = currentObserver, let appElement = currentApp else { return }

        // Remove old focused-element notification if any
        if let oldElement = currentFocusedElement {
            AXObserverRemoveNotification(observer, oldElement, kAXSelectedTextChangedNotification as CFString)
            currentFocusedElement = nil
        }

        // Get the currently focused UI element
        var focusedValue: AnyObject?
        let focusResult = AXUIElementCopyAttributeValue(appElement, kAXFocusedUIElementAttribute as CFString, &focusedValue)
        guard focusResult == .success else {
            NSLog("[Grimly Watcher] Could not get focused element: \(focusResult.rawValue)")
            return
        }

        let focusedElement = focusedValue as! AXUIElement
        currentFocusedElement = focusedElement

        // Register for selection changes on this specific element
        let refcon = Unmanaged.passUnretained(self).toOpaque()
        let r = AXObserverAddNotification(observer, focusedElement, kAXSelectedTextChangedNotification as CFString, refcon)
        NSLog("[Grimly Watcher] Focused-element selectedText registration: \(r.rawValue) (0=success)")
    }

    private func detachCurrentObserver() {
        if let observer = currentObserver {
            if let el = currentFocusedElement {
                AXObserverRemoveNotification(observer, el, kAXSelectedTextChangedNotification as CFString)
            }
            if let app = currentApp {
                AXObserverRemoveNotification(observer, app, kAXFocusedUIElementChangedNotification as CFString)
            }
            CFRunLoopRemoveSource(CFRunLoopGetMain(), AXObserverGetRunLoopSource(observer), .defaultMode)
        }
        currentObserver = nil
        currentApp = nil
        currentFocusedElement = nil
        currentPid = 0
    }

    // MARK: - Notification handling

    fileprivate func handleNotification(_ notification: CFString, element: AXUIElement) {
        let name = notification as String

        if name == kAXFocusedUIElementChangedNotification as String {
            NSLog("[Grimly Watcher] Focus changed — re-attaching to new element")
            delegate?.selectionCleared()
            attachToFocusedElement()
            return
        }

        // kAXSelectedTextChangedNotification
        let mousePos = NSEvent.mouseLocation

        debounceWork?.cancel()
        let work = DispatchWorkItem { [weak self] in
            self?.readSelectionFromElement(element, mouseFallback: mousePos)
        }
        debounceWork = work
        DispatchQueue.main.asyncAfter(deadline: .now() + debounceInterval, execute: work)
    }

    private func readSelectionFromElement(_ element: AXUIElement, mouseFallback: NSPoint) {
        var selectedTextValue: AnyObject?
        let result = AXUIElementCopyAttributeValue(element, kAXSelectedTextAttribute as CFString, &selectedTextValue)

        guard result == .success, let text = selectedTextValue as? String,
              !text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else {
            delegate?.selectionCleared()
            return
        }

        NSLog("[Grimly Watcher] Selected text: '\(text.prefix(40))...' (\(text.count) chars)")

        let screenPoint: NSPoint
        if let axPoint = selectionEndPoint(for: element) {
            NSLog("[Grimly Watcher] Using AX bounds position: \(axPoint)")
            screenPoint = axPoint
        } else {
            NSLog("[Grimly Watcher] Using mouse fallback position: \(mouseFallback)")
            screenPoint = mouseFallback
        }

        guard let app = NSRunningApplication(processIdentifier: currentPid) else {
            delegate?.selectionCleared()
            return
        }

        delegate?.selectionChanged(text: text, screenPoint: screenPoint, app: app)
    }

    private func selectionEndPoint(for element: AXUIElement) -> NSPoint? {
        var rangeValue: AnyObject?
        let rangeResult = AXUIElementCopyAttributeValue(element, kAXSelectedTextRangeAttribute as CFString, &rangeValue)
        guard rangeResult == .success, let rangeRef = rangeValue else { return nil }

        var boundsValue: AnyObject?
        let boundsResult = AXUIElementCopyParameterizedAttributeValue(
            element,
            kAXBoundsForRangeParameterizedAttribute as CFString,
            rangeRef,
            &boundsValue
        )
        guard boundsResult == .success, let boundsRef = boundsValue else { return nil }

        var bounds = CGRect.zero
        guard AXValueGetValue(boundsRef as! AXValue, .cgRect, &bounds) else { return nil }

        // Validate: some apps return a zero or tiny rect even on success
        guard bounds.width > 1 && bounds.height > 1 else { return nil }

        // AX uses screen coordinates with origin at top-left of the primary display.
        // Convert to Cocoa coordinates (origin at bottom-left).
        // Use the primary screen's height (screens[0]), which is what AX coordinates
        // are relative to — even for selections on secondary displays.
        guard let primaryHeight = NSScreen.screens.first?.frame.height else { return nil }
        let cocoaY = primaryHeight - bounds.maxY

        // Sanity check: the resulting point should be on some screen
        let point = NSPoint(x: bounds.maxX, y: cocoaY)
        let onAnyScreen = NSScreen.screens.contains { NSPointInRect(point, $0.frame) }
        guard onAnyScreen else { return nil }

        return point
    }

    deinit {
        stop()
    }
}

// MARK: - C callback

private func axCallback(
    _ observer: AXObserver,
    _ element: AXUIElement,
    _ notification: CFString,
    _ refcon: UnsafeMutableRawPointer?
) {
    guard let refcon else { return }
    let service = Unmanaged<SelectionWatcherService>.fromOpaque(refcon).takeUnretainedValue()
    service.handleNotification(notification, element: element)
}

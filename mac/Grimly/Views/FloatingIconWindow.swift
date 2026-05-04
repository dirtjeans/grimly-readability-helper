import AppKit
import SwiftUI

class FloatingIconWindow: NSPanel {
    private var autoDismissWork: DispatchWorkItem?

    /// How long the icon stays visible before auto-fading (seconds).
    private let autoDismissDelay: TimeInterval = 8.0

    var onClick: (() -> Void)?

    init() {
        super.init(
            contentRect: NSRect(x: 0, y: 0, width: 32, height: 32),
            styleMask: [.nonactivatingPanel, .fullSizeContentView],
            backing: .buffered,
            defer: false
        )

        self.isFloatingPanel = true
        self.level = .popUpMenu
        self.isOpaque = false
        self.backgroundColor = .clear
        self.hasShadow = false
        self.titlebarAppearsTransparent = true
        self.titleVisibility = .hidden
        self.isMovableByWindowBackground = false
        self.collectionBehavior = [.canJoinAllSpaces, .transient, .ignoresCycle]

        let view = FloatingIconView { [weak self] in
            self?.onClick?()
        }
        self.contentView = NSHostingView(rootView: view)
    }

    /// Show the icon at the given screen position (Cocoa coordinates — origin bottom-left).
    func showNear(screenPoint: NSPoint) {
        NSLog("[Grimly Icon] showNear: screenPoint=\(screenPoint)")
        // Place the icon just below and to the right of the selection end
        var origin = NSPoint(x: screenPoint.x + 8, y: screenPoint.y - 36)

        // Keep within screen bounds
        if let screen = NSScreen.main {
            let vis = screen.visibleFrame
            if origin.x + 32 > vis.maxX { origin.x = vis.maxX - 36 }
            if origin.y < vis.minY { origin.y = screenPoint.y + 8 }
            if origin.x < vis.minX { origin.x = vis.minX + 4 }
        }

        setFrameOrigin(origin)
        alphaValue = 1
        orderFrontRegardless()

        resetAutoDismiss()
    }

    func hideIcon() {
        autoDismissWork?.cancel()
        autoDismissWork = nil
        orderOut(nil)
    }

    private func resetAutoDismiss() {
        autoDismissWork?.cancel()
        let work = DispatchWorkItem { [weak self] in
            self?.fadeOut()
        }
        autoDismissWork = work
        DispatchQueue.main.asyncAfter(deadline: .now() + autoDismissDelay, execute: work)
    }

    private func fadeOut() {
        NSAnimationContext.runAnimationGroup({ context in
            context.duration = 0.3
            self.animator().alphaValue = 0
        }, completionHandler: { [weak self] in
            self?.orderOut(nil)
            self?.alphaValue = 1
        })
    }

    /// The center point of the icon in screen coordinates (for animation origin).
    var iconCenter: NSPoint {
        NSPoint(x: frame.midX, y: frame.midY)
    }

    override var canBecomeKey: Bool { false }
    override var canBecomeMain: Bool { false }
}

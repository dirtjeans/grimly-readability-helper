import AppKit
import SwiftUI

class EditorPopupWindow: NSPanel {
    private let finalSize = NSSize(width: 620, height: 500)
    private let targetOpacity: CGFloat
    private var animTimer: Timer?
    private var animStartTime: CFTimeInterval = 0
    private var escapeMonitor: Any?

    init(viewModel: EditorPopupViewModel, opacity: Double) {
        self.targetOpacity = CGFloat(opacity)
        let contentRect = NSRect(x: 0, y: 0, width: finalSize.width, height: finalSize.height)

        super.init(
            contentRect: contentRect,
            styleMask: [.nonactivatingPanel, .titled, .closable, .fullSizeContentView],
            backing: .buffered,
            defer: false
        )

        self.isFloatingPanel = true
        self.level = .floating
        self.titlebarAppearsTransparent = true
        self.titleVisibility = .hidden
        self.isMovableByWindowBackground = true
        self.isOpaque = false
        self.backgroundColor = .clear
        self.hasShadow = true
        self.alphaValue = targetOpacity

        // Hide the traffic light buttons — they don't work on a nonactivating panel
        // and would confuse users. We use our own close button instead.
        self.standardWindowButton(.closeButton)?.isHidden = true
        self.standardWindowButton(.miniaturizeButton)?.isHidden = true
        self.standardWindowButton(.zoomButton)?.isHidden = true

        let hostingView = NSHostingView(rootView: EditorPopupView(viewModel: viewModel))
        self.contentView = hostingView

        viewModel.onRequestClose = { [weak self] in
            self?.animTimer?.invalidate()
            self?.close()
        }

        // Monitor Escape key to close (local monitor works when panel is key)
        self.escapeMonitor = NSEvent.addLocalMonitorForEvents(matching: .keyDown) { [weak self] event in
            if event.keyCode == 53 { // Escape
                self?.animTimer?.invalidate()
                self?.close()
                return nil
            }
            return event
        }
    }

    /// Show the popup near the cursor (standard hotkey behavior).
    func showNearCursor() {
        let finalFrame = frameNearPoint(NSEvent.mouseLocation)
        setFrame(finalFrame, display: true)
    }

    /// Animate the popup springing out from the icon's position using a
    /// manual spring simulation driven by a display-link timer.
    /// This bypasses all NSAnimationContext/CALayer anchor point issues.
    func showAnimatedFrom(point fromPoint: NSPoint) {
        let finalFrame = frameNearPoint(fromPoint)

        // The icon center in the final frame's coordinate space (0,0 = bottom-left)
        let originX = fromPoint.x
        let originY = fromPoint.y

        // Scale spring intensity to display DPI. On Retina (2x) displays the
        // sub-pixel rendering makes the bounce look smooth; on 1x displays the
        // same overshoot looks jarring because each pixel jump is more visible.
        let scaleFactor = NSScreen.main?.backingScaleFactor ?? 2.0
        let mass: CGFloat = 1.0
        let stiffness: CGFloat = scaleFactor >= 2.0 ? 260 : 300
        let damping: CGFloat = scaleFactor >= 2.0 ? 13 : 20
        let duration: CFTimeInterval = scaleFactor >= 2.0 ? 0.65 : 0.45

        // Start: tiny rect centered on the icon
        let startSize: CGFloat = 28
        setFrame(NSRect(
            x: originX - startSize / 2,
            y: originY - startSize / 2,
            width: startSize,
            height: startSize
        ), display: false)
        alphaValue = 0
        orderFrontRegardless()

        animStartTime = CACurrentMediaTime()

        // Run a timer at ~60fps to step the spring
        animTimer?.invalidate()
        animTimer = Timer.scheduledTimer(withTimeInterval: 1.0 / 60.0, repeats: true) { [weak self] timer in
            guard let self else { timer.invalidate(); return }

            let elapsed = CGFloat(CACurrentMediaTime() - self.animStartTime)
            if elapsed >= CGFloat(duration) {
                timer.invalidate()
                self.animTimer = nil
                self.setFrame(finalFrame, display: true)
                self.alphaValue = self.targetOpacity
                return
            }

            // Normalized time 0..1
            let t = elapsed / CGFloat(duration)

            // Damped spring: x(t) = 1 - e^(-ζωt) * cos(ωd * t)
            // where ω = sqrt(k/m), ζ = c/(2√(km)), ωd = ω√(1-ζ²)
            let omega = sqrt(stiffness / mass)
            let zeta = damping / (2 * sqrt(stiffness * mass))
            let omegaD = omega * sqrt(max(1 - zeta * zeta, 0.001))
            let springT = elapsed  // use real time, not normalized

            let progress = 1.0 - exp(-zeta * omega * springT) * cos(omegaD * springT)

            // Interpolate from start rect (icon) to final rect
            let x = originX - startSize / 2 + (finalFrame.origin.x - (originX - startSize / 2)) * progress
            let y = originY - startSize / 2 + (finalFrame.origin.y - (originY - startSize / 2)) * progress
            let w = startSize + (finalFrame.width - startSize) * progress
            let h = startSize + (finalFrame.height - startSize) * progress

            self.setFrame(NSRect(x: x, y: y, width: max(w, 1), height: max(h, 1)), display: true)

            // Fade in quickly over the first 20% of the animation
            let alpha = min(t / 0.2, 1.0) * self.targetOpacity
            self.alphaValue = alpha
        }
    }

    // MARK: - Positioning

    private func frameNearPoint(_ point: NSPoint) -> NSRect {
        guard let screen = NSScreen.main else {
            return NSRect(origin: point, size: finalSize)
        }
        let vis = screen.visibleFrame

        var x = point.x + 16
        var y = point.y - finalSize.height - 16

        if x + finalSize.width > vis.maxX {
            x = vis.maxX - finalSize.width - 16
        }
        if y < vis.minY {
            y = point.y + 16
        }
        if x < vis.minX {
            x = vis.minX + 16
        }
        if y + finalSize.height > vis.maxY {
            y = vis.maxY - finalSize.height - 16
        }

        return NSRect(x: x, y: y, width: finalSize.width, height: finalSize.height)
    }

    override func close() {
        if let monitor = escapeMonitor {
            NSEvent.removeMonitor(monitor)
            escapeMonitor = nil
        }
        animTimer?.invalidate()
        super.close()
    }

    override var canBecomeKey: Bool { true }
}

import Foundation
import Combine

/// App-level connection monitor. Owns the current `ConnectionStatus`,
/// polls Foundry Local in the background at an adaptive cadence, and drives
/// silent reconnect attempts when Foundry drops. Runs for the lifetime of
/// the menu-bar app process so a disconnected model recovers on its own —
/// no popup has to be open, no user action required.
///
/// Cadence, tuned to stay quiet when everything's fine and responsive
/// when it isn't:
///   - `.connected`             → poll every 30 s (steady-state heartbeat)
///   - `.modelNotLoaded`        → 3 → 5 → 7 s … capped at 15 s (fast reconnect)
///   - `.foundryNotRunning`     → same reconnect ladder
///   - `.foundryNotInstalled`   → poll every 60 s (recover if the user
///                                installs Foundry after launching)
///   - `.checking`              → transient; the loop advances quickly
///
/// Views observe `status` (and `isReconnecting`) through Combine — no
/// need for a shared `NotificationCenter` or delegate wire-up.
@MainActor
final class ConnectionMonitor: ObservableObject {
    @Published private(set) var status: ConnectionStatus = .checking

    /// True while we're actively trying to recover from a non-connected
    /// state. Views can show a subtler "Reconnecting…" hint instead of a
    /// hard error, since the app is already handling it.
    @Published private(set) var isReconnecting: Bool = false

    private let foundryManager: FoundryManager
    private var loopTask: Task<Void, Never>?
    private var reconnectAttempt: Int = 0

    init(foundryManager: FoundryManager) {
        self.foundryManager = foundryManager
    }

    /// Start the monitor loop. Idempotent — calling twice is a no-op.
    func start() {
        guard loopTask == nil else { return }
        loopTask = Task { [weak self] in
            await self?.runLoop()
        }
    }

    /// Stop the monitor loop. Idempotent.
    func stop() {
        loopTask?.cancel()
        loopTask = nil
    }

    /// One-shot immediate connection check. Callers can use this to wake
    /// the monitor early after a request-time failure or after the user
    /// clicks the status indicator.
    func refresh() async {
        let next = await foundryManager.checkConnection()
        applyTransition(to: next)
    }

    // MARK: - Loop

    private func runLoop() async {
        while !Task.isCancelled {
            let next = await foundryManager.checkConnection()
            applyTransition(to: next)

            let delayS: Double
            switch status {
            case .connected:
                delayS = 30
            case .modelNotLoaded, .foundryNotRunning:
                // 3, 5, 7, 9, 11, 13, 15, 15, 15 …
                delayS = min(3.0 + Double(reconnectAttempt) * 2.0, 15.0)
            case .foundryNotInstalled:
                delayS = 60
            case .checking:
                // Shouldn't linger here — poll again quickly.
                delayS = 2
            }

            do {
                try await Task.sleep(nanoseconds: UInt64(delayS * 1_000_000_000))
            } catch {
                return
            }
        }
    }

    /// Update status + reconnect bookkeeping in one place so the loop and
    /// `refresh()` stay in sync.
    private func applyTransition(to next: ConnectionStatus) {
        status = next
        switch next {
        case .connected:
            isReconnecting = false
            reconnectAttempt = 0
        case .modelNotLoaded, .foundryNotRunning:
            isReconnecting = true
            reconnectAttempt += 1
        case .foundryNotInstalled:
            isReconnecting = false
            reconnectAttempt = 0
        case .checking:
            break
        }
    }
}

import AppKit
import SwiftUI
import ApplicationServices
import UserNotifications

class AppDelegate: NSObject, NSApplicationDelegate {
    private let settingsService = SettingsService()
    private lazy var foundryManager = FoundryManager(settingsService: settingsService)
    private lazy var foundryClient = FoundryLocalClient(settingsService: settingsService)
    private let clipboardService = ClipboardService()
    private let diffService = TextDiffService()
    private let hotKeyService = HotKeyService()
    private let selectionWatcher = SelectionWatcherService()
    private var floatingIcon: FloatingIconWindow?

    /// Stashed state from the selection watcher for when the icon is clicked.
    private var pendingSelectionApp: NSRunningApplication?
    private var pendingSelectionText: String?

    var settingsViewModel: SettingsViewModel?
    private var settingsWindow: NSWindow?
    private var editorWindow: EditorPopupWindow?
    private var installerWindow: NSWindow?
    private var installer: InstallerService?

    func applicationDidFinishLaunching(_ notification: Notification) {
        // Single-instance guard. If another instance of this app (same
        // bundle ID) is already running, activate it and bail out. Users
        // sometimes double-click the app from Finder while an instance is
        // still live in the menu-bar / system tray, which creates duplicate
        // hotkey registrations and confused tray state.
        if activateExistingInstanceIfAny() {
            NSApp.terminate(nil)
            return
        }
        requestNotificationPermission()
        checkAccessibility()
        registerHotkey()
        setupSelectionWatcher()
        registerService()
        initializeFoundry()
    }

    /// Returns true if another instance of this app is already running
    /// (in which case it's activated and the caller should terminate).
    private func activateExistingInstanceIfAny() -> Bool {
        guard let myBundleId = Bundle.main.bundleIdentifier else { return false }
        let running = NSRunningApplication.runningApplications(withBundleIdentifier: myBundleId)
        let others = running.filter { $0 != NSRunningApplication.current }
        guard let existing = others.first else { return false }
        existing.activate(options: .activateIgnoringOtherApps)
        return true
    }

    private func registerService() {
        NSApp.servicesProvider = self
        // Force macOS to rescan services (important on first install)
        NSUpdateDynamicServices()
    }

    private func requestNotificationPermission() {
        UNUserNotificationCenter.current().requestAuthorization(options: [.alert, .sound]) { _, _ in }
    }

    // MARK: - Accessibility

    private func checkAccessibility() {
        let options = [kAXTrustedCheckOptionPrompt.takeUnretainedValue(): true] as CFDictionary
        let trusted = AXIsProcessTrustedWithOptions(options)

        if !trusted {
            let alert = NSAlert()
            alert.messageText = "Accessibility Permission Required"
            alert.informativeText = """
                Grimly needs Accessibility permission to capture selected text and register \
                a global hotkey. Please grant access in System Settings > Privacy & Security > \
                Accessibility, then relaunch Grimly.
                """
            alert.alertStyle = .warning
            alert.addButton(withTitle: "OK")
            alert.runModal()
        }
    }

    // MARK: - Hotkey

    private func registerHotkey() {
        let settings = settingsService.load()

        guard let keyCode = KeyCodeMapping.keyCode(for: settings.hotkeyKey) else {
            NSLog("[Grimly] Unknown hotkey key: \(settings.hotkeyKey)")
            return
        }

        let modifiers = KeyCodeMapping.modifierFlags(for: settings.hotkeyModifiers)
        NSLog("[Grimly] Registering hotkey: key=\(settings.hotkeyKey) (code=\(keyCode)), modifiers=\(settings.hotkeyModifiers) (flags=\(modifiers.rawValue))")
        NSLog("[Grimly] AXIsProcessTrusted: \(AXIsProcessTrusted())")

        let success = hotKeyService.register(keyCode: keyCode, modifiers: modifiers) { [weak self] in
            NSLog("[Grimly] Hotkey pressed!")
            self?.onHotkeyPressed()
        }

        if success {
            NSLog("[Grimly] Hotkey registered successfully")
        } else {
            NSLog("[Grimly] FAILED to register global hotkey. Accessibility permission may be missing.")
        }
    }

    private func onHotkeyPressed() {
        Task { @MainActor in
            let previousApp = NSWorkspace.shared.frontmostApplication
            NSLog("[Grimly] onHotkeyPressed: frontmostApp=\(previousApp?.localizedName ?? "nil")")

            let selectedText = await clipboardService.getSelectedText(previousApp: previousApp)
            NSLog("[Grimly] Captured text: \(selectedText == nil ? "nil" : "'\(selectedText!.prefix(50))...' (\(selectedText!.count) chars)")")

            guard let text = selectedText, !text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else {
                NSLog("[Grimly] No text selected, not opening popup")
                return
            }

            let settings = settingsService.load()

            let vm = EditorPopupViewModel(
                foundryClient: foundryClient,
                foundryManager: foundryManager,
                clipboardService: clipboardService,
                diffService: diffService
            )
            vm.selectedMode = settings.defaultMode
            vm.previousApp = previousApp
            vm.setCapturedText(text)
            vm.refreshConnectionStatus()

            let popup = EditorPopupWindow(viewModel: vm, opacity: settings.popupOpacity)
            popup.showNearCursor()
            popup.makeKeyAndOrderFront(nil)
            NSApp.activate(ignoringOtherApps: true)

            editorWindow = popup
        }
    }

    // MARK: - Foundry Initialization

    private func initializeFoundry() {
        // Check if Foundry is even installed before doing anything else
        if !foundryManager.isFoundryInstalled() {
            DispatchQueue.main.async { self.promptToInstallFoundry() }
            return
        }

        Task {
            let (running, endpoint) = await foundryManager.checkServiceStatus()

            if running, let endpoint {
                var settings = settingsService.load()
                if settings.foundryEndpoint != endpoint {
                    settings.foundryEndpoint = endpoint
                    settingsService.save(settings)
                }
                showNotification(title: "Grimly", body: "Foundry Local detected. Loading model...")
            } else {
                showNotification(title: "Grimly", body: "Starting Foundry Local service...")
            }

            // EnsureRunning will start the service + load a model automatically
            let (success, _, modelId) = await foundryManager.ensureRunning()

            if success {
                // Health check — verify the model actually responds
                showNotification(title: "Grimly", body: "Checking model responsiveness...")
                let healthy = await foundryManager.healthCheck()

                if !healthy {
                    showNotification(title: "Grimly", body: "Model not responding. Restarting Foundry...")
                    let (retrySuccess, _, retryModelId) = await foundryManager.ensureRunning()
                    if !retrySuccess {
                        showNotification(title: "Grimly", body: "Could not start model. Click Reconnect in the popup if it fails.")
                        return
                    }
                }

                let settings = settingsService.load()
                let hotkeyDesc = "\(settings.hotkeyModifiers)+\(settings.hotkeyKey)"
                showNotification(title: "Grimly", body: "Ready! Model: \(modelId ?? "unknown")\nPress \(hotkeyDesc) to use.")
            } else {
                // Only show manual instructions if automatic startup failed
                await MainActor.run {
                    let alert = NSAlert()

                    if endpoint == nil {
                        alert.messageText = "Foundry Local Setup Required"
                        alert.informativeText = """
                            Grimly could not start Foundry Local automatically.

                            If Foundry Local is not installed, open Terminal and run:
                                brew tap microsoft/foundrylocal
                                brew install foundrylocal

                            Then start the service and load a model:
                                foundry service start
                                foundry model run qwen2.5-7b

                            Grimly will keep running — it will work once Foundry is ready.
                            """
                    } else {
                        alert.messageText = "No Model Loaded"
                        alert.informativeText = """
                            Foundry Local is running but no model could be loaded.

                            Open Terminal and run:
                                foundry model run qwen2.5-7b

                            Grimly will keep running — it will work once a model is loaded.
                            """
                    }

                    alert.alertStyle = .warning
                    alert.addButton(withTitle: "Copy Commands")
                    alert.addButton(withTitle: "OK")

                    let response = alert.runModal()
                    if response == .alertFirstButtonReturn {
                        let commands = endpoint == nil
                            ? "brew tap microsoft/foundrylocal\nbrew install foundrylocal\nfoundry service start\nfoundry model run qwen2.5-7b"
                            : "foundry model run qwen2.5-7b"
                        NSPasteboard.general.clearContents()
                        NSPasteboard.general.setString(commands, forType: .string)
                    }
                }
            }
        }
    }

    @MainActor
    private func promptToInstallFoundry() {
        let alert = NSAlert()
        alert.messageText = "Foundry Local Is Not Installed"
        alert.alertStyle = .warning
        alert.informativeText = """
            Grimly uses Microsoft Foundry Local to run AI models privately on your Mac.

            Click "Install Now" to download and install it. The installer will guide \
            you through the process, then Grimly will download the AI model it needs.
            """
        alert.addButton(withTitle: "Install Now")
        alert.addButton(withTitle: "Skip")

        let response = alert.runModal()

        if response == .alertFirstButtonReturn {
            showInstallerWindow()
        }
    }

    @MainActor
    private func showInstallerWindow() {
        if let existing = installerWindow, existing.isVisible {
            existing.makeKeyAndOrderFront(nil)
            return
        }

        let installer = InstallerService()
        self.installer = installer

        let view = InstallProgressView(installer: installer) { [weak self] in
            self?.installerWindow?.close()
            self?.installerWindow = nil

            // After successful install, kick off Foundry initialization
            if installer.isComplete {
                self?.initializeFoundry()
            }
        }

        let window = NSWindow(
            contentRect: NSRect(x: 0, y: 0, width: 540, height: 460),
            styleMask: [.titled, .closable],
            backing: .buffered,
            defer: false
        )
        window.title = "Install Foundry Local"
        window.contentView = NSHostingView(rootView: view)
        window.center()
        window.isReleasedWhenClosed = false
        window.level = .floating

        installerWindow = window
        window.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)

        Task { await installer.install() }
    }

    private func showNotification(title: String, body: String) {
        let content = UNMutableNotificationContent()
        content.title = title
        content.body = body
        content.sound = .default

        let request = UNNotificationRequest(
            identifier: UUID().uuidString,
            content: content,
            trigger: nil
        )
        UNUserNotificationCenter.current().add(request) { _ in }
    }

    // MARK: - Floating Selection Icon

    private func setupSelectionWatcher() {
        selectionWatcher.delegate = self

        let settings = settingsService.load()
        NSLog("[Grimly] showFloatingIcon setting: \(settings.showFloatingIcon)")
        if settings.showFloatingIcon {
            selectionWatcher.start()
            NSLog("[Grimly] Selection watcher started")
        }
    }

    private func updateSelectionWatcher() {
        let settings = settingsService.load()
        if settings.showFloatingIcon {
            selectionWatcher.start()
        } else {
            selectionWatcher.stop()
            floatingIcon?.hideIcon()
        }
    }

    @MainActor
    private func onFloatingIconClicked() {
        let iconOrigin = floatingIcon?.iconCenter ?? NSEvent.mouseLocation
        floatingIcon?.hideIcon()

        // Use the text we already have from the AX watcher — no clipboard delay
        guard let text = pendingSelectionText,
              !text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else {
            return
        }

        let previousApp = pendingSelectionApp
        let settings = settingsService.load()

        let vm = EditorPopupViewModel(
            foundryClient: foundryClient,
            foundryManager: foundryManager,
            clipboardService: clipboardService,
            diffService: diffService
        )
        vm.selectedMode = settings.defaultMode
        vm.previousApp = previousApp
        vm.setCapturedText(text)
        vm.refreshConnectionStatus()

        let popup = EditorPopupWindow(viewModel: vm, opacity: settings.popupOpacity)
        popup.showAnimatedFrom(point: iconOrigin)
        NSApp.activate(ignoringOtherApps: true)

        editorWindow = popup
    }

    // MARK: - Services (right-click menu)

    /// Called by macOS when the user selects "Revise with Grimly" from the
    /// Services submenu in any app's right-click context menu.
    @objc func reviseText(_ pboard: NSPasteboard, userData: String, error: AutoreleasingUnsafeMutablePointer<NSString?>) {
        guard let text = pboard.string(forType: .string),
              !text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else {
            return
        }

        let previousApp = NSWorkspace.shared.frontmostApplication
        let settings = settingsService.load()

        Task { @MainActor in
            let vm = EditorPopupViewModel(
                foundryClient: foundryClient,
                foundryManager: foundryManager,
                clipboardService: clipboardService,
                diffService: diffService
            )
            vm.selectedMode = settings.defaultMode
            vm.previousApp = previousApp
            vm.setCapturedText(text)
            vm.refreshConnectionStatus()

            let popup = EditorPopupWindow(viewModel: vm, opacity: settings.popupOpacity)
            popup.showNearCursor()
            popup.makeKeyAndOrderFront(nil)
            NSApp.activate(ignoringOtherApps: true)

            editorWindow = popup
        }
    }

    // MARK: - Settings

    @MainActor
    func showSettings() {
        if let existing = settingsWindow, existing.isVisible {
            existing.makeKeyAndOrderFront(nil)
            return
        }

        let vm = SettingsViewModel(settingsService: settingsService, foundryManager: foundryManager)
        self.settingsViewModel = vm

        let settingsView = SettingsView(viewModel: vm)
        let window = NSWindow(
            contentRect: NSRect(x: 0, y: 0, width: 460, height: 600),
            styleMask: [.titled, .closable],
            backing: .buffered,
            defer: false
        )
        window.title = "Grimly Settings"
        window.contentView = NSHostingView(rootView: settingsView)
        window.center()
        window.isReleasedWhenClosed = false

        vm.onRequestClose = { [weak self, weak window] saved in
            DispatchQueue.main.async {
                window?.close()
                if saved {
                    self?.hotKeyService.unregister()
                    self?.registerHotkey()
                    self?.updateSelectionWatcher()
                }
            }
        }

        window.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)
        self.settingsWindow = window
    }
}

// MARK: - SelectionWatcherDelegate

extension AppDelegate: SelectionWatcherDelegate {
    func selectionChanged(text: String, screenPoint: NSPoint, app: NSRunningApplication) {
        pendingSelectionApp = app
        pendingSelectionText = text

        if floatingIcon == nil {
            let icon = FloatingIconWindow()
            icon.onClick = { [weak self] in
                Task { @MainActor in
                    self?.onFloatingIconClicked()
                }
            }
            floatingIcon = icon
        }

        floatingIcon?.showNear(screenPoint: screenPoint)
    }

    func selectionCleared() {
        floatingIcon?.hideIcon()
        pendingSelectionApp = nil
        pendingSelectionText = nil
    }
}

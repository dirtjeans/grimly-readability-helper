import SwiftUI

@main
struct GrimlyApp: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) var appDelegate

    var body: some Scene {
        MenuBarExtra("Grimly", image: "MenuBarIcon") {
            MenuBarView(
                onSettings: { appDelegate.showSettings() },
                onQuit: { NSApplication.shared.terminate(nil) }
            )
        }

        Settings {
            if let vm = appDelegate.settingsViewModel {
                SettingsView(viewModel: vm)
            } else {
                Text("Loading...")
            }
        }
    }
}

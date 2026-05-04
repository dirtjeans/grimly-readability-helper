import SwiftUI

struct MenuBarView: View {
    let onSettings: () -> Void
    let onQuit: () -> Void

    /// User-facing app name — pulled from CFBundleDisplayName (falls back to
    /// CFBundleName, then "Grimly"). Reads the bundle name so forks that
    /// rebrand only need to change the Info.plist.
    private var appName: String {
        (Bundle.main.object(forInfoDictionaryKey: "CFBundleDisplayName") as? String)
            ?? (Bundle.main.object(forInfoDictionaryKey: "CFBundleName") as? String)
            ?? "Grimly"
    }

    var body: some View {
        VStack(spacing: 4) {
            Button("Settings...") {
                onSettings()
            }
            .keyboardShortcut(",", modifiers: .command)
            Divider()
            Button("Quit \(appName)") {
                onQuit()
            }
            .keyboardShortcut("q", modifiers: .command)
        }
        .padding(4)
    }
}

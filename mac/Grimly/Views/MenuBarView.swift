import SwiftUI
import AppKit

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

    /// Version string sourced from Info.plist. Kept dynamic so the About
    /// panel doesn't require a code edit at each release bump.
    private var appVersion: String {
        (Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String) ?? ""
    }

    var body: some View {
        VStack(spacing: 4) {
            Button("Settings...") {
                onSettings()
            }
            .keyboardShortcut(",", modifiers: .command)
            Button("About \(appName)") {
                showAboutPanel()
            }
            Divider()
            Button("Quit \(appName)") {
                onQuit()
            }
            .keyboardShortcut("q", modifiers: .command)
        }
        .padding(4)
    }

    /// Show the standard macOS About panel with version + build date.
    /// Uses `orderFrontStandardAboutPanel` so the panel matches every other
    /// Cocoa app (Cmd-clickable version, correct app icon, etc.) rather
    /// than rolling our own.
    private func showAboutPanel() {
        // Activate first so the panel actually appears in front of whatever
        // app currently owns the front — MenuBarExtra runs us as a UI-element
        // app which doesn't naturally take focus.
        NSApp.activate(ignoringOtherApps: true)

        let build = buildDateString()
        let credits = NSAttributedString(
            string: "Version \(appVersion) — built \(build)\n" +
                    "Local-LLM writing assistant. Runs on Microsoft Foundry Local.",
            attributes: [.font: NSFont.systemFont(ofSize: NSFont.smallSystemFontSize)]
        )
        NSApp.orderFrontStandardAboutPanel(options: [
            .credits: credits,
        ])
    }

    /// Read the app bundle's own modification date as a stand-in for
    /// "build date". Baked into the executable timestamp at codesign time.
    private func buildDateString() -> String {
        guard let bundleURL = Bundle.main.executableURL,
              let attrs = try? FileManager.default.attributesOfItem(atPath: bundleURL.path),
              let date = attrs[.modificationDate] as? Date else {
            return "unknown"
        }
        let formatter = DateFormatter()
        formatter.dateStyle = .medium
        formatter.timeStyle = .none
        return formatter.string(from: date)
    }
}

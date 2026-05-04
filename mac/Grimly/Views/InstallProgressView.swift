import SwiftUI

struct InstallProgressView: View {
    @ObservedObject var installer: InstallerService
    let onClose: () -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            // Header
            HStack {
                Image(systemName: installer.isComplete
                      ? "checkmark.circle.fill"
                      : (installer.errorMessage != nil ? "exclamationmark.triangle.fill" : "arrow.down.circle"))
                    .font(.system(size: 28))
                    .foregroundColor(headerColor)
                VStack(alignment: .leading, spacing: 2) {
                    Text(headerTitle)
                        .font(.system(size: 16, weight: .semibold))
                        .foregroundColor(.white)
                    Text(headerSubtitle)
                        .font(.system(size: 12))
                        .foregroundColor(Color(white: 0.65))
                }
                Spacer()
            }
            .padding(.bottom, 12)

            // Progress bar
            if !installer.isComplete && installer.errorMessage == nil {
                VStack(alignment: .leading, spacing: 6) {
                    HStack {
                        Text("Step \(installer.currentStepIndex) of \(installer.totalSteps)")
                            .font(.system(size: 11))
                            .foregroundColor(Color(white: 0.6))
                        Spacer()
                    }
                    ProgressView(value: progressValue)
                        .progressViewStyle(.linear)
                        .tint(Color(red: 0.30, green: 0.85, blue: 0.30))

                    // Stall warning — reassures the user that a long-running step
                    // hasn't frozen when there's no visible output.
                    if let warning = installer.stallWarning {
                        HStack(spacing: 6) {
                            Image(systemName: "clock")
                                .font(.system(size: 10))
                            Text(warning)
                                .font(.system(size: 10))
                        }
                        .foregroundColor(Color(red: 0.95, green: 0.80, blue: 0.20))
                        .padding(.top, 2)
                    }
                }
                .padding(.bottom, 12)
            }

            // Log output
            ScrollViewReader { proxy in
                ScrollView {
                    LazyVStack(alignment: .leading, spacing: 1) {
                        ForEach(Array(installer.logLines.enumerated()), id: \.offset) { idx, line in
                            Text(line)
                                .font(.system(size: 11, design: .monospaced))
                                .foregroundColor(lineColor(line))
                                .frame(maxWidth: .infinity, alignment: .leading)
                                .id(idx)
                        }
                        // Anchor at the bottom for auto-scroll
                        Color.clear.frame(height: 1).id("bottom")
                    }
                    .padding(8)
                }
                .frame(height: 220)
                .background(Color.black.opacity(0.4))
                .cornerRadius(6)
                .onChange(of: installer.logLines.count) { _ in
                    withAnimation(.easeOut(duration: 0.1)) {
                        proxy.scrollTo("bottom", anchor: .bottom)
                    }
                }
            }

            // Error details
            if let error = installer.errorMessage {
                Text(error)
                    .font(.system(size: 11))
                    .foregroundColor(Color(red: 1, green: 0.5, blue: 0.5))
                    .padding(.top, 8)
                    .fixedSize(horizontal: false, vertical: true)
            }

            // Buttons
            HStack {
                Spacer()
                if installer.isRunning {
                    Button("Cancel") {
                        installer.cancel()
                    }
                    .keyboardShortcut(.cancelAction)
                } else {
                    if installer.errorMessage != nil {
                        Button("Retry") {
                            Task { await installer.install() }
                        }
                    }
                    Button(installer.isComplete ? "Done" : "Close") {
                        onClose()
                    }
                    .keyboardShortcut(.defaultAction)
                }
            }
            .padding(.top, 12)
        }
        .padding(20)
        .frame(width: 540)
        .background(Color(white: 0.12))
    }

    private var headerColor: Color {
        if installer.isComplete { return Color(red: 0.30, green: 0.85, blue: 0.30) }
        if installer.errorMessage != nil { return Color(red: 0.95, green: 0.55, blue: 0.20) }
        return Color(red: 0.45, green: 0.70, blue: 1.0)
    }

    private var headerTitle: String {
        if installer.isComplete { return "Foundry Local installed" }
        if installer.errorMessage != nil { return "Installation failed" }
        return "Installing Foundry Local…"
    }

    private var headerSubtitle: String {
        if installer.isComplete { return "Grimly is ready to use." }
        if installer.errorMessage != nil { return "See details below. You can retry or close." }
        return installer.stepName
    }

    private var progressValue: Double {
        guard installer.totalSteps > 0 else { return 0 }
        // Show completed-step progress; current running step adds 0.5 of its slice
        let base = Double(max(0, installer.currentStepIndex - 1)) / Double(installer.totalSteps)
        let currentSlice = installer.isRunning ? (0.5 / Double(installer.totalSteps)) : 0
        return base + currentSlice
    }

    private func lineColor(_ line: String) -> Color {
        if line.hasPrefix("✓") { return Color(red: 0.5, green: 0.85, blue: 0.5) }
        if line.hasPrefix("✗") { return Color(red: 1, green: 0.5, blue: 0.5) }
        if line.hasPrefix("⚠") { return Color(red: 0.95, green: 0.80, blue: 0.20) }
        if line.hasPrefix("▸") || line.hasPrefix("🎉") { return Color(red: 0.45, green: 0.70, blue: 1.0) }
        return Color(white: 0.78)
    }
}

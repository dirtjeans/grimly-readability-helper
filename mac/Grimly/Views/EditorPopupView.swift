import SwiftUI

struct EditorPopupView: View {
    @ObservedObject var viewModel: EditorPopupViewModel

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            // Title bar
            HStack(spacing: 10) {
                Text("Grimly")
                    .font(.system(size: 14, weight: .semibold))
                    .foregroundColor(Color(white: 0.8))
                Spacer()
                // Gear → Settings. The menu-bar route was too buried; this
                // opens Settings directly from the popup.
                Button(action: { viewModel.onOpenSettings?() }) {
                    Image(systemName: "gearshape.fill")
                        .font(.system(size: 13))
                        .foregroundColor(Color(white: 0.4))
                }
                .buttonStyle(ExpressiveButtonStyle())
                .help("Settings")
                Button(action: { viewModel.dismiss() }) {
                    Image(systemName: "xmark.circle.fill")
                        .font(.system(size: 14))
                        .foregroundColor(Color(white: 0.4))
                }
                .buttonStyle(ExpressiveButtonStyle())
                .help("Close (Esc)")
            }
            .padding(.bottom, 8)

            // Hint text
            if !viewModel.hasResult && !viewModel.isLoading {
                Text("Select text in any app, then click a button below to revise it.")
                    .font(.system(size: 12))
                    .italic()
                    .foregroundColor(Color(white: 0.4))
                    .padding(.bottom, 8)
            }

            // Mode selector
            FlowLayout(spacing: 6) {
                ForEach(EditingMode.uiOrder) { mode in
                    ModePillButton(
                        mode: mode,
                        isApplied: viewModel.isModeApplied(mode),
                        action: {
                            viewModel.selectedMode = mode
                            viewModel.process()
                        }
                    )
                }
            }
            .padding(.bottom, 8)

            // AP Style — runs the deterministic code pass + narrow LLM pass.
            // Orange outline signals it's secondary to the mode pills above,
            // but not tertiary either — it's a full-width action.
            Button(action: { viewModel.runApStyle() }) {
                Text("AP Style")
                    .font(.system(size: 13, weight: .semibold))
                    .foregroundColor(Color(red: 0.90, green: 0.50, blue: 0.20))
                    .frame(maxWidth: .infinity)
                    .padding(.vertical, 6)
                    .overlay(
                        RoundedRectangle(cornerRadius: 6)
                            .stroke(Color(red: 0.90, green: 0.50, blue: 0.20), lineWidth: 1.5)
                    )
            }
            .buttonStyle(ExpressiveButtonStyle())
            .padding(.bottom, 8)
            .disabled(viewModel.isLoading || viewModel.workingText.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty)
            .help("Rewrite this text in AP Stylebook style")

            // Custom prompt input
            if viewModel.isCustomMode {
                TextField("Enter your custom instruction", text: $viewModel.customPrompt)
                    .textFieldStyle(.plain)
                    .font(.system(size: 12))
                    .padding(8)
                    .background(Color.white.opacity(0.1))
                    .cornerRadius(6)
                    .foregroundColor(.white)
                    .padding(.bottom, 8)
            }

            // Text label + Undo
            HStack {
                Text("Text:")
                    .font(.system(size: 11))
                    .foregroundColor(Color(white: 0.53))
                Spacer()
                if viewModel.canUndo {
                    Button("Undo") {
                        viewModel.undo()
                    }
                    .font(.system(size: 11))
                    .padding(.horizontal, 10)
                    .padding(.vertical, 3)
                    .background(Color(red: 0.42, green: 0.19, blue: 0.19))
                    .foregroundColor(Color(white: 0.87))
                    .cornerRadius(6)
                    .buttonStyle(ExpressiveButtonStyle())
                }
            }
            .padding(.bottom, 4)

            // Working text (hidden during review). While the LLM is running
            // an animated red→purple→blue glow border pulses around the
            // editor — a livelier signal than the old "Revising..." bar and
            // one that stays visually anchored to what's being processed.
            if !viewModel.isReviewing {
                TextEditor(text: $viewModel.workingText)
                    .font(.system(size: 13))
                    .foregroundColor(Color(white: 0.93))
                    .scrollContentBackground(.hidden)
                    .background(Color.white.opacity(0.06))
                    .cornerRadius(6)
                    // Grow with the popup window — the resizable window's
                    // extra vertical space flows down into whichever of the
                    // TextEditor / DiffReview is currently visible.
                    .frame(minHeight: 60, maxHeight: .infinity)
                    .overlay(
                        Group {
                            if viewModel.isLoading {
                                LLMGlowBorder()
                            }
                        }
                    )
                    .padding(.bottom, 8)
            }

            // Status indicators: connection (left) + readability (right)
            HStack {
                // Connection LED
                HStack(spacing: 5) {
                    Circle()
                        .fill(viewModel.connectionStatus.color)
                        .frame(width: 8, height: 8)
                    Text(viewModel.connectionStatus.label)
                        .font(.system(size: 10))
                        .foregroundColor(Color(white: 0.47))
                }
                .onTapGesture { viewModel.refreshConnectionStatus() }
                .help("Click to re-check connection")

                Spacer()

                // Readability score
                if !viewModel.readabilityLabel.isEmpty {
                    HStack(spacing: 5) {
                        Circle()
                            .fill(readabilityColor(viewModel.readabilityScore))
                            .frame(width: 8, height: 8)
                        Text(viewModel.readabilityLabel)
                            .font(.system(size: 10))
                            .foregroundColor(Color(white: 0.47))
                    }
                }

                // Case menu — deterministic recasing, result shows as a
                // reviewable diff (same UX as the LLM modes).
                Menu {
                    Button("AP title case")      { viewModel.applyCase(.apTitle) }
                    Button("Chicago title case") { viewModel.applyCase(.chicagoTitle) }
                } label: {
                    Text("Case ▾")
                        .font(.system(size: 10))
                        .padding(.horizontal, 8)
                        .padding(.vertical, 3)
                        .foregroundColor(Color(white: 0.85))
                }
                .menuStyle(.borderlessButton)
                .fixedSize()
                .help("Reformat capitalization (AP or Chicago title case)")
            }
            .padding(.bottom, 6)

            // (Loading indicator moved to the animated glow border overlaid
            // on the working-text TextEditor above — a livelier visual than
            // the old "Revising..." bar.)

            // Review header
            if viewModel.isReviewing {
                HStack {
                    Text("Review changes (click to toggle):")
                        .font(.system(size: 11))
                        .foregroundColor(Color(white: 0.53))
                    Spacer()
                    Button("Accept All") {
                        viewModel.acceptAllChanges()
                    }
                    .font(.system(size: 10))
                    .padding(.horizontal, 8)
                    .padding(.vertical, 3)
                    .background(Color(red: 0.18, green: 0.42, blue: 0.18))
                    .foregroundColor(Color(white: 0.87))
                    .cornerRadius(6)
                    .buttonStyle(ExpressiveButtonStyle())

                    Button("Reject All") {
                        viewModel.rejectAllChanges()
                    }
                    .font(.system(size: 10))
                    .padding(.horizontal, 8)
                    .padding(.vertical, 3)
                    .background(Color(red: 0.42, green: 0.19, blue: 0.19))
                    .foregroundColor(Color(white: 0.87))
                    .cornerRadius(6)
                    .buttonStyle(ExpressiveButtonStyle())
                }
                .padding(.bottom, 4)

                // Diff review area
                DiffReviewView(
                    segments: viewModel.reviewSegments,
                    onToggle: { segId in
                        viewModel.toggleChange(segId)
                    }
                )
                // Same expandable-height rule as the TextEditor above so
                // the diff area also grows when the window is resized.
                .frame(minHeight: 80, maxHeight: .infinity)
                .cornerRadius(6)
                .padding(.bottom, 8)
            }

            // Live grammar / spelling / punctuation panel
            if viewModel.hasViolations {
                ViolationsPanel(
                    violations: viewModel.violations,
                    hasAutoFixable: viewModel.hasAutoFixableViolations,
                    hint: viewModel.quickFixHint,
                    onQuickFix: { viewModel.applyQuickFixes() }
                )
                .padding(.bottom, 8)
            }

            // Error message. When the app is actively reconnecting to
            // Foundry (either the monitor's background loop or a mid-request
            // recovery), use a muted amber tone instead of red — "the app
            // is handling this" reads better than "something is broken."
            if let error = viewModel.errorMessage {
                Text(error)
                    .font(.system(size: 12))
                    .foregroundColor(
                        viewModel.isReconnecting
                            ? Color(red: 0.9, green: 0.7, blue: 0.2)
                            : Color(red: 1, green: 0.4, blue: 0.4)
                    )
                    .padding(.bottom, 8)
            }

            // Action buttons
            HStack {
                Spacer()
                Button("Paste") {
                    viewModel.accept()
                }
                .font(.system(size: 13))
                .padding(.horizontal, 16)
                .padding(.vertical, 8)
                .background(Color(red: 0.18, green: 0.49, blue: 0.18))
                .foregroundColor(.white)
                .cornerRadius(6)
                .buttonStyle(ExpressiveButtonStyle())

                Button("Copy") {
                    viewModel.copyResult()
                }
                .font(.system(size: 13))
                .padding(.horizontal, 16)
                .padding(.vertical, 8)
                .background(Color(white: 0.33))
                .foregroundColor(.white)
                .cornerRadius(6)
                .buttonStyle(ExpressiveButtonStyle())

                Button("Dismiss") {
                    viewModel.dismiss()
                }
                .font(.system(size: 13))
                .padding(.horizontal, 16)
                .padding(.vertical, 8)
                .background(Color(white: 0.27))
                .foregroundColor(Color(white: 0.8))
                .cornerRadius(6)
                .buttonStyle(ExpressiveButtonStyle())
            }
            .padding(.top, 4)
        }
        .padding(16)
        .background(
            RoundedRectangle(cornerRadius: 12)
                .fill(.ultraThinMaterial)
                .overlay(
                    RoundedRectangle(cornerRadius: 12)
                        .fill(Color(white: 0.12, opacity: 0.94))
                )
                .overlay(
                    RoundedRectangle(cornerRadius: 12)
                        .strokeBorder(Color.white.opacity(0.15), lineWidth: 1)
                )
        )
        // Fixed intrinsic width, flexible width once the window is dragged
        // wider. Height fills whatever the window offers.
        .frame(minWidth: 480, idealWidth: 620, maxWidth: .infinity,
               minHeight: 400, maxHeight: .infinity)
    }

    private func readabilityColor(_ score: Double) -> Color {
        switch score {
        case 60...: return Color(red: 0.31, green: 0.82, blue: 0.35)      // bright green
        case 50..<60: return Color(red: 0.55, green: 0.78, blue: 0.31)    // yellow-green
        case 40..<50: return Color(red: 0.71, green: 0.75, blue: 0.27)    // green-yellow
        case 30..<40: return Color(red: 0.86, green: 0.71, blue: 0.20)    // yellow
        case 20..<30: return Color(red: 0.90, green: 0.55, blue: 0.20)    // orange
        default: return Color(red: 0.86, green: 0.31, blue: 0.31)         // red
        }
    }
}

// MARK: - Connection Status Indicator

struct ConnectionStatusIndicator: View {
    let status: ConnectionStatus

    private var color: Color {
        switch status {
        case .checking: return Color(white: 0.55)
        case .connected: return Color(red: 0.30, green: 0.85, blue: 0.30)
        case .modelNotLoaded: return Color(red: 0.95, green: 0.80, blue: 0.20)
        case .foundryNotRunning: return Color(red: 0.95, green: 0.30, blue: 0.30)
        case .foundryNotInstalled: return Color(red: 0.75, green: 0.15, blue: 0.15)
        }
    }

    var body: some View {
        HStack(spacing: 6) {
            Circle()
                .fill(color)
                .frame(width: 8, height: 8)
                .shadow(color: color.opacity(0.6), radius: 2)
            Text(status.label)
                .font(.system(size: 10))
                .foregroundColor(Color(white: 0.65))
        }
        .help(status.label)
    }
}

// MARK: - FlowLayout for mode pills

struct FlowLayout: Layout {
    var spacing: CGFloat = 6

    func sizeThatFits(proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) -> CGSize {
        let result = arrangeSubviews(proposal: proposal, subviews: subviews)
        return result.size
    }

    func placeSubviews(in bounds: CGRect, proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) {
        let result = arrangeSubviews(proposal: proposal, subviews: subviews)
        for (index, position) in result.positions.enumerated() {
            subviews[index].place(
                at: CGPoint(x: bounds.minX + position.x, y: bounds.minY + position.y),
                proposal: .unspecified
            )
        }
    }

    private func arrangeSubviews(proposal: ProposedViewSize, subviews: Subviews) -> (positions: [CGPoint], size: CGSize) {
        let maxWidth = proposal.width ?? .infinity
        var positions: [CGPoint] = []
        var x: CGFloat = 0
        var y: CGFloat = 0
        var rowHeight: CGFloat = 0
        var totalHeight: CGFloat = 0

        for subview in subviews {
            let size = subview.sizeThatFits(.unspecified)
            if x + size.width > maxWidth && x > 0 {
                x = 0
                y += rowHeight + spacing
                rowHeight = 0
            }
            positions.append(CGPoint(x: x, y: y))
            rowHeight = max(rowHeight, size.height)
            x += size.width + spacing
            totalHeight = y + rowHeight
        }

        return (positions, CGSize(width: maxWidth, height: totalHeight))
    }
}

// MARK: - LLM activity glow

/// Rotating red→purple→blue gradient border overlay. Signals that an LLM
/// call is in flight — a livelier visual than the old "Revising…" progress
/// bar and it stays visually anchored to the text being processed.
///
/// Driven by `TimelineView(.animation)` rather than a `withAnimation` +
/// `.onAppear` + `.repeatForever` chain. That chain is unreliable on macOS
/// when the view lands inside a `Group { if condition { … } }` overlay:
/// `.onAppear` doesn't consistently fire when the overlay flips in via the
/// conditional. `TimelineView` sidesteps the whole lifecycle question by
/// updating the view every frame from wall-clock time.
struct LLMGlowBorder: View {
    var body: some View {
        TimelineView(.animation) { context in
            let t = context.date.timeIntervalSinceReferenceDate
            let angle = t.truncatingRemainder(dividingBy: 1.5) / 1.5 * 360
            let opacity = 0.55 + 0.45 * (0.5 + 0.5 * sin(t / 0.9 * .pi))
            RoundedRectangle(cornerRadius: 6)
                .stroke(
                    AngularGradient(
                        gradient: Gradient(colors: [
                            Color(red: 0.23, green: 0.51, blue: 0.96), // #3B82F6 blue
                            Color(red: 0.54, green: 0.36, blue: 0.96), // #8B5CF6 purple
                            Color(red: 0.94, green: 0.27, blue: 0.27), // #EF4444 red
                            Color(red: 0.23, green: 0.51, blue: 0.96),
                        ]),
                        center: .center,
                        angle: .degrees(angle)
                    ),
                    lineWidth: 4
                )
                .padding(-3)
                .opacity(opacity)
                .shadow(color: Color(red: 0.54, green: 0.36, blue: 0.96).opacity(0.7), radius: 12)
                .allowsHitTesting(false)
        }
    }
}

// MARK: - Violations panel

/// Displayed below the working-text area when the live grammar checker
/// finds anything. Shows each violation as a labeled chip + quote +
/// explanation, with an italic hint pointing at the AI Fix Grammar button
/// for items the deterministic checker can't auto-fix. The Quick Fix
/// button only appears when at least one violation has a deterministic fix.
struct ViolationsPanel: View {
    let violations: [Violation]
    let hasAutoFixable: Bool
    let hint: String
    let onQuickFix: () -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack(spacing: 8) {
                Text("Style check")
                    .font(.system(size: 11, weight: .semibold))
                    .foregroundColor(.primary)
                Spacer()
                if !violations.isEmpty {
                    Text("\(violations.count)")
                        .font(.system(size: 10, weight: .semibold))
                        .foregroundColor(.white)
                        .padding(.horizontal, 6)
                        .padding(.vertical, 1)
                        .background(Color(red: 0.86, green: 0.42, blue: 0.20))
                        .cornerRadius(8)
                }
                if hasAutoFixable {
                    Button("Quick Fix", action: onQuickFix)
                        .font(.system(size: 11, weight: .semibold))
                        .padding(.horizontal, 10)
                        .padding(.vertical, 3)
                        .background(Color(white: 0.30))
                        .foregroundColor(.white)
                        .cornerRadius(4)
                        .buttonStyle(ExpressiveButtonStyle())
                        .help("Apply all mechanical fixes — review with accept/reject")
                }
            }

            Text(hint)
                .font(.system(size: 10))
                .italic()
                .foregroundColor(.secondary)
                .fixedSize(horizontal: false, vertical: true)

            ScrollView {
                VStack(alignment: .leading, spacing: 6) {
                    ForEach(violations) { v in
                        ViolationRow(violation: v)
                    }
                }
                .padding(.vertical, 4)
            }
            .frame(maxHeight: 160)
        }
        .padding(10)
        .background(Color(white: 0.18))
        .overlay(RoundedRectangle(cornerRadius: 6).stroke(Color(white: 0.30), lineWidth: 1))
        .cornerRadius(6)
    }
}

struct ViolationRow: View {
    let violation: Violation

    var body: some View {
        HStack(alignment: .top, spacing: 8) {
            Text(violation.category)
                .font(.system(size: 10, weight: .semibold))
                .foregroundColor(.white)
                .padding(.horizontal, 6)
                .padding(.vertical, 2)
                .background(Color(red: 0.86, green: 0.42, blue: 0.20))
                .cornerRadius(4)
                .fixedSize()

            VStack(alignment: .leading, spacing: 2) {
                if !violation.quote.isEmpty {
                    Text("\u{201C}\(violation.quote)\u{201D}")
                        .font(.system(size: 11))
                        .italic()
                        .foregroundColor(.primary)
                }
                Text(violation.explanation)
                    .font(.system(size: 11))
                    .foregroundColor(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
            }
            Spacer(minLength: 0)
        }
    }
}

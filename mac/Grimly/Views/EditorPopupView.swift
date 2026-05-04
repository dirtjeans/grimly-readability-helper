import SwiftUI

struct EditorPopupView: View {
    @ObservedObject var viewModel: EditorPopupViewModel

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            // Title bar
            HStack {
                Text("Grimly")
                    .font(.system(size: 14, weight: .semibold))
                    .foregroundColor(Color(white: 0.8))
                Spacer()
                Button(action: { viewModel.dismiss() }) {
                    Image(systemName: "xmark.circle.fill")
                        .font(.system(size: 14))
                        .foregroundColor(Color(white: 0.4))
                }
                .buttonStyle(.plain)
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
                    .buttonStyle(.plain)
                }
            }
            .padding(.bottom, 4)

            // Working text (hidden during review)
            if !viewModel.isReviewing {
                TextEditor(text: $viewModel.workingText)
                    .font(.system(size: 13))
                    .foregroundColor(Color(white: 0.93))
                    .scrollContentBackground(.hidden)
                    .background(Color.white.opacity(0.06))
                    .cornerRadius(6)
                    .frame(minHeight: 60, maxHeight: 180)
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
            }
            .padding(.bottom, 6)

            // Loading indicator
            if viewModel.isLoading {
                HStack(spacing: 10) {
                    ProgressView()
                        .scaleEffect(0.9)
                        .colorInvert()
                        .brightness(0.4)
                    Text("Revising...")
                        .font(.system(size: 13, weight: .medium))
                        .foregroundColor(Color(white: 0.85))
                }
                .padding(.vertical, 10)
                .padding(.horizontal, 14)
                .background(Color.white.opacity(0.08))
                .cornerRadius(8)
                .padding(.bottom, 8)
            }

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
                    .buttonStyle(.plain)

                    Button("Reject All") {
                        viewModel.rejectAllChanges()
                    }
                    .font(.system(size: 10))
                    .padding(.horizontal, 8)
                    .padding(.vertical, 3)
                    .background(Color(red: 0.42, green: 0.19, blue: 0.19))
                    .foregroundColor(Color(white: 0.87))
                    .cornerRadius(6)
                    .buttonStyle(.plain)
                }
                .padding(.bottom, 4)

                // Diff review area
                DiffReviewView(
                    segments: viewModel.reviewSegments,
                    onToggle: { segId in
                        viewModel.toggleChange(segId)
                    }
                )
                .frame(minHeight: 80, maxHeight: 250)
                .cornerRadius(6)
                .padding(.bottom, 8)
            }

            // Error message
            if let error = viewModel.errorMessage {
                Text(error)
                    .font(.system(size: 12))
                    .foregroundColor(Color(red: 1, green: 0.4, blue: 0.4))
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
                .buttonStyle(.plain)

                Button("Copy") {
                    viewModel.copyResult()
                }
                .font(.system(size: 13))
                .padding(.horizontal, 16)
                .padding(.vertical, 8)
                .background(Color(white: 0.33))
                .foregroundColor(.white)
                .cornerRadius(6)
                .buttonStyle(.plain)

                Button("Dismiss") {
                    viewModel.dismiss()
                }
                .font(.system(size: 13))
                .padding(.horizontal, 16)
                .padding(.vertical, 8)
                .background(Color(white: 0.27))
                .foregroundColor(Color(white: 0.8))
                .cornerRadius(6)
                .buttonStyle(.plain)
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
        .frame(width: 620)
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

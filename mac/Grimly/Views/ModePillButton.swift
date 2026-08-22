import SwiftUI

struct ModePillButton: View {
    let mode: EditingMode
    let isApplied: Bool
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            HStack(spacing: 4) {
                if isApplied {
                    Text("\u{2713}")
                        .font(.system(size: 10))
                        .foregroundColor(Color(red: 0.5, green: 0.75, blue: 0.5))
                }
                Text(mode.displayName)
            }
            .font(.system(size: 12))
            .padding(.horizontal, 12)
            .padding(.vertical, 6)
            .background(
                Capsule()
                    .fill(isApplied
                          ? Color(red: 0.16, green: 0.29, blue: 0.16)
                          : Color.white.opacity(0.2))
            )
            .overlay(
                Capsule()
                    .strokeBorder(
                        isApplied
                        ? Color(red: 0.29, green: 0.54, blue: 0.29)
                        : Color.clear,
                        lineWidth: 1
                    )
            )
            .foregroundColor(isApplied
                             ? Color(red: 0.63, green: 0.85, blue: 0.63)
                             : .white)
        }
        .buttonStyle(ExpressiveButtonStyle())
    }
}

/// Material 3 Expressive-flavored micro-interaction: a 4% hover lift, a
/// press "squish" to 96%, and a springy release with a touch of overshoot.
/// Adds no chrome of its own (like `.plain`), so it's a drop-in on buttons
/// that draw their own background. Deliberately subtle — "touches of
/// delight," not full Material compliance.
struct ExpressiveButtonStyle: ButtonStyle {
    func makeBody(configuration: Configuration) -> some View {
        ExpressiveButtonBody(configuration: configuration)
    }
}

/// Body view for `ExpressiveButtonStyle`. Broken out as a top-level view
/// (not nested) so it can hold the `@State` hover flag — a ButtonStyle's
/// `makeBody` result can't itself carry state.
private struct ExpressiveButtonBody: View {
    let configuration: ButtonStyleConfiguration
    @State private var hovering = false

    var body: some View {
        configuration.label
            .scaleEffect(configuration.isPressed ? 0.96 : (hovering ? 1.04 : 1.0))
            .animation(.spring(response: 0.35, dampingFraction: 0.6), value: configuration.isPressed)
            .animation(.easeOut(duration: 0.1), value: hovering)
            .onHover { hovering = $0 }
    }
}

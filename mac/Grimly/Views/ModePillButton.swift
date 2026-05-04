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
        .buttonStyle(.plain)
    }
}

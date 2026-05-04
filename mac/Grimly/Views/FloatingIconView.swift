import SwiftUI

struct FloatingIconView: View {
    let onClick: () -> Void

    var body: some View {
        Button(action: onClick) {
            ZStack {
                Circle()
                    .fill(Color(white: 0.15, opacity: 0.92))
                    .shadow(color: .black.opacity(0.4), radius: 4, y: 2)

                Circle()
                    .strokeBorder(Color.white.opacity(0.2), lineWidth: 1)

                Image(nsImage: NSApp.applicationIconImage)
                    .resizable()
                    .scaledToFit()
                    .clipShape(Circle())
                    .padding(3)
            }
            .frame(width: 28, height: 28)
        }
        .buttonStyle(.plain)
        .contentShape(Circle())
    }
}

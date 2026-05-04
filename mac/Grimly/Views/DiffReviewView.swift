import SwiftUI
import AppKit

struct DiffReviewView: NSViewRepresentable {
    let segments: [ReviewSegment]
    let onToggle: (Int) -> Void

    func makeNSView(context: Context) -> NSScrollView {
        let scrollView = NSScrollView()
        let textView = ClickableTextView()
        textView.isEditable = false
        textView.isSelectable = true
        textView.backgroundColor = NSColor(white: 0.1, alpha: 0.4)
        textView.textContainerInset = NSSize(width: 8, height: 8)
        textView.isRichText = true
        textView.font = .systemFont(ofSize: 13)

        scrollView.documentView = textView
        scrollView.hasVerticalScroller = true
        scrollView.drawsBackground = false
        scrollView.autohidesScrollers = true

        context.coordinator.textView = textView
        textView.onSegmentClick = { [weak coordinator = context.coordinator] segId in
            coordinator?.onToggle(segId)
        }

        return scrollView
    }

    func updateNSView(_ scrollView: NSScrollView, context: Context) {
        guard let textView = scrollView.documentView as? ClickableTextView else { return }
        context.coordinator.onToggle = onToggle
        context.coordinator.rebuildAttributedString(segments: segments, in: textView)
    }

    func makeCoordinator() -> Coordinator {
        Coordinator(onToggle: onToggle)
    }

    class Coordinator {
        weak var textView: ClickableTextView?
        var onToggle: (Int) -> Void
        var segmentRanges: [(id: Int, range: NSRange)] = []

        init(onToggle: @escaping (Int) -> Void) {
            self.onToggle = onToggle
        }

        func rebuildAttributedString(segments: [ReviewSegment], in textView: ClickableTextView) {
            let attrString = NSMutableAttributedString()
            segmentRanges = []

            // Colors
            let unchangedFg = NSColor(white: 0.7, alpha: 1)

            let pendingRemovedFg = NSColor(red: 0.86, green: 0.39, blue: 0.39, alpha: 1)
            let pendingAddedFg = NSColor(red: 0.31, green: 0.86, blue: 0.31, alpha: 1)
            let pendingAddedBg = NSColor(red: 0, green: 0.7, blue: 0, alpha: 0.15)

            let acceptedFg = NSColor(red: 0.47, green: 0.78, blue: 0.47, alpha: 1)
            let acceptedBg = NSColor(red: 0, green: 0.63, blue: 0, alpha: 0.12)

            let rejectedFg = NSColor(white: 0.47, alpha: 1)
            let rejectedBg = NSColor(red: 1, green: 0, blue: 0, alpha: 0.08)

            let baseFont = NSFont.systemFont(ofSize: 13)
            let smallFont = NSFont.systemFont(ofSize: 10)

            for seg in segments {
                if !seg.isChange {
                    let run = NSAttributedString(string: seg.unchangedText, attributes: [
                        .foregroundColor: unchangedFg,
                        .font: baseFont
                    ])
                    attrString.append(run)
                } else {
                    let startLoc = attrString.length

                    switch seg.state {
                    case .pending:
                        if !seg.removedText.isEmpty {
                            attrString.append(NSAttributedString(string: seg.removedText, attributes: [
                                .foregroundColor: pendingRemovedFg,
                                .strikethroughStyle: NSUnderlineStyle.single.rawValue,
                                .strikethroughColor: pendingRemovedFg,
                                .font: baseFont,
                                .cursor: NSCursor.pointingHand
                            ]))
                        }
                        if !seg.addedText.isEmpty {
                            attrString.append(NSAttributedString(string: seg.addedText, attributes: [
                                .foregroundColor: pendingAddedFg,
                                .backgroundColor: pendingAddedBg,
                                .font: baseFont,
                                .cursor: NSCursor.pointingHand
                            ]))
                        }

                    case .accepted:
                        let acceptedText = !seg.addedText.isEmpty ? seg.addedText : ""
                        attrString.append(NSAttributedString(string: "\u{2713} ", attributes: [
                            .foregroundColor: acceptedFg,
                            .font: smallFont,
                            .cursor: NSCursor.pointingHand
                        ]))
                        attrString.append(NSAttributedString(string: acceptedText, attributes: [
                            .foregroundColor: acceptedFg,
                            .backgroundColor: acceptedBg,
                            .font: baseFont,
                            .cursor: NSCursor.pointingHand
                        ]))

                    case .rejected:
                        let rejectedText = !seg.removedText.isEmpty ? seg.removedText : ""
                        attrString.append(NSAttributedString(string: "\u{2717} ", attributes: [
                            .foregroundColor: rejectedFg,
                            .font: smallFont,
                            .cursor: NSCursor.pointingHand
                        ]))
                        attrString.append(NSAttributedString(string: rejectedText, attributes: [
                            .foregroundColor: rejectedFg,
                            .backgroundColor: rejectedBg,
                            .font: baseFont,
                            .cursor: NSCursor.pointingHand
                        ]))
                    }

                    let endLoc = attrString.length
                    segmentRanges.append((id: seg.id, range: NSRange(location: startLoc, length: endLoc - startLoc)))
                }
            }

            textView.textStorage?.setAttributedString(attrString)
            textView.segmentRanges = segmentRanges
        }
    }
}

class ClickableTextView: NSTextView {
    var segmentRanges: [(id: Int, range: NSRange)] = []
    var onSegmentClick: ((Int) -> Void)?

    private let hoverBg = NSColor(red: 0.39, green: 0.59, blue: 1.0, alpha: 0.15)

    override func mouseDown(with event: NSEvent) {
        let point = convert(event.locationInWindow, from: nil)
        let charIndex = characterIndexForInsertion(at: point)

        for (segId, range) in segmentRanges {
            if NSLocationInRange(charIndex, range) {
                onSegmentClick?(segId)
                return
            }
        }

        super.mouseDown(with: event)
    }

    override func resetCursorRects() {
        super.resetCursorRects()

        guard let layoutManager = layoutManager, let textContainer = textContainer else { return }

        for (_, range) in segmentRanges {
            let glyphRange = layoutManager.glyphRange(forCharacterRange: range, actualCharacterRange: nil)
            layoutManager.enumerateEnclosingRects(
                forGlyphRange: glyphRange,
                withinSelectedGlyphRange: NSRange(location: NSNotFound, length: 0),
                in: textContainer
            ) { rect, _ in
                let adjustedRect = NSRect(
                    x: rect.origin.x + self.textContainerInset.width,
                    y: rect.origin.y + self.textContainerInset.height,
                    width: rect.width,
                    height: rect.height
                )
                self.addCursorRect(adjustedRect, cursor: .pointingHand)
            }
        }
    }
}

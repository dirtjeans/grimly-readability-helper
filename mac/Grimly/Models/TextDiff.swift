import Foundation

enum DiffType {
    case unchanged
    case added
    case removed
}

struct TextDiff {
    let text: String
    let type: DiffType
}

enum ChangeState {
    case pending
    case accepted
    case rejected
}

class ReviewSegment: ObservableObject, Identifiable {
    let id: Int
    let isChange: Bool
    let unchangedText: String
    let removedText: String
    let addedText: String

    @Published var state: ChangeState = .pending

    init(id: Int, isChange: Bool, unchangedText: String = "", removedText: String = "", addedText: String = "") {
        self.id = id
        self.isChange = isChange
        self.unchangedText = unchangedText
        self.removedText = removedText
        self.addedText = addedText
    }

    var resolvedText: String {
        if !isChange { return unchangedText }
        switch state {
        case .accepted: return addedText
        case .rejected: return removedText
        case .pending: return addedText
        }
    }

    func toggle() {
        switch state {
        case .pending: state = .accepted
        case .accepted: state = .rejected
        case .rejected: state = .pending
        }
    }
}

import SwiftUI
import AppKit
import Combine

enum ConnectionStatus {
    case checking
    case connected
    case modelNotLoaded
    case foundryNotRunning
    case foundryNotInstalled

    var label: String {
        switch self {
        case .checking: return "Checking local LLM"
        case .connected: return "Connected to local LLM"
        case .modelNotLoaded: return "Not connected to local LLM (model not loaded)"
        case .foundryNotRunning: return "Not connected to local LLM (Foundry not running)"
        case .foundryNotInstalled: return "Not connected to local LLM (Foundry not installed)"
        }
    }

    var color: Color {
        switch self {
        case .checking: return Color(white: 0.5)
        case .connected: return Color(red: 0.31, green: 0.82, blue: 0.35)
        case .modelNotLoaded: return Color(red: 0.9, green: 0.7, blue: 0.2)
        case .foundryNotRunning: return Color(red: 0.86, green: 0.31, blue: 0.31)
        case .foundryNotInstalled: return Color(red: 0.63, green: 0.2, blue: 0.2)
        }
    }
}

@MainActor
class EditorPopupViewModel: ObservableObject {
    private let foundryClient: FoundryLocalClient
    private let foundryManager: FoundryManager
    private let clipboardService: ClipboardService
    private let diffService: TextDiffService
    private let properNouns = ProperNounService()
    private let spellChecker = SpellCheckerService()
    private lazy var codeChecker = GrammarChecker(spellChecker: spellChecker, properNouns: properNouns)
    private let readabilityService = ReadabilityService()
    private var currentTask: Task<Void, Never>?
    private var backgroundReconnectTask: Task<Void, Never>?
    private var undoStack: [String] = []
    private var preRevisionText: String = ""
    private var cancellables = Set<AnyCancellable>()

    @Published var workingText: String = ""
    @Published var reviewSegments: [ReviewSegment] = []
    @Published var selectedMode: EditingMode = .fixGrammar
    @Published var customPrompt: String = ""
    @Published var isLoading: Bool = false
    @Published var errorMessage: String?
    @Published var hasResult: Bool = false
    @Published var canUndo: Bool = false
    @Published var isReviewing: Bool = false
    @Published var appliedModes: Set<EditingMode> = []
    @Published var connectionStatus: ConnectionStatus = .checking
    @Published var readabilityScore: Double = 0
    @Published var readabilityLabel: String = ""
    @Published var wordCount: Int = 0
    @Published var charCount: Int = 0

    /// Live deterministic violations (grammar, spelling, punctuation, …).
    /// Re-populated ~400 ms after `workingText` last changed.
    @Published var violations: [Violation] = []
    var hasViolations: Bool { !violations.isEmpty }
    /// True when at least one violation has a deterministic auto-fix —
    /// drives Quick Fix button visibility.
    var hasAutoFixableViolations: Bool { violations.contains(where: { $0.canAutoFix }) }

    /// Hint text shown above the violations list.
    let quickFixHint = "Click Quick Fix for the mechanical corrections. Use Fix Grammar for AI-assisted revisions of the rest."

    var previousApp: NSRunningApplication?
    var onRequestClose: (() -> Void)?
    var onReviewSegmentsChanged: (() -> Void)?

    var isCustomMode: Bool { selectedMode == .customPrompt }

    init(foundryClient: FoundryLocalClient, foundryManager: FoundryManager, clipboardService: ClipboardService, diffService: TextDiffService) {
        self.foundryClient = foundryClient
        self.foundryManager = foundryManager
        self.clipboardService = clipboardService
        self.diffService = diffService

        // Update readability score whenever working text changes
        $workingText
            .debounce(for: .milliseconds(200), scheduler: RunLoop.main)
            .sink { [weak self] _ in self?.updateReadability() }
            .store(in: &cancellables)

        // Live deterministic grammar / spelling / punctuation check —
        // independent debounce so the panel updates ~400 ms after the user
        // stops typing. Mirrors the Windows VM. The checker runs on the
        // main actor; for our text volumes (a few thousand words at most)
        // the regex sweep is well under a frame budget.
        $workingText
            .debounce(for: .milliseconds(400), scheduler: RunLoop.main)
            .sink { [weak self] text in self?.runLiveCheck(on: text) }
            .store(in: &cancellables)

        // Silent background reconnect. Whenever the connection status flips
        // to a probably-transient failure (Foundry Local restarted onto a
        // new port, model briefly unloaded), spin up a loop that retries in
        // the background so the user doesn't have to click Retry manually.
        // .foundryNotInstalled is fatal and skipped.
        $connectionStatus
            .removeDuplicates()
            .sink { [weak self] status in
                guard let self else { return }
                if status == .connected {
                    self.backgroundReconnectTask?.cancel()
                    self.backgroundReconnectTask = nil
                } else if status == .modelNotLoaded || status == .foundryNotRunning {
                    self.startBackgroundReconnect()
                }
            }
            .store(in: &cancellables)
    }

    /// Background reconnect loop. Alternates cheap `checkConnection` probes
    /// (in case Foundry's port stayed the same) with heavier `ensureRunning`
    /// calls that can restart the service and re-parse the endpoint. Silent
    /// throughout — no toast, no status flicker; on success it quietly
    /// flips ConnectionStatus back to `.connected` and clears any banner.
    private func startBackgroundReconnect() {
        // Loop already running — don't stack another one.
        if let task = backgroundReconnectTask, !task.isCancelled { return }

        backgroundReconnectTask = Task { [weak self] in
            var attempt = 0
            while true {
                guard let self else { return }
                if Task.isCancelled { return }

                // 3s → 5s → 7s → …, capped at 15s.
                let delayNs = UInt64(min(3_000 + attempt * 2_000, 15_000)) * 1_000_000
                do { try await Task.sleep(nanoseconds: delayNs) } catch { return }
                if Task.isCancelled { return }

                var recovered = false
                let cheapStatus = await self.foundryManager.checkConnection()
                if cheapStatus == .connected {
                    recovered = true
                } else if attempt >= 2 && attempt % 3 == 0 {
                    // Every ~10–30s in, pay for a full ensureRunning to catch
                    // the "Foundry moved to a new port" case.
                    let result = await self.foundryManager.ensureRunning()
                    recovered = result.success
                }

                if recovered {
                    if Task.isCancelled { return }
                    self.connectionStatus = .connected
                    if self.errorMessage == "Cannot connect to local LLM." {
                        self.errorMessage = nil
                    }
                    return
                }
                attempt += 1
            }
        }
    }

    private func runLiveCheck(on text: String) {
        guard !text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else {
            violations = []
            return
        }
        violations = codeChecker.check(text)
    }

    /// Re-case the working text and show the result as a reviewable diff.
    /// Deterministic — no LLM round-trip. Used by the "Case" dropdown for
    /// AP title case, Chicago title case, and sentence case.
    func applyCase(_ style: CaseStyle) {
        let rewritten = CaseFormatter.apply(workingText, style: style)
        guard rewritten != workingText else { return }

        preRevisionText = workingText
        let diffs = diffService.computeDiff(original: preRevisionText, corrected: rewritten)
        let segments = diffService.groupIntoSegments(diffs)
        reviewSegments = segments

        if segments.contains(where: { $0.isChange }) {
            undoStack.append(preRevisionText)
            canUndo = true
            isReviewing = true
            hasResult = true
            rebuildWorkingText()
            onReviewSegmentsChanged?()
        }
    }

    /// Quick Fix — apply every deterministic fix the live checker found, as
    /// a single reviewable diff. Mirrors the accept/reject UX used by Fix
    /// Grammar so the user reviews before committing rather than seeing
    /// changes applied silently.
    func applyQuickFixes() {
        let fixed = codeChecker.applyAutoFixes(workingText)
        guard fixed != workingText else { return }

        preRevisionText = workingText
        let diffs = diffService.computeDiff(original: preRevisionText, corrected: fixed)
        let segments = diffService.groupIntoSegments(diffs)
        reviewSegments = segments

        if segments.contains(where: { $0.isChange }) {
            undoStack.append(preRevisionText)
            canUndo = true
            isReviewing = true
            hasResult = true
            rebuildWorkingText()
            onReviewSegmentsChanged?()
        }
    }

    func refreshConnectionStatus() {
        Task {
            connectionStatus = .checking
            connectionStatus = await foundryManager.checkConnection()
        }
    }

    func isModeApplied(_ mode: EditingMode) -> Bool {
        appliedModes.contains(mode)
    }

    func setCapturedText(_ text: String) {
        workingText = text
        updateReadability()
    }

    private func updateReadability() {
        guard !workingText.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else {
            readabilityScore = 0
            readabilityLabel = ""
            wordCount = 0
            charCount = 0
            return
        }

        wordCount = workingText
            .components(separatedBy: .whitespacesAndNewlines)
            .filter { !$0.isEmpty }
            .count
        charCount = workingText.count
        readabilityScore = readabilityService.calculateFleschReadingEase(workingText)

        let wordLabel = wordCount == 1 ? "word" : "words"
        let charLabel = charCount == 1 ? "char" : "chars"
        readabilityLabel = "\(wordCount.formatted()) \(wordLabel) · \(charCount.formatted()) \(charLabel) · Readability \(String(format: "%.1f", readabilityScore))"
    }

    func process() {
        currentTask?.cancel()

        let task = Task {
            isLoading = true
            errorMessage = nil
            isReviewing = false

            do {
                preRevisionText = workingText

                let result = try await foundryClient.getEditedText(
                    originalText: preRevisionText,
                    mode: selectedMode,
                    customPrompt: selectedMode == .customPrompt ? customPrompt : nil
                )

                if Task.isCancelled { return }

                let diffs = diffService.computeDiff(original: preRevisionText, corrected: result)
                let segments = diffService.groupIntoSegments(diffs)

                reviewSegments = segments

                appliedModes.insert(selectedMode)

                let hasChanges = segments.contains { $0.isChange }
                if hasChanges {
                    errorMessage = nil
                    undoStack.append(preRevisionText)
                    canUndo = true
                    isReviewing = true
                    hasResult = true
                    rebuildWorkingText()
                    onReviewSegmentsChanged?()
                } else {
                    errorMessage = "No changes suggested."
                }
            } catch is CancellationError {
                // Cancelled, no action needed
            } catch is URLError {
                errorMessage = "Cannot connect to Foundry Local. Is it running?"
                refreshConnectionStatus()
            } catch {
                errorMessage = "Error: \(error.localizedDescription)"
                refreshConnectionStatus()
            }

            isLoading = false
        }

        currentTask = task
    }

    func toggleChange(_ segmentId: Int) {
        guard let segment = reviewSegments.first(where: { $0.id == segmentId && $0.isChange }) else { return }
        segment.toggle()
        rebuildWorkingText()
        onReviewSegmentsChanged?()
    }

    func setChangeStates(_ segmentIds: [Int], state: ChangeState) {
        for id in segmentIds {
            if let seg = reviewSegments.first(where: { $0.id == id && $0.isChange }) {
                seg.state = state
            }
        }
        rebuildWorkingText()
        onReviewSegmentsChanged?()
    }

    func acceptAllChanges() {
        for seg in reviewSegments where seg.isChange {
            seg.state = .accepted
        }
        rebuildWorkingText()
        onReviewSegmentsChanged?()
    }

    func rejectAllChanges() {
        for seg in reviewSegments where seg.isChange {
            seg.state = .rejected
        }
        rebuildWorkingText()
        onReviewSegmentsChanged?()
    }

    func applyReview() {
        isReviewing = false
        reviewSegments = []
    }

    private func rebuildWorkingText() {
        workingText = reviewSegments.map(\.resolvedText).joined()
    }

    func undo() {
        guard !undoStack.isEmpty else { return }
        workingText = undoStack.removeLast()
        canUndo = !undoStack.isEmpty
        isReviewing = false
        hasResult = !undoStack.isEmpty
        reviewSegments = []
    }

    func accept() {
        if isReviewing {
            applyReview()
        }

        let textToPaste = workingText
        onRequestClose?()

        Task {
            try? await Task.sleep(nanoseconds: 100_000_000)
            await clipboardService.pasteText(textToPaste, previousApp: previousApp)
        }
    }

    func dismiss() {
        currentTask?.cancel()
        backgroundReconnectTask?.cancel()
        onRequestClose?()
    }

    func copyResult() {
        guard !workingText.isEmpty else { return }
        NSPasteboard.general.clearContents()
        NSPasteboard.general.setString(workingText, forType: .string)
    }
}

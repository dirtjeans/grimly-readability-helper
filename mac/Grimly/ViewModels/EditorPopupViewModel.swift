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
    private let connectionMonitor: ConnectionMonitor
    private let properNouns = ProperNounService()
    private let spellChecker = SpellCheckerService()
    private lazy var codeChecker = GrammarChecker(spellChecker: spellChecker, properNouns: properNouns)
    private let readabilityService = ReadabilityService()
    private let apStyleCodePass = APStyleCodePass()
    private lazy var apStylePipeline = APStylePipeline(codePass: apStyleCodePass, client: foundryClient)
    private var currentTask: Task<Void, Never>?
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
    /// True while the app-level ConnectionMonitor is actively retrying.
    /// The View reads this to show a subtler "Reconnecting…" banner
    /// instead of a red error when the LLM is momentarily unavailable —
    /// the retry will usually recover before the user notices.
    @Published var isReconnecting: Bool = false
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

    init(
        foundryClient: FoundryLocalClient,
        foundryManager: FoundryManager,
        clipboardService: ClipboardService,
        diffService: TextDiffService,
        connectionMonitor: ConnectionMonitor
    ) {
        self.foundryClient = foundryClient
        self.foundryManager = foundryManager
        self.clipboardService = clipboardService
        self.diffService = diffService
        self.connectionMonitor = connectionMonitor

        // Seed from the monitor's current state so the popup renders with
        // the right status the moment it opens (no "checking" flash while
        // Combine settles).
        self.connectionStatus = connectionMonitor.status
        self.isReconnecting = connectionMonitor.isReconnecting

        // Mirror the app-level monitor's state into VM-published fields so
        // the SwiftUI view can observe them without touching the monitor
        // directly. All reconnect logic lives in ConnectionMonitor — the
        // VM used to run its own reconnect loop, but that only worked
        // while the popup was open. The monitor keeps recovering even when
        // the user has closed the window.
        connectionMonitor.$status
            .receive(on: RunLoop.main)
            .sink { [weak self] s in
                self?.connectionStatus = s
                // Clear the red error banner once we're back on the air.
                if s == .connected, let self, self.errorMessage?.starts(with: "Cannot connect") == true {
                    self.errorMessage = nil
                }
            }
            .store(in: &cancellables)

        connectionMonitor.$isReconnecting
            .receive(on: RunLoop.main)
            .sink { [weak self] r in self?.isReconnecting = r }
            .store(in: &cancellables)

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
    /// AP title case and Chicago title case.
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

    /// AP Style — run the two-pass pipeline (deterministic code pass +
    /// narrow LLM pass) on the working text and show the result as a
    /// reviewable diff. The code pass alone is enough for most changes;
    /// the LLM pass adds attribution-verb + passive-voice + editorial-
    /// framing rewrites. Prompt-echo guard in the pipeline protects the
    /// diff from small-model prompt leaks.
    func runApStyle() {
        currentTask?.cancel()
        let task = Task {
            isLoading = true
            errorMessage = nil
            isReviewing = false

            preRevisionText = workingText

            let result = await apStylePipeline.run(preRevisionText)
            if Task.isCancelled { isLoading = false; return }

            let diffs = diffService.computeDiff(original: preRevisionText, corrected: result)
            let segments = diffService.groupIntoSegments(diffs)
            reviewSegments = segments

            let hasChanges = segments.contains { $0.isChange }
            if hasChanges {
                undoStack.append(preRevisionText)
                canUndo = true
                isReviewing = true
                hasResult = true
                rebuildWorkingText()
                onReviewSegmentsChanged?()
            } else {
                errorMessage = "No AP Style changes suggested."
            }
            isLoading = false
        }
        currentTask = task
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

    /// Trigger an immediate connection check on the shared monitor. Used
    /// for the user tapping the status LED and for post-error nudges.
    func refreshConnectionStatus() {
        Task {
            await connectionMonitor.refresh()
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
                // Client already retried once with a refreshed endpoint;
                // both attempts failed. Poke the monitor so its reconnect
                // ladder starts (or restarts) immediately, and use a
                // gentler banner if a reconnect is already in flight —
                // no need to shout when the app is already recovering.
                if isReconnecting {
                    errorMessage = "Reconnecting to local LLM — try again shortly."
                } else {
                    errorMessage = "Cannot connect to Foundry Local. Is it running?"
                }
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
        onRequestClose?()
    }

    func copyResult() {
        guard !workingText.isEmpty else { return }
        NSPasteboard.general.clearContents()
        NSPasteboard.general.setString(workingText, forType: .string)
    }
}

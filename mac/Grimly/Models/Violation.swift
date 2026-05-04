import Foundation

/// One grammar / spelling / punctuation issue surfaced by ``GrammarChecker``.
///
/// Violations populate the live Violations panel that appears below the
/// working text when the deterministic checker finds anything. Rows that
/// carry ``start``, ``length``, and ``autoFix`` are eligible for the
/// one-click "Quick Fix" button; rows without those need user judgment or
/// the LLM Fix Grammar pass.
struct Violation: Identifiable, Hashable {
    let id = UUID()

    /// Short label shown on the chip — "Grammar", "Spelling", "Punctuation",
    /// "Spacing", "Numbers", "Capitalization", "Weak Adverb", etc.
    let category: String

    /// The exact substring of the source text that triggered the rule. Must
    /// equal the span at [start ..< start+length) when ``autoFix`` is set —
    /// the stale-span guard in ``applyAutoFixes`` re-verifies this before
    /// splicing.
    let quote: String

    /// Why it's a violation and what to change.
    let explanation: String

    /// Optional zero-based UTF-16 offset of the violation within the source.
    /// Set when the deterministic checker can pinpoint the span and offer a
    /// one-click fix. Nil for judgment-required rules.
    let start: Int?

    /// Length (in UTF-16 code units) of the violation's span.
    let length: Int?

    /// Deterministic replacement text. When present, the panel's Quick Fix
    /// button swaps the [start..start+length) span for this string.
    let autoFix: String?

    init(category: String, quote: String, explanation: String,
         start: Int? = nil, length: Int? = nil, autoFix: String? = nil) {
        self.category = category
        self.quote = quote
        self.explanation = explanation
        self.start = start
        self.length = length
        self.autoFix = autoFix
    }

    /// True when this violation has a deterministic one-click fix.
    var canAutoFix: Bool { start != nil && length != nil && autoFix != nil }
}

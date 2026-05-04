import AppKit

/// Wraps `NSSpellChecker.shared` (the system-wide spell checker that all
/// native macOS apps use). No external dictionary file needed — macOS
/// already has the dictionary loaded for the OS.
///
/// `NSSpellChecker.shared` is thread-safe but not free; for our short text
/// volumes (a few thousand words at most) per-word `checkSpelling(of:…)` is
/// fast enough to run on the call site without batching.
final class SpellCheckerService {

    func isKnown(_ word: String) -> Bool {
        guard !word.isEmpty else { return true }

        // Normalize typographic / "smart" apostrophes to ASCII before lookup.
        // macOS apps auto-correct typed `'` to `’` (U+2019), so contractions
        // like "isn’t" arrive with the curly form. NSSpellChecker accepts
        // both, but normalizing keeps inputs consistent.
        let normalized = word
            .replacingOccurrences(of: "\u{2019}", with: "'")
            .replacingOccurrences(of: "\u{2018}", with: "'")

        // checkSpelling(of:startingAt:) returns the range of the FIRST
        // misspelling. For a single isolated word, that means either
        // NSNotFound (no misspelling) or 0..<word.count (misspelled).
        let range = NSSpellChecker.shared.checkSpelling(of: normalized, startingAt: 0)
        return range.location == NSNotFound
    }
}

import Foundation

/// In-memory case-insensitive index of well-known proper nouns (countries,
/// US states, major cities, common first/last names, top companies, brands,
/// universities, programming languages, etc.).
///
/// The list is bundled as a plain text resource. Load cost is ~5–15 ms at
/// first construction; per-word lookup after that is O(1) hash probe.
///
/// Used only for grammar-check spelling suppression — the sentence-case
/// bigram / ambiguity logic that lived here previously was removed with
/// sentence case itself.
final class ProperNounService {

    private let properNouns: Set<String>

    init() {
        var set: Set<String> = []
        if let url = Bundle.main.url(forResource: "proper_nouns", withExtension: "txt"),
           let text = try? String(contentsOf: url, encoding: .utf8) {
            for line in text.split(separator: "\n", omittingEmptySubsequences: true) {
                let trimmed = line.trimmingCharacters(in: .whitespaces)
                if trimmed.isEmpty { continue }
                if trimmed.hasPrefix("#") { continue }
                set.insert(trimmed.lowercased())
            }
        }
        self.properNouns = set
    }

    /// True if the word matches an entry in the bundled proper-noun list.
    /// Case-insensitive: "kenneth" and "Kenneth" both match.
    func isProperNoun(_ word: String) -> Bool {
        guard !word.isEmpty else { return false }
        return properNouns.contains(word.lowercased())
    }
}

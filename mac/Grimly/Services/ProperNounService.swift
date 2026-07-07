import Foundation

/// In-memory case-insensitive index of well-known proper nouns
/// (countries, US states, major cities, common first/last names, top
/// companies, brands, universities, programming languages, etc.) plus a
/// small stoplist of ambiguous words that overlap with common English
/// (bill, mark, apple, nice, turkey, …).
///
/// The list is bundled as a plain text resource. Load cost is ~5–15 ms
/// at first construction; per-word lookup after that is O(1) hash probe.
final class ProperNounService {

    private let properNouns: Set<String>
    private let ambiguous: Set<String>

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
        self.ambiguous = Set(Self.ambiguousStoplist.map { $0.lowercased() })
    }

    /// True if the word matches an entry in the bundled proper-noun list.
    /// Case-insensitive: "kenneth" and "Kenneth" both match.
    func isProperNoun(_ word: String) -> Bool {
        guard !word.isEmpty else { return false }
        return properNouns.contains(word.lowercased())
    }

    /// True if the word appears in the ambiguous stoplist — words that are
    /// also common English (names like "bill", "mark", "will"; places like
    /// "nice", "turkey"; brands like "apple", "target"). Used by sentence
    /// case to decide whether to preserve capitalization.
    func isAmbiguous(_ word: String) -> Bool {
        guard !word.isEmpty else { return false }
        return ambiguous.contains(word.lowercased())
    }

    /// Sentence-case helper: should we preserve the capitalization of this
    /// word during a title→sentence conversion? True when the word is a
    /// proper noun AND not in the ambiguous stoplist.
    func shouldPreserveInSentenceCase(_ word: String) -> Bool {
        return isProperNoun(word) && !isAmbiguous(word)
    }

    /// Words that are proper nouns AND common English. Written down here
    /// (not in the .txt file) so the intent is visible in code — these are
    /// deliberate exclusions from the "always preserve capitalization"
    /// behavior. Extend when a real-world usage surfaces a bad conversion.
    private static let ambiguousStoplist: [String] = [
        // Common names that are also common English words
        "bill", "mark", "will", "nick", "jack", "jean", "rob", "rich",
        "grant", "hope", "faith", "grace", "joy", "dawn", "amber", "angel",
        "chip", "ernest", "frank", "gill", "gene", "ginger", "homer",
        "iris", "june", "may", "april", "autumn", "ruby", "rose", "sandy",
        "skip", "summer", "wade", "chuck", "peggy", "penny", "lance",
        "miles", "cliff", "art", "buck", "carol", "carry", "chance",
        "cherry", "colt", "daisy", "dick", "dolly", "duke", "earl",
        "guy", "hazel", "holly", "jasmine", "jimmy", "king", "lily",
        "marshall", "mercy", "olive", "pat", "peter", "polly", "randy",
        "ray", "reed", "rocky", "sonny", "star", "sunny", "trinity",
        "van", "victor", "violet", "wally",

        // Places that are also common English words
        "nice", "turkey", "china", "jordan", "phoenix", "aurora",
        "madison", "jackson", "jefferson", "victoria", "georgia",
        "florence", "reading", "buffalo", "mobile", "eden", "salem",
        "hyde", "york", "windsor",

        // Brands that are also common English words
        "apple", "target", "chase", "coach", "ford", "fisher", "subway",
        "orange", "sub", "sprint", "shell", "dove", "crest", "tide",
        "gap", "arm", "corning", "lego", "match", "monster", "polo",
        "puma", "swatch", "twitter", "wing",

        // Ambiguous common tech / product names that clash with English
        "java", "python", "ruby", "swift", "go", "rust", "dart", "kotlin",
        "scratch", "processing", "rails", "django", "flask", "spring",
        "storm", "spark", "flame", "atom", "code", "sublime",
    ]
}

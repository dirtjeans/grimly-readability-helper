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
    private let bigrams: Set<String>

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
        self.bigrams = Set(Self.multiWordProperNouns.map { $0.lowercased() })
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

    /// True if the pair (first, second) is a known multi-word proper noun
    /// ("New York", "Los Angeles", "Sri Lanka", …). Both parts are treated
    /// case-insensitively.
    func isBigramProperNoun(_ first: String, _ second: String) -> Bool {
        guard !first.isEmpty, !second.isEmpty else { return false }
        return bigrams.contains("\(first.lowercased()) \(second.lowercased())")
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

    /// Two-word proper nouns whose individual parts are common English
    /// ("New York", "Los Angeles", "Hong Kong", …) and would otherwise
    /// get downcased in sentence case. Space-separated; case-insensitive.
    private static let multiWordProperNouns: [String] = [
        // US places
        "new york", "los angeles", "san francisco", "san diego",
        "san antonio", "san jose", "santa fe", "santa monica",
        "santa barbara", "santa clara", "santa cruz", "las vegas",
        "new orleans", "new mexico", "new jersey", "new hampshire",
        "new haven", "new england", "north carolina", "south carolina",
        "north dakota", "south dakota", "west virginia", "rhode island",
        "long island", "long beach", "salt lake", "las cruces", "el paso",
        "grand rapids", "grand canyon", "st louis", "st paul", "st petersburg",
        "silicon valley", "wall street", "central park", "times square",
        "empire state",

        // Countries + regions
        "new zealand", "sri lanka", "hong kong", "cape town", "cape verde",
        "cape breton", "kuala lumpur", "buenos aires", "rio de", "de janeiro",
        "de la", "sao paulo", "abu dhabi", "el salvador", "costa rica",
        "puerto rico", "dominican republic", "czech republic",
        "united states", "united kingdom", "united nations",
        "united arab", "arab emirates", "north korea", "south korea",
        "north america", "south america", "south africa", "south sudan",
        "west africa", "east africa", "middle east", "far east",
        "eastern europe", "western europe", "central europe",
        "great britain", "great wall", "vatican city", "cape cod",

        // People (patterns like "Van Buren", "de Gaulle")
        "van gogh", "van buren", "van halen", "de gaulle", "el greco",
        "st francis", "st thomas", "st peter",

        // Historical / political phrases
        "cold war", "civil war", "world war", "french revolution",
        "industrial revolution", "great depression", "middle ages",
        "iron age", "bronze age", "stone age", "big bang", "milky way",
        "solar system",

        // Companies + brands
        "bank of", "of america", "wells fargo", "morgan stanley", "jp morgan",
        "general motors", "general electric", "home depot", "walt disney",
        "warner bros", "american airlines", "united airlines",

        // Recurring event / concept phrases
        "black friday", "cyber monday", "labor day", "memorial day",
        "independence day", "new year", "mother's day", "father's day",
        "st patrick", "st valentine",
    ]
}

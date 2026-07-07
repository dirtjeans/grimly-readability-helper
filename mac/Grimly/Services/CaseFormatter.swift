import Foundation

/// Case-formatting styles offered by the "Case" dropdown.
enum CaseStyle {
    /// AP style — capitalize principal words. Lowercase articles,
    /// coordinating conjunctions, and prepositions of 3 or fewer letters.
    /// Prepositions of 4+ letters (with, from, over) are capitalized.
    case apTitle
    /// Chicago style — capitalize principal words. Lowercase ALL
    /// prepositions regardless of length, plus articles and coordinating
    /// conjunctions.
    case chicagoTitle
}

/// Deterministic title-case reformatting. Applied to the user's selection
/// as-is; result flows into the same reviewable-diff UI the LLM modes use.
/// Sentence case was removed because it depends on proper-noun context an
/// LLM couldn't reliably supply at the model sizes we ship with.
enum CaseFormatter {

    // ─── Word lists ───

    private static let articles: Set<String> = ["a", "an", "the"]

    // FANBOYS + a couple of extras Chicago also treats as coordinating.
    private static let coordinatingConjunctions: Set<String> = [
        "and", "but", "or", "for", "nor", "yet", "so",
    ]

    // AP: lowercase these (≤3 letters) but capitalize prepositions of 4+
    // letters like "with", "from", "over".
    private static let shortPrepositions: Set<String> = [
        "at", "by", "for", "in", "of", "on", "to", "up", "as", "if", "off",
        "per", "via", "vs",
    ]

    // Chicago-only additions (4+ letter prepositions Chicago lowercases but
    // AP capitalizes).
    private static let longPrepositions: Set<String> = [
        "about", "above", "across", "after", "against", "along", "among",
        "around", "before", "behind", "below", "beneath", "beside",
        "between", "beyond", "concerning", "despite", "down", "during",
        "except", "following", "from", "inside", "into", "like", "near",
        "onto", "outside", "over", "past", "regarding", "since", "than",
        "through", "throughout", "toward", "towards", "under", "underneath",
        "until", "upon", "versus", "with", "within", "without",
    ]

    // MARK: - Public API

    static func apply(_ text: String, style: CaseStyle) -> String {
        switch style {
        case .apTitle:      return titleCase(text, chicago: false)
        case .chicagoTitle: return titleCase(text, chicago: true)
        }
    }

    // MARK: - Title case

    private static func titleCase(_ text: String, chicago: Bool) -> String {
        guard !text.isEmpty else { return text }

        var tokens = tokenize(text)
        let wordIndices = tokens.indices.filter { !tokens[$0].isEmpty && !tokens[$0].first!.isWhitespace }
        guard let firstWord = wordIndices.first, let lastWord = wordIndices.last else { return text }

        // Any `.`, `!`, `?`, or `:` inside the title ends a clause; the
        // next word starts a new "sentence" and is force-capitalized.
        // Modern headline style routinely uses `.` as a real terminator
        // ("Balogun Is Back. But How Will..."). Decimals ("v2.0") and
        // initials ("J.R.R.") are not disrupted — a digit doesn't take
        // capitalization, and the word after an initials cluster is a
        // proper noun that gets capitalized regardless.
        var afterClauseEnd = false
        for i in tokens.indices {
            if tokens[i].isEmpty || tokens[i].first!.isWhitespace { continue }
            let forceCap = (i == firstWord) || (i == lastWord) || afterClauseEnd
            tokens[i] = transformWord(tokens[i], forceCap: forceCap, chicago: chicago)
            let last = tokens[i].last!
            afterClauseEnd = last == ":" || last == "?" || last == "!" || last == "."
        }
        return tokens.joined()
    }

    private static func transformWord(_ token: String, forceCap: Bool, chicago: Bool) -> String {
        let (lead, core, trail) = splitPunctuation(token)
        guard !core.isEmpty else { return token }

        var parts = splitOnHyphens(core)
        var firstPart = true
        for i in parts.indices {
            let part = parts[i]
            if part.isEmpty { continue }
            if part.count == 1, "-‐–—".contains(part.first!) {
                firstPart = false
                continue
            }
            parts[i] = casePart(part, forceCap: forceCap, isLeadingHyphenPart: firstPart, chicago: chicago)
            firstPart = false
        }
        return lead + parts.joined() + trail
    }

    private static func casePart(_ part: String, forceCap: Bool, isLeadingHyphenPart: Bool, chicago: Bool) -> String {
        if isPreserveShape(part) { return part }
        if part.caseInsensitiveCompare("i") == .orderedSame { return "I" }

        var lowercase = shouldLowercase(part, chicago: chicago)
        // Chicago hyphenated compounds: only capitalize the first part.
        if !isLeadingHyphenPart, chicago, !forceCap {
            lowercase = true
        }

        if forceCap || !lowercase { return capitalize(part) }
        return part.lowercased()
    }

    private static func shouldLowercase(_ word: String, chicago: Bool) -> Bool {
        let key = word.lowercased()
        if articles.contains(key) { return true }
        if coordinatingConjunctions.contains(key) { return true }
        if shortPrepositions.contains(key) { return true }
        if chicago, longPrepositions.contains(key) { return true }
        return false
    }

    // MARK: - Helpers

    /// Split text into an array of alternating word/whitespace tokens so we
    /// can rebuild it without collapsing runs.
    private static func tokenize(_ text: String) -> [String] {
        var out: [String] = []
        var current = ""
        var currentIsWhitespace: Bool? = nil
        for c in text {
            let isWs = c.isWhitespace
            if let mode = currentIsWhitespace, mode != isWs {
                out.append(current)
                current = ""
            }
            current.append(c)
            currentIsWhitespace = isWs
        }
        if !current.isEmpty { out.append(current) }
        return out
    }

    private static func splitPunctuation(_ token: String) -> (String, String, String) {
        let chars = Array(token)
        var leadEnd = 0
        while leadEnd < chars.count, !(chars[leadEnd].isLetter || chars[leadEnd].isNumber) { leadEnd += 1 }
        var trailStart = chars.count
        while trailStart > leadEnd, !(chars[trailStart - 1].isLetter || chars[trailStart - 1].isNumber) { trailStart -= 1 }
        if leadEnd >= trailStart { return (token, "", "") }
        return (String(chars[0..<leadEnd]),
                String(chars[leadEnd..<trailStart]),
                String(chars[trailStart..<chars.count]))
    }

    private static func splitOnHyphens(_ word: String) -> [String] {
        var out: [String] = []
        var current = ""
        for c in word {
            if "-‐–—".contains(c) {
                if !current.isEmpty { out.append(current); current = "" }
                out.append(String(c))
            } else {
                current.append(c)
            }
        }
        if !current.isEmpty { out.append(current) }
        return out
    }

    /// True when the word already has a "shape" we shouldn't overwrite:
    /// all-caps acronyms (API, NASA) or mixed-case brands (iPhone, eBay).
    private static func isPreserveShape(_ word: String) -> Bool {
        guard word.count >= 2 else { return false }
        var hasLower = false
        var hasInternalUpper = false
        for (i, c) in word.enumerated() {
            if c.isLowercase { hasLower = true }
            else if i > 0, c.isUppercase { hasInternalUpper = true }
        }
        if hasLower, hasInternalUpper { return true }
        if !hasLower, word.contains(where: { $0.isLetter }) { return true }
        return false
    }

    private static func capitalize(_ word: String) -> String {
        guard let first = word.first else { return word }
        let rest = word.dropFirst()
        return String(first).uppercased() + rest.lowercased()
    }
}

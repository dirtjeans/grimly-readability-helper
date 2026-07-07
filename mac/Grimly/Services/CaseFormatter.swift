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
    /// Sentence case — capitalize first word and words after sentence-
    /// ending punctuation. Preserves ALL-CAPS acronyms, mixed-case words
    /// (iPhone, eBay), and the pronoun "I". Cannot infer proper nouns
    /// from title-cased input — the review UI lets the user un-do
    /// individual changes.
    case sentence
}

/// Deterministic case reformatting for headings and titles. Applied to
/// the user's selection as-is; result flows into the same reviewable-diff
/// UI the LLM modes use, so the user accepts / rejects individual changes.
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

    static func apply(_ text: String, style: CaseStyle,
                      properNouns: ProperNounService? = nil,
                      spell: SpellCheckerService? = nil) -> String {
        switch style {
        case .apTitle:      return titleCase(text, chicago: false)
        case .chicagoTitle: return titleCase(text, chicago: true)
        case .sentence:     return sentenceCase(text, properNouns: properNouns, spell: spell)
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

    // MARK: - Sentence case

    private static func sentenceCase(_ text: String,
                                     properNouns: ProperNounService?,
                                     spell: SpellCheckerService?) -> String {
        guard !text.isEmpty else { return text }

        var tokens = tokenize(text)

        // Preprocess: find token indices to force-preserve because they're
        // part of a multi-word proper noun ("New York", "Los Angeles").
        let bigramForcePreserve = findBigramMatches(tokens, properNouns: properNouns)

        var nextIsSentenceStart = true

        for i in tokens.indices {
            let tok = tokens[i]
            if tok.isEmpty || tok.first!.isWhitespace { continue }

            tokens[i] = sentenceCaseWord(tok,
                                         isSentenceStart: nextIsSentenceStart,
                                         bigramForcePreserve: bigramForcePreserve.contains(i),
                                         properNouns: properNouns,
                                         spell: spell)

            // Does this token end in sentence-terminal punctuation?
            nextIsSentenceStart = false
            for c in tok.reversed() {
                if c.isWhitespace { continue }
                if c == "." || c == "!" || c == "?" {
                    nextIsSentenceStart = true
                }
                break
            }
        }
        return tokens.joined()
    }

    /// Walk the token array and return indices to force-preserve because
    /// they participate in a known multi-word proper-noun bigram.
    private static func findBigramMatches(_ tokens: [String],
                                          properNouns: ProperNounService?) -> Set<Int> {
        var result: Set<Int> = []
        guard let properNouns = properNouns else { return result }
        let wordIndices = tokens.indices.filter {
            !tokens[$0].isEmpty && !tokens[$0].first!.isWhitespace
        }
        for k in 0..<max(0, wordIndices.count - 1) {
            let a = extractCleanCore(tokens[wordIndices[k]])
            let b = extractCleanCore(tokens[wordIndices[k + 1]])
            if a.isEmpty || b.isEmpty { continue }
            if properNouns.isBigramProperNoun(a, b) {
                result.insert(wordIndices[k])
                result.insert(wordIndices[k + 1])
            }
        }
        return result
    }

    /// Strip leading/trailing punctuation from a token, return the letter core.
    private static func extractCleanCore(_ token: String) -> String {
        let chars = Array(token)
        var lead = 0
        while lead < chars.count, !(chars[lead].isLetter || chars[lead].isNumber) { lead += 1 }
        var trailStart = chars.count
        while trailStart > lead, !(chars[trailStart - 1].isLetter || chars[trailStart - 1].isNumber) { trailStart -= 1 }
        if lead >= trailStart { return "" }
        return String(chars[lead..<trailStart])
    }

    private static func sentenceCaseWord(_ token: String, isSentenceStart: Bool,
                                         bigramForcePreserve: Bool,
                                         properNouns: ProperNounService?,
                                         spell: SpellCheckerService?) -> String {
        let (lead, core, trail) = splitPunctuation(token)
        guard !core.isEmpty else { return token }

        var parts = splitOnHyphens(core)
        var wordStart = isSentenceStart
        for i in parts.indices {
            let part = parts[i]
            if part.isEmpty { continue }
            if part.count == 1, "-‐–—".contains(part.first!) { continue }

            if isPreserveShape(part) {
                wordStart = false
                continue
            }
            if part.caseInsensitiveCompare("i") == .orderedSame {
                parts[i] = "I"
                wordStart = false
                continue
            }
            // Bigram-forced preservation: this token is part of a known
            // multi-word proper noun ("New York", "Los Angeles"). Capitalize
            // regardless of whether the individual word is common English.
            if bigramForcePreserve {
                parts[i] = capitalize(part)
                wordStart = false
                continue
            }
            // Single-word proper noun preservation with two guards:
            //   1. Ambiguous stoplist (bill, mark, apple, nice, turkey, …)
            //   2. Hunspell — if the lowercased form is a known English
            //      word, it's ambiguous even if it's on our proper-noun
            //      list. Kills false positives for common surnames like
            //      "Best", "Brown", "Young", "Long".
            if let properNouns = properNouns,
               properNouns.isProperNoun(part),
               !properNouns.isAmbiguous(part),
               spell?.isKnown(part.lowercased()) != true {
                parts[i] = capitalize(part)
                wordStart = false
                continue
            }

            parts[i] = wordStart ? capitalize(part) : part.lowercased()
            wordStart = false
        }
        return lead + parts.joined() + trail
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

import Foundation

/// Deterministic grammar / punctuation / spelling checker. Handles the
/// mechanical rules an LLM keeps forgetting — doubled words, lowercase "i",
/// "would of" → "would have", missing leading zero on decimals, weak
/// adverbs, etc. The LLM Fix Grammar pass only has to handle rules that
/// need genuine judgment.
final class GrammarChecker {

    // ─── Tier-1 grammar (high-confidence patterns) ───

    /// Doubled adjacent words: "the the", "is is", "and and".
    private static let rxDoubledWord =
        try! NSRegularExpression(pattern: #"\b(\w+)\s+\1\b"#, options: [.caseInsensitive])

    /// Repeated punctuation: 3+ exclamation/question marks, or 4+ periods.
    private static let rxRepeatedPunctuation =
        try! NSRegularExpression(pattern: #"!{3,}|\?{3,}|\.{4,}"#)

    /// Space before sentence-ending punctuation.
    private static let rxSpaceBeforePunct =
        try! NSRegularExpression(pattern: #"[A-Za-z]\s+([.,;:!?])"#)

    /// Wrong possessive contractions (your's, their's, her's, our's).
    private static let rxBadPossessive =
        try! NSRegularExpression(pattern: #"\b(your's|their's|her's|our's)\b"#, options: [.caseInsensitive])

    /// "would/could/should/might/must of" → "... have"
    private static let rxWouldOf =
        try! NSRegularExpression(pattern: #"\b(would|could|should|might|must)\s+of\b"#, options: [.caseInsensitive])

    /// Standalone lowercase "i" between word boundaries.
    private static let rxLoneLowerI =
        try! NSRegularExpression(pattern: #"(?<=\s|^|[.!?]\s)i(?=\s|[.,!?;:'’]|$)"#)

    /// Two or more spaces between words (compresses to one).
    private static let rxRunOnWhitespace =
        try! NSRegularExpression(pattern: #"(\S)  +(\S)"#)

    /// "its'" — never correct in English.
    private static let rxItsApostrophe =
        try! NSRegularExpression(pattern: #"\bits'"#, options: [.caseInsensitive])

    /// Lowercase letter starting a sentence (after .!? + space).
    private static let rxLowerSentenceStart =
        try! NSRegularExpression(pattern: #"(?<=[.!?]\s)([a-z])"#)

    /// Weak adverbs that almost always read as filler. The list is
    /// deliberately short — these specific words are rarely load-bearing in
    /// good prose. Adverbs that often carry meaning ("not", "never",
    /// "well", "soon", "literally") are excluded. The match consumes the
    /// trailing whitespace so the auto-fix is just an empty string.
    private static let rxWeakAdverb =
        try! NSRegularExpression(pattern: #"\b(very|really|actually|basically|simply)\s+"#,
                                 options: [.caseInsensitive])

    // ─── Style ───

    private static let rxAllCaps =
        try! NSRegularExpression(pattern: #"\b[A-Z]{4,}\b"#)

    private static let rxAmpersandInProse =
        try! NSRegularExpression(pattern: #"(?<=[a-z]\s)&(?=\s[a-z])"#, options: [.caseInsensitive])

    private static let rxPercentWord =
        try! NSRegularExpression(pattern: #"\bpercent\b"#, options: [.caseInsensitive])

    private static let rxDoubleSpaceAfterPeriod =
        try! NSRegularExpression(pattern: #"[.:]\s{2,}(?=[A-Z])"#)

    // ─── Numbers ───

    private static let rxLargeNumberRaw =
        try! NSRegularExpression(pattern: #"\$?\d{7,}\b"#)

    private static let rxOrdinalNumeral =
        try! NSRegularExpression(pattern: #"\b\d+(?:st|nd|rd|th)\b"#, options: [.caseInsensitive])

    private static let rxMissingLeadingZero =
        try! NSRegularExpression(pattern: #"(?<![\d\w.])\.\d+\b"#)

    private static let rxBareInches =
        try! NSRegularExpression(pattern: #"\b\d+(?:\.\d+)?\s+in\b(?!\.)"#)

    private static let rxThousandsRawNumber =
        try! NSRegularExpression(pattern: #"(?<![\w.,-])\d{4,6}(?![\w,-])"#)

    private static let rxYearOrUnitSuffix =
        try! NSRegularExpression(pattern: #"^\s*(?:pixels?|px|baud|BCE?|AD|CE|[x×]\s*\d)\b"#,
                                 options: [.caseInsensitive])

    // ─── Spelling ───

    /// Tokenizer for the spell-check pass. The character class includes
    /// ASCII apostrophe AND curly single quotes (U+2018, U+2019) — most
    /// apps auto-correct typed `'` to `’`, so contractions arrive with the
    /// curly form. Without the curly variants the tokenizer split "isn’t"
    /// into "isn" + "t" and the dictionary correctly flagged "isn".
    private static let rxWordToken =
        try! NSRegularExpression(pattern: #"\b[A-Za-z][A-Za-z'‘’\-]*[A-Za-z]?\b"#)

    private let spellChecker: SpellCheckerService?

    init(spellChecker: SpellCheckerService? = SpellCheckerService()) {
        self.spellChecker = spellChecker
    }

    // MARK: - Check

    func check(_ text: String) -> [Violation] {
        guard !text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else { return [] }

        let ns = text as NSString
        let fullRange = NSRange(location: 0, length: ns.length)
        var results: [Violation] = []

        // ALL CAPS for emphasis (with known-acronym allowlist).
        for m in Self.rxAllCaps.matches(in: text, range: fullRange) {
            let word = ns.substring(with: m.range)
            if Self.knownAcronyms.contains(word) { continue }
            results.append(Violation(
                category: "Capitalization",
                quote: word,
                explanation: "Avoid ALL CAPS for emphasis. Use bold or italics instead."))
        }

        // Ampersands — only allowed in proper nouns
        for m in Self.rxAmpersandInProse.matches(in: text, range: fullRange) {
            results.append(Violation(
                category: "Punctuation", quote: "&",
                explanation: "Ampersands are only for proper nouns (e.g., \"Ernst & Young\"). Use \"and\" in running text.",
                start: m.range.location, length: m.range.length, autoFix: "and"))
        }

        // "percent" as a word — prefer the % symbol
        for _ in Self.rxPercentWord.matches(in: text, range: fullRange) {
            results.append(Violation(
                category: "Numbers", quote: "percent",
                explanation: "Use the % symbol, never \"percent.\""))
        }

        // Two spaces after period or colon — collapse to one. Quote must
        // equal the full match span so the stale-span guard in
        // applyAutoFixes recognizes it.
        for m in Self.rxDoubleSpaceAfterPeriod.matches(in: text, range: fullRange) {
            let raw = ns.substring(with: m.range)
            let fix = String(raw.first!) + " "
            results.append(Violation(
                category: "Spacing", quote: raw,
                explanation: "One space after periods/colons, not two.",
                start: m.range.location, length: m.range.length, autoFix: fix))
        }

        // Large numbers in raw-digit form
        for m in Self.rxLargeNumberRaw.matches(in: text, range: fullRange) {
            if Self.looksLikeIdentifier(match: m, in: text) { continue }
            results.append(Violation(
                category: "Numbers",
                quote: ns.substring(with: m.range),
                explanation: "Numbers ≥ 1,000,000 should be abbreviated (e.g., \"1.2 million\")."))
        }

        // Ordinal numerals
        for m in Self.rxOrdinalNumeral.matches(in: text, range: fullRange) {
            results.append(Violation(
                category: "Ordinal",
                quote: ns.substring(with: m.range),
                explanation: "Spell out ordinal numbers (e.g., \"twenty-first\"); for dates use cardinals (e.g., \"June 1\", not \"June 1st\")."))
        }

        // Missing leading zero
        for m in Self.rxMissingLeadingZero.matches(in: text, range: fullRange) {
            let raw = ns.substring(with: m.range)
            results.append(Violation(
                category: "Numbers", quote: raw,
                explanation: "Add a leading zero to decimal fractions less than one (e.g., \"0.75\", not \".75\").",
                start: m.range.location, length: m.range.length, autoFix: "0" + raw))
        }

        // Bare "in" — needs the period to disambiguate inches abbreviation
        for m in Self.rxBareInches.matches(in: text, range: fullRange) {
            results.append(Violation(
                category: "Units",
                quote: ns.substring(with: m.range),
                explanation: "Use \"in.\" with a period for inches (or spell out \"inches\"). Bare \"in\" can be confused with the preposition."))
        }

        // Thousands separators
        for m in Self.rxThousandsRawNumber.matches(in: text, range: fullRange) {
            let raw = ns.substring(with: m.range)
            if raw.count == 4 {
                if let val = Int(raw), val >= 1000 && val <= 2099 { continue }
                if Self.isFollowedByYearOrUnitContext(match: m, in: ns) { continue }
            }
            // AutoFix: format with thousands separators ("12345" → "12,345").
            var autoFix: String? = nil
            if let n = Int64(raw) {
                let f = NumberFormatter()
                f.numberStyle = .decimal
                f.locale = Locale(identifier: "en_US_POSIX")
                autoFix = f.string(from: NSNumber(value: n))
            }
            results.append(Violation(
                category: "Numbers", quote: raw,
                explanation: "Use thousands separators in numbers with 4+ digits (e.g., \"1,234\"). Exception: years, pixels, and baud only need commas at 5+ digits.",
                start: m.range.location, length: m.range.length, autoFix: autoFix))
        }

        // ─── Tier-1 grammar / punctuation ───

        for m in Self.rxDoubledWord.matches(in: text, range: fullRange) {
            let word = ns.substring(with: m.range(at: 1))
            let lower = word.lowercased()
            if lower == "had" || lower == "that" { continue }
            results.append(Violation(
                category: "Grammar",
                quote: ns.substring(with: m.range),
                explanation: "Doubled word \"\(word)\". Likely a typo — keep one.",
                start: m.range.location, length: m.range.length, autoFix: word))
        }

        for m in Self.rxRepeatedPunctuation.matches(in: text, range: fullRange) {
            let raw = ns.substring(with: m.range)
            let fix: String
            if raw.first == "!" { fix = "!" }
            else if raw.first == "?" { fix = "?" }
            else { fix = "..." }
            results.append(Violation(
                category: "Punctuation", quote: raw,
                explanation: "\"\(raw)\" reads as heavy emphasis. Use a single mark for clearer prose.",
                start: m.range.location, length: m.range.length, autoFix: fix))
        }

        for m in Self.rxSpaceBeforePunct.matches(in: text, range: fullRange) {
            let raw = ns.substring(with: m.range)
            let punct = ns.substring(with: m.range(at: 1))
            let fix = String(raw.first!) + punct
            results.append(Violation(
                category: "Punctuation", quote: raw,
                explanation: "Remove the space before \"\(punct)\".",
                start: m.range.location, length: m.range.length, autoFix: fix))
        }

        for m in Self.rxBadPossessive.matches(in: text, range: fullRange) {
            let raw = ns.substring(with: m.range)
            let fixed = raw.replacingOccurrences(of: "'", with: "")
            results.append(Violation(
                category: "Grammar", quote: raw,
                explanation: "\"\(raw)\" is never correct — use \"\(fixed)\".",
                start: m.range.location, length: m.range.length, autoFix: fixed))
        }

        for m in Self.rxWouldOf.matches(in: text, range: fullRange) {
            let raw = ns.substring(with: m.range)
            let fix = ns.substring(with: m.range(at: 1)) + " have"
            results.append(Violation(
                category: "Grammar", quote: raw,
                explanation: "\"\(raw)\" should be \"\(fix)\". \"Of\" is never a verb.",
                start: m.range.location, length: m.range.length, autoFix: fix))
        }

        for m in Self.rxLoneLowerI.matches(in: text, range: fullRange) {
            results.append(Violation(
                category: "Grammar", quote: "i",
                explanation: "The pronoun \"I\" is always capitalized.",
                start: m.range.location, length: m.range.length, autoFix: "I"))
        }

        for m in Self.rxRunOnWhitespace.matches(in: text, range: fullRange) {
            let raw = ns.substring(with: m.range)
            let lead = ns.substring(with: m.range(at: 1))
            let trail = ns.substring(with: m.range(at: 2))
            let fix = lead + " " + trail
            results.append(Violation(
                category: "Spacing", quote: raw,
                explanation: "Multiple spaces between words — collapse to one.",
                start: m.range.location, length: m.range.length, autoFix: fix))
        }

        for m in Self.rxItsApostrophe.matches(in: text, range: fullRange) {
            let raw = ns.substring(with: m.range)
            results.append(Violation(
                category: "Grammar", quote: raw,
                explanation: "\"its'\" is never correct. Use \"its\" (possessive) or \"it's\" (contraction).",
                start: m.range.location, length: m.range.length, autoFix: "its"))
        }

        for m in Self.rxLowerSentenceStart.matches(in: text, range: fullRange) {
            let raw = ns.substring(with: m.range)
            results.append(Violation(
                category: "Capitalization", quote: raw,
                explanation: "Capitalize the first letter of a sentence.",
                start: m.range.location, length: m.range.length, autoFix: raw.uppercased()))
        }

        // Weak adverbs — quote is the full span so the verification guard in
        // applyAutoFixes recognizes it; explanation references the trimmed word.
        for m in Self.rxWeakAdverb.matches(in: text, range: fullRange) {
            let word = ns.substring(with: m.range(at: 1))
            let fullSpan = ns.substring(with: m.range)
            results.append(Violation(
                category: "Weak Adverb", quote: fullSpan,
                explanation: "\"\(word)\" is usually filler — try the sentence without it.",
                start: m.range.location, length: m.range.length, autoFix: ""))
        }

        // ─── Spelling ───
        if let spell = spellChecker {
            var seenSpellings = Set<String>()
            for m in Self.rxWordToken.matches(in: text, range: fullRange) {
                let word = ns.substring(with: m.range)
                if word.count < 3 { continue }
                if word.contains(where: { $0.isNumber }) { continue }
                if word.allSatisfy({ !$0.isLetter || $0.isUppercase }) &&
                   word.contains(where: { $0.isLetter }) {
                    continue
                }
                if spell.isKnown(word) { continue }

                var key = word.lowercased()
                if key.hasSuffix("'s") { key = String(key.dropLast(2)) }
                else if key.hasSuffix("s") { key = String(key.dropLast()) }
                if !seenSpellings.insert(key).inserted { continue }

                results.append(Violation(
                    category: "Spelling", quote: word,
                    explanation: "\"\(word)\" isn't in the dictionary. Check the spelling."))
            }
        }

        return results
    }

    // MARK: - Quick Fix

    /// Apply every auto-fixable violation as a single batch and return the
    /// rewritten text. Used by the "Quick Fix" button to bundle the
    /// deterministic fixes into one reviewable diff.
    func applyAutoFixes(_ text: String) -> String {
        guard !text.isEmpty else { return text }

        // Sort by start descending so each splice doesn't shift later
        // indices. Skip overlaps defensively.
        let fixable = check(text)
            .filter { $0.canAutoFix }
            .sorted { ($0.start ?? 0) > ($1.start ?? 0) }

        var current = text as NSString
        var lastStart = Int.max
        for v in fixable {
            guard let start = v.start, let length = v.length, let fix = v.autoFix else { continue }
            if start + length > lastStart { continue }
            if start < 0 || start + length > current.length { continue }
            let span = current.substring(with: NSRange(location: start, length: length))
            if span != v.quote { continue }
            current = current.replacingCharacters(in: NSRange(location: start, length: length),
                                                  with: fix) as NSString
            lastStart = start
        }
        return current as String
    }

    // MARK: - Helpers

    private static func isFollowedByYearOrUnitContext(match m: NSTextCheckingResult, in ns: NSString) -> Bool {
        let after = m.range.location + m.range.length
        guard after < ns.length else { return false }
        let remaining = min(20, ns.length - after)
        let suffix = ns.substring(with: NSRange(location: after, length: remaining))
        let suffixNS = suffix as NSString
        return Self.rxYearOrUnitSuffix.firstMatch(
            in: suffix,
            range: NSRange(location: 0, length: suffixNS.length)
        ) != nil
    }

    private static func looksLikeIdentifier(match m: NSTextCheckingResult, in text: String) -> Bool {
        if m.range.length <= 4 { return true }
        let ns = text as NSString
        let before = m.range.location - 1
        if before >= 0 {
            let ch = ns.character(at: before)
            if (ch >= UInt16(UInt8(ascii: "a")) && ch <= UInt16(UInt8(ascii: "z"))) ||
               (ch >= UInt16(UInt8(ascii: "A")) && ch <= UInt16(UInt8(ascii: "Z"))) ||
               ch == UInt16(UInt8(ascii: "-")) || ch == UInt16(UInt8(ascii: "_")) { return true }
        }
        let after = m.range.location + m.range.length
        if after < ns.length {
            let ch = ns.character(at: after)
            if (ch >= UInt16(UInt8(ascii: "a")) && ch <= UInt16(UInt8(ascii: "z"))) ||
               (ch >= UInt16(UInt8(ascii: "A")) && ch <= UInt16(UInt8(ascii: "Z"))) ||
               ch == UInt16(UInt8(ascii: "-")) || ch == UInt16(UInt8(ascii: "_")) || ch == UInt16(UInt8(ascii: "x")) { return true }
        }
        return false
    }

    /// Acronyms exempt from the ALL CAPS rule. Conservative — keep additions
    /// to genuinely common abbreviations to avoid false negatives.
    private static let knownAcronyms: Set<String> = [
        // General tech
        "API", "APIS", "ACL", "ACLS", "HTTP", "HTTPS", "TCP", "UDP", "DNS",
        "TLS", "SSL", "SSH", "RDP", "VPN", "MFA", "SSO", "SAML", "OIDC",
        "OAUTH", "JSON", "YAML", "HTML", "CSS", "SQL", "CSV", "XML", "URL",
        "URI", "URN", "UUID", "GUID", "REST", "GRAPHQL", "GRPC",
        // Security
        "SIEM", "SOAR", "EDR", "XDR", "NDR", "MDR", "CASB", "CSPM", "CNAPP",
        "CWPP", "IAM", "CIAM", "PAM", "IDS", "IPS", "WAF", "DDOS",
        "MITM", "PII", "PHI", "PCI", "GDPR", "SOC", "NIST", "ISO",
        // Infrastructure
        "AWS", "GCP", "IBM", "VMWARE", "CDN", "LDAP",
        // Generic
        "AI", "ML", "LLM", "GPU", "CPU", "RAM", "ROM", "SSD", "HDD", "NVME",
        "USB", "HDMI", "MAC", "IOT",
        "CEO", "CTO", "CIO", "CISO", "CFO",
        "OEM", "SMB", "SME", "SUV",
        // U.S. states/countries
        "USA", "USD", "UAE",
    ]
}

import Foundation

class ReadabilityService {

    func calculateFleschReadingEase(_ text: String) -> Double {
        guard !text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else { return 0 }

        // Word defines a "word" as any string of characters between spaces.
        // Tokens must contain at least one letter or digit to count.
        let tokens = text.components(separatedBy: .whitespacesAndNewlines)
            .filter { !$0.isEmpty }

        var wordCount = 0
        var syllableCount = 0

        for token in tokens {
            guard token.contains(where: { $0.isLetter || $0.isNumber }) else { continue }
            wordCount += 1
            syllableCount += countSyllables(token)
        }

        guard wordCount > 0 else { return 0 }

        // Sentence boundaries: . ! ? ; :
        //
        // Periods are special — they appear in abbreviations (U.S., Mr., e.g.,
        // Inc.) and decimals (3.14), which are NOT sentence endings. Empirical
        // testing confirmed Word counts "Mr. Smith ran the meeting." and
        // "The U.S. is a federal republic." as single sentences.
        //
        // Heuristic: a period ends a sentence only when followed by
        // whitespace + an uppercase letter or digit, OR by end-of-text.
        // Other terminators (!, ?, ;, :) don't have this ambiguity.
        let chars = Array(text)
        var sentenceCount = 0
        for i in 0..<chars.count {
            let c = chars[i]
            if c == "!" || c == "?" || c == ";" || c == ":" {
                sentenceCount += 1
            } else if c == "." {
                if isSentenceEndingPeriod(chars, at: i) {
                    sentenceCount += 1
                }
            }
        }
        if sentenceCount == 0 { sentenceCount = 1 }

        let asl = Double(wordCount) / Double(sentenceCount)
        let asw = Double(syllableCount) / Double(wordCount)

        let score = 206.835 - (1.015 * asl) - (84.6 * asw)
        return (max(min(score, 100), 0) * 10).rounded() / 10
    }

    /// Common abbreviations that end in a period and are typically followed
    /// by an uppercase proper noun. Word empirically treats these as
    /// mid-sentence markers, not sentence ends. Case-insensitive.
    private static let abbreviationTokens: Set<String> = [
        // Titles
        "mr", "mrs", "ms", "dr", "prof", "rev", "hon", "sr", "jr", "st",
        // Company / org types
        "inc", "ltd", "co", "corp", "llc", "plc", "gmbh", "sa",
        // Address abbreviations
        "ave", "blvd", "rd", "mt", "ft", "pl", "sq",
        // Common Latin / reference
        "etc", "vs", "cf", "al", "ca",
        // Days of week
        "mon", "tue", "tues", "wed", "thu", "thur", "thurs", "fri", "sat", "sun",
        // Months
        "jan", "feb", "mar", "apr", "jun", "jul", "aug", "sep", "sept", "oct", "nov", "dec",
        // Bibliography / technical
        "no", "vol", "pp", "fig", "ch", "ed"
    ]

    /// Returns true if the period at `i` is a sentence-ending period (as
    /// opposed to an abbreviation marker, initial, or decimal separator).
    /// Rule: followed by whitespace + an uppercase letter or digit (or
    /// end-of-text), AND the token before the period is not an abbreviation.
    private func isSentenceEndingPeriod(_ chars: [Character], at i: Int) -> Bool {
        var j = i + 1
        // Skip optional closing punctuation.
        while j < chars.count {
            let c = chars[j]
            if c == "\"" || c == "'" || c == "\u{201D}" || c == "\u{2019}"
                || c == ")" || c == "]" || c == "}" {
                j += 1
            } else {
                break
            }
        }
        // End of text = sentence end.
        if j >= chars.count { return true }
        // Must be followed by whitespace.
        if !chars[j].isWhitespace { return false }
        // Skip whitespace.
        while j < chars.count && chars[j].isWhitespace { j += 1 }
        // Whitespace to end-of-text = sentence end.
        if j >= chars.count { return true }
        // Lowercase follow-up = clearly not a sentence end.
        if !chars[j].isUppercase && !chars[j].isNumber { return false }
        // Even when followed by uppercase/digit, some tokens-before-period
        // are abbreviations ("Robert F. Kennedy", "Acme Inc. filed").
        return !isAbbreviationBefore(chars, periodIndex: i)
    }

    /// True if the letter-only token immediately preceding the period at
    /// `periodIndex` is a known abbreviation or a single uppercase letter
    /// (an initial).
    private func isAbbreviationBefore(_ chars: [Character], periodIndex: Int) -> Bool {
        var start = periodIndex - 1
        while start >= 0 && chars[start].isLetter { start -= 1 }
        let length = periodIndex - 1 - start
        if length <= 0 { return false }

        // Single uppercase letter = initial (F., J., A., etc.)
        if length == 1 && chars[start + 1].isUppercase { return true }

        // Known abbreviation (case-insensitive).
        let token = String(chars[(start + 1)..<periodIndex]).lowercased()
        return ReadabilityService.abbreviationTokens.contains(token)
    }

    private func countSyllables(_ token: String) -> Int {
        // Trim leading/trailing punctuation but keep internal chars so we can
        // classify currency ($50), percentages (72%), and comma-separated
        // numbers (1,000) by their raw form.
        let trimChars: Set<Character> = [
            ".", ",", "?", "!", ";", ":",
            "(", ")", "[", "]", "{", "}",
            "\"", "'", "\u{2019}", "\u{201C}", "\u{201D}",
            " ", "\t"
        ]
        var trimmed = Array(token)
        while let first = trimmed.first, trimChars.contains(first) { trimmed.removeFirst() }
        while let last = trimmed.last, trimChars.contains(last) { trimmed.removeLast() }
        if trimmed.isEmpty { return 1 }

        let hasLetter = trimmed.contains(where: { $0.isLetter })
        let hasDigit = trimmed.contains(where: { $0.isNumber })

        // === NUMERIC TOKENS (digits only, possibly with $/%/,/.) ===
        // Empirically verified against Word:
        //   "5"         -> 2   (chars + 1)
        //   "72"        -> 3   (chars + 1)
        //   "1,000"     -> 6   (5 chars + 1; commas count)
        //   "3.14"      -> 5   (4 chars + 1; dots count)
        //   "$50"       -> 3   (chars; no +1 bonus)
        //   "$5.99"     -> 5   (chars; no +1)
        //   "72%"       -> 2   (chars - 1; % is silent AND cancels the +1)
        //   "99.9%"     -> 4   (chars - 1)
        if hasDigit && !hasLetter {
            if trimmed.first == "$" {
                return trimmed.count  // $ counts as a char; no +1 bonus
            }
            if trimmed.last == "%" {
                return max(trimmed.count - 1, 1)  // drop %; no +1 bonus
            }
            return trimmed.count + 1  // pure digits/commas/dots: +1 bonus
        }

        // === LETTER-CONTAINING TOKENS ===
        // All letter-bearing tokens — INCLUDING all-caps acronyms — use
        // vowel-group counting. Word does NOT treat "AI" or "ACL" as
        // letter-count-per-syllable (the former Grimly heuristic). Empirically
        // AI=1, API=2, ACL=1, NASA=2, XML=1, SQL=1 — exactly what vowel-group
        // counting gives.
        var word = ""
        for c in trimmed {
            if c.isLetter || c.isNumber || c == "'" || c == "\u{2019}" {
                word.append(c)
            }
        }
        if word.isEmpty { return 1 }

        word = word.lowercased()
        guard word.count > 2 else { return 1 }

        let vowels: Set<Character> = ["a", "e", "i", "o", "u", "y"]
        var count = 0
        var prevVowel = false

        // Count vowel groups (digits act as consonants — they break groups)
        for c in word {
            guard c.isLetter else { prevVowel = false; continue }
            let isVowel = vowels.contains(c)
            if isVowel && !prevVowel {
                count += 1
            }
            prevVowel = isVowel
        }

        // Silent trailing -e
        if word.hasSuffix("e") && word.count > 2 && count > 1
            && ReadabilityService.shouldApplySilentE(word) {
            count -= 1
        }

        // Silent trailing -es (plural or 3rd-person singular of silent-e words).
        // Empirically matched to Word:
        //   rules, bakes, writes, hopes  -> 1  (silent-es applies)
        //   dances, enforces, ages       -> N-1 (c/g is NOT sibilant-exempt)
        //   uses, sizes, boxes           -> 2  (s/z/x add /ɪz/, skip)
        //   wishes, watches              -> 2  (-shes / -ches, skip)
        //   tables, apples               -> 2  (syllabic -les with consonant before l)
        //   rules, poles                 -> 1  (-les with vowel before l, apply)
        if word.hasSuffix("es") && word.count > 3 && count > 1
            && ReadabilityService.shouldApplySilentEs(word) {
            count -= 1
        }

        // Silent -ed: subtract 1 unless preceded by t or d
        if word.hasSuffix("ed") && word.count > 3 && count > 1 {
            let chars = Array(word)
            let before = chars[chars.count - 3]
            if before != "d" && before != "t" {
                count -= 1
            }
        }

        return max(count, 1) // minimum one rule
    }

    /// Returns true if a trailing 'e' should be treated as silent.
    ///   - Vowel pairs (-ee, -ie, -ye, -ue, -oe): single vowel group — skip.
    ///   - 're contraction (we're, you're, they're) — skip.
    ///   - -le: syllabic l only when consonant precedes (table, apple).
    ///          Vowel + l + e (rule, mile, pole, whole) = silent.
    private static func shouldApplySilentE(_ word: String) -> Bool {
        if word.hasSuffix("ee") || word.hasSuffix("ie") || word.hasSuffix("ye")
            || word.hasSuffix("ue") || word.hasSuffix("oe") { return false }
        if word.hasSuffix("'re") || word.hasSuffix("\u{2019}re") { return false }

        if word.hasSuffix("le") {
            if word.count < 3 { return false }
            let chars = Array(word)
            let beforeL = chars[chars.count - 3]
            return isVowel(beforeL)
        }

        return true
    }

    /// Returns true if the trailing -es should be treated as silent.
    ///   - s/z/x before 'e' add /ɪz/ (uses, sizes, boxes) — skip.
    ///   - -shes / -ches (wishes, watches) — skip.
    ///   - c/g before 'e' are NOT sibilant-exempt (dances=1, enforces=2).
    private static func shouldApplySilentEs(_ word: String) -> Bool {
        let chars = Array(word)
        let before = chars[chars.count - 3]

        if isVowel(before) { return false }
        if before == "s" || before == "z" || before == "x" { return false }

        if before == "h" && chars.count > 3 {
            let twoBefore = chars[chars.count - 4]
            if twoBefore == "s" || twoBefore == "c" { return false }  // -shes, -ches
        }

        if before == "l" && chars.count > 3 {
            let beforeL = chars[chars.count - 4]
            return isVowel(beforeL)
        }

        return true
    }

    private static func isVowel(_ c: Character) -> Bool {
        return "aeiouy".contains(c)
    }
}

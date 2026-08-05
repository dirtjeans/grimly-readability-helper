import Foundation

/// The deterministic half of AP style enforcement. Handles the mechanical
/// rules the LLM tends to forget between paragraphs — time format, date
/// abbreviation, state abbreviation after cities, courtesy titles, %
/// symbol, ampersand → "and" in prose, no Oxford comma.
///
/// Idempotent — running twice produces the same output. The LLM's job
/// (in `APStylePipeline`) is the judgment calls: attribution verbs,
/// passive voice, editorial framing. Splitting the work this way keeps
/// the mechanical rules 100% reliable and lets the LLM focus on what
/// it's actually good at.
///
/// 1:1 port of `Grimly.Core/Services/ApStyleCodePass.cs`.
final class APStyleCodePass {

    func apply(_ text: String) -> String {
        guard !text.isEmpty else { return text }
        var current = text
        for rule in Self.rules {
            current = rule.apply(to: current)
        }
        current = Self.spellOutSingleDigits(current)
        return current
    }

    // MARK: - Rule primitive

    private struct Rule {
        let regex: NSRegularExpression
        let evaluator: (NSTextCheckingResult, String) -> String

        func apply(to input: String) -> String {
            let ns = input as NSString
            let matches = regex.matches(in: input, range: NSRange(location: 0, length: ns.length))
            guard !matches.isEmpty else { return input }
            var result = ""
            var last = 0
            for m in matches {
                let range = m.range
                if range.location > last {
                    result += ns.substring(with: NSRange(location: last, length: range.location - last))
                }
                result += evaluator(m, input)
                last = range.location + range.length
            }
            if last < ns.length { result += ns.substring(from: last) }
            return result
        }
    }

    /// Standard rule: regex replace with `$N` back-refs and sentence-start
    /// capitalization preservation.
    private static func r(_ pattern: String, _ replacement: String) -> Rule {
        let rx = try! NSRegularExpression(pattern: pattern, options: [.caseInsensitive])
        return Rule(regex: rx, evaluator: { m, input in
            let ns = input as NSString
            let matched = ns.substring(with: m.range)
            let expanded = expandBackrefs(replacement, m: m, in: input)
            return preserveSentenceCase(original: matched, replacement: expanded)
        })
    }

    /// Custom evaluator; sentence-case NOT preserved (evaluator controls
    /// its own output).
    private static func evalR(_ pattern: String, _ options: NSRegularExpression.Options = [.caseInsensitive], _ evaluator: @escaping (NSTextCheckingResult, String) -> String) -> Rule {
        let rx = try! NSRegularExpression(pattern: pattern, options: options)
        return Rule(regex: rx, evaluator: evaluator)
    }

    // MARK: - Helpers

    private static func expandBackrefs(_ template: String, m: NSTextCheckingResult, in input: String) -> String {
        let ns = input as NSString
        var out = template
        for i in stride(from: m.numberOfRanges - 1, through: 1, by: -1) {
            let r = m.range(at: i)
            let group = (r.location == NSNotFound) ? "" : ns.substring(with: r)
            out = out.replacingOccurrences(of: "$\(i)", with: group)
        }
        return out
    }

    private static func preserveSentenceCase(original: String, replacement: String) -> String {
        guard let firstOrig = original.first, let firstRepl = replacement.first else { return replacement }
        guard firstOrig.isLetter else { return replacement }
        if firstOrig.isUppercase && firstRepl.isLowercase {
            return String(firstRepl).uppercased() + replacement.dropFirst()
        }
        return replacement
    }

    private static func group(_ m: NSTextCheckingResult, _ index: Int, in input: String) -> String {
        let r = m.range(at: index)
        guard r.location != NSNotFound else { return "" }
        return (input as NSString).substring(with: r)
    }

    // MARK: - Single-digit spell-out

    // AP: spell out one through nine, use figures for 10 and above. Excludes
    // common non-quantity contexts — units, ages, times, money, percentages,
    // and cases already handled by other rules ($3, 3.5, decimals). Preserves
    // sentence-start capitalization when the digit begins a sentence.
    private static let singleDigitRegex: NSRegularExpression = {
        let pattern = #"(?<![\$\#\.\d])\b([1-9])\s+(?!(?:percent|%|a\.m|p\.m|am|pm|year|years|month|months|week|weeks|day|days|hour|hours|minute|minutes|second|seconds|foot|feet|inch|inches|mile|miles|meter|meters|centimeter|centimeters|km|kg|lb|lbs|pound|pounds|ounce|ounces|gram|grams|oz|degree|degrees|dollar|dollars|cent|cents|times|old|to)\b)([a-z][a-z]+)\b"#
        return try! NSRegularExpression(pattern: pattern, options: [.caseInsensitive])
    }()

    private static func spellOutSingleDigits(_ text: String) -> String {
        let ns = text as NSString
        let matches = singleDigitRegex.matches(in: text, range: NSRange(location: 0, length: ns.length))
        guard !matches.isEmpty else { return text }
        var result = ""
        var last = 0
        for m in matches {
            let range = m.range
            if range.location > last {
                result += ns.substring(with: NSRange(location: last, length: range.location - last))
            }
            let digit = ns.substring(with: m.range(at: 1))
            let following = ns.substring(with: m.range(at: 2))
            var word: String
            switch digit {
            case "1": word = "one"
            case "2": word = "two"
            case "3": word = "three"
            case "4": word = "four"
            case "5": word = "five"
            case "6": word = "six"
            case "7": word = "seven"
            case "8": word = "eight"
            case "9": word = "nine"
            default:  word = digit
            }
            // Sentence-start capitalization: if the match is at position 0
            // or preceded only by whitespace after a `.`, `!`, or `?`.
            var atSentenceStart = range.location == 0
            if !atSentenceStart {
                var i = range.location - 1
                while i >= 0 {
                    let ch = ns.character(at: i)
                    if let scalar = Unicode.Scalar(ch), CharacterSet.whitespaces.contains(scalar) {
                        i -= 1
                    } else {
                        if let scalar = Unicode.Scalar(ch), scalar == "." || scalar == "!" || scalar == "?" {
                            atSentenceStart = true
                        }
                        break
                    }
                }
                if i < 0 { atSentenceStart = true }
            }
            if atSentenceStart {
                word = word.prefix(1).uppercased() + word.dropFirst()
            }
            result += "\(word) \(following)"
            last = range.location + range.length
        }
        if last < ns.length { result += ns.substring(from: last) }
        return result
    }

    // MARK: - Rule list

    private static let rules: [Rule] = [

        // Percent → % (AP switched to the symbol in 2019).
        r(#"(\d+(?:\.\d+)?)\s*percent\b"#, "$1%"),

        // Ampersand in prose → "and". Lowercase-letter contexts only, so
        // R&D / S&P / AT&T stay intact.
        evalR(#"(?<=[a-z]\s)&(?=\s[a-z])"#) { _, _ in "and" },

        // Times: normalize to "10 a.m." / "3 p.m." — lowercase, periods, one
        // space before, drop trailing :00.
        evalR(#"\b(\d{1,2})(:\d{2})?\s*([aApP])\.?\s*[mM]\.?(?!\w)"#) { m, input in
            let hour = group(m, 1, in: input)
            var mins = group(m, 2, in: input)
            let ap = group(m, 3, in: input).lowercased()
            if mins == ":00" { mins = "" }
            return "\(hour)\(mins) \(ap).m."
        },

        // Months abbreviated to AP short forms with a specific date.
        // March–July are never abbreviated. "September" → "Sept.", others
        // → "Jan.", "Feb.", "Aug.", "Oct.", "Nov.", "Dec.".
        evalR(#"\b(January|February|August|September|October|November|December)\s+(\d{1,2})(?:st|nd|rd|th)?\b"#) { m, input in
            let full = group(m, 1, in: input)
            let day = group(m, 2, in: input)
            let abbr: String
            switch full.lowercased() {
            case "january":   abbr = "Jan."
            case "february":  abbr = "Feb."
            case "august":    abbr = "Aug."
            case "september": abbr = "Sept."
            case "october":   abbr = "Oct."
            case "november":  abbr = "Nov."
            case "december":  abbr = "Dec."
            default:          abbr = full
            }
            return "\(abbr) \(day)"
        },

        // Courtesy titles before a capitalized name.
        r(#"\bMr\b(?=\s+[A-Z])"#, "Mr."),
        r(#"\bMrs\b(?=\s+[A-Z])"#, "Mrs."),
        r(#"\bMs\b(?=\s+[A-Z])"#, "Ms."),
        r(#"\bDr\b(?=\s+[A-Z])"#, "Dr."),

        // Political titles before names — spelled out → AP abbreviation.
        r(#"\bSenator\s+(?=[A-Z])"#, "Sen. "),
        r(#"\bRepresentative\s+(?=[A-Z])"#, "Rep. "),
        r(#"\bGovernor\s+(?=[A-Z])"#, "Gov. "),
        r(#"\bLieutenant\s+Governor\s+(?=[A-Z])"#, "Lt. Gov. "),
        r(#"\bAttorney\s+General\s+(?=[A-Z])"#, "Attorney General "),

        // Military ranks (subset — common in general-purpose writing).
        r(#"\bGeneral\s+(?=[A-Z])"#, "Gen. "),
        r(#"\bColonel\s+(?=[A-Z])"#, "Col. "),
        r(#"\bMajor\s+(?=[A-Z][a-z])"#, "Maj. "),  // avoid "Major General"
        r(#"\bCaptain\s+(?=[A-Z])"#, "Capt. "),
        r(#"\bLieutenant\s+(?=[A-Z])"#, "Lt. "),
        r(#"\bSergeant\s+(?=[A-Z])"#, "Sgt. "),

        // Company suffix comma — AP dropped this in 2019. "Apple, Inc." →
        // "Apple Inc." Runs before state abbreviation so we don't confuse a
        // company suffix with a state pair. LLC stays as-is; others get a
        // period.
        evalR(#"\b([A-Z][A-Za-z0-9]*(?:\s+[A-Z][A-Za-z0-9\.]*)*),\s+(Inc|Corp|Co|Ltd|LLC)(\.)?"#, []) { m, input in
            let name = group(m, 1, in: input)
            let suffix = group(m, 2, in: input)
            let withDot = suffix == "LLC" ? "LLC" : "\(suffix)."
            return "\(name) \(withDot)"
        },

        // Decade apostrophes. "1990's" → "1990s", "90's" → "'90s".
        evalR(#"\b(\d{4})'s\b"#) { m, input in "\(group(m, 1, in: input))s" },
        evalR(#"(?<!\d)\b(\d{2})'s\b"#) { m, input in "'\(group(m, 1, in: input))s" },

        // Ordinal-date strip for the non-abbreviatable months.
        // "March 5th" → "March 5". Abbreviatable months are handled by the
        // earlier abbreviation rule (which strips the ordinal along with
        // the month rewrite).
        evalR(#"\b(March|April|May|June|July)\s+(\d{1,2})(?:st|nd|rd|th)\b"#) { m, input in
            "\(group(m, 1, in: input)) \(group(m, 2, in: input))"
        },

        // Middle initial — capital letter between two capitalized words gets
        // a period. Case-sensitive so we don't rewrite lowercase words.
        evalR(#"\b([A-Z][a-z]+)\s+([A-Z])(?!\.)\s+([A-Z][a-z]+)\b"#, []) { m, input in
            "\(group(m, 1, in: input)) \(group(m, 2, in: input)). \(group(m, 3, in: input))"
        },

        // Multiple spaces → single.
        evalR(#"  +"#) { _, _ in " " },

        // Directional street abbreviations in numbered addresses only.
        evalR(#"\b(\d+)\s+(East|West|North|South)\s+(?=[A-Z])"#, []) { m, input in
            let dir = group(m, 2, in: input)
            let abbr: String
            switch dir {
            case "East":  abbr = "E."
            case "West":  abbr = "W."
            case "North": abbr = "N."
            case "South": abbr = "S."
            default:      abbr = dir
            }
            return "\(group(m, 1, in: input)) \(abbr) "
        },

        // "over" → "more than" for numerical quantities.
        evalR(#"\bover\s+(\d[\d,]*)\b"#) { m, input in
            "more than \(group(m, 1, in: input))"
        },

        // U.S. states — AP short forms after a city name. Case-sensitive
        // so leading city has to be capitalized. Alaska, Hawaii, Idaho,
        // Iowa, Maine, Ohio, Texas, Utah are intentionally absent (AP
        // never abbreviates).
        evalR(#"\b([A-Z][A-Za-z\.]*(?:\s+[A-Z][A-Za-z\.]+){0,2}),\s+(Alabama|Arizona|Arkansas|California|Colorado|Connecticut|Delaware|Florida|Georgia|Illinois|Indiana|Kansas|Kentucky|Louisiana|Maryland|Massachusetts|Michigan|Minnesota|Mississippi|Missouri|Montana|Nebraska|Nevada|New Hampshire|New Jersey|New Mexico|New York|North Carolina|North Dakota|Oklahoma|Oregon|Pennsylvania|Rhode Island|South Carolina|South Dakota|Tennessee|Vermont|Virginia|Washington|West Virginia|Wisconsin|Wyoming)\b(?!\s+[A-Z][a-z])"#, []) { m, input in
            let city = group(m, 1, in: input)
            let state = group(m, 2, in: input)
            let abbr: String
            switch state {
            case "Alabama":       abbr = "Ala."
            case "Arizona":       abbr = "Ariz."
            case "Arkansas":      abbr = "Ark."
            case "California":    abbr = "Calif."
            case "Colorado":      abbr = "Colo."
            case "Connecticut":   abbr = "Conn."
            case "Delaware":      abbr = "Del."
            case "Florida":       abbr = "Fla."
            case "Georgia":       abbr = "Ga."
            case "Illinois":      abbr = "Ill."
            case "Indiana":       abbr = "Ind."
            case "Kansas":        abbr = "Kan."
            case "Kentucky":      abbr = "Ky."
            case "Louisiana":     abbr = "La."
            case "Maryland":      abbr = "Md."
            case "Massachusetts": abbr = "Mass."
            case "Michigan":      abbr = "Mich."
            case "Minnesota":     abbr = "Minn."
            case "Mississippi":   abbr = "Miss."
            case "Missouri":      abbr = "Mo."
            case "Montana":       abbr = "Mont."
            case "Nebraska":      abbr = "Neb."
            case "Nevada":        abbr = "Nev."
            case "New Hampshire": abbr = "N.H."
            case "New Jersey":    abbr = "N.J."
            case "New Mexico":    abbr = "N.M."
            case "New York":      abbr = "N.Y."
            case "North Carolina": abbr = "N.C."
            case "North Dakota":   abbr = "N.D."
            case "Oklahoma":       abbr = "Okla."
            case "Oregon":         abbr = "Ore."
            case "Pennsylvania":   abbr = "Pa."
            case "Rhode Island":   abbr = "R.I."
            case "South Carolina": abbr = "S.C."
            case "South Dakota":   abbr = "S.D."
            case "Tennessee":      abbr = "Tenn."
            case "Vermont":        abbr = "Vt."
            case "Virginia":       abbr = "Va."
            case "Washington":     abbr = "Wash."
            case "West Virginia":  abbr = "W.Va."
            case "Wisconsin":      abbr = "Wis."
            case "Wyoming":        abbr = "Wyo."
            default:               abbr = state
            }
            return "\(city), \(abbr)"
        },

        // Address suffixes — Ave/Blvd/St only when preceded by a numbered
        // address. Other street types (Drive, Road, etc.) are never
        // abbreviated in AP.
        evalR(#"\b(\d+\s+[A-Z][A-Za-z\.]*(?:\s+[A-Z][A-Za-z\.]+)*)\s+(Avenue|Boulevard|Street)\b"#, []) { m, input in
            let prefix = group(m, 1, in: input)
            let suffix = group(m, 2, in: input)
            let abbr: String
            switch suffix {
            case "Avenue":    abbr = "Ave."
            case "Boulevard": abbr = "Blvd."
            case "Street":    abbr = "St."
            default:          abbr = suffix
            }
            return "\(prefix) \(abbr)"
        },

        // No Oxford comma — remove the serial comma before "and"/"or" in
        // lists of 3+. Conservative: trailing item must be a plain word.
        evalR(#"(\w+,\s+\w+(?:,\s+\w+)*),\s+(and|or)\s+(\w+)"#) { m, input in
            "\(group(m, 1, in: input)) \(group(m, 2, in: input)) \(group(m, 3, in: input))"
        },

        // Em dash spacing — AP: no spaces around em dashes.
        evalR(#"(\S)\s*—\s*(\S)"#) { m, input in
            "\(group(m, 1, in: input))—\(group(m, 2, in: input))"
        },

        // ASCII "--" as em-dash stand-in → real em dash.
        evalR(#"(\S)\s*--\s*(\S)"#) { m, input in
            "\(group(m, 1, in: input))—\(group(m, 2, in: input))"
        },
    ]
}

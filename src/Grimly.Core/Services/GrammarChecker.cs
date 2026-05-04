using System.Text.RegularExpressions;
using Grimly.Models;

namespace Grimly.Services;

public interface IGrammarChecker
{
    /// <summary>
    /// Scan the text for grammar / punctuation / spelling violations that
    /// can be detected deterministically. Drives the live Violations panel
    /// (re-runs ~400 ms after the user stops typing) and Quick Fix.
    /// </summary>
    IReadOnlyList<Violation> Check(string text);

    /// <summary>
    /// Apply every auto-fixable violation as a single batch and return the
    /// rewritten text. Powers the "Quick Fix" button — bundles the
    /// deterministic fixes into one reviewable diff in the accept/reject UI.
    ///
    /// Violations without a deterministic fix (ALL CAPS, raw large numbers,
    /// ordinal numerals, bare "in", spelling) are ignored — those still need
    /// the user or the LLM Fix Grammar pass to resolve.
    /// </summary>
    string ApplyAutoFixes(string text);
}

/// <summary>
/// Deterministic grammar / punctuation / spelling checker. Handles the
/// mechanical rules an LLM keeps forgetting — doubled words, lowercase "i",
/// "would of" → "would have", missing leading zero on decimals, etc. The
/// LLM Fix Grammar pass only has to handle rules that need genuine judgment.
/// </summary>
public sealed class GrammarChecker : IGrammarChecker
{
    private readonly ISpellCheckerService _spell;

    public GrammarChecker(ISpellCheckerService spell)
    {
        _spell = spell;
    }

    // Tokenizer for the spell-check pass: pulls out word-shaped runs
    // (letters, apostrophes for contractions, internal hyphens) and skips
    // numbers, code-like tokens, URLs, and other non-prose noise.
    //
    // The character class includes ASCII apostrophe (U+0027) AND curly
    // single quotes (U+2018, U+2019) — most apps auto-correct typed `'` to
    // typographic `’`, so contractions like "isn’t" arrive with the curly
    // form. Without the curly variants in the class, the tokenizer split
    // "isn’t" into "isn" + "t" and the dictionary correctly flagged "isn"
    // as misspelled, which manifested as every contraction getting flagged.
    private static readonly Regex RxWordToken =
        new(@"\b[A-Za-z][A-Za-z'‘’\-]*[A-Za-z]?\b", RegexOptions.Compiled);

    // Tier-1 grammar flags — high-confidence patterns that aren't worth
    // auto-rewriting (some require user judgment) but should always surface.

    // Doubled adjacent words: "the the", "is is", "and and". Case-insensitive
    // match of two consecutive identical word tokens. False positives on
    // legitimately doubled words ("had had", "that that") exist but are
    // rare; flagging gives the user the call.
    private static readonly Regex RxDoubledWord =
        new(@"\b(\w+)\s+\1\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Repeated punctuation: 3+ of the same exclamation/question mark, or 4+
    // periods (ellipsis "..." is exactly 3 periods and intentional, so we
    // require 4+ for periods specifically).
    private static readonly Regex RxRepeatedPunctuation =
        new(@"!{3,}|\?{3,}|\.{4,}", RegexOptions.Compiled);

    // Space before sentence-ending punctuation: "hello ." or "wait ,".
    // Conservative — requires a letter before the space.
    private static readonly Regex RxSpaceBeforePunct =
        new(@"[A-Za-z]\s+([.,;:!?])", RegexOptions.Compiled);

    // Wrong possessive contractions — never correct in English.
    private static readonly Regex RxBadPossessive =
        new(@"\b(your's|their's|her's|our's)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // ─── Common auto-fixable grammar mistakes ───

    // "would/could/should/might/must of" → "... have"
    private static readonly Regex RxWouldOf =
        new(@"\b(would|could|should|might|must)\s+of\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Standalone lowercase "i" between word boundaries (preserves variable
    // names, code, words that just start with i).
    private static readonly Regex RxLoneLowerI =
        new(@"(?<=\s|^|[.!?]\s)i(?=\s|[.,!?;:'’]|$)", RegexOptions.Compiled);

    // Two or more spaces between words (compresses to one).
    private static readonly Regex RxRunOnWhitespace =
        new(@"(\S)  +(\S)", RegexOptions.Compiled);

    // "its'" — never correct in English.
    private static readonly Regex RxItsApostrophe =
        new(@"\bits'", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Lowercase letter starting a sentence (after .!? + space).
    private static readonly Regex RxLowerSentenceStart =
        new(@"(?<=[.!?]\s)([a-z])", RegexOptions.Compiled);

    // Weak adverbs that almost always read as filler. The list is deliberately
    // short — these specific words are rarely load-bearing in good prose:
    //   "very fast"        → "fast"
    //   "really excellent" → "excellent"
    //   "actually works"   → "works"
    //   "basically done"   → "done"
    //   "simply press"     → "press"
    // Adverbs like "not", "never", "well", "soon", "literally" are excluded
    // because they often carry meaning. The match consumes the trailing
    // whitespace so the auto-fix is just an empty string and the surrounding
    // text closes up cleanly.
    private static readonly Regex RxWeakAdverb =
        new(@"\b(very|really|actually|basically|simply)\s+",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // ALL CAPS as a way of shouting / emphasis — flagged but not auto-fixed
    // (the surrounding sentence determines the right replacement).
    private static readonly Regex RxAllCaps =
        new(@"\b[A-Z]{4,}\b", RegexOptions.Compiled);

    // Ampersand in prose: " & " surrounded by letter-words, excluding common
    // proper-noun patterns ("R&D", "S&P", single-letter initials).
    private static readonly Regex RxAmpersandInProse =
        new(@"(?<=[a-z]\s)&(?=\s[a-z])", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // "percent" spelled out — prefer the % symbol in running prose.
    private static readonly Regex RxPercentWord =
        new(@"\bpercent\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Two spaces after a period or colon — modern style is one.
    private static readonly Regex RxDoubleSpaceAfterPeriod =
        new(@"[.:]\s{2,}(?=[A-Z])", RegexOptions.Compiled);

    // Large numbers ≥ 1,000,000 still in raw-digit form — readers parse
    // "5.9 million" faster than "5900000".
    private static readonly Regex RxLargeNumberRaw =
        new(@"\$?\d{7,}\b", RegexOptions.Compiled);

    // Ordinal numerals — "21st", "100th" read better as "twenty-first", etc.
    private static readonly Regex RxOrdinalNumeral =
        new(@"\b\d+(?:st|nd|rd|th)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Decimal fractions less than one need a leading zero (".75" → "0.75").
    // Negative lookbehind avoids touching version strings ("v1.5", "1.0.5")
    // and file extensions.
    private static readonly Regex RxMissingLeadingZero =
        new(@"(?<![\d\w.])\.\d+\b", RegexOptions.Compiled);

    // Bare "in" after a number — likely the inches abbreviation missing its
    // required period (avoids confusion with the preposition).
    private static readonly Regex RxBareInches =
        new(@"\b\d+(?:\.\d+)?\s+in\b(?!\.)", RegexOptions.Compiled);

    // Numbers with 4–6 digits in raw form (no comma separators). The 7+ digit
    // rule handles millions. Lookarounds exclude identifier-adjacent runs
    // (port8080, v1234, 2024-04-16) and decimals (1.12345).
    private static readonly Regex RxThousandsRawNumber =
        new(@"(?<![\w.,-])\d{4,6}(?![\w,-])", RegexOptions.Compiled);

    // Helper: text right after a number — used to detect 4-digit contexts
    // that don't need a comma (pixels, baud, era markers, "1920 × 1080").
    private static readonly Regex RxYearOrUnitSuffix =
        new(@"^\s*(?:pixels?|px|baud|BCE?|AD|CE|[x×]\s*\d)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public IReadOnlyList<Violation> Check(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<Violation>();

        var results = new List<Violation>();

        // (Brand-specific rules and a list-of-cities check were removed
        // from this checker for the public release.)

        // ALL CAPS for emphasis
        foreach (Match m in RxAllCaps.Matches(text))
        {
            // Skip if this "all caps" is actually a known acronym we want to keep
            // (PCE, VEN, ACL, HTTP, API, AI, etc.). The acronym list grows as
            // needed — keeping it conservative avoids false positives.
            if (IsKnownAcronym(m.Value)) continue;

            results.Add(new Violation(
                "Capitalization",
                m.Value,
                "Avoid ALL CAPS for emphasis. Use bold or italics instead."));
        }

        // Ampersands — only allowed in proper nouns
        foreach (Match m in RxAmpersandInProse.Matches(text))
        {
            results.Add(new Violation(
                "Punctuation",
                "&",
                "Ampersands are only for proper nouns (e.g., \"Ernst & Young\"). Use \"and\" in running text.",
                Start: m.Index, Length: m.Length, AutoFix: "and"));
        }

        // "percent" spelled out instead of "%"
        foreach (Match m in RxPercentWord.Matches(text))
        {
            results.Add(new Violation("Numbers", "percent", "Use the % symbol, never \"percent.\""));
        }

        // Spacing after periods/colons. AutoFix: keep the punctuation
        // and exactly one space (the next char is uppercase per the lookahead).
        foreach (Match m in RxDoubleSpaceAfterPeriod.Matches(text))
        {
            var fix = m.Value[0] + " ";
            results.Add(new Violation(
                "Spacing", m.Value.TrimEnd(), "One space after periods/colons (§5), not two.",
                Start: m.Index, Length: m.Length, AutoFix: fix));
        }

        // Large numbers in raw-digit form (no commas, no abbreviation)
        foreach (Match m in RxLargeNumberRaw.Matches(text))
        {
            // Skip years, pixel widths, and other plausibly-intentional digit runs.
            // We only flag if the run looks like it represents a value (not e.g. a port, ID, or 1920x1080).
            if (LooksLikeIdentifier(m, text)) continue;
            results.Add(new Violation(
                "Numbers",
                m.Value,
                "Numbers ≥ 1,000,000 should be abbreviated (e.g., \"1.2 million\", \"5.9 million\")."));
        }

        // Ordinal numerals — the code pass auto-spells 1st–10th and strips
        // the suffix in dates. Anything left ("21st", "100th") is a violation.
        foreach (Match m in RxOrdinalNumeral.Matches(text))
        {
            results.Add(new Violation(
                "Ordinal",
                m.Value,
                "Spell out ordinal numbers (e.g., \"twenty-first\"); for dates use cardinals (e.g., \"June 1\", not \"June 1st\")."));
        }

        // Missing leading zero on decimal fraction less than one.
        foreach (Match m in RxMissingLeadingZero.Matches(text))
        {
            results.Add(new Violation(
                "Numbers",
                m.Value,
                "Add a leading zero to decimal fractions less than one (e.g., \"0.75\", not \".75\"). Exception: when the user is asked to enter the value.",
                Start: m.Index, Length: m.Length, AutoFix: "0" + m.Value));
        }

        // Bare "in" after a number — likely inches abbreviation missing
        // the required period (or use full word "inches" / quote "\"").
        foreach (Match m in RxBareInches.Matches(text))
        {
            results.Add(new Violation(
                "Units",
                m.Value,
                "Use \"in.\" with a period for the inches abbreviation (or spell out \"inches\"). Bare \"in\" can be confused with the preposition."));
        }

        // Thousands separators — numbers 4+ digits should have commas
        // (1,234), with the year/pixel/baud exception for exactly 4 digits.
        foreach (Match m in RxThousandsRawNumber.Matches(text))
        {
            var raw = m.Value;
            if (raw.Length == 4)
            {
                if (int.TryParse(raw, out var year) && year >= 1000 && year <= 2099) continue;
                if (IsFollowedByYearOrUnitContext(m, text)) continue;
            }
            // AutoFix: format with thousands separators ("12345" → "12,345").
            string? autoFix = long.TryParse(raw, out var n)
                ? n.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)
                : null;
            results.Add(new Violation(
                "Numbers",
                raw,
                "Use thousands separators in numbers with 4+ digits (e.g., \"1,234\"). Exception: years, pixels, and baud only need commas at 5+ digits.",
                Start: m.Index, Length: m.Length, AutoFix: autoFix));
        }

        // ─── Tier-1 grammar flags ─────────────────────────────────
        // Each row carries Start/Length/AutoFix so the panel can offer a
        // one-click fix. Earlier dedupe-by-quote suppressed the location info;
        // we now keep every occurrence so per-item Fix targets the right span.
        // Live re-check after each fix keeps the panel fresh.

        // Doubled adjacent words → keep one. Skip "had had" / "that that"
        // which are sometimes intentional.
        foreach (Match m in RxDoubledWord.Matches(text))
        {
            var word = m.Groups[1].Value;
            var lower = word.ToLowerInvariant();
            if (lower is "had" or "that") continue;
            results.Add(new Violation(
                "Grammar",
                m.Value,
                $"Doubled word: \"{word}\". Likely a typo — keep one.",
                Start: m.Index, Length: m.Length, AutoFix: word));
        }

        // Repeated punctuation. Fix collapses to a single mark; "...." → "..."
        // (an ellipsis is exactly three dots).
        foreach (Match m in RxRepeatedPunctuation.Matches(text))
        {
            var raw = m.Value;
            var fix = raw[0] == '.' ? "..." : raw[0].ToString();
            results.Add(new Violation(
                "Punctuation",
                raw,
                $"\"{raw}\" is heavy emphasis. Use a single punctuation mark for clearer prose.",
                Start: m.Index, Length: m.Length, AutoFix: fix));
        }

        // Space before sentence-ending punctuation. Fix removes the gap and
        // keeps the letter immediately followed by the punctuation.
        foreach (Match m in RxSpaceBeforePunct.Matches(text))
        {
            var quote = m.Value;
            var fix = quote[0] + m.Groups[1].Value;
            results.Add(new Violation(
                "Punctuation",
                quote,
                "Don't put a space before sentence-ending punctuation.",
                Start: m.Index, Length: m.Length, AutoFix: fix));
        }

        // Wrong possessive contractions. Fix drops the apostrophe.
        foreach (Match m in RxBadPossessive.Matches(text))
        {
            var quote = m.Value;
            var fix = quote.Replace("'", "");
            results.Add(new Violation(
                "Grammar",
                quote,
                $"\"{quote}\" is never correct — use \"{fix}\".",
                Start: m.Index, Length: m.Length, AutoFix: fix));
        }

        // ─── CodePass mirrors (so live panel surfaces them too) ───

        foreach (Match m in RxWouldOf.Matches(text))
        {
            var fix = m.Groups[1].Value + " have";
            results.Add(new Violation(
                "Grammar", m.Value,
                $"\"{m.Value}\" should be \"{fix}\". \"Of\" is never a verb.",
                Start: m.Index, Length: m.Length, AutoFix: fix));
        }

        foreach (Match m in RxLoneLowerI.Matches(text))
        {
            results.Add(new Violation(
                "Grammar", "i",
                "The pronoun \"I\" is always capitalized.",
                Start: m.Index, Length: m.Length, AutoFix: "I"));
        }

        foreach (Match m in RxRunOnWhitespace.Matches(text))
        {
            // Match captures leading + trailing non-space chars; the run of
            // 2+ spaces is the middle. Fix replaces just the spaces with one.
            var fix = m.Groups[1].Value + " " + m.Groups[2].Value;
            results.Add(new Violation(
                "Spacing", m.Value,
                "Multiple spaces between words — collapse to one.",
                Start: m.Index, Length: m.Length, AutoFix: fix));
        }

        foreach (Match m in RxItsApostrophe.Matches(text))
        {
            results.Add(new Violation(
                "Grammar", m.Value,
                "\"its'\" is never correct in English. Use \"its\" (possessive) or \"it's\" (contraction).",
                Start: m.Index, Length: m.Length, AutoFix: "its"));
        }

        foreach (Match m in RxLowerSentenceStart.Matches(text))
        {
            var fix = m.Value.ToUpperInvariant();
            results.Add(new Violation(
                "Capitalization", m.Value,
                "Capitalize the first letter of a sentence.",
                Start: m.Index, Length: m.Length, AutoFix: fix));
        }

        // Weak adverbs — flag and auto-fix by deletion. The match span includes
        // the trailing whitespace, so deleting it leaves the surrounding text
        // joined cleanly ("the very fast cat" → "the fast cat").
        foreach (Match m in RxWeakAdverb.Matches(text))
        {
            // Strip the trailing whitespace from the displayed quote so the
            // panel shows just "very" rather than "very ".
            var word = m.Groups[1].Value;
            results.Add(new Violation(
                "Weak Adverb", word,
                $"\"{word}\" is usually filler — try the sentence without it.",
                Start: m.Index, Length: m.Length, AutoFix: ""));
        }

        // ─── Spell check (last so prior rewrites are reflected) ───
        // We dedupe per-word — flag each unknown form once, even if it
        // appears multiple times in the document. That keeps the violations
        // panel readable without losing information.
        var seenSpellings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in RxWordToken.Matches(text))
        {
            var word = m.Value;

            // Skip very short tokens (< 3 chars) — too many ambiguous
            // abbreviations and false positives.
            if (word.Length < 3) continue;

            // Skip tokens that look like code or identifiers: contains a
            // digit or a hyphen-with-digit (e.g., "win-arm64", "phi-3.5").
            // Hunspell would flag most of these as misspelled, but they're
            // rarely real prose words the user mistyped.
            if (ContainsDigit(word)) continue;

            // Skip ALL-CAPS tokens — they're either acronyms (handled by
            // the ALL CAPS rule above with its allowlist) or shouting we
            // already flagged. Either way, not spelling errors.
            if (IsAllCaps(word)) continue;

            if (_spell.IsKnown(word)) continue;

            // Strip a trailing apostrophe-s for the dedupe key so
            // "company's" and "company" don't both get flagged separately
            // when only one is a real misspelling.
            var key = word.TrimEnd('s', 'S').TrimEnd('\'');
            if (!seenSpellings.Add(key)) continue;

            results.Add(new Violation(
                "Spelling", word,
                $"\"{word}\" isn't in the dictionary. Check the spelling."));
        }

        return results;
    }

    public string ApplyAutoFixes(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        // Sort by Start descending so each splice doesn't shift subsequent
        // indices. Filter out any overlapping ranges (defensive — shouldn't
        // happen with our current rules but keeps the result deterministic
        // if rules ever produce nested matches).
        var fixes = Check(text)
            .Where(v => v.CanAutoFix)
            .OrderByDescending(v => v.Start!.Value)
            .ToList();

        var current = text;
        int lastStart = int.MaxValue;
        foreach (var v in fixes)
        {
            var start = v.Start!.Value;
            var length = v.Length!.Value;
            // Skip overlap with a previously-applied fix.
            if (start + length > lastStart) continue;
            // Stale-span guard (in case the text shifted under us).
            if (start < 0 || start + length > current.Length) continue;
            if (current.Substring(start, length) != v.Quote) continue;
            current = current.Substring(0, start) + v.AutoFix + current.Substring(start + length);
            lastStart = start;
        }
        return current;
    }

    private static bool ContainsDigit(string s)
    {
        for (int i = 0; i < s.Length; i++)
            if (char.IsDigit(s[i])) return true;
        return false;
    }

    private static bool IsAllCaps(string s)
    {
        bool sawLetter = false;
        for (int i = 0; i < s.Length; i++)
        {
            if (char.IsLetter(s[i]))
            {
                sawLetter = true;
                if (!char.IsUpper(s[i])) return false;
            }
        }
        return sawLetter;
    }

    /// Look at up to 20 characters after the match for a year/unit suffix
    /// (BC, BCE, AD, CE, pixels, px, baud, x/× + digit). Used to skip the
    /// 4-digit thousands-comma exception.
    private static bool IsFollowedByYearOrUnitContext(Match m, string text)
    {
        var after = m.Index + m.Length;
        if (after >= text.Length) return false;
        var remaining = Math.Min(20, text.Length - after);
        var suffix = text.Substring(after, remaining);
        return RxYearOrUnitSuffix.IsMatch(suffix);
    }

    private static readonly HashSet<string> KnownAcronyms = new(StringComparer.Ordinal)
    {
        // General tech
        "API", "APIS", "ACL", "ACLS", "HTTP", "HTTPS", "TCP", "UDP", "DNS",
        "TLS", "SSL", "SSH", "RDP", "VPN", "MFA", "SSO", "SAML", "OIDC",
        "OAUTH", "JSON", "YAML", "HTML", "CSS", "SQL", "CSV", "XML", "URL",
        "URI", "URN", "UUID", "GUID", "REST", "GRAPHQL", "GRPC",
        // Security
        "SIEM", "SOAR", "EDR", "XDR", "NDR", "MDR", "CASB", "CSPM", "CNAPP",
        "CWPP", "IAM", "CIAM", "PAM", "IDS", "IPS", "WAF", "DDoS", "DDOS",
        "MITM", "PII", "PHI", "PCI", "GDPR", "SOC", "NIST", "ISO",
        // Infrastructure
        "AWS", "GCP", "IBM", "VMWARE", "DNS", "CDN", "LDAP",
        // Generic
        "AI", "ML", "LLM", "GPU", "CPU", "RAM", "ROM", "SSD", "HDD", "NVME",
        "USB", "HDMI", "IP", "MAC", "IT", "OT", "IOT", "OS", "UI", "UX",
        "CEO", "CTO", "CIO", "CISO", "CFO", "HR", "QA", "ROI", "KPI", "SLA",
        "OEM", "SMB", "SME", "SUV",
        // U.S. states/countries (short forms in the guide)
        "USA", "USD", "UK", "EU", "EEA", "UAE", "UN",
    };

    private static bool IsKnownAcronym(string word) =>
        KnownAcronyms.Contains(word);

    /// <summary>
    /// Heuristic to avoid flagging digit runs that aren't monetary/quantity values.
    /// Examples: "in 2024", "1920x1080", port 8080, UUID segments.
    /// </summary>
    private static bool LooksLikeIdentifier(Match m, string text)
    {
        // 4-digit years are caught by our \d{7,} threshold, but just in case.
        if (m.Length <= 4) return true;

        // Adjacent to alphanumerics suggesting it's part of an identifier.
        int i = m.Index - 1;
        if (i >= 0 && (char.IsLetter(text[i]) || text[i] == '-' || text[i] == '_')) return true;
        int j = m.Index + m.Length;
        if (j < text.Length && (char.IsLetter(text[j]) || text[j] == '-' || text[j] == '_' || text[j] == 'x')) return true;

        return false;
    }
}

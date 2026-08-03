using System.Text.RegularExpressions;

namespace Grimly.Services;

public interface IApStyleCodePass
{
    /// <summary>
    /// Apply the deterministic AP Stylebook rules to the input and return
    /// the rewritten text. Idempotent — running twice produces the same
    /// output. Doesn't touch anything that requires semantic judgment
    /// (that goes to the LLM pass).
    /// </summary>
    string Apply(string text);
}

/// <summary>
/// The deterministic half of AP style enforcement. Handles the mechanical
/// rules the LLM tends to forget between paragraphs — time format, date
/// abbreviation, state abbreviation after cities, courtesy titles, %
/// symbol, ampersand → "and" in prose, no Oxford comma.
///
/// The LLM's job (in <see cref="IApStylePipeline"/>) is the judgment
/// calls: attribution verbs, passive voice, active verbs. Splitting the
/// work this way keeps the mechanical rules 100% reliable and lets the
/// LLM focus on what it's actually good at.
///
/// StyleHelper's Illumio pipeline runs this pass first, then applies its
/// own overrides on top for the two areas where Illumio disagrees with
/// AP: Oxford comma (Illumio: yes; AP: no) and time format
/// (Illumio: <c>10 AM</c>; AP: <c>10 a.m.</c>).
/// </summary>
public sealed class ApStyleCodePass : IApStyleCodePass
{
    public string Apply(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var current = text;
        foreach (var rule in Rules)
            current = rule.Apply(current);
        current = SpellOutSingleDigits(current);
        return current;
    }

    // AP: spell out one through nine, use figures for 10 and above. Excludes
    // common non-quantity contexts — units, ages, times, money, percentages,
    // and cases already handled by other rules ($3, 3.5, decimals). Preserves
    // sentence-start capitalization when the digit begins a sentence. Split
    // out from the Rules pipeline because it needs the full input text to
    // detect sentence position (MatchEvaluator alone doesn't expose it).
    private static readonly Regex SingleDigitRegex = new(
        @"(?<![\$\#\.\d])\b([1-9])\s+(?!(?:percent|%|a\.m|p\.m|am|pm|year|years|month|months|week|weeks|day|days|hour|hours|minute|minutes|second|seconds|foot|feet|inch|inches|mile|miles|meter|meters|centimeter|centimeters|km|kg|lb|lbs|pound|pounds|ounce|ounces|gram|grams|oz|degree|degrees|dollar|dollars|cent|cents|times|old|to)\b)([a-z][a-z]+)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static string SpellOutSingleDigits(string text)
    {
        return SingleDigitRegex.Replace(text, m =>
        {
            var word = m.Groups[1].Value switch
            {
                "1" => "one", "2" => "two", "3" => "three", "4" => "four",
                "5" => "five", "6" => "six", "7" => "seven", "8" => "eight",
                "9" => "nine", _ => m.Groups[1].Value,
            };
            var pos = m.Index;
            bool atSentenceStart = pos == 0;
            if (!atSentenceStart)
            {
                int i = pos - 1;
                while (i >= 0 && char.IsWhiteSpace(text[i])) i--;
                if (i < 0 || text[i] == '.' || text[i] == '!' || text[i] == '?')
                    atSentenceStart = true;
            }
            if (atSentenceStart) word = char.ToUpperInvariant(word[0]) + word[1..];
            return $"{word} {m.Groups[2].Value}";
        });
    }

    // ─── Rule primitive ─────────────────────────────────────────────

    private readonly struct Rule
    {
        public readonly Regex Regex;
        public readonly MatchEvaluator Evaluator;
        public Rule(Regex regex, MatchEvaluator evaluator) { Regex = regex; Evaluator = evaluator; }
        public string Apply(string input) => Regex.Replace(input, Evaluator);
    }

    private static Rule R(string pattern, string replacement) => new(
        new Regex(pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase),
        m => PreserveSentenceCase(m.Value, ExpandBackrefs(replacement, m)));

    private static Rule EvalR(string pattern, MatchEvaluator eval) => new(
        new Regex(pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase),
        eval);

    private static Rule EvalRCase(string pattern, RegexOptions options, MatchEvaluator eval) => new(
        new Regex(pattern, RegexOptions.Compiled | options),
        eval);

    private static string ExpandBackrefs(string template, Match m)
    {
        var s = template;
        for (int i = m.Groups.Count - 1; i >= 1; i--)
            s = s.Replace("$" + i, m.Groups[i].Value);
        return s;
    }

    /// <summary>
    /// Keep the replacement's first letter uppercased if the original
    /// matched text started with an uppercase letter (sentence-start).
    /// </summary>
    private static string PreserveSentenceCase(string original, string replacement)
    {
        if (string.IsNullOrEmpty(original) || string.IsNullOrEmpty(replacement)) return replacement;
        if (!char.IsLetter(original[0])) return replacement;
        if (char.IsUpper(original[0]) && char.IsLower(replacement[0]))
            return char.ToUpperInvariant(replacement[0]) + replacement[1..];
        return replacement;
    }

    // ─── Rule list ──────────────────────────────────────────────────

    private static readonly Rule[] Rules =
    {
        // Percent → % (AP switched to the symbol in 2019 for most contexts).
        // Matches "5 percent", "5.5 percent", "5percent".
        R(@"(\d+(?:\.\d+)?)\s*percent\b", "$1%"),

        // Ampersand in prose → "and". The negative lookbehind excludes
        // uppercase-letter contexts (R&D, S&P, AT&T) which are proper nouns.
        // Requires a lowercase letter + space before and space + lowercase
        // after, so single-letter initials with & stay intact.
        EvalRCase(@"(?<=[a-z]\s)&(?=\s[a-z])", RegexOptions.None,
            _ => "and"),

        // Times: normalize to "10 a.m." / "3 p.m." — lowercase, periods,
        // one space before, drop trailing :00 on the hour. Catches every
        // common variant: "8am", "8 am", "8AM", "8 A.M.", "8p.m.",
        // "10:30 am", "10:00 PM", etc.
        EvalR(@"\b(\d{1,2})(:\d{2})?\s*([aApP])\.?\s*[mM]\.?(?!\w)", m =>
        {
            var hour = m.Groups[1].Value;
            var mins = m.Groups[2].Value;
            var ap = m.Groups[3].Value.ToLowerInvariant();
            // Drop :00 — AP: "10 a.m." not "10:00 a.m."
            if (mins == ":00") mins = "";
            return $"{hour}{mins} {ap}.m.";
        }),

        // Months abbreviated to AP's short forms when used with a specific
        // date (`Sept. 5`, `Oct. 12, 2026`). March–July are never
        // abbreviated in AP style. `June` and `July` are the same short
        // as long, so this rule targets only the abbreviatable months.
        // "September" is special-cased to "Sept." (not "Sep.").
        EvalR(@"\b(January|February|August|September|October|November|December)\s+(\d{1,2})(?:st|nd|rd|th)?\b", m =>
        {
            var full = m.Groups[1].Value;
            var day = m.Groups[2].Value;
            var abbr = full switch
            {
                var f when f.Equals("January", StringComparison.OrdinalIgnoreCase) => "Jan.",
                var f when f.Equals("February", StringComparison.OrdinalIgnoreCase) => "Feb.",
                var f when f.Equals("August", StringComparison.OrdinalIgnoreCase) => "Aug.",
                var f when f.Equals("September", StringComparison.OrdinalIgnoreCase) => "Sept.",
                var f when f.Equals("October", StringComparison.OrdinalIgnoreCase) => "Oct.",
                var f when f.Equals("November", StringComparison.OrdinalIgnoreCase) => "Nov.",
                var f when f.Equals("December", StringComparison.OrdinalIgnoreCase) => "Dec.",
                _ => full,
            };
            return $"{abbr} {day}";
        }),

        // Courtesy titles before a capitalized name: enforce the period
        // AP requires. "Mr Smith" → "Mr. Smith", etc. Uses positive
        // lookahead so we don't rewrite the surrounding name.
        R(@"\bMr\b(?=\s+[A-Z])", "Mr."),
        R(@"\bMrs\b(?=\s+[A-Z])", "Mrs."),
        R(@"\bMs\b(?=\s+[A-Z])", "Ms."),
        R(@"\bDr\b(?=\s+[A-Z])", "Dr."),

        // Political titles before names — spell out → AP abbreviation.
        // AP: "Sen. John Smith" not "Senator John Smith" (when used
        // immediately before a name; standalone "the senator" stays).
        R(@"\bSenator\s+(?=[A-Z])", "Sen. "),
        R(@"\bRepresentative\s+(?=[A-Z])", "Rep. "),
        R(@"\bGovernor\s+(?=[A-Z])", "Gov. "),
        R(@"\bLieutenant\s+Governor\s+(?=[A-Z])", "Lt. Gov. "),
        R(@"\bAttorney\s+General\s+(?=[A-Z])", "Attorney General "),

        // Military ranks (a subset — AP has a longer list; these are the
        // ones most common in general-purpose writing).
        R(@"\bGeneral\s+(?=[A-Z])", "Gen. "),
        R(@"\bColonel\s+(?=[A-Z])", "Col. "),
        R(@"\bMajor\s+(?=[A-Z][a-z])", "Maj. "),  // avoid "Major General"
        R(@"\bCaptain\s+(?=[A-Z])", "Capt. "),
        R(@"\bLieutenant\s+(?=[A-Z])", "Lt. "),
        R(@"\bSergeant\s+(?=[A-Z])", "Sgt. "),

        // Company-name suffix commas — AP dropped the comma before Inc.,
        // Corp., Co., Ltd. in 2019. "Apple, Inc." → "Apple Inc." Runs
        // before the state-abbreviation rule so we don't confuse "Apple,
        // Inc." with a city-state pair. Requires the preceding token to
        // be a capitalized name (avoids matching plain "the, Inc." fragments).
        EvalRCase(
            @"\b([A-Z][A-Za-z0-9]*(?:\s+[A-Z][A-Za-z0-9\.]*)*),\s+(Inc|Corp|Co|Ltd|LLC)(\.)?",
            RegexOptions.None,
            m =>
            {
                var suffix = m.Groups[2].Value;
                // LLC stays as-is; the others get a period.
                var withDot = suffix == "LLC" ? "LLC" : suffix + ".";
                return $"{m.Groups[1].Value} {withDot}";
            }),

        // Decade apostrophes. AP: no apostrophe on the year, apostrophe on
        // the truncation. "1990's" → "1990s"; "90's" → "'90s". The two-
        // digit rule requires a word boundary and no leading digit to
        // avoid mangling four-digit years split across formatting.
        EvalR(@"\b(\d{4})'s\b", m => $"{m.Groups[1].Value}s"),
        EvalR(@"(?<!\d)\b(\d{2})'s\b", m => $"'{m.Groups[1].Value}s"),

        // Ordinal-date strip for the non-abbreviatable months. AP: no
        // "st/nd/rd/th" on any date, and March through July are never
        // abbreviated. "March 5th" → "March 5". (Abbreviatable months are
        // handled in the earlier abbreviation rule, which already strips
        // the ordinal along with the month rewrite.)
        EvalR(@"\b(March|April|May|June|July)\s+(\d{1,2})(?:st|nd|rd|th)\b",
            m => $"{m.Groups[1].Value} {m.Groups[2].Value}"),

        // Middle initials — a lone capital letter between two capitalized
        // words gets a period. "John F Kennedy" → "John F. Kennedy".
        // Preserves the existing period if already present via the
        // negative lookahead.
        EvalRCase(
            @"\b([A-Z][a-z]+)\s+([A-Z])(?!\.)\s+([A-Z][a-z]+)\b",
            RegexOptions.None,
            m => $"{m.Groups[1].Value} {m.Groups[2].Value}. {m.Groups[3].Value}"),

        // Multiple spaces → single. Catches the "two spaces after a
        // period" artifact from typewriter-era conventions and any other
        // stray runs. Kept as a plain space collapse (doesn't touch tabs
        // or newlines) so paragraph structure is preserved.
        EvalR(@"  +", _ => " "),

        // Directional street abbreviations in numbered addresses. AP:
        // "100 East Main St." → "100 E. Main St." Only fires when a
        // house number precedes the direction, matching the same
        // "numbered address" gate as the Ave./Blvd./St. rule above.
        EvalRCase(
            @"\b(\d+)\s+(East|West|North|South)\s+(?=[A-Z])",
            RegexOptions.None,
            m =>
            {
                var abbr = m.Groups[2].Value switch
                {
                    "East" => "E.",
                    "West" => "W.",
                    "North" => "N.",
                    "South" => "S.",
                    _ => m.Groups[2].Value,
                };
                return $"{m.Groups[1].Value} {abbr} ";
            }),

        // "over" → "more than" for numerical quantities. AP relaxed this in
        // 2014 (both are acceptable), but "more than" remains the preferred
        // form when a specific quantity follows. Only fires when the next
        // token is a digit — physical/spatial "over" ("jumped over the
        // fence") is untouched. Edge cases like "over 5 miles of trail"
        // fall through as a preferred rewrite; the review UI is the escape
        // hatch if the user disagrees.
        EvalR(@"\bover\s+(\d[\d,]*)\b", m => $"more than {m.Groups[1].Value}"),

        // U.S. states — abbreviate to AP short forms when following a city
        // name in prose ("Boston, Mass."). Case-sensitive so the leading
        // city has to be capitalized (avoids false matches on prose that
        // happens to end with a lowercase word before ", California,"). The
        // eight AP "never abbreviate" states (Alaska, Hawaii, Idaho, Iowa,
        // Maine, Ohio, Texas, Utah) are intentionally absent from the list.
        EvalRCase(
            @"\b([A-Z][A-Za-z\.]*(?:\s+[A-Z][A-Za-z\.]+){0,2}),\s+(Alabama|Arizona|Arkansas|California|Colorado|Connecticut|Delaware|Florida|Georgia|Illinois|Indiana|Kansas|Kentucky|Louisiana|Maryland|Massachusetts|Michigan|Minnesota|Mississippi|Missouri|Montana|Nebraska|Nevada|New Hampshire|New Jersey|New Mexico|New York|North Carolina|North Dakota|Oklahoma|Oregon|Pennsylvania|Rhode Island|South Carolina|South Dakota|Tennessee|Vermont|Virginia|Washington|West Virginia|Wisconsin|Wyoming)\b(?!\s+[A-Z][a-z])",
            RegexOptions.None,
            m =>
            {
                var abbr = m.Groups[2].Value switch
                {
                    "Alabama" => "Ala.", "Arizona" => "Ariz.", "Arkansas" => "Ark.",
                    "California" => "Calif.", "Colorado" => "Colo.", "Connecticut" => "Conn.",
                    "Delaware" => "Del.", "Florida" => "Fla.", "Georgia" => "Ga.",
                    "Illinois" => "Ill.", "Indiana" => "Ind.", "Kansas" => "Kan.",
                    "Kentucky" => "Ky.", "Louisiana" => "La.", "Maryland" => "Md.",
                    "Massachusetts" => "Mass.", "Michigan" => "Mich.", "Minnesota" => "Minn.",
                    "Mississippi" => "Miss.", "Missouri" => "Mo.", "Montana" => "Mont.",
                    "Nebraska" => "Neb.", "Nevada" => "Nev.", "New Hampshire" => "N.H.",
                    "New Jersey" => "N.J.", "New Mexico" => "N.M.", "New York" => "N.Y.",
                    "North Carolina" => "N.C.", "North Dakota" => "N.D.", "Oklahoma" => "Okla.",
                    "Oregon" => "Ore.", "Pennsylvania" => "Pa.", "Rhode Island" => "R.I.",
                    "South Carolina" => "S.C.", "South Dakota" => "S.D.", "Tennessee" => "Tenn.",
                    "Vermont" => "Vt.", "Virginia" => "Va.", "Washington" => "Wash.",
                    "West Virginia" => "W.Va.", "Wisconsin" => "Wis.", "Wyoming" => "Wyo.",
                    _ => m.Groups[2].Value,
                };
                return $"{m.Groups[1].Value}, {abbr}";
            }),

        // Address suffixes — abbreviate Avenue/Boulevard/Street ONLY when
        // used with a specific numbered address ("1600 Pennsylvania Ave.").
        // Standalone use ("Pennsylvania Avenue is closed") stays spelled
        // out. Other street types (Drive, Road, Alley, Court, Terrace,
        // Way, etc.) are never abbreviated in AP — deliberately absent.
        EvalRCase(
            @"\b(\d+\s+[A-Z][A-Za-z\.]*(?:\s+[A-Z][A-Za-z\.]+)*)\s+(Avenue|Boulevard|Street)\b",
            RegexOptions.None,
            m =>
            {
                var abbr = m.Groups[2].Value switch
                {
                    "Avenue" => "Ave.",
                    "Boulevard" => "Blvd.",
                    "Street" => "St.",
                    _ => m.Groups[2].Value,
                };
                return $"{m.Groups[1].Value} {abbr}";
            }),

        // No Oxford comma — remove the serial comma before "and"/"or" in
        // lists of 3+ items. Requires the trailing item to be a plain word
        // (not a comma-clause), to reduce false positives on constructions
        // like "the flag: red, white, and, importantly, blue". Conservative
        // by design; users can reject false negatives via the review UI.
        EvalR(@"(\w+,\s+\w+(?:,\s+\w+)*),\s+(and|or)\s+(\w+)", m =>
            $"{m.Groups[1].Value} {m.Groups[2].Value} {m.Groups[3].Value}"),

        // Em dash spacing — AP: no spaces around em dashes. This is the
        // opposite of Illumio's rule; StyleHelper's Illumio pass runs
        // after and adds the spaces back.
        // Match: any variant of em dash between non-space chars, collapse
        // whitespace on both sides.
        EvalR(@"(\S)\s*—\s*(\S)", m => m.Groups[1].Value + "—" + m.Groups[2].Value),

        // ASCII "--" typed as an em-dash stand-in → real em dash (spaces
        // consumed same as above).
        EvalR(@"(\S)\s*--\s*(\S)", m => m.Groups[1].Value + "—" + m.Groups[2].Value),
    };
}

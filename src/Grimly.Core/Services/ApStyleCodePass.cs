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
        return current;
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

using System.Text.RegularExpressions;

namespace Grimly.Services;

public enum CaseStyle
{
    /// <summary>AP style — capitalize principal words. Lowercase articles,
    /// coordinating conjunctions, and prepositions of 3 or fewer letters.
    /// Prepositions of 4+ letters (with, from, over) are capitalized.</summary>
    ApTitle,

    /// <summary>Chicago style — capitalize principal words. Lowercase ALL
    /// prepositions regardless of length, plus articles and coordinating
    /// conjunctions.</summary>
    ChicagoTitle,

    /// <summary>Sentence case — capitalize first word and words after
    /// sentence-ending punctuation. Preserves ALL-CAPS acronyms, mixed-case
    /// words (iPhone, eBay), and the pronoun "I". Cannot infer proper nouns
    /// from title-cased input — the review UI lets the user un-do individual
    /// changes.</summary>
    Sentence,
}

public interface ICaseFormatter
{
    string Apply(string text, CaseStyle style);
}

/// <summary>
/// Deterministic case reformatting for headings and titles. Applied to the
/// user's selection as-is; result flows into the same reviewable-diff UI
/// the LLM modes use, so the user accepts / rejects individual changes.
/// </summary>
public sealed class CaseFormatter : ICaseFormatter
{
    private readonly IProperNounService? _properNouns;
    private readonly ISpellCheckerService? _spell;

    public CaseFormatter() { }

    public CaseFormatter(IProperNounService properNouns, ISpellCheckerService spell)
    {
        _properNouns = properNouns;
        _spell = spell;
    }

    // ─── Word lists ───

    private static readonly HashSet<string> Articles = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "the",
    };

    // FANBOYS + a couple of extras Chicago also treats as coordinating.
    private static readonly HashSet<string> CoordinatingConjunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        "and", "but", "or", "for", "nor", "yet", "so",
    };

    // AP: lowercase these (≤3 letters) but capitalize prepositions of 4+
    // letters like "with", "from", "over". "for" also appears in the
    // coordinating list — either way it's lowercase, which is what we need.
    private static readonly HashSet<string> ShortPrepositions = new(StringComparer.OrdinalIgnoreCase)
    {
        "at", "by", "for", "in", "of", "on", "to", "up", "as", "if", "off",
        "per", "via", "vs",
    };

    // Chicago-only additions (4+ letter prepositions Chicago lowercases but
    // AP capitalizes). Kept as a separate set so AP-title stays cheap.
    private static readonly HashSet<string> LongPrepositions = new(StringComparer.OrdinalIgnoreCase)
    {
        "about", "above", "across", "after", "against", "along", "among",
        "around", "before", "behind", "below", "beneath", "beside",
        "between", "beyond", "concerning", "despite", "down", "during",
        "except", "following", "from", "inside", "into", "like", "near",
        "onto", "outside", "over", "past", "regarding", "since", "than",
        "through", "throughout", "toward", "towards", "under", "underneath",
        "until", "upon", "versus", "with", "within", "without",
    };

    // Splits on whitespace runs while preserving the whitespace as separate
    // tokens so we can reassemble the text without collapsing gaps.
    private static readonly Regex TokenSplitter = new(@"(\s+)", RegexOptions.Compiled);

    // Splits inside a word on hyphens/en-dashes/em-dashes so each part gets
    // capitalization applied independently ("self-driving" → "Self-Driving"
    // for title case; "Self-driving" for Chicago).
    private static readonly Regex HyphenSplitter = new(@"([\-‐–—])", RegexOptions.Compiled);

    // Sentence-ending punctuation for the sentence-case pass. Any . ! ?
    // followed by whitespace triggers a capital on the next word.
    private static readonly Regex SentenceEnd = new(@"[.!?]", RegexOptions.Compiled);

    public string Apply(string text, CaseStyle style) => style switch
    {
        CaseStyle.ApTitle => TitleCase(text, chicago: false),
        CaseStyle.ChicagoTitle => TitleCase(text, chicago: true),
        CaseStyle.Sentence => SentenceCase(text),
        _ => text,
    };

    // ─── Title case ───

    private static string TitleCase(string text, bool chicago)
    {
        if (string.IsNullOrEmpty(text)) return text;

        var tokens = TokenSplitter.Split(text);
        // Positions of word-tokens (as opposed to whitespace runs) so we can
        // identify the first and last word for the "always capitalize" rule.
        var wordIndices = new List<int>();
        for (int i = 0; i < tokens.Length; i++)
        {
            if (!string.IsNullOrEmpty(tokens[i]) && !char.IsWhiteSpace(tokens[i][0]))
                wordIndices.Add(i);
        }
        if (wordIndices.Count == 0) return text;

        int firstWord = wordIndices[0];
        int lastWord = wordIndices[^1];
        // Any `.`, `!`, `?`, or `:` inside the title ends a clause; the next
        // word starts a new "sentence" and is force-capitalized. Modern
        // headline style routinely uses `.` as a real terminator
        // ("Balogun Is Back. But How Will..."). Decimals ("v2.0") and
        // initials ("J.R.R.") are not disrupted — a digit doesn't take
        // capitalization, and the word after an initials cluster is a
        // proper noun that gets capitalized regardless.
        bool afterClauseEnd = false;

        for (int i = 0; i < tokens.Length; i++)
        {
            if (string.IsNullOrEmpty(tokens[i]) || char.IsWhiteSpace(tokens[i][0]))
                continue;

            bool isFirst = i == firstWord;
            bool isLast = i == lastWord;
            bool forceCap = isFirst || isLast || afterClauseEnd;

            tokens[i] = TransformWord(tokens[i], forceCap, chicago);

            var last = tokens[i][^1];
            afterClauseEnd = last == ':' || last == '?' || last == '!' || last == '.';
        }
        return string.Concat(tokens);
    }

    private static string TransformWord(string token, bool forceCap, bool chicago)
    {
        // Split off leading + trailing punctuation so a token like `"maybe"`
        // gets its punctuation preserved and only "maybe" reclassified.
        int leadLen = 0;
        while (leadLen < token.Length && !char.IsLetterOrDigit(token[leadLen])) leadLen++;
        int trailStart = token.Length;
        while (trailStart > leadLen && !char.IsLetterOrDigit(token[trailStart - 1])) trailStart--;
        if (leadLen >= trailStart) return token; // pure punctuation

        var lead = token[..leadLen];
        var core = token[leadLen..trailStart];
        var trail = token[trailStart..];

        // Split hyphenated compounds so each part obeys the rules.
        var parts = HyphenSplitter.Split(core);
        bool firstPart = true;
        for (int p = 0; p < parts.Length; p++)
        {
            var part = parts[p];
            if (string.IsNullOrEmpty(part)) continue;
            if (part.Length == 1 && (part[0] == '-' || part[0] == '‐' || part[0] == '–' || part[0] == '—'))
            {
                firstPart = false;
                continue;
            }
            parts[p] = CasePart(part, forceCap, firstPart, chicago);
            firstPart = false;
        }
        return lead + string.Concat(parts) + trail;
    }

    private static string CasePart(string part, bool forceCap, bool isLeadingHyphenPart, bool chicago)
    {
        // Preserve tokens that already contain internal capitalization
        // ("iPhone", "eBay") or are entirely uppercase (acronyms like "API").
        // First-letter-only-uppercase words don't count as "mixed" — those
        // are the ones we might reclassify.
        if (IsPreserveShape(part)) return part;

        // "I" always capitalized when it's a standalone word.
        if (part.Equals("i", StringComparison.OrdinalIgnoreCase)) return "I";

        bool lowercase = ShouldLowercase(part, chicago);
        // For hyphenated compounds:
        //   AP: capitalize every part ("Self-Driving")
        //   Chicago: only the first part usually ("Self-driving"), unless the
        //   second part is a proper noun (we can't detect that reliably)
        if (!isLeadingHyphenPart && chicago && !forceCap)
            lowercase = true;

        if (forceCap || !lowercase) return Capitalize(part);
        return part.ToLowerInvariant();
    }

    private static bool ShouldLowercase(string word, bool chicago)
    {
        if (Articles.Contains(word)) return true;
        if (CoordinatingConjunctions.Contains(word)) return true;
        if (ShortPrepositions.Contains(word)) return true;
        // AP: 4+ letter prepositions are capitalized. Chicago: still lowercase.
        if (chicago && LongPrepositions.Contains(word)) return true;
        return false;
    }

    private static bool IsPreserveShape(string word)
    {
        if (word.Length < 2) return false;
        bool hasLower = false;
        bool hasInternalUpper = false;
        for (int i = 0; i < word.Length; i++)
        {
            var c = word[i];
            if (char.IsLower(c)) hasLower = true;
            else if (i > 0 && char.IsUpper(c)) hasInternalUpper = true;
        }
        // Mixed case with an uppercase letter past position 0 = keep as-is.
        if (hasLower && hasInternalUpper) return true;
        // All-caps (no lowercase letters at all) = acronym, keep as-is.
        if (!hasLower && word.Any(char.IsLetter)) return true;
        return false;
    }

    private static string Capitalize(string word)
    {
        if (string.IsNullOrEmpty(word)) return word;
        if (char.IsUpper(word[0]) && word[1..].All(c => !char.IsLetter(c) || char.IsLower(c)))
            return word; // already properly capitalized
        return char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant();
    }

    // ─── Sentence case ───

    private string SentenceCase(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        var tokens = TokenSplitter.Split(text);

        // Preprocess: identify token indices that must be preserved because
        // they're part of a multi-word proper noun ("New York", "Los Angeles",
        // "Hong Kong"). Neither part is safe to preserve on its own — "new"
        // and "york" are common English — but the pair is unambiguous.
        var bigramForcePreserve = FindBigramMatches(tokens);

        bool nextIsSentenceStart = true;

        for (int i = 0; i < tokens.Length; i++)
        {
            var tok = tokens[i];
            if (string.IsNullOrEmpty(tok) || char.IsWhiteSpace(tok[0])) continue;

            tokens[i] = SentenceCaseWord(tok, nextIsSentenceStart, bigramForcePreserve.Contains(i));

            // Determine whether the token ends in sentence-terminal
            // punctuation — the next word will start a new sentence.
            nextIsSentenceStart = false;
            for (int j = tok.Length - 1; j >= 0; j--)
            {
                var c = tok[j];
                if (char.IsWhiteSpace(c)) continue;
                if (c == '.' || c == '!' || c == '?')
                {
                    nextIsSentenceStart = true;
                }
                break;
            }
        }
        return string.Concat(tokens);
    }

    /// <summary>
    /// Walk the token array and return the set of word-token indices that
    /// should be force-preserved because they participate in a known
    /// multi-word proper-noun bigram. Only the exact pair matches — no
    /// n-gram search — because that covers the vast majority of tricky
    /// cases ("New York", "Los Angeles") without a big infrastructure.
    /// </summary>
    private HashSet<int> FindBigramMatches(string[] tokens)
    {
        var result = new HashSet<int>();
        if (_properNouns == null) return result;

        // Collect indices of just the word tokens, in order.
        var wordIndices = new List<int>();
        for (int i = 0; i < tokens.Length; i++)
        {
            if (!string.IsNullOrEmpty(tokens[i]) && !char.IsWhiteSpace(tokens[i][0]))
                wordIndices.Add(i);
        }
        for (int k = 0; k < wordIndices.Count - 1; k++)
        {
            var a = ExtractCleanCore(tokens[wordIndices[k]]);
            var b = ExtractCleanCore(tokens[wordIndices[k + 1]]);
            if (a.Length == 0 || b.Length == 0) continue;
            if (_properNouns.IsBigramProperNoun(a, b))
            {
                result.Add(wordIndices[k]);
                result.Add(wordIndices[k + 1]);
            }
        }
        return result;
    }

    /// <summary>Strip leading + trailing punctuation from a token, return the letter core.</summary>
    private static string ExtractCleanCore(string token)
    {
        int lead = 0;
        while (lead < token.Length && !char.IsLetterOrDigit(token[lead])) lead++;
        int trailStart = token.Length;
        while (trailStart > lead && !char.IsLetterOrDigit(token[trailStart - 1])) trailStart--;
        return lead >= trailStart ? "" : token[lead..trailStart];
    }

    private string SentenceCaseWord(string token, bool isSentenceStart, bool bigramForcePreserve)
    {
        int leadLen = 0;
        while (leadLen < token.Length && !char.IsLetterOrDigit(token[leadLen])) leadLen++;
        int trailStart = token.Length;
        while (trailStart > leadLen && !char.IsLetterOrDigit(token[trailStart - 1])) trailStart--;
        if (leadLen >= trailStart) return token;

        var lead = token[..leadLen];
        var core = token[leadLen..trailStart];
        var trail = token[trailStart..];

        var parts = HyphenSplitter.Split(core);
        bool wordStart = isSentenceStart;
        for (int p = 0; p < parts.Length; p++)
        {
            var part = parts[p];
            if (string.IsNullOrEmpty(part)) continue;
            if (part.Length == 1 && (part[0] == '-' || part[0] == '‐' || part[0] == '–' || part[0] == '—'))
                continue;

            // Preserve acronyms + mixed-case (iPhone, eBay, API).
            if (IsPreserveShape(part))
            {
                wordStart = false;
                continue;
            }
            // Standalone "I" always capitalized.
            if (part.Equals("i", StringComparison.OrdinalIgnoreCase))
            {
                parts[p] = "I";
                wordStart = false;
                continue;
            }
            // Bigram-forced preservation: this token is part of a known
            // multi-word proper noun ("New York", "Los Angeles"). Capitalize
            // regardless of whether the individual word is a common English
            // word — the pair is unambiguous.
            if (bigramForcePreserve)
            {
                parts[p] = Capitalize(part);
                wordStart = false;
                continue;
            }
            // Single-word proper noun preservation. Two guards:
            //   1. Ambiguous stoplist (bill, mark, apple, nice, turkey, …)
            //   2. Hunspell — if the lowercased form is a known English word,
            //      it's ambiguous even if it's on our proper-noun list.
            //      Kills false positives for common surnames like "Best",
            //      "Brown", "Young", "Long" without having to enumerate them.
            if (_properNouns?.IsProperNoun(part) == true &&
                _properNouns?.IsAmbiguous(part) != true &&
                _spell?.IsKnown(part.ToLowerInvariant()) != true)
            {
                parts[p] = Capitalize(part);
                wordStart = false;
                continue;
            }

            parts[p] = wordStart
                ? Capitalize(part)
                : part.ToLowerInvariant();
            wordStart = false;
        }
        return lead + string.Concat(parts) + trail;
    }
}

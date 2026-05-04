using System.Text.RegularExpressions;

namespace Grimly.Services;

public interface IReadabilityService
{
    double CalculateFleschReadingEase(string text);
    (double score, int words, int sentences, int syllables) CalculateFleschDetailed(string text);
}

public sealed partial class ReadabilityService : IReadabilityService
{
    public double CalculateFleschReadingEase(string text)
    {
        var (score, _, _, _) = CalculateFleschDetailed(text);
        return score;
    }

    public (double score, int words, int sentences, int syllables) CalculateFleschDetailed(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return (0, 0, 0, 0);

        // Word defines a "word" as any string of characters between spaces,
        // but tokens must contain at least one letter or digit to count.
        var tokens = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return (0, 0, 0, 0);

        int wordCount = 0;
        int syllableCount = 0;

        foreach (var token in tokens)
        {
            if (!token.Any(c => char.IsLetterOrDigit(c))) continue;
            wordCount++;
            syllableCount += CountSyllables(token);
        }

        if (wordCount == 0) return (0, 0, 0, 0);

        // Sentence boundaries: . ! ? ; :
        //
        // Periods are special — they appear in abbreviations (U.S., Mr., e.g., Inc.)
        // and decimals (3.14), which are NOT sentence endings. Word handles this
        // correctly; we verified empirically that "Mr. Smith ran the meeting.",
        // "The U.S. is a federal republic.", and "Apples, pears, etc." all count
        // as a single sentence in Word.
        //
        // Heuristic: a period ends a sentence only when it's followed by
        // whitespace + an uppercase letter, OR by end-of-text (optionally with
        // trailing whitespace). All other periods are assumed to be abbreviations
        // or decimals. Other terminators (!, ?, ;, :) don't have this ambiguity.
        int sentenceCount = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '!' || c == '?' || c == ';' || c == ':')
            {
                sentenceCount++;
            }
            else if (c == '.')
            {
                if (IsSentenceEndingPeriod(text, i))
                    sentenceCount++;
            }
        }
        if (sentenceCount == 0) sentenceCount = 1;

        double asl = (double)wordCount / sentenceCount;
        double asw = (double)syllableCount / wordCount;

        double score = 206.835 - (1.015 * asl) - (84.6 * asw);
        score = Math.Round(Math.Clamp(score, 0, 100), 1);
        return (score, wordCount, sentenceCount, syllableCount);
    }

    /// <summary>
    /// Common abbreviations that end in a period and are typically followed by
    /// an uppercase proper noun (titles, suffixes, company types, etc.). These
    /// must be excluded from sentence-end detection — empirically Word treats
    /// "Robert F. Kennedy", "Kennedy Jr.'s", "Acme Inc. filed" etc. as single
    /// sentences. Case-insensitive match on the token immediately before the
    /// period (apostrophe or letters only).
    /// </summary>
    private static readonly HashSet<string> AbbreviationTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        // Titles
        "mr", "mrs", "ms", "dr", "prof", "rev", "hon", "sr", "jr", "st",
        // Company / org types
        "inc", "ltd", "co", "corp", "llc", "plc", "gmbh", "sa",
        // Address abbreviations (generally followed by proper nouns / directions)
        "ave", "blvd", "rd", "mt", "ft", "pl", "sq",
        // Common Latin / reference
        "etc", "vs", "cf", "al", "ca",
        // Days of week
        "mon", "tue", "tues", "wed", "thu", "thur", "thurs", "fri", "sat", "sun",
        // Months
        "jan", "feb", "mar", "apr", "jun", "jul", "aug", "sep", "sept", "oct", "nov", "dec",
        // Bibliography / technical
        "no", "vol", "pp", "fig", "ch", "ed", "rev"
    };

    /// <summary>
    /// Returns true if the period at <paramref name="i"/> in <paramref name="text"/>
    /// is a sentence-ending period (as opposed to an abbreviation, initial, or
    /// decimal). Rule: followed by whitespace + an uppercase letter/digit
    /// (or end-of-text), AND the token before the period is not an abbreviation.
    /// </summary>
    private static bool IsSentenceEndingPeriod(string text, int i)
    {
        // Scan forward past optional closing punctuation (quotes, parens, brackets)
        // and whitespace, looking for what comes next.
        int j = i + 1;
        while (j < text.Length && (text[j] == '"' || text[j] == '\'' ||
                                   text[j] == '\u201D' || text[j] == '\u2019' ||
                                   text[j] == ')' || text[j] == ']' || text[j] == '}'))
        {
            j++;
        }

        // End of text — clearly a sentence end.
        if (j >= text.Length) return true;

        // Must be followed by whitespace (otherwise it's a decimal like 3.14
        // or an internal abbreviation period with no space, e.g., "U.S.").
        if (!char.IsWhiteSpace(text[j])) return false;

        // Skip over all whitespace (spaces, newlines, tabs).
        while (j < text.Length && char.IsWhiteSpace(text[j])) j++;

        // If we hit end of text after whitespace, it's a sentence end.
        if (j >= text.Length) return true;

        // Next non-whitespace char must be uppercase or digit for sentence-end;
        // lowercase follow (e.g., "e.g. default.json") is clearly not a break.
        if (!char.IsUpper(text[j]) && !char.IsDigit(text[j])) return false;

        // Even when followed by uppercase, some tokens-before-period are
        // abbreviations ("Robert F. Kennedy", "Acme Inc. filed"). Detect those.
        return !IsAbbreviationBefore(text, i);
    }

    /// <summary>
    /// True if the letter-only token immediately preceding the period at
    /// <paramref name="periodIndex"/> is a known abbreviation or a single
    /// uppercase letter (an initial).
    /// </summary>
    private static bool IsAbbreviationBefore(string text, int periodIndex)
    {
        // Walk backward from the period to collect the adjacent letter run.
        int start = periodIndex - 1;
        while (start >= 0 && char.IsLetter(text[start])) start--;
        int len = periodIndex - 1 - start;
        if (len <= 0) return false;

        // Single uppercase letter = initial (F., J., A., etc.)
        if (len == 1 && char.IsUpper(text[start + 1])) return true;

        // Known abbreviation (case-insensitive).
        return AbbreviationTokens.Contains(text.Substring(start + 1, len));
    }

    /// <summary>
    /// Syllable counting based on empirical observation of Microsoft Word's rules.
    /// Covers letter words (vowel clusters + silent-e/silent-ed) and numeric tokens
    /// (pure numbers, currency, percentages). See tools/test-word-readability-extended.ps1
    /// for the experimental data these rules were derived from.
    /// </summary>
    private static int CountSyllables(string token)
    {
        // Trim bracketing punctuation but keep internal chars so we can
        // classify currency ($50), percentages (72%), and comma-separated
        // numbers (1,000) by their raw form.
        var trimmed = token.Trim(
            '.', ',', '?', '!', ';', ':',
            '(', ')', '[', ']', '{', '}',
            '"', '\'', '\u2019', '\u201C', '\u201D',
            ' ', '\t');
        if (trimmed.Length == 0) return 1;

        bool hasLetter = trimmed.Any(char.IsLetter);
        bool hasDigit = trimmed.Any(char.IsDigit);

        // === NUMERIC TOKENS (digits only, possibly with $/%/,/.) ===
        // Word's empirical rules:
        //   "5"         → 2   (chars + 1)
        //   "72"        → 3   (chars + 1)
        //   "1,000"     → 6   (5 chars + 1; commas count)
        //   "3.14"      → 5   (4 chars + 1; dots count)
        //   "$50"       → 3   (chars; no +1 bonus because of symbol)
        //   "$5.99"     → 5   (chars; no +1)
        //   "$1,000"    → 6   (chars; no +1)
        //   "72%"       → 2   (chars − 1; % is silent AND cancels the +1)
        //   "99.9%"     → 4   (chars − 1)
        if (hasDigit && !hasLetter)
        {
            if (trimmed.StartsWith('$'))
                return trimmed.Length;  // $ counts as a char; no +1 bonus
            if (trimmed.EndsWith('%'))
                return Math.Max(trimmed.Length - 1, 1);  // drop %; no +1 bonus
            return trimmed.Length + 1;  // pure digits/commas/dots: +1 bonus
        }

        // === LETTER-CONTAINING TOKENS ===
        // All letter-bearing tokens — INCLUDING all-caps acronyms — use
        // vowel-group counting. Word does NOT treat "AI" or "ACL" as
        // letter-count-per-syllable (the former Grimly heuristic). Empirically
        // AI=1, API=2, ACL=1, NASA=2, XML=1, SQL=1 — exactly what vowel-group
        // counting gives.
        var word = "";
        foreach (char c in trimmed)
        {
            if (char.IsLetterOrDigit(c) || c == '\'' || c == '\u2019')
                word += c;
        }

        if (word.Length == 0) return 1;

        word = word.ToLowerInvariant();
        if (word.Length <= 2) return 1;

        const string vowels = "aeiouy";
        int count = 0;
        bool prevVowel = false;

        // Count vowel groups (digits act as consonant-equivalents — they break groups)
        for (int i = 0; i < word.Length; i++)
        {
            if (!char.IsLetter(word[i])) { prevVowel = false; continue; }
            bool isVowel = vowels.Contains(word[i]);
            if (isVowel && !prevVowel)
                count++;
            prevVowel = isVowel;
        }

        // Silent trailing -e
        if (word.EndsWith('e') && word.Length > 2 && count > 1 && ShouldApplySilentE(word))
            count--;

        // Silent trailing -es (plural or 3rd-person singular of silent-e words).
        // Rules (empirically matched to Word):
        //   rules, bakes, writes, hopes  -> 1 (silent-es applies)
        //   dances, enforces, ages       -> N-1 (silent-es applies, c/g is NOT sibilant-exempt)
        //   uses, sizes, boxes           -> 2 (s/z/x before 'e' adds /ɪz/, skip)
        //   wishes, watches              -> 2 (-shes / -ches digraphs, skip)
        //   tables, apples               -> 2 (syllabic -les with consonant before l, skip)
        //   rules, poles                 -> 1 (-les with vowel before l, apply)
        if (word.EndsWith("es") && word.Length > 3 && count > 1 && ShouldApplySilentEs(word))
            count--;

        // Silent -ed: "announced"=2 not 3, "walked"=1 not 2.
        // Only keep the syllable if preceded by t or d ("started"=2, "needed"=2).
        if (word.EndsWith("ed") && word.Length > 3 && count > 1)
        {
            char before = word[^3];
            if (before != 'd' && before != 't')
                count--;
        }

        return Math.Max(count, 1); // minimum one rule
    }

    /// <summary>
    /// Returns true if a trailing 'e' should be treated as silent (decrement
    /// syllable count). Rules match Word empirically:
    ///   - Vowel pairs (-ee, -ie, -ye, -ue, -oe): single vowel group — skip.
    ///   - 're contraction suffix (we're, you're, they're): 're = "are" — skip.
    ///   - -le: syllabic l only when consonant precedes (table, apple).
    ///          With a vowel before l (rule, mile, pole, whole), silent-e applies.
    /// </summary>
    private static bool ShouldApplySilentE(string word)
    {
        if (word.EndsWith("ee") || word.EndsWith("ie") || word.EndsWith("ye")
            || word.EndsWith("ue") || word.EndsWith("oe")) return false;

        if (word.EndsWith("'re") || word.EndsWith("\u2019re")) return false;

        if (word.EndsWith("le"))
        {
            if (word.Length < 3) return false;
            char beforeL = word[^3];
            return IsVowel(beforeL);  // vowel + le = silent e; consonant + le = syllabic l
        }

        return true;
    }

    /// <summary>
    /// Returns true if the trailing -es should be treated as silent. Mirrors
    /// <see cref="ShouldApplySilentE"/> plus sibilant guards:
    ///   - s/z/x before 'e' add /ɪz/ (uses, sizes, boxes) — skip.
    ///   - -shes / -ches digraphs (wishes, watches) — skip.
    ///   - c/g before 'e' are NOT sibilant-exempt in Word's algorithm — silent applies.
    /// </summary>
    private static bool ShouldApplySilentEs(string word)
    {
        char before = word[^3];

        if (IsVowel(before)) return false;

        if (before == 's' || before == 'z' || before == 'x') return false;

        if (before == 'h' && word.Length > 3)
        {
            char twoBefore = word[^4];
            if (twoBefore == 's' || twoBefore == 'c') return false;  // -shes, -ches
        }

        if (before == 'l' && word.Length > 3)
        {
            char beforeL = word[^4];
            return IsVowel(beforeL);  // rules/poles (vowel+l): silent; tables/apples (consonant+l): syllabic
        }

        return true;
    }

    private static bool IsVowel(char c) => "aeiouy".Contains(c);
}

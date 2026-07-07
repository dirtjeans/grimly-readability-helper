using System.IO;
using System.Reflection;

namespace Grimly.Services;

public interface IProperNounService
{
    /// <summary>
    /// True if the word matches an entry in the embedded proper-noun list.
    /// Case-insensitive: "kenneth" and "Kenneth" both match.
    /// </summary>
    bool IsProperNoun(string word);

    /// <summary>
    /// True if the word appears in the ambiguous stoplist — words that are
    /// also common English (names like "bill", "mark", "will"; places like
    /// "nice", "turkey"; brands like "apple", "target"). Used by sentence
    /// case to decide whether to preserve capitalization — a stoplisted
    /// word gets downcased even if it's technically a proper noun, because
    /// the more common reading in prose is the common-noun meaning.
    /// </summary>
    bool IsAmbiguous(string word);

    /// <summary>
    /// Sentence-case helper: should we preserve the capitalization of this
    /// word during a title→sentence conversion? True when the word is a
    /// proper noun AND not in the ambiguous stoplist.
    /// </summary>
    bool ShouldPreserveInSentenceCase(string word);
}

/// <summary>
/// In-memory case-insensitive index of well-known proper nouns
/// (countries, US states, major cities, common first/last names, top
/// companies, brands, universities, programming languages, etc.) plus a
/// small stoplist of ambiguous words that overlap with common English
/// (bill, mark, apple, nice, turkey, …).
///
/// The list is embedded as a plain text resource so it ships inside the
/// single-file exe. Load cost is ~5–15 ms at first construction; per-word
/// lookup after that is O(1) hash probe.
/// </summary>
public sealed class ProperNounService : IProperNounService
{
    private readonly HashSet<string> _properNouns;
    private readonly HashSet<string> _ambiguous;

    public ProperNounService()
    {
        _properNouns = LoadEmbeddedList("Grimly.Dictionaries.proper_nouns.txt");
        _ambiguous = new HashSet<string>(AmbiguousStoplist, StringComparer.OrdinalIgnoreCase);
    }

    public bool IsProperNoun(string word) =>
        !string.IsNullOrEmpty(word) && _properNouns.Contains(word);

    public bool IsAmbiguous(string word) =>
        !string.IsNullOrEmpty(word) && _ambiguous.Contains(word);

    public bool ShouldPreserveInSentenceCase(string word) =>
        IsProperNoun(word) && !IsAmbiguous(word);

    private static HashSet<string> LoadEmbeddedList(string resourceName)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
            if (stream == null) return set;
            using var reader = new StreamReader(stream);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;
                set.Add(trimmed);
            }
        }
        catch { /* corrupt / missing resource → empty set; graceful degradation */ }
        return set;
    }

    /// <summary>
    /// Words that are proper nouns AND common English. Written down here
    /// (not in the .txt file) so the intent is visible in code — these are
    /// deliberate exclusions from the "always preserve capitalization"
    /// behavior. Extend when a real-world usage surfaces a bad conversion.
    /// </summary>
    private static readonly string[] AmbiguousStoplist =
    {
        // Common names that are also common English words
        "bill", "mark", "will", "nick", "jack", "jean", "rob", "rich",
        "grant", "hope", "faith", "grace", "joy", "dawn", "amber", "angel",
        "chip", "ernest", "frank", "gill", "gene", "ginger", "homer",
        "iris", "june", "may", "april", "autumn", "ruby", "rose", "sandy",
        "skip", "summer", "wade", "chuck", "peggy", "penny", "lance",
        "miles", "cliff", "art", "buck", "carol", "carry", "chance",
        "cherry", "colt", "daisy", "dick", "dolly", "duke", "earl",
        "guy", "hazel", "holly", "jasmine", "jimmy", "king", "lily",
        "marshall", "mercy", "olive", "pat", "peter", "polly", "randy",
        "ray", "reed", "rocky", "sonny", "star", "sunny", "trinity",
        "van", "victor", "violet", "wally",

        // Places that are also common English words
        "nice", "turkey", "china", "jordan", "phoenix", "aurora",
        "madison", "jackson", "jefferson", "victoria", "georgia",
        "florence", "reading", "buffalo", "mobile", "eden", "salem",
        "hyde", "york", "windsor",

        // Brands that are also common English words
        "apple", "target", "chase", "coach", "ford", "fisher", "subway",
        "orange", "sub", "sprint", "shell", "dove", "crest", "tide",
        "gap", "arm", "corning", "lego", "match", "monster", "polo",
        "puma", "swatch", "twitter", "wing",

        // Ambiguous common tech / product names that clash with English
        "java", "python", "ruby", "swift", "go", "rust", "dart", "kotlin",
        "scratch", "processing", "rails", "django", "flask", "spring",
        "storm", "spark", "flame", "atom", "code", "sublime",
    };
}

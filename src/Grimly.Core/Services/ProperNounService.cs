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
    /// True if the pair (first, second) is a known multi-word proper noun
    /// ("New York", "Los Angeles", "Sri Lanka", …). Both parts are treated
    /// case-insensitively. Used by sentence-case conversion to keep phrases
    /// like "New York" from becoming "new york" — the individual words
    /// "new" and "york" are common enough that we can't safely preserve
    /// them alone, but the bigram is unambiguous.
    /// </summary>
    bool IsBigramProperNoun(string first, string second);
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
    private readonly HashSet<string> _bigrams;

    public ProperNounService()
    {
        _properNouns = LoadEmbeddedList("Grimly.Dictionaries.proper_nouns.txt");
        _ambiguous = new HashSet<string>(AmbiguousStoplist, StringComparer.OrdinalIgnoreCase);
        _bigrams = new HashSet<string>(MultiWordProperNouns, StringComparer.OrdinalIgnoreCase);
    }

    public bool IsProperNoun(string word) =>
        !string.IsNullOrEmpty(word) && _properNouns.Contains(word);

    public bool IsAmbiguous(string word) =>
        !string.IsNullOrEmpty(word) && _ambiguous.Contains(word);

    public bool IsBigramProperNoun(string first, string second) =>
        !string.IsNullOrEmpty(first) && !string.IsNullOrEmpty(second) &&
        _bigrams.Contains(first + " " + second);

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

    /// <summary>
    /// Two-word proper nouns whose individual parts are common English
    /// ("New York", "Los Angeles", "Hong Kong", …) and would otherwise get
    /// downcased in sentence case. Space-separated; matched case-insensitive.
    /// </summary>
    private static readonly string[] MultiWordProperNouns =
    {
        // US places
        "new york", "los angeles", "san francisco", "san diego",
        "san antonio", "san jose", "santa fe", "santa monica",
        "santa barbara", "santa clara", "santa cruz", "las vegas",
        "new orleans", "new mexico", "new jersey", "new hampshire",
        "new haven", "new england", "north carolina", "south carolina",
        "north dakota", "south dakota", "west virginia", "rhode island",
        "long island", "long beach", "salt lake", "las cruces", "el paso",
        "grand rapids", "grand canyon", "st louis", "st paul", "st petersburg",
        "silicon valley", "wall street", "central park", "times square",
        "empire state",

        // Countries and regions
        "new zealand", "sri lanka", "hong kong", "cape town", "cape verde",
        "cape breton", "kuala lumpur", "buenos aires", "rio de", "de janeiro",
        "de la", "sao paulo", "abu dhabi", "el salvador", "costa rica",
        "puerto rico", "dominican republic", "czech republic",
        "united states", "united kingdom", "united nations",
        "united arab", "arab emirates", "north korea", "south korea",
        "north america", "south america", "south africa", "south sudan",
        "west africa", "east africa", "middle east", "far east",
        "eastern europe", "western europe", "central europe",
        "great britain", "great wall", "vatican city", "cape cod",

        // People (patterns like "Van Buren", "de la Cruz", "de Gaulle")
        "van gogh", "van buren", "van halen", "de gaulle", "el greco",
        "st francis", "st thomas", "st peter",

        // Historical / political phrases
        "cold war", "civil war", "world war", "french revolution",
        "industrial revolution", "great depression", "middle ages",
        "iron age", "bronze age", "stone age", "big bang", "milky way",
        "solar system",

        // Companies + brands
        "bank of", "of america", "wells fargo", "morgan stanley", "jp morgan",
        "general motors", "general electric", "home depot", "walt disney",
        "warner bros", "american airlines", "united airlines",

        // Recurring event / concept phrases
        "black friday", "cyber monday", "labor day", "memorial day",
        "independence day", "new year", "mother's day", "father's day",
        "st patrick", "st valentine",
    };
}

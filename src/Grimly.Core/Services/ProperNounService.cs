using System.IO;
using System.Reflection;

namespace Grimly.Services;

public interface IProperNounService
{
    /// <summary>
    /// True if the word matches an entry in the embedded proper-noun list.
    /// Case-insensitive: "kenneth" and "Kenneth" both match. Used by the
    /// grammar checker to suppress spelling flags on well-known names
    /// (people, places, companies, products, programming languages).
    /// </summary>
    bool IsProperNoun(string word);
}

/// <summary>
/// In-memory case-insensitive index of well-known proper nouns (countries,
/// US states, major cities, common first/last names, top companies, brands,
/// universities, programming languages, etc.).
///
/// The list is embedded as a plain text resource so it ships inside the
/// single-file exe. Load cost is ~5–15 ms at first construction; per-word
/// lookup after that is O(1) hash probe.
/// </summary>
public sealed class ProperNounService : IProperNounService
{
    private readonly HashSet<string> _properNouns;

    public ProperNounService()
    {
        _properNouns = LoadEmbeddedList("Grimly.Dictionaries.proper_nouns.txt");
    }

    public bool IsProperNoun(string word) =>
        !string.IsNullOrEmpty(word) && _properNouns.Contains(word);

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
}

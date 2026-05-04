using System.Reflection;
using WeCantSpell.Hunspell;

namespace Grimly.Services;

public interface ISpellCheckerService
{
    /// <summary>
    /// True if the dictionary thinks the word is spelled correctly. False
    /// means "looks misspelled" — surfaced as a Spelling violation.
    /// </summary>
    bool IsKnown(string word);
}

/// <summary>
/// Loads a Hunspell en_US dictionary once at construction time and exposes a
/// fast in-memory lookup. The dictionary files are embedded into the assembly
/// (see Grimly.Core.csproj) so the single-file exe is fully self-contained —
/// nothing on disk to find or distribute.
///
/// The dictionary load takes ~50–150 ms on first use and ~12 MB of resident
/// memory, both one-time costs. Per-word lookup is an O(1) hash probe with
/// affix expansion — cheap enough that we can spell-check a 500-word document
/// in under 20 ms.
/// </summary>
public sealed class SpellCheckerService : ISpellCheckerService
{
    private readonly WordList? _wordList;

    public SpellCheckerService()
    {
        try
        {
            // Embedded resources in the compiled .csproj ship with names like
            // "Grimly.Dictionaries.en_US.dic" — RootNamespace +
            // folder + filename, dots between segments.
            var asm = Assembly.GetExecutingAssembly();
            using var dicStream = asm.GetManifestResourceStream("Grimly.Dictionaries.en_US.dic");
            using var affStream = asm.GetManifestResourceStream("Grimly.Dictionaries.en_US.aff");

            if (dicStream != null && affStream != null)
            {
                _wordList = WordList.CreateFromStreams(dicStream, affStream);
            }
        }
        catch
        {
            // If the dictionary fails to load for any reason, IsKnown falls
            // back to "everything is known" — safer to skip spell-checking
            // entirely than to flag every word as a typo.
            _wordList = null;
        }
    }

    public bool IsKnown(string word)
    {
        if (string.IsNullOrEmpty(word)) return true;

        // Normalize typographic / "smart" apostrophes to ASCII before lookup.
        // The en_US Hunspell dictionary stores contractions with plain ASCII
        // apostrophes ("isn't", "don't"), so an input containing U+2019
        // ("isn’t") would otherwise miss. The .aff ICONV directive is
        // supposed to handle this, but we normalize here too as a belt-and-
        // suspenders measure.
        var normalized = word
            .Replace('’', '\'')   // RIGHT SINGLE QUOTATION MARK
            .Replace('‘', '\'');  // LEFT SINGLE QUOTATION MARK

        // Dictionary lookup (case-insensitive — Hunspell handles that itself
        // based on the .aff file's case-sensitivity rules for English).
        return _wordList?.Check(normalized) ?? true;
    }
}

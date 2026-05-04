namespace Grimly.Models;

/// <summary>
/// One style-guide violation reported to the user. Violations come from two
/// sources that feed the same list:
/// <list type="bullet">
///   <item>Deterministic code checks (mechanical rules: terminology, numbers,
///     possessives, city qualifiers, legacy product names, absolute claims…).</item>
///   <item>The LLM checker pass, which looks for semantic issues the code
///     can't catch reliably (alarmism, weak verbs in context, patronizing
///     phrasing, attribution quality).</item>
/// </list>
/// </summary>
/// <param name="Category">
/// Short label shown on the violation chip — e.g., "Terminology", "Possessive",
/// "Numbers", "Legacy Term", "Voice". Keep under ~14 characters for display.
/// </param>
/// <param name="Quote">The short snippet from the text that triggered the rule.</param>
/// <param name="Explanation">Why it's a violation and what to change.</param>
/// <param name="Start">
/// Optional zero-based character offset of the violation within the source
/// text. Set when the deterministic checker can pinpoint the exact span
/// (and therefore offer a one-click fix). Null for LLM-sourced violations
/// or when the location isn't recoverable.
/// </param>
/// <param name="Length">
/// Optional length (in characters) of the violation's span. Paired with
/// <paramref name="Start"/>. Null when <paramref name="Start"/> is null.
/// </param>
/// <param name="AutoFix">
/// Optional deterministic replacement text. When present, the UI shows a
/// "Fix" button that swaps the [Start..Start+Length) span for this string.
/// Null when the fix isn't mechanical (legacy product names, absolute
/// claims, spelling — those need user judgment or an LLM pass).
/// </param>
public sealed record Violation(
    string Category,
    string Quote,
    string Explanation,
    int? Start = null,
    int? Length = null,
    string? AutoFix = null)
{
    /// <summary>True when this violation has a deterministic one-click fix.</summary>
    public bool CanAutoFix => Start.HasValue && Length.HasValue && AutoFix != null;
}

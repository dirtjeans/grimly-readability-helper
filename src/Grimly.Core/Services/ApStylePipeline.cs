using Grimly.Models;

namespace Grimly.Services;

public interface IApStylePipeline
{
    /// <summary>
    /// Run the AP Style pipeline on the input text.
    /// Pass 1: deterministic rules (times, dates, %, ampersand, Oxford
    /// comma removal, courtesy titles) via <see cref="IApStyleCodePass"/>.
    /// Pass 2: LLM pass for judgment calls (attribution verb, passive
    /// voice, alarmism) via <see cref="IFoundryLocalClient"/>.
    /// Returns the fully rewritten text.
    /// </summary>
    Task<string> RunAsync(string text, CancellationToken ct = default);
}

/// <summary>
/// AP Style pipeline for public Grimly. Deterministic + LLM in that
/// order — the code pass nails the mechanical rules 100% of the time
/// and hands the LLM cleaner input so its judgment work is easier.
///
/// StyleHelper doesn't use this class directly; its own pipeline
/// (<c>StyleGuidePipeline</c>) invokes <see cref="IApStyleCodePass"/>
/// as its first step and then applies Illumio-specific overrides.
/// </summary>
public sealed class ApStylePipeline : IApStylePipeline
{
    private readonly IApStyleCodePass _codePass;
    private readonly IFoundryLocalClient _foundryClient;

    public ApStylePipeline(IApStyleCodePass codePass, IFoundryLocalClient foundryClient)
    {
        _codePass = codePass;
        _foundryClient = foundryClient;
    }

    public async Task<string> RunAsync(string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;

        // Pass 1: mechanical AP rules.
        var afterCodePass = _codePass.Apply(text);

        // Pass 2: LLM pass for judgment. Keep the prompt narrow — the
        // deterministic pass has already handled everything mechanical,
        // so the LLM should only address the items listed here. Explicit
        // "no other changes" language reduces drift on small models.
        const string prompt =
            "Revise this text to comply with the Associated Press Stylebook. " +
            "Apply ONLY these changes:\n" +
            "  1. Attribution verbs: prefer 'said' over 'claimed', 'stated', 'noted', " +
            "'commented', 'remarked', 'expressed', 'declared'. Use 'said' unless the " +
            "sentence specifically calls for a different verb.\n" +
            "  2. Passive-voice attribution: rewrite 'was said by X' as 'X said'.\n" +
            "  3. Editorial framing: strip alarmist, promotional, or opinion-loaded " +
            "adjectives ('groundbreaking', 'shocking', 'unprecedented' — unless the " +
            "source is quoted saying so).\n" +
            "\n" +
            "Do NOT change: sentence order, sentence structure beyond active/passive " +
            "swaps, factual content, punctuation, capitalization, numbers, times, " +
            "dates, or word choice outside the categories above. If none of the three " +
            "categories apply, return the text unchanged.\n" +
            "\n" +
            "Return ONLY the revised text — no preamble, no explanation, no quotes.";

        var refined = await _foundryClient.GetEditedTextAsync(
            afterCodePass,
            EditingMode.CustomPrompt,
            prompt,
            ct,
            temperature: 0.0);

        if (string.IsNullOrWhiteSpace(refined)) return afterCodePass;
        return refined.Trim().Trim('"');
    }
}

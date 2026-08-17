using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Grimly.Models;

namespace Grimly.Services;

public interface IFoundryLocalClient
{
    Task<string> GetEditedTextAsync(string originalText, EditingMode mode, string? customPrompt, CancellationToken ct = default, double? temperature = null);
}

public sealed class FoundryLocalClient : IFoundryLocalClient
{
    private readonly HttpClient _httpClient;
    private readonly ISettingsService _settingsService;
    private readonly IExternalLlmProviderService? _externalProviders;
    private readonly IWindowsAiClient? _windowsAi;

    public FoundryLocalClient(
        HttpClient httpClient,
        ISettingsService settingsService,
        IExternalLlmProviderService? externalProviders = null,
        IWindowsAiClient? windowsAi = null)
    {
        _httpClient = httpClient;
        _settingsService = settingsService;
        _externalProviders = externalProviders;
        _windowsAi = windowsAi;
    }

    public async Task<string> GetEditedTextAsync(string originalText, EditingMode mode, string? customPrompt, CancellationToken ct = default, double? temperature = null)
    {
        var settings = _settingsService.Load();

        var systemPrompt = mode == EditingMode.CustomPrompt && customPrompt != null
            ? customPrompt
            : mode.GetSystemPrompt();

        // Language anchor. Qwen models (trained on mixed English + Chinese
        // data) sometimes drift to Chinese output when the system prompt is
        // short or the input is ambiguous. Prepending an explicit language
        // instruction pins the output to whatever the input was written in.
        // Cheap for models that would have stayed on-language anyway; the
        // safety net kicks in only when the model was going to drift.
        systemPrompt =
            "Reply in the same language as the input text. If the input is in English, respond in English.\n\n"
            + systemPrompt;

        // Windows AI (Aion Instruct) route. When the user picked the
        // "windows-ai" virtual model, skip Foundry's HTTP path entirely and
        // run the same prompt on-NPU. Aion has no system/user role split or
        // temperature control — instructions and text concatenate into one
        // prompt with a separator, matching how small instruct models are
        // trained to read them.
        if (_windowsAi is not null &&
            string.Equals(settings.ModelName, IWindowsAiClient.ModelId, StringComparison.OrdinalIgnoreCase))
        {
            // Text FIRST, instructions after. Aion (like other small
            // instruct models) parrots or paraphrases instructions far
            // more when they lead the prompt; leading with the text and
            // closing with an output-only reminder keeps its attention on
            // the material being edited.
            var aionResult = await _windowsAi.GenerateAsync(
                "--- TEXT ---\n" + originalText
                + "\n--- END TEXT ---\n\nApply these editing instructions to the text above:\n"
                + systemPrompt
                + "\n\nReply with ONLY the revised text. Never repeat, describe, or summarize these instructions.",
                ct);
            if (string.IsNullOrWhiteSpace(aionResult)) return originalText;
            var aionText = StripMetaPreamble(aionResult!);
            if (string.IsNullOrWhiteSpace(aionText)) return originalText;
            if (LooksLikePromptEcho(aionText, systemPrompt)) return originalText;
            if (LooksLikeInstructionParaphrase(aionText, systemPrompt, originalText)) return originalText;
            if (LooksLikeModelVerdict(aionText, originalText)) return originalText;
            return aionText;
        }

        // Compute temperature: mode baseline + creativity offset
        double finalTemp;
        if (temperature.HasValue)
        {
            finalTemp = temperature.Value; // explicit override (used by revision variants)
        }
        else
        {
            double baseTemp = mode.GetBaseTemperature();
            double offset = (settings.Creativity - 0.5) * 0.4; // -0.2 to +0.2
            finalTemp = Math.Clamp(baseTemp + offset, 0.0, 1.0);
        }

        var modelId = settings.ModelName;
        var endpoint = settings.FoundryEndpoint.TrimEnd('/');
        var chatPath = "/v1/chat/completions";
        var external = _externalProviders?.MatchProvider(modelId);
        if (external is not null)
        {
            var colon = modelId.IndexOf(':');
            modelId = colon >= 0 ? modelId[(colon + 1)..] : modelId;
            endpoint = external.BaseUrl.TrimEnd('/');
            chatPath = external.ChatEndpoint;
        }

        var request = new ChatCompletionRequest
        {
            Model = modelId,
            Temperature = finalTemp,
            MaxTokens = settings.MaxTokens,
            Messages =
            [
                ChatMessage.System(systemPrompt),
                ChatMessage.User(originalText)
            ]
        };

        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // 2-minute timeout — local LLMs can be slow on long text but shouldn't take longer
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromMinutes(2));

        try
        {
            var response = await _httpClient.PostAsync(
                $"{endpoint}{chatPath}", content, timeoutCts.Token);

            if (!response.IsSuccessStatusCode)
            {
                // Surface Foundry's actual error text so the pipeline can log/
                // report something useful instead of a bare status code.
                string body;
                try { body = await response.Content.ReadAsStringAsync(timeoutCts.Token); }
                catch { body = ""; }
                throw new HttpRequestException(
                    $"Foundry returned {(int)response.StatusCode} {response.StatusCode}: {TrimBody(body)}",
                    inner: null,
                    statusCode: response.StatusCode);
            }

            var responseJson = await response.Content.ReadAsStringAsync(timeoutCts.Token);
            var result = JsonSerializer.Deserialize<ChatCompletionResponse>(responseJson);

            var reply = result?.Choices.FirstOrDefault()?.Message.Content?.Trim();
            if (string.IsNullOrWhiteSpace(reply)) return originalText;
            var replyText = StripMetaPreamble(reply!);
            if (string.IsNullOrWhiteSpace(replyText)) return originalText;
            if (LooksLikePromptEcho(replyText, systemPrompt)) return originalText;
            if (LooksLikeInstructionParaphrase(replyText, systemPrompt, originalText)) return originalText;
            if (LooksLikeModelVerdict(replyText, originalText)) return originalText;
            return replyText;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Our timeout fired, not the caller's cancellation
            throw new HttpRequestException("Request timed out after 2 minutes. The model may be overloaded or unresponsive.");
        }
    }

    /// <summary>
    /// Dynamic prompt-echo detector. Small on-device models sometimes
    /// return the instructions instead of (or wrapped around) the edited
    /// text. Rather than maintaining hand-curated signature lists per
    /// prompt, this checks whether any substantive line of the system
    /// prompt appears verbatim in the response — instruction lines never
    /// legitimately belong in edited user text. Also catches the common
    /// meta-commentary preambles.
    /// </summary>
    internal static bool LooksLikePromptEcho(string response, string systemPrompt)
    {
        foreach (var rawLine in systemPrompt.Split('\n'))
        {
            var line = rawLine.Trim();
            // Short lines ("RULES:", "1.") are too generic to be reliable
            // evidence; 20+ chars of verbatim instruction text is.
            if (line.Length < 20) continue;
            if (response.Contains(line, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Detects instruction paraphrase — the model restating the prompt's
    /// rules in its own words ("It avoids complex vocabulary. It uses
    /// contractions…"), which verbatim line-matching can't catch. The tell
    /// is vocabulary provenance: a genuine rewrite is assembled mostly from
    /// the user's own words, while a paraphrased rulebook is assembled from
    /// the prompt's words. Flag responses whose word stock leans clearly
    /// toward the prompt.
    /// </summary>
    internal static bool LooksLikeInstructionParaphrase(string response, string systemPrompt, string originalText)
    {
        var respTokens = WordRegex.Matches(response.ToLowerInvariant())
            .Select(m => m.Value).ToList();
        // Tiny responses don't give the statistic enough to grip on; the
        // verdict detector owns that range.
        if (respTokens.Count < 12) return false;

        var promptSet = new HashSet<string>(
            WordRegex.Matches(systemPrompt.ToLowerInvariant()).Select(m => m.Value));
        var textSet = new HashSet<string>(
            WordRegex.Matches(originalText.ToLowerInvariant()).Select(m => m.Value));

        double promptFrac = respTokens.Count(promptSet.Contains) / (double)respTokens.Count;
        double textFrac = respTokens.Count(textSet.Contains) / (double)respTokens.Count;

        // Rewrites typically score textFrac ≥ 0.6 (the content is the
        // user's). Paraphrased instructions score promptFrac high and
        // textFrac low. Require both a strong absolute prompt share and a
        // clear margin over the text share so heavy rewrites (which add
        // some new wording) stay safe.
        return promptFrac > 0.55 && promptFrac > textFrac * 1.5;
    }

    // Words of 4+ letters — short function words (the/is/and) appear in
    // everything and would wash out the signal.
    private static readonly Regex WordRegex = new(@"[a-z']{4,}", RegexOptions.Compiled);

    /// <summary>
    /// Strips announcer preambles the model wraps around an otherwise-good
    /// rewrite — "The revised text is: :", "Here's the rewritten version:",
    /// "Sure, here is the edited text -" and friends. Unlike echo/verdict
    /// detection this KEEPS the response: the rewrite that follows the
    /// preamble is usually exactly what the user asked for.
    /// </summary>
    internal static string StripMetaPreamble(string response)
    {
        var s = MetaPreambleRegex.Replace(response.TrimStart(), "", 1);
        // Mop up stragglers the model sometimes doubles after the preamble
        // (the "…is: :" case) plus any opening quote it added.
        return s.TrimStart(':', '-', ' ', '\r', '\n', '"', '“');
    }

    private static readonly Regex MetaPreambleRegex = new(
        @"^[""“']?\s*(?:sure[,.!]?\s*)?(?:here(?:'s|\s+is)\s+)?(?:the\s+|your\s+|a\s+)?(?:revised|rewritten|edited|updated|corrected|improved|conversational)\s+(?:text|version)(?:\s+is)?\s*[:\-–]+\s*",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Detects verdict responses — the model narrating a judgment about the
    /// text ("The revisions are complete. The text is now grammatically
    /// correct…") instead of returning the text. Two signals, both
    /// required:
    ///   1. Structural collapse — the response is far too short to be a
    ///      rewrite of the input. Even aggressive modes (Bullet Points,
    ///      Cut Filler) keep most of the content; verdicts are a sentence
    ///      or two regardless of input size.
    ///   2. Editing vocabulary — the response talks about grammar/spelling/
    ///      revisions in words the user's own text doesn't use.
    /// Callers map a verdict to "no changes" by returning the original
    /// text, which the popup reports as "No changes suggested."
    /// </summary>
    internal static bool LooksLikeModelVerdict(string response, string originalText)
    {
        // Structural gate. Short inputs can't distinguish a verdict from a
        // legitimate tightening, so only fire when the input is substantial
        // and the response collapsed to a fraction of it (or is just tiny).
        bool collapsed =
            (originalText.Length >= 240 && response.Length < originalText.Length / 3)
            || response.Length <= 160;
        if (!collapsed) return false;

        foreach (var term in VerdictVocabulary)
        {
            if (response.Contains(term, StringComparison.OrdinalIgnoreCase) &&
                !originalText.Contains(term, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>Editing-meta vocabulary that marks a response as being
    /// ABOUT the text rather than the text. Stems, so tense/phrasing
    /// variants ("is/now grammatically correct", "revision/revisions")
    /// all match.</summary>
    private static readonly string[] VerdictVocabulary =
    {
        "grammatical", "grammatically", "spelling error", "punctuation error",
        "no corrections", "no changes", "no edits", "no revisions",
        "revisions are complete", "revision is complete", "edits are complete",
        "already well-written", "already correct", "well-written and",
        "free of errors", "error-free", "the text is", "your text is",
        "text has been", "nothing to correct", "does not require any",
        "doesn't require any",
    };

    private static string TrimBody(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return "(no body)";
        // Response bodies can be large; keep a reasonable preview.
        var oneLine = body.Replace('\n', ' ').Replace('\r', ' ').Trim();
        return oneLine.Length > 300 ? oneLine[..300] + "…" : oneLine;
    }
}

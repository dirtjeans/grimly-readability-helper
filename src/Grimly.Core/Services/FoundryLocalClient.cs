using System.Net.Http;
using System.Text;
using System.Text.Json;
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
            var aionResult = await _windowsAi.GenerateAsync(
                systemPrompt + "\n\n---\n\n" + originalText, ct);
            return string.IsNullOrWhiteSpace(aionResult) ? originalText : aionResult!;
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

        // Route by model-id prefix. Foundry Local models are unprefixed
        // and hit the endpoint from settings. Prefixed ids (e.g.
        // "ollama:llama3.2", "lmstudio:qwen2.5-7b-instruct") get sent to
        // the matching provider's OpenAI-compatible endpoint, with the
        // prefix stripped from the model name so the target service sees
        // its own native id.
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

            return result?.Choices.FirstOrDefault()?.Message.Content?.Trim() ?? originalText;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Our timeout fired, not the caller's cancellation
            throw new HttpRequestException("Request timed out after 2 minutes. The model may be overloaded or unresponsive.");
        }
    }

    private static string TrimBody(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return "(no body)";
        // Response bodies can be large; keep a reasonable preview.
        var oneLine = body.Replace('\n', ' ').Replace('\r', ' ').Trim();
        return oneLine.Length > 300 ? oneLine[..300] + "…" : oneLine;
    }
}

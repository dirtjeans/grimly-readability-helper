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

    public FoundryLocalClient(HttpClient httpClient, ISettingsService settingsService)
    {
        _httpClient = httpClient;
        _settingsService = settingsService;
    }

    public async Task<string> GetEditedTextAsync(string originalText, EditingMode mode, string? customPrompt, CancellationToken ct = default, double? temperature = null)
    {
        var settings = _settingsService.Load();

        var systemPrompt = mode == EditingMode.CustomPrompt && customPrompt != null
            ? customPrompt
            : mode.GetSystemPrompt();

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

        var request = new ChatCompletionRequest
        {
            Model = settings.ModelName,
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

        var endpoint = settings.FoundryEndpoint.TrimEnd('/');

        // 2-minute timeout — local LLMs can be slow on long text but shouldn't take longer
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromMinutes(2));

        try
        {
            var response = await _httpClient.PostAsync(
                $"{endpoint}/v1/chat/completions", content, timeoutCts.Token);

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

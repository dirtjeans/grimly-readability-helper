using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using Grimly.Models;

namespace Grimly.Services;

public interface IFoundryManager
{
    Task<(bool success, string? endpoint, string? modelId)> EnsureRunningAsync(CancellationToken ct = default);
    Task<List<string>> GetAvailableModelsAsync(CancellationToken ct = default);
    Task<int?> GetMaxOutputTokensAsync(string modelId, CancellationToken ct = default);
    Task<(bool running, string? endpoint)> CheckServiceStatusAsync(CancellationToken ct = default);
    Task<ConnectionStatus> CheckConnectionAsync(CancellationToken ct = default);
    bool IsFoundryInstalled();
    bool IsWingetInstalled();
    Task<bool> ForceReconnectAsync(CancellationToken ct = default);
    Task<bool> FallbackToCpuModelAsync(CancellationToken ct = default);
    Task<bool> HealthCheckAsync(CancellationToken ct = default);
}

public enum ConnectionStatus
{
    Unknown,
    Connected,
    ServiceNotRunning,
    ModelNotLoaded,
    NotInstalled,
    Error
}

public sealed class FoundryManager : IFoundryManager
{
    private readonly ISettingsService _settingsService;

    public FoundryManager(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public async Task<(bool success, string? endpoint, string? modelId)> EnsureRunningAsync(CancellationToken ct = default)
    {
        // Step 1: Check if the service is running, start it if not
        var endpoint = await EnsureServiceRunningAsync(ct);
        if (endpoint == null)
            return (false, null, null);

        // Update endpoint immediately (port may have changed since last run)
        var settings0 = _settingsService.Load();
        if (settings0.FoundryEndpoint != endpoint)
        {
            settings0.FoundryEndpoint = endpoint;
            _settingsService.Save(settings0);
        }

        // Step 2: Check if a model is loaded, prefer user's last-used model
        var modelId = await EnsureModelLoadedAsync(endpoint, settings0.ModelName, ct);
        if (modelId == null)
            return (false, endpoint, null);

        // Step 3: Update settings with discovered endpoint, model, and max tokens
        var settings = _settingsService.Load();
        var maxTokens = await GetMaxOutputTokensAsync(modelId, ct);

        bool changed = false;
        if (settings.FoundryEndpoint != endpoint) { settings.FoundryEndpoint = endpoint; changed = true; }
        if (settings.ModelName != modelId) { settings.ModelName = modelId; changed = true; }
        if (maxTokens.HasValue && settings.MaxTokens != maxTokens.Value) { settings.MaxTokens = maxTokens.Value; changed = true; }
        if (changed) _settingsService.Save(settings);

        return (true, endpoint, modelId);
    }

    public async Task<bool> ForceReconnectAsync(CancellationToken ct = default)
    {
        var settings = _settingsService.Load();
        var modelAlias = settings.ModelName;

        // Step 1: Stop the service (with timeout — don't hang if it's stuck)
        using var stopCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        stopCts.CancelAfter(TimeSpan.FromSeconds(10));
        try { await RunFoundryCommandAsync("service stop", stopCts.Token); }
        catch { }

        await Task.Delay(1000, ct);

        // Step 2: Start the service fresh
        await RunFoundryCommandAsync("service start", ct);

        // Wait for service to come back — re-discover the endpoint (port may change)
        string? endpoint = null;
        for (int i = 0; i < 20; i++)
        {
            await Task.Delay(1000, ct);
            var (exitCode, output) = await RunFoundryCommandAsync("service status", ct);
            if (exitCode == 0 && output.Contains("running", StringComparison.OrdinalIgnoreCase))
            {
                endpoint = ExtractEndpoint(output);
                break;
            }
        }

        if (endpoint == null) return false;

        // Update endpoint in settings (port may have changed!)
        settings = _settingsService.Load();
        if (settings.FoundryEndpoint != endpoint)
        {
            settings.FoundryEndpoint = endpoint;
            _settingsService.Save(settings);
        }

        // Step 3: Load the model
        // Extract short alias from full model ID (e.g. "qwen2.5-7b-instruct-qnn-npu:2" → "qwen2.5-7b")
        var shortAlias = modelAlias;
        var dashIdx = modelAlias.IndexOf("-instruct", StringComparison.OrdinalIgnoreCase);
        if (dashIdx > 0) shortAlias = modelAlias[..dashIdx];

        // Try model load with a 60-second timeout
        using var loadCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        loadCts.CancelAfter(TimeSpan.FromSeconds(60));
        try
        {
            await RunFoundryCommandAsync($"model load {shortAlias}", loadCts.Token);
        }
        catch { }

        // Check if the model appeared
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        for (int i = 0; i < 10; i++)
        {
            await Task.Delay(1000, ct);
            var loaded = await CheckForLoadedModel(endpoint, http, ct);
            if (loaded != null)
            {
                settings = _settingsService.Load();
                if (settings.ModelName != loaded) { settings.ModelName = loaded; _settingsService.Save(settings); }
                return true;
            }
        }

        return false;
    }

    public async Task<bool> FallbackToCpuModelAsync(CancellationToken ct = default)
    {
        // Stop the service completely to clear any hung NPU state
        using var stopCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        stopCts.CancelAfter(TimeSpan.FromSeconds(10));
        try { await RunFoundryCommandAsync("service stop", stopCts.Token); } catch { }

        await Task.Delay(2000, ct);

        // Start fresh
        await RunFoundryCommandAsync("service start", ct);

        // Wait for service
        string? endpoint = null;
        for (int i = 0; i < 20; i++)
        {
            await Task.Delay(1000, ct);
            var (exitCode, output) = await RunFoundryCommandAsync("service status", ct);
            if (exitCode == 0 && output.Contains("running", StringComparison.OrdinalIgnoreCase))
            {
                endpoint = ExtractEndpoint(output);
                break;
            }
        }

        if (endpoint == null) return false;

        // Load phi-4-mini (CPU model — doesn't use the NPU, always reliable)
        using var loadCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        loadCts.CancelAfter(TimeSpan.FromSeconds(60));
        try
        {
            await RunFoundryCommandAsync("model load phi-4-mini", loadCts.Token);
        }
        catch { }

        // Wait for model to appear
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        for (int i = 0; i < 10; i++)
        {
            await Task.Delay(1000, ct);
            var loaded = await CheckForLoadedModel(endpoint, http, ct);
            if (loaded != null)
            {
                // Update settings with new endpoint and model
                var settings = _settingsService.Load();
                settings.FoundryEndpoint = endpoint;
                settings.ModelName = loaded;
                _settingsService.Save(settings);
                return true;
            }
        }

        return false;
    }

    public async Task<bool> HealthCheckAsync(CancellationToken ct = default)
    {
        // First re-discover the endpoint in case the port changed
        var (running, liveEndpoint) = await CheckServiceStatusAsync(ct);
        if (!running || liveEndpoint == null) return false;

        // Update settings if endpoint changed
        var settings = _settingsService.Load();
        if (settings.FoundryEndpoint != liveEndpoint)
        {
            settings.FoundryEndpoint = liveEndpoint;
            _settingsService.Save(settings);
        }

        // Send a tiny test prompt to verify the model is actually responsive.
        //
        // NPU-quantized models often take 20-40 seconds for their very first
        // inference after the service starts (kernel compilation, weight
        // mapping). The old 15s single-attempt timeout routinely declared
        // healthy models "not responding" at startup, triggering a needless
        // restart toast. Now we give the first call 75s and retry up to
        // twice before giving up — a truly broken model will exhaust this
        // budget; a cold-starting one will succeed on attempt 1 or 2.
        var endpoint = liveEndpoint.TrimEnd('/');
        var requestBody = new
        {
            model = settings.ModelName,
            messages = new[] { new { role = "user", content = "hi" } },
            max_tokens = 1,
            temperature = 0.0
        };
        var json = System.Text.Json.JsonSerializer.Serialize(requestBody);

        for (int attempt = 1; attempt <= 3; attempt++)
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(75) };
            try
            {
                var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
                var response = await http.PostAsync($"{endpoint}/v1/chat/completions", content, ct);
                if (response.IsSuccessStatusCode) return true;
                // Non-2xx status: if it's a transient 5xx, retry; if it's 4xx,
                // it's a hard config error (bad model name) — no point retrying.
                if ((int)response.StatusCode < 500) return false;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return false; // caller cancelled
            }
            catch
            {
                // Timeout or network — fall through to retry
            }

            if (ct.IsCancellationRequested) return false;
            // Brief pause between retries; cold-start tends to succeed on retry.
            try { await Task.Delay(1500, ct); } catch { return false; }
        }
        return false;
    }

    public async Task<(bool running, string? endpoint)> CheckServiceStatusAsync(CancellationToken ct = default)
    {
        var (exitCode, output) = await RunFoundryCommandAsync("service status", ct);
        if (exitCode == 0 && output.Contains("running", StringComparison.OrdinalIgnoreCase))
            return (true, ExtractEndpoint(output));
        return (false, null);
    }

    public async Task<List<string>> GetAvailableModelsAsync(CancellationToken ct = default)
    {
        var models = new List<string>();
        var settings = _settingsService.Load();
        var endpoint = settings.FoundryEndpoint;

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        try
        {
            var response = await http.GetStringAsync($"{endpoint}/v1/models", ct);
            var doc = JsonDocument.Parse(response);
            var data = doc.RootElement.GetProperty("data");

            foreach (var model in data.EnumerateArray())
            {
                var id = model.GetProperty("id").GetString();
                if (id != null) models.Add(id);
            }
        }
        catch { }

        return models;
    }

    public bool IsFoundryInstalled()
    {
        return IsCommandAvailable("foundry");
    }

    public bool IsWingetInstalled()
    {
        return IsCommandAvailable("winget");
    }

    private static bool IsCommandAvailable(string command)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "where",
                Arguments = command,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            if (process == null) return false;
            process.WaitForExit(2000);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public async Task<ConnectionStatus> CheckConnectionAsync(CancellationToken ct = default)
    {
        if (!IsFoundryInstalled())
            return ConnectionStatus.NotInstalled;

        // Always re-discover the endpoint (port can change between restarts)
        var (running, liveEndpoint) = await CheckServiceStatusAsync(ct);
        if (!running || liveEndpoint == null)
            return ConnectionStatus.ServiceNotRunning;

        var settings = _settingsService.Load();
        if (settings.FoundryEndpoint != liveEndpoint)
        {
            settings.FoundryEndpoint = liveEndpoint;
            _settingsService.Save(settings);
        }

        var endpoint = liveEndpoint;

        // Verify the *configured* model is in Foundry's loaded list. Checking
        // for any model at all is misleading after a model switch: the old
        // model may still be loaded (so /v1/models returns it) while the new
        // one hasn't been loaded yet, and requests to the new model would fail.
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        try
        {
            var response = await http.GetStringAsync($"{endpoint}/v1/models", ct);
            var doc = JsonDocument.Parse(response);
            var data = doc.RootElement.GetProperty("data");

            bool anyModels = false;
            bool configuredModelLoaded = false;
            var configured = settings.ModelName;

            foreach (var model in data.EnumerateArray())
            {
                anyModels = true;
                if (model.TryGetProperty("id", out var idProp) &&
                    string.Equals(idProp.GetString(), configured, StringComparison.OrdinalIgnoreCase))
                {
                    configuredModelLoaded = true;
                    break;
                }
            }

            if (configuredModelLoaded) return ConnectionStatus.Connected;
            return anyModels ? ConnectionStatus.ModelNotLoaded : ConnectionStatus.ModelNotLoaded;
        }
        catch
        {
            return ConnectionStatus.ServiceNotRunning;
        }
    }

    public async Task<int?> GetMaxOutputTokensAsync(string modelId, CancellationToken ct = default)
    {
        var settings = _settingsService.Load();
        var endpoint = settings.FoundryEndpoint;

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        try
        {
            var response = await http.GetStringAsync($"{endpoint}/v1/models", ct);
            var doc = JsonDocument.Parse(response);
            var data = doc.RootElement.GetProperty("data");

            foreach (var model in data.EnumerateArray())
            {
                var id = model.GetProperty("id").GetString();
                if (id == modelId && model.TryGetProperty("maxOutputTokens", out var maxTokens))
                    return maxTokens.GetInt32();
            }
        }
        catch { }

        return null;
    }

    private static async Task<string?> CheckForLoadedModel(string endpoint, HttpClient http, CancellationToken ct)
    {
        try
        {
            var response = await http.GetStringAsync($"{endpoint}/v1/models", ct);
            var doc = System.Text.Json.JsonDocument.Parse(response);
            var data = doc.RootElement.GetProperty("data");
            foreach (var model in data.EnumerateArray())
            {
                var id = model.GetProperty("id").GetString();
                if (id != null) return id;
            }
        }
        catch { }
        return null;
    }

    private static async Task<string?> EnsureServiceRunningAsync(CancellationToken ct)
    {
        // Check service status
        var (exitCode, output) = await RunFoundryCommandAsync("service status", ct);

        if (exitCode == 0 && output.Contains("running", StringComparison.OrdinalIgnoreCase))
        {
            // Parse endpoint from output like "running on http://127.0.0.1:51318/..."
            return ExtractEndpoint(output);
        }

        // Service not running — start it
        var (startExit, startOutput) = await RunFoundryCommandAsync("service start", ct);
        if (startExit != 0)
            return null;

        // Wait for service to be ready
        for (int i = 0; i < 15; i++)
        {
            await Task.Delay(1000, ct);
            var (checkExit, checkOutput) = await RunFoundryCommandAsync("service status", ct);
            if (checkExit == 0 && checkOutput.Contains("running", StringComparison.OrdinalIgnoreCase))
                return ExtractEndpoint(checkOutput);
        }

        return null;
    }

    private static async Task<string?> EnsureModelLoadedAsync(string endpoint, string preferredModel, CancellationToken ct)
    {
        // Step 1 — Ask the service what's loaded. If it lists the user's
        // preferred model (or any model), we're done.
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        try
        {
            var response = await http.GetStringAsync($"{endpoint}/v1/models", ct);
            var doc = JsonDocument.Parse(response);
            var data = doc.RootElement.GetProperty("data");

            string? bestModel = null;
            foreach (var model in data.EnumerateArray())
            {
                var id = model.GetProperty("id").GetString();
                if (id == null) continue;

                if (!string.IsNullOrEmpty(preferredModel) && id == preferredModel)
                    return id;

                bestModel ??= id;
            }

            if (bestModel != null)
                return bestModel;
        }
        catch { /* fall through to the optimistic path below */ }

        // Step 2 — /v1/models returned empty or failed, BUT if the user has a
        // previously-working model name in settings, trust it. Foundry lazy-
        // loads models on first inference; the /v1/models endpoint sometimes
        // lags behind the actual load state, especially right after service
        // start. The per-request timeout in FoundryLocalClient handles real
        // failures at point of use — no need to block startup with a modal
        // "Setup Required" dialog when the model was working last time.
        if (!string.IsNullOrEmpty(preferredModel))
            return preferredModel;

        // Step 3 — no preferred model in settings. First-launch or fresh
        // install. Try loading a known-good default. If that fails, we'll
        // surface the setup dialog to the user.
        var (exitCode, _) = await RunFoundryCommandAsync("model load qwen2.5-7b", ct);
        if (exitCode != 0)
        {
            var (dlExit, _) = await RunFoundryCommandAsync("model download qwen2.5-7b", ct);
            if (dlExit == 0)
                (exitCode, _) = await RunFoundryCommandAsync("model load qwen2.5-7b", ct);
        }

        if (exitCode == 0)
        {
            // Brief poll (max ~20s) to surface the actual model ID once
            // /v1/models catches up. If it doesn't in that window, return the
            // alias optimistically — inference will still work.
            for (int i = 0; i < 10; i++)
            {
                await Task.Delay(2000, ct);
                try
                {
                    var response = await http.GetStringAsync($"{endpoint}/v1/models", ct);
                    var doc = JsonDocument.Parse(response);
                    foreach (var model in doc.RootElement.GetProperty("data").EnumerateArray())
                    {
                        var id = model.GetProperty("id").GetString();
                        if (id != null && id.Contains("qwen2.5-7b", StringComparison.OrdinalIgnoreCase))
                            return id;
                    }
                }
                catch { }
            }
            return "qwen2.5-7b";
        }

        return null;
    }

    private static string? ExtractEndpoint(string output)
    {
        // Parse "running on http://127.0.0.1:51318/..."
        var idx = output.IndexOf("http://", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) idx = output.IndexOf("https://", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return "http://127.0.0.1:51318"; // fallback

        var end = output.IndexOfAny([' ', '\n', '\r', '/'], idx + 8);
        if (end < 0) end = output.Length;

        var url = output[idx..end].TrimEnd('/');

        // The status output may include a path like /openai/status — strip it
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return $"{uri.Scheme}://{uri.Authority}";

        return url;
    }

    /// <summary>
    /// Default hard cap for foundry CLI calls. Overridable by linking a
    /// shorter token from the caller. Without this, commands like
    /// `foundry model load` can hang indefinitely if the model or service
    /// misbehaves — a real-world symptom was a startup toast stuck on
    /// "Warming up the local model…" forever while the app was actually fine.
    /// </summary>
    private static readonly TimeSpan FoundryCommandTimeout = TimeSpan.FromMinutes(2);

    private static async Task<(int exitCode, string output)> RunFoundryCommandAsync(string args, CancellationToken ct)
    {
        // Link the caller's token with an always-on timeout so we never
        // await indefinitely on a hung foundry process.
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(FoundryCommandTimeout);
        var token = timeoutCts.Token;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "foundry",
                Arguments = args,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null)
                return (-1, "Failed to start foundry process");

            // Close stdin so commands don't hang waiting for input
            try { process.StandardInput.Close(); } catch { }

            // Read stdout and stderr concurrently to avoid deadlock
            var stdoutTask = process.StandardOutput.ReadToEndAsync(token);
            var stderrTask = process.StandardError.ReadToEndAsync(token);

            try
            {
                await process.WaitForExitAsync(token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Our timeout fired, not the caller's cancellation. Kill the
                // process tree so we don't leave orphan foundry CLI invocations.
                try { process.Kill(entireProcessTree: true); } catch { }
                return (-1, $"Error: `foundry {args}` timed out after {FoundryCommandTimeout.TotalSeconds:F0}s");
            }

            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            return (process.ExitCode, stdout + stderr);
        }
        catch (Exception ex)
        {
            return (-1, $"Error: {ex.Message}");
        }
    }
}

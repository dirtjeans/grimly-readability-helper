using System.IO;
using System.Net.Http;
using System.Text.Json;
using Grimly.Models;

namespace Grimly.Services;

/// <summary>
/// One external local-LLM provider Grimly can talk to alongside Foundry
/// Local. All three providers (Ollama, LM Studio, Jan) expose a similar
/// pattern: a well-known localhost port, a "list models" endpoint, and an
/// OpenAI-compatible <c>/v1/chat/completions</c> that we can post to
/// without any provider-specific request shaping.
/// </summary>
public sealed record ExternalProvider(
    string Prefix,        // "ollama", "lmstudio" — becomes the "prefix:" on model ids
    string DisplayLabel,  // "Ollama", "LM Studio" — shown as the Device column in the browser
    string BaseUrl,       // "http://localhost:11434"
    string ChatEndpoint,  // "/v1/chat/completions"
    string ListPath,      // "/api/tags" (Ollama) or "/v1/models" (LM Studio)
    Func<JsonElement, IEnumerable<string>> ExtractModels,
    // On-demand server start: CLI candidates tried in order (bare names
    // resolve via PATH; absolute paths cover installs that don't add
    // themselves to PATH) and the arguments that start the server.
    // Null = provider can't be auto-started.
    string[]? StartExeCandidates = null,
    string? StartArgs = null,
    // Full-inventory listing via the provider's CLI. Some servers
    // (LM Studio, GenieX) only report *loaded* models over HTTP; their
    // CLIs enumerate everything downloaded. Null = HTTP list is already
    // complete (Ollama). Entries may carry a DeviceLabel override so
    // runtime-specific tags (e.g. GenieX qairt = NPU) survive into the
    // catalog and its NPU/GPU filters.
    string? ListCliArgs = null,
    Func<string, IEnumerable<CliModelEntry>>? ParseCliList = null,
    // Remote discovery. None of these providers exposes an enumerable
    // catalog API, so the browser links to where the catalog actually
    // lives, and PullArgsTemplate ({0} = model name) fetches a model the
    // user found there.
    string? CatalogUrl = null,
    string? PullArgsTemplate = null);

/// <summary>One model reported by a provider's CLI listing. DeviceLabel
/// overrides the provider's DisplayLabel in the catalog when the CLI
/// exposes per-model runtime info (GenieX: qairt = NPU, llama_cpp =
/// CPU/GPU).</summary>
public sealed record CliModelEntry(string Id, string? DeviceLabel = null);

public interface IExternalLlmProviderService
{
    /// <summary>
    /// Every provider Grimly knows how to detect. The list is fixed —
    /// changing it requires code. Order matters only for the model-picker
    /// display; detection is independent.
    /// </summary>
    IReadOnlyList<ExternalProvider> Providers { get; }

    /// <summary>
    /// Probe every provider concurrently and return the union of the models
    /// each one reports. Providers that don't respond within a short window
    /// contribute nothing — the app still starts, they just don't appear in
    /// the model list. Returned entries are ready to add to the catalog
    /// (prefixed id, provider name as Device, cached=true because these
    /// models are already downloaded by their respective providers).
    ///
    /// With <paramref name="autoStartInstalled"/> set, a provider that
    /// doesn't answer the probe but whose CLI is present on the machine
    /// gets its server started on the spot (detached; keeps running after
    /// the app exits) and is polled until ready before listing. Used by
    /// the Settings picker so "installed but not launched" providers show
    /// their models on demand.
    /// </summary>
    Task<IReadOnlyList<CatalogModelInfo>> DiscoverAsync(bool autoStartInstalled = false, CancellationToken ct = default);

    /// <summary>
    /// Route helper — given a model id (possibly prefixed), return the
    /// provider whose prefix matches, or null when the id is a plain
    /// Foundry Local model. Called by the LLM client per request.
    /// </summary>
    ExternalProvider? MatchProvider(string modelId);

    /// <summary>
    /// Make sure one provider's server is answering — starting it from its
    /// CLI when installed but idle. Returns true when the server responds.
    /// Called at app startup for the provider that owns the selected model,
    /// so a reboot doesn't leave the user's model unreachable at first use.
    /// </summary>
    Task<bool> EnsureRunningAsync(ExternalProvider provider, CancellationToken ct = default);

    /// <summary>
    /// True when the provider's CLI exists on this machine (absolute
    /// candidate paths checked directly; bare names searched on PATH).
    /// Drives the Settings UI: installed providers show a Start link,
    /// missing ones show an Install link.
    /// </summary>
    bool IsInstalled(ExternalProvider provider);

    /// <summary>
    /// Download a model through the provider's CLI (`ollama pull …`,
    /// `lms get …`, `geniex pull …`), streaming its output lines. Returns
    /// true on exit 0. The model name is whatever the user found on the
    /// provider's catalog site.
    /// </summary>
    Task<bool> PullModelAsync(ExternalProvider provider, string modelName, IProgress<string>? progress, CancellationToken ct = default);
}

public sealed class ExternalLlmProviderService : IExternalLlmProviderService
{
    private readonly HttpClient _http;

    // Kept short so provider probes never noticeably delay the model
    // browser's refresh. If a provider is running but sluggish, the user
    // can retry — the miss isn't fatal.
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(2);

    public ExternalLlmProviderService(HttpClient http)
    {
        _http = http;
    }

    public IReadOnlyList<ExternalProvider> Providers { get; } = new[]
    {
        // Ollama's native /api/tags returns { "models": [ { "name": "…" } ] }.
        // Chat completions accept OpenAI-shaped requests at /v1/chat/completions.
        new ExternalProvider(
            Prefix: "ollama",
            DisplayLabel: "Ollama",
            BaseUrl: "http://localhost:11434",
            ChatEndpoint: "/v1/chat/completions",
            ListPath: "/api/tags",
            ExtractModels: root =>
            {
                if (!root.TryGetProperty("models", out var arr) || arr.ValueKind != JsonValueKind.Array)
                    return Array.Empty<string>();
                return arr.EnumerateArray()
                    .Where(e => e.TryGetProperty("name", out _))
                    .Select(e => e.GetProperty("name").GetString() ?? "")
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .ToArray();
            },
            StartExeCandidates:
            [
                "ollama",
                Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%\Programs\Ollama\ollama.exe"),
            ],
            StartArgs: "serve",
            CatalogUrl: "https://ollama.com/library",
            PullArgsTemplate: "pull {0}"),

        // LM Studio's OpenAI-compatible /v1/models returns { "data": [ { "id": "…" } ] }.
        new ExternalProvider(
            Prefix: "lmstudio",
            DisplayLabel: "LM Studio",
            BaseUrl: "http://localhost:1234",
            ChatEndpoint: "/v1/chat/completions",
            ListPath: "/v1/models",
            ExtractModels: ExtractOpenAiModelIds,
            StartExeCandidates:
            [
                "lms",
                Environment.ExpandEnvironmentVariables(@"%USERPROFILE%\.lmstudio\bin\lms.exe"),
            ],
            StartArgs: "server start",
            // `lms ls --json` → [{ "type": "llm"|"vlm"|"embedding",
            // "modelKey": "openai/gpt-oss-20b", … }]. modelKey is the id the
            // server accepts; embeddings can't chat, so they're skipped.
            ListCliArgs: "ls --json",
            ParseCliList: json =>
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Array) return Array.Empty<CliModelEntry>();
                return doc.RootElement.EnumerateArray()
                    .Where(e => e.TryGetProperty("type", out var t)
                             && t.GetString() is "llm" or "vlm")
                    .Where(e => e.TryGetProperty("modelKey", out _))
                    .Select(e => e.GetProperty("modelKey").GetString() ?? "")
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .Select(n => new CliModelEntry(n))
                    .ToArray();
            },
            // LM Studio's discovery is Hugging Face; `lms get` fetches by
            // name/query. --yes auto-accepts the best match.
            CatalogUrl: "https://lmstudio.ai/models",
            PullArgsTemplate: "get {0} --yes"),

        // Qualcomm GenieX — `geniex serve` exposes an OpenAI-compatible
        // server on port 18181. Lets Snapdragon users run NPU-optimized
        // models (e.g. Gemma variants from Qualcomm AI Hub) and pick them
        // here like any other local provider.
        new ExternalProvider(
            Prefix: "geniex",
            DisplayLabel: "GenieX",
            BaseUrl: "http://localhost:18181",
            ChatEndpoint: "/v1/chat/completions",
            ListPath: "/v1/models",
            ExtractModels: ExtractOpenAiModelIds,
            StartExeCandidates:
            [
                "geniex",
                Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%\GenieX CLI\geniex.exe"),
            ],
            StartArgs: "serve",
            // `geniex list --format json` → [{ "name": "google/gemma-4-…",
            // "precisions": ["Q4_0"], … }]. Serve-time ids are
            // "name:precision", matching what /v1/models reports.
            ListCliArgs: "list --format json",
            ParseCliList: json =>
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Array) return Array.Empty<CliModelEntry>();
                var entries = new List<CliModelEntry>();
                foreach (var e in doc.RootElement.EnumerateArray())
                {
                    var name = e.TryGetProperty("name", out var n) ? n.GetString() : null;
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    // qairt = Qualcomm AI Runtime = runs on the Hexagon NPU;
                    // llama_cpp = CPU/GPU. Tag the device so the catalog's
                    // NPU filter can tell them apart.
                    var runtime = e.TryGetProperty("runtime", out var r) ? r.GetString() : null;
                    var device = string.Equals(runtime, "qairt", StringComparison.OrdinalIgnoreCase)
                        ? "GenieX NPU"
                        : "GenieX";
                    if (e.TryGetProperty("precisions", out var precs) && precs.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var prec in precs.EnumerateArray())
                        {
                            var p = prec.GetString();
                            if (!string.IsNullOrWhiteSpace(p)) entries.Add(new CliModelEntry($"{name}:{p}", device));
                        }
                    }
                    else
                    {
                        entries.Add(new CliModelEntry(name!, device));
                    }
                }
                return entries;
            },
            CatalogUrl: "https://aihub.qualcomm.com/",
            PullArgsTemplate: "pull {0}"),
    };

    /// <summary>Shared extractor for OpenAI-compatible "/v1/models"
    /// responses: { "data": [ { "id": "…" } ] }.</summary>
    private static IEnumerable<string> ExtractOpenAiModelIds(JsonElement root)
    {
        if (!root.TryGetProperty("data", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();
        return arr.EnumerateArray()
            .Where(e => e.TryGetProperty("id", out _))
            .Select(e => e.GetProperty("id").GetString() ?? "")
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .ToArray();
    }

    public async Task<IReadOnlyList<CatalogModelInfo>> DiscoverAsync(bool autoStartInstalled = false, CancellationToken ct = default)
    {
        // Probe all providers concurrently. Any single provider that's not
        // running (or is slow) fails fast into an empty list — the others
        // aren't blocked waiting.
        var probes = Providers.Select(p => ProbeAsync(p, autoStartInstalled, ct));
        var results = await Task.WhenAll(probes);
        return results.SelectMany(r => r).ToArray();
    }

    private async Task<IReadOnlyList<CatalogModelInfo>> ProbeAsync(ExternalProvider p, bool autoStart, CancellationToken ct)
    {
        var httpModels = await TryListAsync(p, ProbeTimeout, ct);
        if (httpModels is null && autoStart && await EnsureRunningAsync(p, ct))
        {
            httpModels = await TryListAsync(p, TimeSpan.FromSeconds(2), ct);
        }

        // Union with the CLI's full inventory. LM Studio and GenieX only
        // report *loaded* models over HTTP; their CLIs list everything
        // downloaded, which is what users expect the picker to show.
        var cliModels = await TryListViaCliAsync(p, ct);

        if (httpModels is null && cliModels is null) return Array.Empty<CatalogModelInfo>();

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var union = new List<CatalogModelInfo>();
        foreach (var m in (httpModels ?? []).Concat(cliModels ?? []))
        {
            if (seen.Add(m.Id)) union.Add(m);
        }
        return union;
    }

    /// <summary>Enumerate a provider's downloaded models via its CLI, or
    /// null when the provider has no CLI listing or it fails.</summary>
    private async Task<IReadOnlyList<CatalogModelInfo>?> TryListViaCliAsync(ExternalProvider p, CancellationToken ct)
    {
        if (p.ListCliArgs is null || p.ParseCliList is null || p.StartExeCandidates is null) return null;

        foreach (var exe in p.StartExeCandidates)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = p.ListCliArgs,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true,
                };
                using var process = System.Diagnostics.Process.Start(psi);
                if (process is null) continue;

                var output = await process.StandardOutput.ReadToEndAsync(ct);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                await process.WaitForExitAsync(timeout.Token);
                if (process.ExitCode != 0) return null;

                return p.ParseCliList(output)
                    .Select(e => new CatalogModelInfo(
                        Id: $"{p.Prefix}:{e.Id}",
                        Device: e.DeviceLabel ?? p.DisplayLabel,
                        SizeBytes: null,
                        IsCached: true))
                    .ToArray();
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch
            {
                // Executable not found at this candidate, or parse failed —
                // try the next candidate.
            }
        }
        return null;
    }

    public async Task<bool> EnsureRunningAsync(ExternalProvider p, CancellationToken ct = default)
    {
        if (await TryListAsync(p, ProbeTimeout, ct) is not null) return true;
        if (p.StartExeCandidates is null) return false;

        // Server not answering: try to launch the provider's CLI. Missing
        // executables fail per-candidate; none found = not installed.
        if (!TryStartServer(p)) return false;

        // Poll until the freshly started server answers. Servers like
        // `ollama serve` are up within a second or two; give a generous
        // window for slower first starts, but bail early on cancellation.
        for (int i = 0; i < 8 && !ct.IsCancellationRequested; i++)
        {
            await Task.Delay(1000, ct);
            if (await TryListAsync(p, TimeSpan.FromSeconds(2), ct) is not null) return true;
        }
        return false;
    }

    /// <summary>List a provider's models, or null when unreachable.</summary>
    private async Task<IReadOnlyList<CatalogModelInfo>?> TryListAsync(ExternalProvider p, TimeSpan timeout, CancellationToken ct)
    {
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linked.CancelAfter(timeout);

            using var resp = await _http.GetAsync(p.BaseUrl + p.ListPath, linked.Token);
            if (!resp.IsSuccessStatusCode) return null;

            using var stream = await resp.Content.ReadAsStreamAsync(linked.Token);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: linked.Token);

            return p.ExtractModels(doc.RootElement)
                .Select(id => new CatalogModelInfo(
                    Id: $"{p.Prefix}:{id}",
                    Device: p.DisplayLabel,
                    SizeBytes: null,
                    IsCached: true))
                .ToArray();
        }
        catch
        {
            // Provider isn't reachable, port is closed, or the response
            // shape drifted — treat as "provider absent".
            return null;
        }
    }

    public bool IsInstalled(ExternalProvider p)
    {
        if (p.StartExeCandidates is null) return false;
        foreach (var exe in p.StartExeCandidates)
        {
            if (Path.IsPathRooted(exe))
            {
                if (File.Exists(exe)) return true;
                continue;
            }
            // Bare name: search every PATH directory for exe / exe.exe.
            var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var dir in pathVar.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    if (File.Exists(Path.Combine(dir.Trim(), exe + ".exe")) ||
                        File.Exists(Path.Combine(dir.Trim(), exe)))
                        return true;
                }
                catch { /* malformed PATH entry */ }
            }
        }
        return false;
    }

    public async Task<bool> PullModelAsync(ExternalProvider p, string modelName, IProgress<string>? progress, CancellationToken ct = default)
    {
        if (p.PullArgsTemplate is null || p.StartExeCandidates is null) return false;
        if (string.IsNullOrWhiteSpace(modelName)) return false;

        // Guard against argument injection through the free-text model
        // name — provider model ids never legitimately contain quotes or
        // shell-significant whitespace beyond none at all.
        var name = modelName.Trim();
        if (name.Contains('"') || name.Contains(' ')) return false;

        foreach (var exe in p.StartExeCandidates)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = string.Format(p.PullArgsTemplate, name),
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };
                using var process = new System.Diagnostics.Process { StartInfo = psi };
                process.OutputDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) progress?.Report(e.Data!); };
                process.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) progress?.Report(e.Data!); };
                if (!process.Start()) continue;
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                try
                {
                    await process.WaitForExitAsync(ct);
                }
                catch (OperationCanceledException)
                {
                    try { process.Kill(entireProcessTree: true); } catch { }
                    throw;
                }
                return process.ExitCode == 0;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // Executable not found at this candidate — try the next.
            }
        }
        return false;
    }

    private static bool TryStartServer(ExternalProvider p)
    {
        foreach (var exe in p.StartExeCandidates!)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = p.StartArgs ?? "",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                // Detached on purpose: the server outlives this app so the
                // user's next session finds it already running.
                System.Diagnostics.Process.Start(psi);
                return true;
            }
            catch
            {
                // Executable not found at this candidate — try the next.
            }
        }
        return false;
    }

    public ExternalProvider? MatchProvider(string modelId)
    {
        if (string.IsNullOrEmpty(modelId)) return null;
        var colon = modelId.IndexOf(':');
        if (colon <= 0) return null;
        var prefix = modelId[..colon];
        return Providers.FirstOrDefault(p =>
            string.Equals(p.Prefix, prefix, StringComparison.OrdinalIgnoreCase));
    }
}

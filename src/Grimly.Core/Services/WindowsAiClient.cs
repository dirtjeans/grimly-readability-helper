using AionInstructPreview.Text;
using Grimly.Hosting;

namespace Grimly.Services;

public interface IWindowsAiClient
{
    /// <summary>Sentinel model id that routes requests through this client
    /// instead of Foundry Local's HTTP endpoint. Injected as a virtual
    /// entry in the model catalog when the framework is available.</summary>
    const string ModelId = "windows-ai";

    /// <summary>True when the Aion Instruct framework package is installed
    /// and this process could take a dependency on it. Cached after the
    /// first probe.</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Send a full prompt (instructions + text) to the on-device model and
    /// return its text response. Unlike the retired Phi Silica TextRewriter
    /// wrapper, Aion accepts arbitrary prompts, so callers can express any
    /// editing instruction. Returns null on failure (model unavailable,
    /// generation error) — callers fall back to showing an error, never
    /// partial text.
    /// </summary>
    Task<string?> GenerateAsync(string prompt, CancellationToken ct = default);
}

/// <summary>
/// On-device language model client backed by Microsoft's Aion Instruct
/// Preview (the successor to Phi Silica — no LAF token, works in unpackaged
/// apps). Runs on the NPU of Copilot+ PCs; the framework MSIX
/// (Microsoft.AionInstructPreview.Framework) must be installed on the
/// machine, which the availability probe detects.
///
/// The first CreateAsync on a machine compiles the model for the NPU and
/// can take several minutes; subsequent app runs are fast. The model and
/// context are cached for the process lifetime, so within one app session
/// only the first Windows AI call pays the spin-up cost.
/// </summary>
public sealed class WindowsAiClient : IWindowsAiClient
{
    private readonly Lazy<bool> _isAvailable;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private LanguageModel? _model;

    public WindowsAiClient()
    {
        _isAvailable = new Lazy<bool>(ProbeAvailability);
    }

    public bool IsAvailable => _isAvailable.Value;

    private static bool ProbeAvailability()
    {
        try
        {
            AionFrameworkDependency.EnsureLoaded();
            StartupLog.Write("WindowsAi: Aion framework dependency loaded.");
            return true;
        }
        catch (Exception ex)
        {
            StartupLog.Write($"WindowsAi: unavailable — {ex.Message}");
            return false;
        }
    }

    public async Task<string?> GenerateAsync(string prompt, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(prompt)) return null;
        if (!IsAvailable) return null;

        try
        {
            var model = await GetModelAsync(ct);

            // Fresh context per request — Grimly calls are independent
            // one-shot edits, not a running conversation, and reusing a
            // context would leak prior text into later rewrites.
            var context = model.CreateContext();

            var op = model.GenerateResponseAsync(context, prompt);
            ct.Register(() => { try { op.Cancel(); } catch { } });
            LanguageModelResponseResult result = await op;

            if (result.Status != LanguageModelResponseStatus.Complete)
            {
                StartupLog.Write($"WindowsAi: generation ended with status {result.Status}");
                return null;
            }

            var text = result.Text?.Trim();
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            StartupLog.Write($"WindowsAi: generation failed — {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Create (or return the cached) LanguageModel. Serialized so parallel
    /// first calls don't both pay the multi-minute NPU compile.
    /// </summary>
    private async Task<LanguageModel> GetModelAsync(CancellationToken ct)
    {
        if (_model is not null) return _model;

        await _initLock.WaitAsync(ct);
        try
        {
            if (_model is null)
            {
                StartupLog.Write("WindowsAi: creating LanguageModel (first call may take minutes)…");
                _model = await LanguageModel.CreateAsync();
                StartupLog.Write("WindowsAi: LanguageModel ready.");
            }
            return _model;
        }
        finally
        {
            _initLock.Release();
        }
    }
}

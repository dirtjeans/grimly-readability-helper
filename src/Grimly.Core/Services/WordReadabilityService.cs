using System.Runtime.InteropServices;

namespace Grimly.Services;

/// <summary>
/// Computes readability statistics by deferring to a local Microsoft Word
/// installation via COM interop. Returns the exact Flesch Reading Ease value
/// Word itself would report, which sidesteps heuristic divergence from the
/// local <see cref="ReadabilityService"/>.
///
/// Availability is probed by ProgID at startup (fast, no Word launch).
/// The Word.Application instance is created lazily on first use and kept alive
/// for subsequent calls (cold-start is ~1–2s; warm calls are ~100–300ms).
///
/// Not available on macOS — that target uses a different implementation.
/// </summary>
public interface IWordReadabilityService : IDisposable
{
    /// <summary>True if Word COM is registered on this machine.</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Returns Word's Flesch Reading Ease score for <paramref name="text"/>,
    /// or null if Word is unavailable, the call fails, or the text is empty.
    /// </summary>
    Task<double?> CalculateFleschAsync(string text, CancellationToken ct = default);
}

public sealed class WordReadabilityService : IWordReadabilityService
{
    private dynamic? _app;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _disposed;
    private bool? _availabilityCache;

    public bool IsAvailable
    {
        get
        {
            _availabilityCache ??= Type.GetTypeFromProgID("Word.Application") != null;
            return _availabilityCache.Value;
        }
    }

    public async Task<double?> CalculateFleschAsync(string text, CancellationToken ct = default)
    {
        if (_disposed || !IsAvailable) return null;
        if (string.IsNullOrWhiteSpace(text)) return null;

        // Word.Application expects serialized access — one caller at a time.
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_disposed) return null;
            EnsureApp();
            if (_app == null) return null;

            // Run the blocking COM calls on a background thread so the UI
            // thread isn't frozen during the ~100–300ms Word takes to respond.
            return await Task.Run(() => QueryFlesch(text), ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { return null; }
        catch { return null; }
        finally
        {
            _lock.Release();
        }
    }

    private double? QueryFlesch(string text)
    {
        dynamic? doc = null;
        try
        {
            doc = _app!.Documents.Add();
            doc.Content.Text = text;
            var stats = doc.Content.ReadabilityStatistics;
            int count = stats.Count;
            for (int i = 1; i <= count; i++)
            {
                dynamic s = stats[i];
                if ((string)s.Name == "Flesch Reading Ease")
                    return (double?)s.Value;
            }
            return null;
        }
        catch
        {
            // Connection may have died (Word crashed/closed) — invalidate so
            // the next call will re-create.
            TryReleaseApp();
            return null;
        }
        finally
        {
            try { doc?.Close(false); } catch { }
        }
    }

    private void EnsureApp()
    {
        if (_app != null) return;
        try
        {
            var type = Type.GetTypeFromProgID("Word.Application");
            if (type == null) { _availabilityCache = false; return; }
            _app = Activator.CreateInstance(type);
            if (_app == null) return;
            _app.Visible = false;
            _app.DisplayAlerts = 0;  // wdAlertsNone — no save/close prompts
        }
        catch
        {
            TryReleaseApp();
        }
    }

    private void TryReleaseApp()
    {
        try { _app?.Quit(); } catch { }
        if (_app != null)
        {
            try { Marshal.FinalReleaseComObject(_app); } catch { }
            _app = null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _lock.Wait(TimeSpan.FromSeconds(3)); } catch { }
        try { TryReleaseApp(); }
        finally
        {
            try { _lock.Release(); } catch { }
            _lock.Dispose();
        }
    }
}

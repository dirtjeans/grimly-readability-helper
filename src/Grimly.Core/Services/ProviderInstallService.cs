using System.Diagnostics;

namespace Grimly.Services;

public interface IProviderInstallService
{
    /// <summary>
    /// Install a package via winget (silent, agreements pre-accepted).
    /// Returns true when winget exits 0. Progress lines stream the raw
    /// winget output so the caller can show them.
    /// </summary>
    Task<bool> InstallWingetPackageAsync(string wingetId, IProgress<string>? progress, CancellationToken ct = default);
}

/// <summary>
/// One-click installer for external LLM providers (Ollama, LM Studio).
/// Thin wrapper over winget — the same mechanism FoundryInstallerService
/// uses — kept separate because provider installs are single-step (no
/// service start, no model load) and callable straight from Settings.
/// </summary>
public sealed class ProviderInstallService : IProviderInstallService
{
    public async Task<bool> InstallWingetPackageAsync(string wingetId, IProgress<string>? progress, CancellationToken ct = default)
    {
        progress?.Report($"> winget install {wingetId}");

        var psi = new ProcessStartInfo
        {
            FileName = "winget",
            Arguments =
                $"install {wingetId} " +
                "--accept-source-agreements --accept-package-agreements " +
                "--disable-interactivity --silent",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        using var process = new Process { StartInfo = psi };
        process.OutputDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) progress?.Report(e.Data!); };
        process.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) progress?.Report(e.Data!); };

        if (!process.Start()) return false;
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

        progress?.Report($"winget exited with code {process.ExitCode}");
        return process.ExitCode == 0;
    }
}

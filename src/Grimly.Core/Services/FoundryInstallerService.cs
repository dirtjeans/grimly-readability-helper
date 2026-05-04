using System.Diagnostics;
using Microsoft.Win32;

namespace Grimly.Services;

public interface IFoundryInstallerService
{
    event Action<string>? StepChanged;
    event Action<string>? LogLine;
    event Action<InstallStage>? StageChanged;

    Task<bool> InstallAsync(CancellationToken ct = default);
}

public enum InstallStage
{
    NotStarted,
    InstallingFoundry,
    StartingService,
    LoadingModel,
    Completed,
    Failed
}

public sealed class FoundryInstallerService : IFoundryInstallerService
{
    public event Action<string>? StepChanged;
    public event Action<string>? LogLine;
    public event Action<InstallStage>? StageChanged;

    public async Task<bool> InstallAsync(CancellationToken ct = default)
    {
        try
        {
            // Step 1: winget install Microsoft.FoundryLocal
            StageChanged?.Invoke(InstallStage.InstallingFoundry);
            StepChanged?.Invoke("Step 1 of 3 — Downloading and installing Foundry Local...");
            LogLine?.Invoke("> winget install Microsoft.FoundryLocal");

            var wingetResult = await RunProcessAsync(
                "winget",
                "install Microsoft.FoundryLocal " +
                "--accept-source-agreements --accept-package-agreements " +
                "--disable-interactivity --silent",
                ct);

            if (wingetResult.ExitCode != 0)
            {
                LogLine?.Invoke($"winget exited with code {wingetResult.ExitCode}");
                StageChanged?.Invoke(InstallStage.Failed);
                return false;
            }

            LogLine?.Invoke("Foundry Local installed successfully.");
            LogLine?.Invoke("");

            // Refresh PATH environment so we can find foundry.exe
            var foundryPath = FindFoundryExecutable();
            if (foundryPath == null)
            {
                LogLine?.Invoke("ERROR: Could not locate foundry.exe after install.");
                LogLine?.Invoke("You may need to restart Grimly for PATH changes to take effect.");
                StageChanged?.Invoke(InstallStage.Failed);
                return false;
            }

            LogLine?.Invoke($"Found foundry at: {foundryPath}");
            LogLine?.Invoke("");

            // Step 2: foundry service start
            // NOTE: "foundry service start" may run as a blocking foreground process
            // that never exits. We launch it detached and poll the HTTP endpoint instead.
            StageChanged?.Invoke(InstallStage.StartingService);
            StepChanged?.Invoke("Step 2 of 3 — Starting the Foundry service...");
            LogLine?.Invoke("> foundry service start");

            var capturedOutput = new System.Text.StringBuilder();
            var serviceProcess = LaunchDetached(foundryPath, "service start", capturedOutput);

            // The service announces its endpoint in stdout, e.g.:
            //   "Service is Started on http://127.0.0.1:65440/, PID 17796!"
            // Wait for the endpoint to appear, or fall back to polling common ports.
            string? serviceEndpoint = null;

            for (int i = 0; i < 30 && serviceEndpoint == null; i++)
            {
                await Task.Delay(1000, ct);
                serviceEndpoint = ExtractEndpointFromOutput(capturedOutput.ToString());
            }

            // Fall back to well-known port if we couldn't parse the endpoint
            serviceEndpoint ??= "http://127.0.0.1:51318";

            LogLine?.Invoke($"Using endpoint: {serviceEndpoint}");

            var serviceReady = await PollServiceEndpointAsync(serviceEndpoint, maxWaitSeconds: 30, ct);

            if (!serviceReady)
            {
                LogLine?.Invoke("Foundry service did not become ready within 30 seconds.");
                StageChanged?.Invoke(InstallStage.Failed);
                return false;
            }

            LogLine?.Invoke("Service started.");
            LogLine?.Invoke("");

            // Step 3: foundry model download qwen2.5-7b
            StageChanged?.Invoke(InstallStage.LoadingModel);
            StepChanged?.Invoke("Step 3 of 3 — Downloading the qwen2.5-7b model (this may take several minutes)...");
            LogLine?.Invoke("> foundry model download qwen2.5-7b");

            var modelResult = await RunProcessAsync(foundryPath, "model download qwen2.5-7b", ct);
            if (modelResult.ExitCode != 0)
            {
                LogLine?.Invoke($"foundry model download exited with code {modelResult.ExitCode}");
                StageChanged?.Invoke(InstallStage.Failed);
                return false;
            }

            LogLine?.Invoke("Model downloaded.");
            LogLine?.Invoke("");
            LogLine?.Invoke("Loading model into service...");

            var loadResult = await RunProcessAsync(foundryPath, "model load qwen2.5-7b", ct);
            if (loadResult.ExitCode != 0)
            {
                LogLine?.Invoke($"Note: model load returned {loadResult.ExitCode} (may still be usable)");
            }

            StepChanged?.Invoke("Installation complete!");
            LogLine?.Invoke("");
            LogLine?.Invoke("=== Foundry Local is ready ===");
            StageChanged?.Invoke(InstallStage.Completed);
            return true;
        }
        catch (OperationCanceledException)
        {
            LogLine?.Invoke("");
            LogLine?.Invoke("Installation cancelled.");
            StageChanged?.Invoke(InstallStage.Failed);
            return false;
        }
        catch (Exception ex)
        {
            LogLine?.Invoke("");
            LogLine?.Invoke($"ERROR: {ex.Message}");
            StageChanged?.Invoke(InstallStage.Failed);
            return false;
        }
    }

    // Strip ANSI escape codes from output
    private static readonly System.Text.RegularExpressions.Regex AnsiRegex =
        new(@"\x1B\[[0-9;]*[A-Za-z]|\x1B\].*?\x07", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static string StripAnsi(string text) => AnsiRegex.Replace(text, "");

    private async Task<(int ExitCode, string Output)> RunProcessAsync(string fileName, string arguments, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8
        };

        // Set environment variables to discourage interactive prompts
        psi.EnvironmentVariables["CI"] = "1";
        psi.EnvironmentVariables["NO_COLOR"] = "1";

        // Refresh PATH in case winget just installed something
        var refreshedPath = GetRefreshedPath();
        if (refreshedPath != null)
            psi.EnvironmentVariables["PATH"] = refreshedPath;

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var output = new System.Text.StringBuilder();
        var outputLock = new object();
        var lastOutputTime = DateTime.UtcNow;
        var stallWarningShown = false;

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                var clean = StripAnsi(e.Data);
                lock (outputLock) { output.AppendLine(clean); }
                LogLine?.Invoke(clean);
                lastOutputTime = DateTime.UtcNow;
                stallWarningShown = false;
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                var clean = StripAnsi(e.Data);
                lock (outputLock) { output.AppendLine(clean); }
                LogLine?.Invoke(clean);
                lastOutputTime = DateTime.UtcNow;
                stallWarningShown = false;
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        // Close stdin so any command waiting for input gets EOF
        try { process.StandardInput.Close(); } catch { }

        var stallWarningTimeout = TimeSpan.FromSeconds(30);
        var hardTimeout = TimeSpan.FromMinutes(30);
        using var hardCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        hardCts.CancelAfter(hardTimeout);

        using var stallCts = CancellationTokenSource.CreateLinkedTokenSource(hardCts.Token);

        var watchdog = Task.Run(async () =>
        {
            while (!process.HasExited && !stallCts.Token.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), stallCts.Token);

                var elapsed = DateTime.UtcNow - lastOutputTime;

                // Show a yellow warning after 30s of silence
                if (elapsed > stallWarningTimeout && !stallWarningShown)
                {
                    stallWarningShown = true;
                    LogLine?.Invoke($"[Still working — no output for {(int)elapsed.TotalSeconds}s. Large downloads can be quiet for a while.]");
                }
            }
        }, stallCts.Token);

        try
        {
            await process.WaitForExitAsync(hardCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Hard timeout hit (not user cancel)
            LogLine?.Invoke($"[Command timed out after {hardTimeout.TotalMinutes} minutes. Killing.]");
            try { process.Kill(entireProcessTree: true); } catch { }
            throw;
        }
        finally
        {
            stallCts.Cancel();
            try { await watchdog; } catch { }
        }

        lock (outputLock) { return (process.ExitCode, output.ToString()); }
    }

    /// <summary>
    /// Starts a process without waiting for it to exit.
    /// Used for "foundry service start" which may block as a foreground process.
    /// </summary>
    private Process? LaunchDetached(string fileName, string arguments, System.Text.StringBuilder? capturedOutput = null)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8
            };

            psi.EnvironmentVariables["CI"] = "1";
            psi.EnvironmentVariables["NO_COLOR"] = "1";

            var refreshedPath = GetRefreshedPath();
            if (refreshedPath != null)
                psi.EnvironmentVariables["PATH"] = refreshedPath;

            var process = new Process { StartInfo = psi };

            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null)
                {
                    var clean = StripAnsi(e.Data);
                    capturedOutput?.AppendLine(clean);
                    LogLine?.Invoke(clean);
                }
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null)
                {
                    var clean = StripAnsi(e.Data);
                    capturedOutput?.AppendLine(clean);
                    LogLine?.Invoke(clean);
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            try { process.StandardInput.Close(); } catch { }

            return process;
        }
        catch (Exception ex)
        {
            LogLine?.Invoke($"Failed to start process: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Polls the Foundry HTTP endpoint until it responds, confirming the service is ready.
    /// </summary>
    private async Task<bool> PollServiceEndpointAsync(string endpoint, int maxWaitSeconds, CancellationToken ct)
    {
        using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(3) };

        for (int i = 0; i < maxWaitSeconds; i++)
        {
            try
            {
                var response = await http.GetAsync($"{endpoint}/v1/models", ct);
                if (response.IsSuccessStatusCode)
                    return true;
            }
            catch { }

            await Task.Delay(1000, ct);

            if (i > 0 && i % 5 == 0)
                LogLine?.Invoke($"[Waiting for service... {i}s]");
        }

        return false;
    }

    /// <summary>
    /// Parses an endpoint URL from process output like:
    ///   "Service is Started on http://127.0.0.1:65440/, PID 17796!"
    /// </summary>
    private static string? ExtractEndpointFromOutput(string output)
    {
        var idx = output.IndexOf("http://", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) idx = output.IndexOf("https://", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;

        var end = output.IndexOfAny([' ', ',', '\n', '\r'], idx + 8);
        if (end < 0) end = output.Length;

        var url = output[idx..end].TrimEnd('/');

        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return $"{uri.Scheme}://{uri.Authority}";

        return null;
    }

    private static string? GetRefreshedPath()
    {
        try
        {
            var userPath = Registry.GetValue(
                @"HKEY_CURRENT_USER\Environment",
                "PATH",
                "") as string ?? "";
            var machinePath = Registry.GetValue(
                @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Session Manager\Environment",
                "PATH",
                "") as string ?? "";

            return $"{machinePath};{userPath}";
        }
        catch
        {
            return null;
        }
    }

    private static string? FindFoundryExecutable()
    {
        // Try the refreshed PATH first
        var path = GetRefreshedPath();
        if (path != null)
        {
            foreach (var dir in path.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    var candidate = System.IO.Path.Combine(dir, "foundry.exe");
                    if (System.IO.File.Exists(candidate))
                        return candidate;
                }
                catch { }
            }
        }

        // Common winget install locations
        var commonPaths = new[]
        {
            Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%\Microsoft\WinGet\Links\foundry.exe"),
            Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%\Programs\FoundryLocal\foundry.exe"),
            Environment.ExpandEnvironmentVariables(@"%ProgramFiles%\Microsoft\FoundryLocal\foundry.exe"),
            Environment.ExpandEnvironmentVariables(@"%ProgramFiles(x86)%\Microsoft\FoundryLocal\foundry.exe"),
        };

        foreach (var p in commonPaths)
        {
            if (System.IO.File.Exists(p))
                return p;
        }

        return null;
    }
}

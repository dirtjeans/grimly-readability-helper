import Foundation
@preconcurrency import Dispatch

// MARK: - Thread-safe helpers

/// Thread-safe line buffer for streaming process output.
final class LineBuffer: @unchecked Sendable {
    private var contents: String = ""
    private let lock = NSLock()

    func append(_ chunk: String) -> [String] {
        lock.lock()
        defer { lock.unlock() }

        contents += chunk
        var lines: [String] = []
        while let newlineRange = contents.range(of: "\n") {
            let line = String(contents[..<newlineRange.lowerBound])
            contents.removeSubrange(contents.startIndex..<newlineRange.upperBound)
            if !line.isEmpty {
                lines.append(line)
            }
        }
        return lines
    }

    func drain() -> String {
        lock.lock()
        defer { lock.unlock() }
        let remaining = contents
        contents = ""
        return remaining
    }
}

/// Guards a CheckedContinuation so it can only be resumed once from any thread.
final class ResumeGate: @unchecked Sendable {
    private var resumed = false
    private let lock = NSLock()
    private let continuation: CheckedContinuation<String?, Never>

    init(_ continuation: CheckedContinuation<String?, Never>) {
        self.continuation = continuation
    }

    func resume(_ value: String?) {
        lock.lock()
        defer { lock.unlock() }
        guard !resumed else { return }
        resumed = true
        continuation.resume(returning: value)
    }
}

/// Thread-safe timestamp holder for the stall watchdog.
final class AtomicDate: @unchecked Sendable {
    private var value = Date()
    private let lock = NSLock()

    func set(_ d: Date) {
        lock.lock(); defer { lock.unlock() }
        value = d
    }

    func get() -> Date {
        lock.lock(); defer { lock.unlock() }
        return value
    }
}

// MARK: - Installer

@MainActor
class InstallerService: ObservableObject {
    @Published var currentStepIndex: Int = 0
    @Published var stepName: String = ""
    @Published var logLines: [String] = []
    @Published var isRunning: Bool = false
    @Published var isComplete: Bool = false
    @Published var errorMessage: String?
    /// Set to a message like "Still working... (no output for 45s)" when the
    /// current step has been silent for a while. Cleared when new output arrives.
    @Published var stallWarning: String?

    private var currentProcess: Process?
    private var wasCancelled = false
    private let maxLogLines = 18

    /// Seconds of silence before we warn the user that the step is still running.
    private let stallWarnThreshold: TimeInterval = 30

    /// Hard timeout per step in seconds (safety net for truly hung processes).
    private let stepTimeout: TimeInterval = 30 * 60  // 30 minutes

    /// URL for the macOS .pkg.zip installer from Microsoft's GitHub releases.
    private let pkgZipURL = "https://github.com/microsoft/Foundry-Local/releases/download/v0.8.119/FoundryLocal-osx-arm64-0.8.119.pkg.zip"

    let totalSteps: Int = 4

    func install() async {
        isRunning = true
        isComplete = false
        errorMessage = nil
        stallWarning = nil
        wasCancelled = false
        logLines = []
        currentStepIndex = 0

        let stepDefs: [(name: String, action: () async -> String?)] = [
            ("Downloading Foundry Local installer", { [self] in await downloadInstaller() }),
            ("Installing Foundry Local (follow the installer prompts)", { [self] in await runPkgInstaller() }),
            ("Starting Foundry service", { [self] in await runCommand("foundry", args: ["service", "start"]) }),
            ("Downloading model (qwen2.5-7b — several GB, please be patient)", { [self] in await runCommand("foundry", args: ["model", "download", "qwen2.5-7b"]) }),
        ]

        for (idx, step) in stepDefs.enumerated() {
            if wasCancelled { break }

            currentStepIndex = idx + 1
            stepName = step.name
            stallWarning = nil
            appendLog("\n▸ \(step.name)")

            let result = await step.action()

            if wasCancelled {
                appendLog("⚠ Cancelled by user")
                break
            }

            if let error = result {
                errorMessage = "\(step.name) failed.\n\n\(error)"
                appendLog("✗ Failed: \(error)")
                isRunning = false
                stallWarning = nil
                return
            } else {
                appendLog("✓ Done")
            }
        }

        // Clean up temp files
        cleanupTempFiles()

        isRunning = false
        stallWarning = nil
        if !wasCancelled && errorMessage == nil {
            isComplete = true
            stepName = "Installation complete!"
            appendLog("\n🎉 Foundry Local is ready. You can now use Grimly.")
        }
    }

    // MARK: - Download

    private var downloadedPkgPath: String?

    private func downloadInstaller() async -> String? {
        guard let url = URL(string: pkgZipURL) else {
            return "Invalid download URL"
        }

        appendLog("Downloading from GitHub...")

        do {
            let (tempURL, response) = try await URLSession.shared.download(from: url)

            if let httpResponse = response as? HTTPURLResponse, httpResponse.statusCode != 200 {
                return "Download failed with HTTP \(httpResponse.statusCode)"
            }

            let tempDir = FileManager.default.temporaryDirectory.appendingPathComponent("GrimlyInstall")
            try FileManager.default.createDirectory(at: tempDir, withIntermediateDirectories: true)

            // Move the downloaded zip
            let zipPath = tempDir.appendingPathComponent("FoundryLocal.pkg.zip")
            if FileManager.default.fileExists(atPath: zipPath.path) {
                try FileManager.default.removeItem(at: zipPath)
            }
            try FileManager.default.moveItem(at: tempURL, to: zipPath)

            let fileSize = (try? FileManager.default.attributesOfItem(atPath: zipPath.path)[.size] as? Int) ?? 0
            appendLog("Downloaded \(fileSize / (1024 * 1024)) MB")

            // Unzip
            appendLog("Extracting installer...")
            let unzipResult = await runCommand("/usr/bin/unzip", args: ["-o", zipPath.path, "-d", tempDir.path])
            if let err = unzipResult { return "Failed to extract: \(err)" }

            // Find the .pkg file — unzip may place it in a subdirectory
            guard let pkgPath = findFile(withExtension: "pkg", in: tempDir.path) else {
                return "No .pkg file found in the downloaded archive"
            }

            downloadedPkgPath = pkgPath
            appendLog("Installer ready: \(URL(fileURLWithPath: pkgPath).lastPathComponent)")
            return nil
        } catch is CancellationError {
            return "Download cancelled"
        } catch {
            return error.localizedDescription
        }
    }

    // MARK: - Run .pkg installer

    private func runPkgInstaller() async -> String? {
        guard let pkgPath = downloadedPkgPath else {
            return "Installer file not found"
        }

        appendLog("Opening macOS installer...")
        appendLog("(Follow the prompts in the installer window)")

        // Use 'open -W' to launch the native macOS .pkg installer and wait
        // for it to finish. The user sees the standard Apple install wizard.
        let result = await runCommand("/usr/bin/open", args: ["-W", pkgPath])

        if let err = result {
            return "Installer failed: \(err)"
        }

        // Verify foundry was installed
        let foundryInstalled = FileManager.default.isExecutableFile(atPath: "/opt/homebrew/bin/foundry")
            || FileManager.default.isExecutableFile(atPath: "/usr/local/bin/foundry")

        // Also check common .pkg install locations
        let pkgPaths = ["/usr/local/bin/foundry", "/opt/foundry/bin/foundry", "/Applications/FoundryLocal/bin/foundry"]
        let installedPath = pkgPaths.first { FileManager.default.isExecutableFile(atPath: $0) }

        if !foundryInstalled && installedPath == nil {
            // The user might have cancelled the installer
            return "Foundry Local was not detected after installation. The installer may have been cancelled."
        }

        appendLog("Foundry Local installed successfully")
        return nil
    }

    private func cleanupTempFiles() {
        let tempDir = FileManager.default.temporaryDirectory.appendingPathComponent("GrimlyInstall")
        try? FileManager.default.removeItem(at: tempDir)
        downloadedPkgPath = nil
    }

    func cancel() {
        wasCancelled = true
        currentProcess?.terminate()
    }

    /// Run a command and stream its output to the log. Returns nil on success
    /// or an error message on failure.
    private func runCommand(_ command: String, args: [String]) async -> String? {
        let binaryPath: String
        if command.hasPrefix("/") {
            // Absolute path — use directly
            binaryPath = command
        } else {
            guard let found = findBinary(command) else {
                return "\(command) not found in /opt/homebrew/bin or /usr/local/bin"
            }
            binaryPath = found
        }

        return await withCheckedContinuation { (continuation: CheckedContinuation<String?, Never>) in
            let process = Process()
            let pipe = Pipe()
            let buffer = LineBuffer()
            let gate = ResumeGate(continuation)
            let lastOutput = AtomicDate()
            lastOutput.set(Date())

            // Wrap command in /usr/bin/script to get a pty for line-buffered output.
            // Syntax: script -q /dev/null <command> [args...]
            process.executableURL = URL(fileURLWithPath: "/usr/bin/script")
            process.arguments = ["-q", "/dev/null", binaryPath] + args

            process.standardOutput = pipe
            process.standardError = pipe

            // --- Close stdin so any unexpected prompts fail fast instead of hanging ---
            if let devNull = FileHandle(forReadingAtPath: "/dev/null") {
                process.standardInput = devNull
            }

            // --- Environment tuning ---
            var env = ProcessInfo.processInfo.environment
            let extraPath = "/opt/homebrew/bin:/usr/local/bin"
            if let existing = env["PATH"] {
                env["PATH"] = "\(extraPath):\(existing)"
            } else {
                env["PATH"] = extraPath
            }
            env["HOMEBREW_NO_AUTO_UPDATE"] = "1"
            env["HOMEBREW_NO_INSTALL_CLEANUP"] = "1"
            env["HOMEBREW_NO_ENV_HINTS"] = "1"
            env["HOMEBREW_NO_COLOR"] = "1"
            env["NONINTERACTIVE"] = "1"
            env["CI"] = "1"  // many tools skip prompts when CI is set
            env["TERM"] = "dumb"
            process.environment = env

            // --- Streaming output handler ---
            pipe.fileHandleForReading.readabilityHandler = { handle in
                let data = handle.availableData
                if data.isEmpty { return }
                guard var chunk = String(data: data, encoding: .utf8) else { return }

                // Normalize line endings from pty. Progress bars emit \r which
                // we treat as a line break so users see updates instead of an
                // ever-growing line.
                chunk = chunk.replacingOccurrences(of: "\r\n", with: "\n")
                chunk = chunk.replacingOccurrences(of: "\r", with: "\n")

                lastOutput.set(Date())

                let lines = buffer.append(chunk)
                if !lines.isEmpty {
                    Task { @MainActor [weak self] in
                        self?.stallWarning = nil
                        for line in lines {
                            self?.appendLog(line)
                        }
                    }
                }
            }

            self.currentProcess = process

            // --- Watchdog: warn if output stalls ---
            let watchdog = DispatchSource.makeTimerSource(queue: DispatchQueue.global(qos: .utility))
            watchdog.schedule(deadline: .now() + 5, repeating: 5)
            watchdog.setEventHandler { [weak self] in
                let elapsed = Date().timeIntervalSince(lastOutput.get())
                if elapsed >= self?.stallWarnThreshold ?? 30 {
                    let seconds = Int(elapsed)
                    Task { @MainActor [weak self] in
                        self?.stallWarning = "Still working — no output for \(seconds)s. Large downloads can be quiet for a while."
                    }
                }
            }
            watchdog.resume()

            // --- Per-step timeout ---
            let timeoutWork = DispatchWorkItem { [weak self] in
                process.terminate()
                let minutes = Int((self?.stepTimeout ?? 1800) / 60)
                gate.resume("Timed out after \(minutes) minutes with no completion")
            }
            DispatchQueue.global().asyncAfter(deadline: .now() + stepTimeout, execute: timeoutWork)

            DispatchQueue.global(qos: .userInitiated).async {
                func cleanup() {
                    watchdog.cancel()
                    timeoutWork.cancel()
                    pipe.fileHandleForReading.readabilityHandler = nil
                }

                do {
                    try process.run()
                    process.waitUntilExit()
                    cleanup()

                    // Drain any remaining buffered text
                    let remaining = buffer.drain()
                    if !remaining.isEmpty {
                        Task { @MainActor [weak self] in
                            self?.appendLog(remaining)
                        }
                    }

                    if process.terminationStatus == 0 {
                        gate.resume(nil)
                    } else {
                        gate.resume("Process exited with code \(process.terminationStatus). Check the log above for details.")
                    }
                } catch {
                    cleanup()
                    gate.resume(error.localizedDescription)
                }
            }
        }
    }

    private func appendLog(_ line: String) {
        let cleaned = stripAnsi(line)
        // Split on any embedded newlines (happens when we drain the buffer)
        for sub in cleaned.split(separator: "\n", omittingEmptySubsequences: false) {
            let piece = String(sub)
            if piece.isEmpty && logLines.last?.isEmpty == true { continue }
            logLines.append(piece)
        }
        if logLines.count > maxLogLines {
            logLines.removeFirst(logLines.count - maxLogLines)
        }
    }

    private func stripAnsi(_ s: String) -> String {
        let pattern = "\u{001B}\\[[0-9;?]*[a-zA-Z]"
        guard let regex = try? NSRegularExpression(pattern: pattern) else { return s }
        let range = NSRange(s.startIndex..., in: s)
        return regex.stringByReplacingMatches(in: s, range: range, withTemplate: "")
    }

    /// Recursively find the first file with the given extension in a directory.
    private func findFile(withExtension ext: String, in directory: String) -> String? {
        let fm = FileManager.default
        guard let enumerator = fm.enumerator(atPath: directory) else { return nil }
        while let file = enumerator.nextObject() as? String {
            if file.hasSuffix(".\(ext)") {
                return (directory as NSString).appendingPathComponent(file)
            }
        }
        return nil
    }

    private func findBinary(_ name: String) -> String? {
        let candidates = [
            "/opt/homebrew/bin/\(name)",
            "/usr/local/bin/\(name)",
        ]
        return candidates.first { FileManager.default.isExecutableFile(atPath: $0) }
    }
}

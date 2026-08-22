import Foundation

/// One external local-LLM provider Grimly can talk to alongside Foundry
/// Local. Each exposes a well-known localhost port, a "list models"
/// endpoint, and an OpenAI-compatible `/v1/chat/completions` we can post to
/// without provider-specific shaping.
///
/// Mac port of the Windows `ExternalProvider` record. GenieX (Snapdragon
/// NPU) is intentionally omitted — there's nothing to detect on a Mac.
struct ExternalProvider {
    let prefix: String        // "ollama" / "lmstudio" — becomes the "prefix:" on model ids
    let displayLabel: String  // "Ollama" / "LM Studio"
    let baseURL: String       // "http://localhost:11434"
    let chatEndpoint: String  // "/v1/chat/completions"
    let listPath: String      // "/api/tags" (Ollama) or "/v1/models" (LM Studio)
    /// Pull model ids out of the provider's parsed list JSON.
    let extractModels: (Any) -> [String]
    /// CLI executables tried in order — absolute paths cover installs that
    /// don't add themselves to a GUI app's PATH; bare names resolve via PATH.
    let startExeCandidates: [String]
    let startArgs: [String]
    /// Full-inventory CLI listing. Some servers (LM Studio) only report
    /// *loaded* models over HTTP; the CLI enumerates everything downloaded.
    /// nil = HTTP list already complete (Ollama).
    let listCliArgs: [String]?
    let parseCliList: ((String) -> [String])?
    /// Remote discovery — no provider has an enumerable catalog API, so we
    /// link to where the catalog lives and pull by name.
    let catalogURL: String?
    /// Builds the pull argument list from a model name.
    let pullArgs: ((String) -> [String])?
}

/// Detects and talks to external local-LLM providers (Ollama, LM Studio).
/// Runtime probe/route paths require the provider actually installed and
/// running — build-verified here; end-to-end testing needs such a machine.
final class ExternalLlmProviderService {

    /// Kept short so provider probes never noticeably delay the model
    /// browser's refresh. A running-but-sluggish provider just gets missed;
    /// the user can retry.
    private static let probeTimeout: TimeInterval = 2.0

    let providers: [ExternalProvider]

    init() {
        // Expand ~ for the LM Studio CLI path once at construction.
        let home = FileManager.default.homeDirectoryForCurrentUser.path

        providers = [
            // Ollama: /api/tags → { "models": [ { "name": "…" } ] }.
            ExternalProvider(
                prefix: "ollama",
                displayLabel: "Ollama",
                baseURL: "http://localhost:11434",
                chatEndpoint: "/v1/chat/completions",
                listPath: "/api/tags",
                extractModels: { json in
                    guard let root = json as? [String: Any],
                          let arr = root["models"] as? [[String: Any]] else { return [] }
                    return arr.compactMap { $0["name"] as? String }
                        .filter { !$0.trimmingCharacters(in: .whitespaces).isEmpty }
                },
                startExeCandidates: ["/opt/homebrew/bin/ollama", "/usr/local/bin/ollama", "ollama"],
                startArgs: ["serve"],
                listCliArgs: nil,       // /api/tags is already complete
                parseCliList: nil,
                catalogURL: "https://ollama.com/library",
                pullArgs: { name in ["pull", name] }
            ),

            // LM Studio: OpenAI-compatible /v1/models → { "data": [ { "id" } ] }.
            ExternalProvider(
                prefix: "lmstudio",
                displayLabel: "LM Studio",
                baseURL: "http://localhost:1234",
                chatEndpoint: "/v1/chat/completions",
                listPath: "/v1/models",
                extractModels: Self.extractOpenAiModelIds,
                startExeCandidates: ["\(home)/.lmstudio/bin/lms", "/opt/homebrew/bin/lms", "lms"],
                startArgs: ["server", "start"],
                // `lms ls --json` → [{ "type": "llm"|"vlm"|"embedding",
                // "modelKey": "openai/gpt-oss-20b" }]. modelKey is the id the
                // server accepts; embeddings can't chat, so they're skipped.
                listCliArgs: ["ls", "--json"],
                parseCliList: { output in
                    guard let data = output.data(using: .utf8),
                          let arr = try? JSONSerialization.jsonObject(with: data) as? [[String: Any]]
                    else { return [] }
                    return arr.compactMap { e -> String? in
                        guard let type = e["type"] as? String,
                              type == "llm" || type == "vlm",
                              let key = e["modelKey"] as? String,
                              !key.trimmingCharacters(in: .whitespaces).isEmpty
                        else { return nil }
                        return key
                    }
                },
                catalogURL: "https://lmstudio.ai/models",
                pullArgs: { name in ["get", name, "--yes"] }
            ),
        ]
    }

    private static func extractOpenAiModelIds(_ json: Any) -> [String] {
        guard let root = json as? [String: Any],
              let arr = root["data"] as? [[String: Any]] else { return [] }
        return arr.compactMap { $0["id"] as? String }
            .filter { !$0.trimmingCharacters(in: .whitespaces).isEmpty }
    }

    /// Probe every provider concurrently and return the union of the
    /// prefixed model ids each reports. Providers that don't answer within
    /// the probe window contribute nothing. With `autoStartInstalled`, a
    /// provider that's installed but idle gets its server started and polled.
    func discover(autoStartInstalled: Bool = false) async -> [String] {
        await withTaskGroup(of: [String].self) { group in
            for p in providers {
                group.addTask { await self.probe(p, autoStart: autoStartInstalled) }
            }
            var seen = Set<String>()
            var union: [String] = []
            for await ids in group {
                for id in ids where seen.insert(id).inserted { union.append(id) }
            }
            return union
        }
    }

    private func probe(_ p: ExternalProvider, autoStart: Bool) async -> [String] {
        var httpModels = await tryList(p, timeout: Self.probeTimeout)
        if httpModels == nil, autoStart, await ensureRunning(p) {
            httpModels = await tryList(p, timeout: 2.0)
        }
        let cliModels = await tryListViaCli(p)
        if httpModels == nil && cliModels == nil { return [] }

        var seen = Set<String>()
        var union: [String] = []
        for id in (httpModels ?? []) + (cliModels ?? []) where seen.insert(id).inserted {
            union.append(id)
        }
        return union
    }

    /// List a provider's models over HTTP, or nil when unreachable.
    /// Returns prefixed ids ("ollama:llama3").
    private func tryList(_ p: ExternalProvider, timeout: TimeInterval) async -> [String]? {
        guard let url = URL(string: p.baseURL + p.listPath) else { return nil }
        var req = URLRequest(url: url)
        req.timeoutInterval = timeout
        do {
            let (data, response) = try await URLSession.shared.data(for: req)
            guard let http = response as? HTTPURLResponse, http.statusCode == 200 else { return nil }
            let json = try JSONSerialization.jsonObject(with: data)
            return p.extractModels(json).map { "\(p.prefix):\($0)" }
        } catch {
            return nil
        }
    }

    /// Enumerate a provider's downloaded models via its CLI, or nil when the
    /// provider has no CLI listing or it fails.
    private func tryListViaCli(_ p: ExternalProvider) async -> [String]? {
        guard let args = p.listCliArgs, let parse = p.parseCliList else { return nil }
        for exe in p.startExeCandidates {
            if let (code, output) = await runProcess(exe: exe, args: args, timeout: 10), code == 0 {
                return parse(output).map { "\(p.prefix):\($0)" }
            }
        }
        return nil
    }

    /// Make sure a provider's server answers — starting it from its CLI when
    /// installed but idle. Returns true when the server responds.
    func ensureRunning(_ p: ExternalProvider) async -> Bool {
        if await tryList(p, timeout: Self.probeTimeout) != nil { return true }
        guard startServer(p) else { return false }
        for _ in 0..<8 {
            try? await Task.sleep(nanoseconds: 1_000_000_000)
            if await tryList(p, timeout: 2.0) != nil { return true }
        }
        return false
    }

    /// True when the provider's CLI exists on this machine.
    func isInstalled(_ p: ExternalProvider) -> Bool {
        for exe in p.startExeCandidates {
            if exe.hasPrefix("/") {
                if FileManager.default.isExecutableFile(atPath: exe) { return true }
                continue
            }
            // Bare name: search PATH.
            let pathVar = ProcessInfo.processInfo.environment["PATH"] ?? ""
            for dir in pathVar.split(separator: ":") {
                let candidate = "\(dir)/\(exe)"
                if FileManager.default.isExecutableFile(atPath: candidate) { return true }
            }
        }
        return false
    }

    /// Route helper — given a model id (possibly prefixed), return the
    /// provider whose prefix matches, or nil for a plain Foundry model.
    func matchProvider(_ modelId: String) -> ExternalProvider? {
        guard let colon = modelId.firstIndex(of: ":"), colon != modelId.startIndex else { return nil }
        let prefix = String(modelId[modelId.startIndex..<colon])
        return providers.first { $0.prefix.caseInsensitiveCompare(prefix) == .orderedSame }
    }

    /// Download a model through the provider's CLI (`ollama pull …`,
    /// `lms get …`), streaming output lines to `onOutput`. Returns true on
    /// exit 0. Rejects names with shell-significant characters.
    func pullModel(_ p: ExternalProvider, modelName: String, onOutput: @escaping (String) -> Void) async -> Bool {
        guard let buildArgs = p.pullArgs else { return false }
        let name = modelName.trimmingCharacters(in: .whitespaces)
        guard !name.isEmpty, !name.contains("\""), !name.contains(" ") else { return false }

        for exe in p.startExeCandidates {
            if let ok = await runProcessStreaming(exe: exe, args: buildArgs(name), onOutput: onOutput) {
                return ok
            }
        }
        return false
    }

    // MARK: - Process helpers

    /// Launch the server detached — it outlives this app so the user's next
    /// session finds it already running.
    private func startServer(_ p: ExternalProvider) -> Bool {
        for exe in p.startExeCandidates {
            let proc = Process()
            if exe.hasPrefix("/") {
                guard FileManager.default.isExecutableFile(atPath: exe) else { continue }
                proc.executableURL = URL(fileURLWithPath: exe)
                proc.arguments = p.startArgs
            } else {
                proc.executableURL = URL(fileURLWithPath: "/usr/bin/env")
                proc.arguments = [exe] + p.startArgs
            }
            proc.environment = Self.augmentedEnv()
            proc.standardOutput = FileHandle.nullDevice
            proc.standardError = FileHandle.nullDevice
            proc.standardInput = FileHandle.nullDevice
            do { try proc.run(); return true } catch { continue }
        }
        return false
    }

    /// Run a CLI to completion, returning (exitCode, combined output), or nil
    /// if the executable couldn't be launched at this candidate.
    private func runProcess(exe: String, args: [String], timeout: TimeInterval) async -> (Int32, String)? {
        await withCheckedContinuation { continuation in
            DispatchQueue.global(qos: .userInitiated).async {
                let proc = Process()
                if exe.hasPrefix("/") {
                    guard FileManager.default.isExecutableFile(atPath: exe) else {
                        continuation.resume(returning: nil); return
                    }
                    proc.executableURL = URL(fileURLWithPath: exe)
                    proc.arguments = args
                } else {
                    proc.executableURL = URL(fileURLWithPath: "/usr/bin/env")
                    proc.arguments = [exe] + args
                }
                proc.environment = Self.augmentedEnv()
                let pipe = Pipe()
                proc.standardOutput = pipe
                proc.standardError = FileHandle.nullDevice
                proc.standardInput = FileHandle.nullDevice

                let lock = NSLock()
                var resumed = false
                func resumeOnce(_ v: (Int32, String)?) {
                    lock.lock(); defer { lock.unlock() }
                    if resumed { return }; resumed = true
                    continuation.resume(returning: v)
                }

                DispatchQueue.global().asyncAfter(deadline: .now() + timeout) {
                    if proc.isRunning { proc.terminate(); resumeOnce(nil) }
                }
                do {
                    try proc.run()
                } catch {
                    resumeOnce(nil); return
                }
                let data = pipe.fileHandleForReading.readDataToEndOfFile()
                proc.waitUntilExit()
                let out = String(data: data, encoding: .utf8) ?? ""
                resumeOnce((proc.terminationStatus, out))
            }
        }
    }

    /// Run a CLI, streaming each output line to `onOutput`. Returns exit==0,
    /// or nil if the executable couldn't be launched at this candidate.
    private func runProcessStreaming(exe: String, args: [String], onOutput: @escaping (String) -> Void) async -> Bool? {
        await withCheckedContinuation { continuation in
            DispatchQueue.global(qos: .userInitiated).async {
                let proc = Process()
                if exe.hasPrefix("/") {
                    guard FileManager.default.isExecutableFile(atPath: exe) else {
                        continuation.resume(returning: nil); return
                    }
                    proc.executableURL = URL(fileURLWithPath: exe)
                    proc.arguments = args
                } else {
                    proc.executableURL = URL(fileURLWithPath: "/usr/bin/env")
                    proc.arguments = [exe] + args
                }
                proc.environment = Self.augmentedEnv()
                let pipe = Pipe()
                proc.standardOutput = pipe
                proc.standardError = pipe
                proc.standardInput = FileHandle.nullDevice

                let handle = pipe.fileHandleForReading
                handle.readabilityHandler = { h in
                    let d = h.availableData
                    guard !d.isEmpty, let s = String(data: d, encoding: .utf8) else { return }
                    for line in s.split(separator: "\n") where !line.trimmingCharacters(in: .whitespaces).isEmpty {
                        onOutput(String(line))
                    }
                }

                do {
                    try proc.run()
                } catch {
                    continuation.resume(returning: nil); return
                }
                proc.waitUntilExit()
                handle.readabilityHandler = nil
                continuation.resume(returning: proc.terminationStatus == 0)
            }
        }
    }

    /// PATH augmented with the common Homebrew locations a GUI-launched app
    /// otherwise misses, so bare `ollama`/`lms` resolve.
    private static func augmentedEnv() -> [String: String] {
        var env = ProcessInfo.processInfo.environment
        let extra = "/opt/homebrew/bin:/usr/local/bin"
        env["PATH"] = env["PATH"].map { "\(extra):\($0)" } ?? extra
        return env
    }
}

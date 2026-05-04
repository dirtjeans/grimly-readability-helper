import Foundation

class FoundryManager {
    private let settingsService: SettingsService

    init(settingsService: SettingsService) {
        self.settingsService = settingsService
    }

    // MARK: - Public API

    func ensureRunning() async -> (success: Bool, endpoint: String?, modelId: String?) {
        guard let endpoint = await ensureServiceRunning() else {
            return (false, nil, nil)
        }

        let settings = settingsService.load()
        guard let modelId = await ensureModelLoaded(endpoint: endpoint, preferredModel: settings.modelName) else {
            return (false, endpoint, nil)
        }

        // Update settings with discovered values
        var updated = settingsService.load()
        var changed = false

        if updated.foundryEndpoint != endpoint { updated.foundryEndpoint = endpoint; changed = true }
        if updated.modelName != modelId { updated.modelName = modelId; changed = true }

        if let maxTokens = await getMaxOutputTokens(modelId: modelId) {
            if updated.maxTokens != maxTokens { updated.maxTokens = maxTokens; changed = true }
        }

        if changed { settingsService.save(updated) }

        return (true, endpoint, modelId)
    }

    func checkServiceStatus() async -> (running: Bool, endpoint: String?) {
        let (exitCode, output) = await runFoundryCommand("service status")
        if exitCode == 0 && output.localizedCaseInsensitiveContains("running") {
            return (true, extractEndpoint(from: output))
        }
        return (false, nil)
    }

    func getAvailableModels() async -> [String] {
        let settings = settingsService.load()
        let endpoint = settings.foundryEndpoint

        guard let url = URL(string: "\(endpoint)/v1/models") else { return [] }

        do {
            var request = URLRequest(url: url)
            request.timeoutInterval = 5
            let (data, _) = try await URLSession.shared.data(for: request)
            let json = try JSONSerialization.jsonObject(with: data) as? [String: Any]
            let dataArray = json?["data"] as? [[String: Any]] ?? []
            return dataArray.compactMap { $0["id"] as? String }
        } catch {
            return []
        }
    }

    func getMaxOutputTokens(modelId: String) async -> Int? {
        let settings = settingsService.load()
        let endpoint = settings.foundryEndpoint

        guard let url = URL(string: "\(endpoint)/v1/models") else { return nil }

        do {
            var request = URLRequest(url: url)
            request.timeoutInterval = 5
            let (data, _) = try await URLSession.shared.data(for: request)
            let json = try JSONSerialization.jsonObject(with: data) as? [String: Any]
            let dataArray = json?["data"] as? [[String: Any]] ?? []

            for model in dataArray {
                if let id = model["id"] as? String, id == modelId,
                   let maxTokens = model["maxOutputTokens"] as? Int {
                    return maxTokens
                }
            }
        } catch {}

        return nil
    }

    func healthCheck() async -> Bool {
        // Re-discover endpoint in case port changed
        let (running, liveEndpoint) = await checkServiceStatus()
        guard running, let endpoint = liveEndpoint else { return false }

        // Update settings if endpoint changed
        var settings = settingsService.load()
        if settings.foundryEndpoint != endpoint {
            settings.foundryEndpoint = endpoint
            settingsService.save(settings)
        }

        // Send a tiny test prompt to verify the model responds
        guard let url = URL(string: "\(endpoint)/v1/chat/completions") else { return false }

        let body: [String: Any] = [
            "model": settings.modelName,
            "messages": [["role": "user", "content": "hi"]],
            "max_tokens": 1,
            "temperature": 0.0
        ]

        do {
            var request = URLRequest(url: url)
            request.httpMethod = "POST"
            request.setValue("application/json", forHTTPHeaderField: "Content-Type")
            request.httpBody = try JSONSerialization.data(withJSONObject: body)
            request.timeoutInterval = 15

            let (_, response) = try await URLSession.shared.data(for: request)
            if let httpResponse = response as? HTTPURLResponse {
                return httpResponse.statusCode == 200
            }
        } catch {}

        return false
    }

    func checkConnection() async -> ConnectionStatus {
        if !isFoundryInstalled() {
            return .foundryNotInstalled
        }

        let settings = settingsService.load()
        let endpoint = settings.foundryEndpoint

        guard let url = URL(string: "\(endpoint)/v1/models") else {
            return .foundryNotRunning
        }

        do {
            var request = URLRequest(url: url)
            request.timeoutInterval = 3
            let (data, _) = try await URLSession.shared.data(for: request)
            let json = try JSONSerialization.jsonObject(with: data) as? [String: Any]
            let dataArray = json?["data"] as? [[String: Any]] ?? []

            let targetModel = settings.modelName
            var anyModels = false
            for model in dataArray {
                anyModels = true
                if let id = model["id"] as? String, id == targetModel {
                    return .connected
                }
            }
            // Endpoint reachable but configured model not found — still usable
            return anyModels ? .connected : .modelNotLoaded
        } catch {
            return .foundryNotRunning
        }
    }

    // MARK: - Private

    private func ensureServiceRunning() async -> String? {
        let (exitCode, output) = await runFoundryCommand("service status")

        if exitCode == 0 && output.localizedCaseInsensitiveContains("running") {
            return extractEndpoint(from: output)
        }

        // Start the service
        let (startExit, _) = await runFoundryCommand("service start")
        if startExit != 0 { return nil }

        // Wait for it to be ready
        for _ in 0..<15 {
            try? await Task.sleep(nanoseconds: 1_000_000_000)
            let (checkExit, checkOutput) = await runFoundryCommand("service status")
            if checkExit == 0 && checkOutput.localizedCaseInsensitiveContains("running") {
                return extractEndpoint(from: checkOutput)
            }
        }

        return nil
    }

    private func ensureModelLoaded(endpoint: String, preferredModel: String) async -> String? {
        guard let url = URL(string: "\(endpoint)/v1/models") else { return nil }

        do {
            var request = URLRequest(url: url)
            request.timeoutInterval = 5
            let (data, _) = try await URLSession.shared.data(for: request)
            let json = try JSONSerialization.jsonObject(with: data) as? [String: Any]
            let dataArray = json?["data"] as? [[String: Any]] ?? []

            // First check for exact match with saved model name
            var bestModel: String?
            for model in dataArray {
                guard let id = model["id"] as? String else { continue }
                if !preferredModel.isEmpty && id == preferredModel { return id }
                // Also try contains-based match (e.g. "qwen2.5-7b" matches "qwen2.5-7b-instruct-generic-cpu:4")
                if !preferredModel.isEmpty && id.localizedCaseInsensitiveContains(preferredModel) {
                    bestModel = bestModel ?? id
                }
                if bestModel == nil { bestModel = id }
            }

            if let bestModel { return bestModel }
        } catch {}

        // No models loaded — download and load qwen2.5-7b
        // Use "model download" + "model load" instead of "model run"
        // ("model run" starts interactive mode which hangs forever)
        let (dlExit, _) = await runFoundryCommand("model download qwen2.5-7b")
        if dlExit != 0 { return nil }

        let (loadExit, _) = await runFoundryCommand("model load qwen2.5-7b")
        if loadExit != 0 {
            // model load may return non-zero but model might still be usable
        }

        // Wait for model to appear
        for _ in 0..<30 {
            try? await Task.sleep(nanoseconds: 2_000_000_000)
            do {
                var request = URLRequest(url: url)
                request.timeoutInterval = 5
                let (data, _) = try await URLSession.shared.data(for: request)
                let json = try JSONSerialization.jsonObject(with: data) as? [String: Any]
                let dataArray = json?["data"] as? [[String: Any]] ?? []

                for model in dataArray {
                    if let id = model["id"] as? String,
                       id.localizedCaseInsensitiveContains("qwen2.5-7b") {
                        return id
                    }
                }
            } catch {}
        }

        return nil
    }

    private func extractEndpoint(from output: String) -> String {
        let patterns = ["http://", "https://"]
        for pattern in patterns {
            if let range = output.range(of: pattern, options: .caseInsensitive) {
                let rest = output[range.lowerBound...]
                let endChars = CharacterSet(charactersIn: " \n\r/")
                let urlPart: String
                if let endIdx = rest.dropFirst(8).unicodeScalars.firstIndex(where: { endChars.contains($0) }) {
                    urlPart = String(rest[rest.startIndex..<endIdx]).trimmingCharacters(in: CharacterSet(charactersIn: "/"))
                } else {
                    urlPart = String(rest).trimmingCharacters(in: CharacterSet(charactersIn: "/"))
                }

                if let url = URL(string: urlPart), let scheme = url.scheme, let host = url.host {
                    let port = url.port.map { ":\($0)" } ?? ""
                    return "\(scheme)://\(host)\(port)"
                }

                return urlPart
            }
        }

        return "http://127.0.0.1:51318"
    }

    private func findFoundryBinary() -> String {
        let candidates = [
            "/opt/homebrew/bin/foundry",     // Apple Silicon Homebrew
            "/usr/local/bin/foundry",         // Intel Homebrew
        ]

        for path in candidates {
            if FileManager.default.isExecutableFile(atPath: path) {
                return path
            }
        }

        return "foundry"
    }

    func isFoundryInstalled() -> Bool {
        let candidates = [
            "/opt/homebrew/bin/foundry",
            "/usr/local/bin/foundry",
        ]
        return candidates.contains { FileManager.default.isExecutableFile(atPath: $0) }
    }

    func isHomebrewInstalled() -> Bool {
        let candidates = [
            "/opt/homebrew/bin/brew",
            "/usr/local/bin/brew",
        ]
        return candidates.contains { FileManager.default.isExecutableFile(atPath: $0) }
    }

    private func runFoundryCommand(_ args: String, timeout: TimeInterval = 300) async -> (exitCode: Int32, output: String) {
        let foundryPath = self.findFoundryBinary()
        return await withCheckedContinuation { continuation in
            DispatchQueue.global(qos: .userInitiated).async {
                var hasResumed = false
                let lock = NSLock()

                func resumeOnce(_ result: (Int32, String)) {
                    lock.lock()
                    defer { lock.unlock() }
                    guard !hasResumed else { return }
                    hasResumed = true
                    continuation.resume(returning: result)
                }

                do {
                    let process = Process()
                    let pipe = Pipe()

                    if foundryPath == "foundry" {
                        process.executableURL = URL(fileURLWithPath: "/usr/bin/env")
                        process.arguments = ["foundry"] + args.split(separator: " ").map(String.init)
                    } else {
                        process.executableURL = URL(fileURLWithPath: foundryPath)
                        process.arguments = args.split(separator: " ").map(String.init)
                    }

                    // Close stdin so interactive commands get EOF and exit
                    process.standardInput = FileHandle.nullDevice

                    var env = ProcessInfo.processInfo.environment
                    let extraPaths = "/opt/homebrew/bin:/usr/local/bin"
                    if let existingPath = env["PATH"] {
                        env["PATH"] = "\(extraPaths):\(existingPath)"
                    } else {
                        env["PATH"] = extraPaths
                    }
                    process.environment = env

                    process.standardOutput = pipe
                    process.standardError = pipe

                    // Timeout watchdog
                    DispatchQueue.global().asyncAfter(deadline: .now() + timeout) {
                        if process.isRunning {
                            process.terminate()
                            resumeOnce((-1, "Command timed out after \(Int(timeout))s"))
                        }
                    }

                    try process.run()
                    process.waitUntilExit()

                    let data = pipe.fileHandleForReading.readDataToEndOfFile()
                    let output = String(data: data, encoding: .utf8) ?? ""
                    resumeOnce((process.terminationStatus, output))
                } catch {
                    resumeOnce((-1, "Error: \(error.localizedDescription)"))
                }
            }
        }
    }
}

import Foundation

class FoundryLocalClient {
    private let settingsService: SettingsService
    /// Optional — used only for retry-with-endpoint-refresh. Kept optional
    /// so a smaller test harness can construct a client without a manager.
    private let foundryManager: FoundryManager?

    init(settingsService: SettingsService, foundryManager: FoundryManager? = nil) {
        self.settingsService = settingsService
        self.foundryManager = foundryManager
    }

    /// Prepended to every system prompt. Qwen (and to a lesser extent Phi)
    /// otherwise drifts to Chinese for anything longer than a paragraph,
    /// regardless of the input language. Anchors the reply to the input's
    /// language and calls out English explicitly for the common case.
    private static let languageAnchor =
        "Reply in the same language as the input text. If the input is in English, respond in English.\n\n"

    func getEditedText(
        originalText: String,
        mode: EditingMode,
        customPrompt: String? = nil,
        temperature: Double? = nil
    ) async throws -> String {
        let modePrompt = (mode == .customPrompt && customPrompt != nil)
            ? customPrompt!
            : mode.systemPrompt
        let systemPrompt = Self.languageAnchor + modePrompt

        // First attempt with the endpoint we have in settings.
        do {
            return try await attempt(
                systemPrompt: systemPrompt,
                originalText: originalText,
                mode: mode,
                explicitTemperature: temperature
            )
        } catch is CancellationError {
            throw CancellationError()
        } catch let urlErr as URLError {
            // Connection- or transport-layer problems only. Anything else
            // (400s, malformed JSON) rethrows immediately — retrying won't
            // help.
            let transient: Set<URLError.Code> = [
                .cannotConnectToHost, .networkConnectionLost, .notConnectedToInternet,
                .timedOut, .badServerResponse, .cannotFindHost, .dnsLookupFailed,
                .resourceUnavailable, .cannotLoadFromNetwork,
            ]
            guard transient.contains(urlErr.code) else { throw urlErr }

            // Ask the manager to re-discover Foundry's current endpoint —
            // it may have moved to a new port on a silent restart. This
            // updates settings if the endpoint changed. If we don't have a
            // manager (test harness), skip the refresh and just retry.
            if let mgr = foundryManager {
                let (running, liveEndpoint) = await mgr.checkServiceStatus()
                if running, let liveEndpoint {
                    var s = settingsService.load()
                    if s.foundryEndpoint != liveEndpoint {
                        s.foundryEndpoint = liveEndpoint
                        settingsService.save(s)
                    }
                }
            }

            // Retry once. If this also fails, the error surfaces to the caller.
            return try await attempt(
                systemPrompt: systemPrompt,
                originalText: originalText,
                mode: mode,
                explicitTemperature: temperature
            )
        }
    }

    /// Single request attempt. Reads the current endpoint from settings at
    /// call time so a retry after an endpoint refresh picks up the new port.
    private func attempt(
        systemPrompt: String,
        originalText: String,
        mode: EditingMode,
        explicitTemperature: Double?
    ) async throws -> String {
        let settings = settingsService.load()

        let finalTemp: Double
        if let temp = explicitTemperature {
            finalTemp = temp
        } else {
            let baseTemp = mode.baseTemperature
            let offset = (settings.creativity - 0.5) * 0.4
            finalTemp = min(max(baseTemp + offset, 0.0), 1.0)
        }

        let request = ChatCompletionRequest(
            model: settings.modelName,
            messages: [
                .system(systemPrompt),
                .user(originalText)
            ],
            temperature: finalTemp,
            maxTokens: settings.maxTokens
        )

        let endpoint = settings.foundryEndpoint.trimmingCharacters(in: CharacterSet(charactersIn: "/"))
        guard let url = URL(string: "\(endpoint)/v1/chat/completions") else {
            throw URLError(.badURL)
        }

        var urlRequest = URLRequest(url: url)
        urlRequest.httpMethod = "POST"
        urlRequest.setValue("application/json", forHTTPHeaderField: "Content-Type")
        urlRequest.httpBody = try JSONEncoder().encode(request)

        let (data, response) = try await URLSession.shared.data(for: urlRequest)

        if let httpResponse = response as? HTTPURLResponse, httpResponse.statusCode != 200 {
            throw URLError(.badServerResponse)
        }

        let result = try JSONDecoder().decode(ChatCompletionResponse.self, from: data)
        return result.choices.first?.message.content.trimmingCharacters(in: .whitespacesAndNewlines) ?? originalText
    }
}

import Foundation

class FoundryLocalClient {
    private let settingsService: SettingsService
    /// Optional — used only for retry-with-endpoint-refresh. Kept optional
    /// so a smaller test harness can construct a client without a manager.
    private let foundryManager: FoundryManager?
    /// Optional external providers (Ollama, LM Studio). When the selected
    /// model carries a provider prefix ("ollama:llama3"), requests route to
    /// that provider's OpenAI-compatible endpoint instead of Foundry.
    private let externalProviders: ExternalLlmProviderService?

    init(settingsService: SettingsService,
         foundryManager: FoundryManager? = nil,
         externalProviders: ExternalLlmProviderService? = nil) {
        self.settingsService = settingsService
        self.foundryManager = foundryManager
        self.externalProviders = externalProviders
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

            // Endpoint refresh only makes sense for Foundry (external
            // providers have fixed localhost ports). Skip it when the
            // selected model is a provider model.
            if externalProviders?.matchProvider(settingsService.load().modelName) == nil,
               let mgr = foundryManager {
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

        // Route: a provider-prefixed model ("ollama:llama3") goes to that
        // provider's base URL with the prefix stripped off the model id;
        // a plain model id goes to Foundry's endpoint from settings.
        var modelId = settings.modelName
        var endpoint = settings.foundryEndpoint.trimmingCharacters(in: CharacterSet(charactersIn: "/"))
        var chatPath = "/v1/chat/completions"
        if let provider = externalProviders?.matchProvider(modelId) {
            if let colon = modelId.firstIndex(of: ":") {
                modelId = String(modelId[modelId.index(after: colon)...])
            }
            endpoint = provider.baseURL.trimmingCharacters(in: CharacterSet(charactersIn: "/"))
            chatPath = provider.chatEndpoint
        }

        let request = ChatCompletionRequest(
            model: modelId,
            messages: [
                .system(systemPrompt),
                .user(originalText)
            ],
            temperature: finalTemp,
            maxTokens: settings.maxTokens
        )

        guard let url = URL(string: "\(endpoint)\(chatPath)") else {
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
        let reply = result.choices.first?.message.content.trimmingCharacters(in: .whitespacesAndNewlines) ?? ""
        guard !reply.isEmpty else { return originalText }

        // Response hygiene. Small on-device models (phi-3.5, qwen) leak
        // instructions into results four ways; scrub/reject each. A rejected
        // response returns the original text, which the popup reports as
        // "No changes suggested."
        let stripped = ResponseHygiene.stripMetaPreamble(reply)
        guard !stripped.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else { return originalText }
        if ResponseHygiene.looksLikePromptEcho(stripped, systemPrompt: systemPrompt) { return originalText }
        if ResponseHygiene.looksLikeInstructionParaphrase(stripped, systemPrompt: systemPrompt, originalText: originalText) { return originalText }
        if ResponseHygiene.looksLikeModelVerdict(stripped, originalText: originalText) { return originalText }
        return stripped
    }
}

/// Guards against small on-device models leaking their instructions into the
/// output. Ported 1:1 from the Windows `FoundryLocalClient` hygiene methods
/// (StripMetaPreamble / LooksLikePromptEcho / LooksLikeInstructionParaphrase
/// / LooksLikeModelVerdict). Kept as a standalone enum so the StyleHelper
/// client can reuse the exact same logic.
enum ResponseHygiene {

    // Words of 4+ letters — short function words (the/is/and) appear in
    // everything and would wash out the provenance signal.
    private static let wordRegex = try! NSRegularExpression(pattern: "[a-z']{4,}")

    /// Strips announcer preambles the model wraps around an otherwise-good
    /// rewrite — "The revised text is: :", "Here's the rewritten version:",
    /// "Sure, here is the edited text -". KEEPS the rewrite that follows.
    static func stripMetaPreamble(_ response: String) -> String {
        let trimmed = String(response.drop(while: { $0 == " " || $0 == "\t" || $0 == "\n" || $0 == "\r" }))
        let ns = trimmed as NSString
        let full = NSRange(location: 0, length: ns.length)
        var s = trimmed
        if let m = metaPreambleRegex.firstMatch(in: trimmed, range: full), m.range.location == 0 {
            s = ns.replacingCharacters(in: m.range, with: "")
        }
        // Mop up stragglers the model sometimes doubles after the preamble
        // (the "…is: :" case) plus any opening quote it added.
        return String(s.drop(while: { ":-–  \r\n\"\u{201C}".contains($0) }))
    }

    // The literal “ (U+201C) is embedded directly: in a Swift raw string
    // `\u{201C}` would NOT be interpreted, and ICU regex doesn't accept the
    // `\u{...}` escape form either.
    private static let metaPreambleRegex = try! NSRegularExpression(
        pattern: #"^["“']?\s*(?:sure[,.!]?\s*)?(?:here(?:'s|\s+is)\s+)?(?:the\s+|your\s+|a\s+)?(?:revised|rewritten|edited|updated|corrected|improved|conversational)\s+(?:text|version)(?:\s+is)?\s*[:\-–]+\s*"#,
        options: [.caseInsensitive])

    /// True when any substantive (20+ char) line of the system prompt shows
    /// up verbatim in the response — instruction text never legitimately
    /// belongs in edited user text.
    static func looksLikePromptEcho(_ response: String, systemPrompt: String) -> Bool {
        let lowerResponse = response.lowercased()
        for rawLine in systemPrompt.split(separator: "\n", omittingEmptySubsequences: false) {
            let line = rawLine.trimmingCharacters(in: .whitespaces)
            if line.count < 20 { continue }
            if lowerResponse.contains(line.lowercased()) { return true }
        }
        return false
    }

    /// Detects instruction paraphrase via vocabulary provenance: a genuine
    /// rewrite is assembled mostly from the user's words; a paraphrased
    /// rulebook is assembled from the prompt's words.
    static func looksLikeInstructionParaphrase(_ response: String, systemPrompt: String, originalText: String) -> Bool {
        let respTokens = words(in: response)
        if respTokens.count < 12 { return false }  // verdict detector owns tiny responses

        let promptSet = Set(words(in: systemPrompt))
        let textSet = Set(words(in: originalText))

        let promptHits = respTokens.filter { promptSet.contains($0) }.count
        let textHits = respTokens.filter { textSet.contains($0) }.count
        let promptFrac = Double(promptHits) / Double(respTokens.count)
        let textFrac = Double(textHits) / Double(respTokens.count)

        // Require both a strong absolute prompt share and a clear margin over
        // the text share so heavy rewrites (which add new wording) stay safe.
        return promptFrac > 0.55 && promptFrac > textFrac * 1.5
    }

    /// Detects verdict responses — the model narrating a judgment ("The text
    /// is now grammatically correct…") instead of returning the text. Both
    /// signals required: structural collapse + editing vocabulary the user's
    /// text doesn't use.
    static func looksLikeModelVerdict(_ response: String, originalText: String) -> Bool {
        let collapsed =
            (originalText.count >= 240 && response.count < originalText.count / 3)
            || response.count <= 160
        if !collapsed { return false }

        let lowerResponse = response.lowercased()
        let lowerOriginal = originalText.lowercased()
        for term in verdictVocabulary {
            if lowerResponse.contains(term) && !lowerOriginal.contains(term) { return true }
        }
        return false
    }

    /// Editing-meta vocabulary marking a response as being ABOUT the text
    /// rather than the text. Stored lowercase for case-insensitive contains.
    private static let verdictVocabulary: [String] = [
        "grammatical", "grammatically", "spelling error", "punctuation error",
        "no corrections", "no changes", "no edits", "no revisions",
        "revisions are complete", "revision is complete", "edits are complete",
        "already well-written", "already correct", "well-written and",
        "free of errors", "error-free", "the text is", "your text is",
        "text has been", "nothing to correct", "does not require any",
        "doesn't require any",
    ]

    private static func words(in text: String) -> [String] {
        let lower = text.lowercased()
        let ns = lower as NSString
        let matches = wordRegex.matches(in: lower, range: NSRange(location: 0, length: ns.length))
        return matches.map { ns.substring(with: $0.range) }
    }
}

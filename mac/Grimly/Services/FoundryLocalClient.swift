import Foundation

class FoundryLocalClient {
    private let settingsService: SettingsService

    init(settingsService: SettingsService) {
        self.settingsService = settingsService
    }

    func getEditedText(
        originalText: String,
        mode: EditingMode,
        customPrompt: String? = nil,
        temperature: Double? = nil
    ) async throws -> String {
        let settings = settingsService.load()

        let systemPrompt = (mode == .customPrompt && customPrompt != nil)
            ? customPrompt!
            : mode.systemPrompt

        let finalTemp: Double
        if let temp = temperature {
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

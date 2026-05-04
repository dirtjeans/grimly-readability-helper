import SwiftUI

@MainActor
class SettingsViewModel: ObservableObject {
    private let settingsService: SettingsService
    private let foundryManager: FoundryManager

    @Published var hotkeyModifiers: String = "Cmd+Option"
    @Published var hotkeyKey: String = "G"
    @Published var foundryEndpoint: String = "http://127.0.0.1:51318"
    @Published var modelName: String = "qwen2.5-7b"
    @Published var defaultMode: EditingMode = .fixGrammar
    @Published var creativity: Double = 0.5
    @Published var maxTokens: Int = 2048
    @Published var popupOpacity: Double = 0.95
    @Published var showFloatingIcon: Bool = false
    @Published var isLoadingModels: Bool = false
    @Published var foundryStatus: String = "Checking..."
    @Published var maxTokensInfo: String = ""
    @Published var availableModels: [String] = []

    var onRequestClose: ((Bool) -> Void)?

    var creativityLabel: String {
        if creativity < 0.3 { return "(precise)" }
        if creativity > 0.7 { return "(varied)" }
        return "(balanced)"
    }

    init(settingsService: SettingsService, foundryManager: FoundryManager) {
        self.settingsService = settingsService
        self.foundryManager = foundryManager
        loadFromSettings()

        if !modelName.isEmpty {
            availableModels.append(modelName)
        }

        Task { await loadModels() }
    }

    private func loadFromSettings() {
        let s = settingsService.load()
        hotkeyModifiers = s.hotkeyModifiers
        hotkeyKey = s.hotkeyKey
        foundryEndpoint = s.foundryEndpoint
        modelName = s.modelName
        defaultMode = s.defaultMode
        creativity = s.creativity
        maxTokens = s.maxTokens
        popupOpacity = s.popupOpacity
        showFloatingIcon = s.showFloatingIcon
    }

    func loadModels() async {
        isLoadingModels = true
        foundryStatus = "Checking Foundry Local..."

        let (running, endpoint) = await foundryManager.checkServiceStatus()

        if !running {
            foundryStatus = "Not running"
            isLoadingModels = false
            return
        }

        if let endpoint, foundryEndpoint != endpoint {
            foundryEndpoint = endpoint
        }

        foundryStatus = "Connected"

        var models = await foundryManager.getAvailableModels()
        let savedModel = modelName

        if !savedModel.isEmpty && !models.contains(savedModel) {
            models.insert(savedModel, at: 0)
        }

        availableModels = models
        modelName = savedModel

        // Fetch max tokens for current model
        if let maxTokens = await foundryManager.getMaxOutputTokens(modelId: savedModel) {
            self.maxTokens = maxTokens
            maxTokensInfo = "(model max: \(maxTokens))"
        }

        isLoadingModels = false
    }

    func refreshModels() {
        Task { await loadModels() }
    }

    func save() {
        let s = AppSettings(
            hotkeyModifiers: hotkeyModifiers,
            hotkeyKey: hotkeyKey,
            foundryEndpoint: foundryEndpoint,
            modelName: modelName,
            defaultMode: defaultMode,
            creativity: creativity,
            maxTokens: maxTokens,
            popupOpacity: popupOpacity,
            showFloatingIcon: showFloatingIcon
        )
        settingsService.save(s)
        onRequestClose?(true)
    }

    func cancel() {
        onRequestClose?(false)
    }

    func resetDefaults() {
        let defaults = AppSettings()
        hotkeyModifiers = defaults.hotkeyModifiers
        hotkeyKey = defaults.hotkeyKey
        foundryEndpoint = defaults.foundryEndpoint
        modelName = defaults.modelName
        defaultMode = defaults.defaultMode
        creativity = defaults.creativity
        maxTokens = defaults.maxTokens
        popupOpacity = defaults.popupOpacity
        showFloatingIcon = defaults.showFloatingIcon
    }
}

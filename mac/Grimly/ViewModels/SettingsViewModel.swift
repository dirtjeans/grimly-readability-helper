import SwiftUI

@MainActor
class SettingsViewModel: ObservableObject {
    private let settingsService: SettingsService
    private let foundryManager: FoundryManager
    private let externalProviders: ExternalLlmProviderService?

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

    // Pull-by-name (v1.3.2). No provider exposes an enumerable remote
    // catalog API, so the user browses the provider's site and pulls a
    // model by name here. `pullProviderPrefix` selects which installed
    // provider to pull from.
    @Published var installedProviderPrefixes: [String] = []
    @Published var pullProviderPrefix: String = ""
    @Published var pullModelName: String = ""
    @Published var isPulling: Bool = false
    @Published var pullStatus: String = ""

    var onRequestClose: ((Bool) -> Void)?

    /// Hint shown under the model list. Adapts to what's actually installed.
    var providerHint: String {
        "Ollama and LM Studio models appear here automatically if their app is running."
    }

    /// Catalog URL for the currently-selected pull provider (browse link).
    var pullCatalogURL: URL? {
        guard let p = externalProviders?.providers.first(where: { $0.prefix == pullProviderPrefix }),
              let s = p.catalogURL else { return nil }
        return URL(string: s)
    }

    var creativityLabel: String {
        if creativity < 0.3 { return "(precise)" }
        if creativity > 0.7 { return "(varied)" }
        return "(balanced)"
    }

    init(settingsService: SettingsService, foundryManager: FoundryManager, externalProviders: ExternalLlmProviderService? = nil) {
        self.settingsService = settingsService
        self.foundryManager = foundryManager
        self.externalProviders = externalProviders
        loadFromSettings()

        // Which providers are installed (drives the pull-from dropdown).
        if let ext = externalProviders {
            installedProviderPrefixes = ext.providers.filter { ext.isInstalled($0) }.map { $0.prefix }
            pullProviderPrefix = installedProviderPrefixes.first ?? ""
        }

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

        let savedModel = modelName
        var models: [String] = []

        let (running, endpoint) = await foundryManager.checkServiceStatus()
        if running {
            if let endpoint, foundryEndpoint != endpoint { foundryEndpoint = endpoint }
            foundryStatus = "Connected"
            models = await foundryManager.getAvailableModels()
        } else {
            foundryStatus = "Not running"
        }

        // Merge in external-provider models (Ollama, LM Studio). autoStart so
        // installed-but-idle providers still surface their downloaded models.
        if let ext = externalProviders {
            let providerModels = await ext.discover(autoStartInstalled: true)
            for m in providerModels where !models.contains(m) { models.append(m) }
        }

        // Keep the saved selection visible even if nothing enumerated it.
        if !savedModel.isEmpty && !models.contains(savedModel) {
            models.insert(savedModel, at: 0)
        }

        availableModels = models
        modelName = savedModel

        // Max tokens only applies to Foundry models.
        if running, externalProviders?.matchProvider(savedModel) == nil,
           let maxTokens = await foundryManager.getMaxOutputTokens(modelId: savedModel) {
            self.maxTokens = maxTokens
            maxTokensInfo = "(model max: \(maxTokens))"
        }

        isLoadingModels = false
    }

    func refreshModels() {
        Task { await loadModels() }
    }

    /// Pull a model by name through the selected provider's CLI, streaming
    /// progress into `pullStatus`. On success, refresh the model list so the
    /// new model appears.
    func pullModel() {
        guard let ext = externalProviders,
              let provider = ext.providers.first(where: { $0.prefix == pullProviderPrefix })
        else { return }
        let name = pullModelName.trimmingCharacters(in: .whitespaces)
        guard !name.isEmpty else { return }
        guard !name.contains(" "), !name.contains("\"") else {
            pullStatus = "Invalid model name."
            return
        }

        isPulling = true
        pullStatus = "Pulling \(name)…"
        Task {
            let ok = await ext.pullModel(provider, modelName: name) { [weak self] line in
                Task { @MainActor in self?.pullStatus = line }
            }
            await MainActor.run {
                self.isPulling = false
                self.pullStatus = ok ? "Pulled \(name)." : "Pull failed."
                if ok {
                    self.pullModelName = ""
                    self.refreshModels()
                }
            }
        }
    }

    func save() {
        // Load-mutate-save, not construct-fresh. Building a new AppSettings
        // from only the dialog's fields silently resets any field the dialog
        // doesn't edit (the Windows save-preservation bug). Mutating the
        // loaded object preserves everything else.
        var s = settingsService.load()
        s.hotkeyModifiers = hotkeyModifiers
        s.hotkeyKey = hotkeyKey
        s.foundryEndpoint = foundryEndpoint
        s.modelName = modelName
        s.defaultMode = defaultMode
        s.creativity = creativity
        s.maxTokens = maxTokens
        s.popupOpacity = popupOpacity
        s.showFloatingIcon = showFloatingIcon
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

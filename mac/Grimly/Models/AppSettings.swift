import Foundation

struct AppSettings: Codable {
    var hotkeyModifiers: String = "Cmd+Option"
    var hotkeyKey: String = "G"
    var foundryEndpoint: String = "http://127.0.0.1:51318"
    var modelName: String = "qwen2.5-7b"
    var defaultMode: EditingMode = .fixGrammar
    var creativity: Double = 0.5
    var maxTokens: Int = 2048
    var popupOpacity: Double = 0.95
    var showFloatingIcon: Bool = true

    enum CodingKeys: String, CodingKey {
        case hotkeyModifiers = "hotkey_modifiers"
        case hotkeyKey = "hotkey_key"
        case foundryEndpoint = "foundry_endpoint"
        case modelName = "model_name"
        case defaultMode = "default_mode"
        case creativity
        case maxTokens = "max_tokens"
        case popupOpacity = "popup_opacity"
        case showFloatingIcon = "show_floating_icon"
    }
}

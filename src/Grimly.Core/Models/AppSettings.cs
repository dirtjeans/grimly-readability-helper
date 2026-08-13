using System.Text.Json.Serialization;

namespace Grimly.Models;

public sealed class AppSettings
{
    [JsonPropertyName("hotkey_modifiers")]
    public string HotkeyModifiers { get; set; } = "Ctrl+Alt";

    [JsonPropertyName("hotkey_key")]
    public string HotkeyKey { get; set; } = "G";

    [JsonPropertyName("foundry_endpoint")]
    public string FoundryEndpoint { get; set; } = "http://127.0.0.1:51318";

    /// <summary>
    /// Factory default Foundry model, as a named constant so startup logic
    /// can distinguish "user never picked a model" from a deliberate choice.
    /// </summary>
    public const string FactoryDefaultModel = "qwen2.5-7b-instruct-qnn-npu:2";

    [JsonPropertyName("model_name")]
    public string ModelName { get; set; } = FactoryDefaultModel;

    [JsonPropertyName("default_mode")]
    public EditingMode DefaultMode { get; set; } = EditingMode.FixGrammar;

    [JsonPropertyName("creativity")]
    public double Creativity { get; set; } = 0.5; // 0=precise, 0.5=default, 1=varied

    [JsonPropertyName("max_tokens")]
    public int MaxTokens { get; set; } = 2048;

    [JsonPropertyName("popup_opacity")]
    public double PopupOpacity { get; set; } = 0.95;

    [JsonPropertyName("show_floating_icon")]
    public bool ShowFloatingIcon { get; set; } = true;

    /// <summary>
    /// True once the app has auto-selected the Windows AI (Aion) model on a
    /// compatible machine. The flip happens at most once — after that the
    /// user's picker choice (including switching back to a Foundry model)
    /// is always respected.
    /// </summary>
    [JsonPropertyName("windows_ai_defaulted")]
    public bool WindowsAiDefaulted { get; set; }
}

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

    [JsonPropertyName("model_name")]
    public string ModelName { get; set; } = "qwen2.5-7b-instruct-qnn-npu:2";

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
}

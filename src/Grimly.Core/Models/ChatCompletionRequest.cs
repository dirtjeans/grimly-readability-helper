using System.Text.Json.Serialization;

namespace Grimly.Models;

public sealed class ChatCompletionRequest
{
    [JsonPropertyName("model")]
    // Never actually sent — FoundryLocalClient overwrites this with the
    // model from settings before dispatching. Aligned with the AppSettings
    // default so a reader glancing at the DTO isn't misled.
    public string Model { get; set; } = "qwen2.5-7b-instruct-qnn-npu:2";

    [JsonPropertyName("messages")]
    public List<ChatMessage> Messages { get; set; } = [];

    [JsonPropertyName("temperature")]
    public double Temperature { get; set; } = 0.3;

    [JsonPropertyName("max_tokens")]
    public int MaxTokens { get; set; } = 2048;

    [JsonPropertyName("stream")]
    public bool Stream { get; set; } = false;
}

public sealed class ChatMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = "";

    [JsonPropertyName("content")]
    public string Content { get; set; } = "";

    public static ChatMessage System(string content) => new() { Role = "system", Content = content };
    public static ChatMessage User(string content) => new() { Role = "user", Content = content };
}

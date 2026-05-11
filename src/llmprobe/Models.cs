using System.Text.Json.Serialization;

namespace LlmProbe;

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(PingResult))]
[JsonSerializable(typeof(ModelList))]
[JsonSerializable(typeof(TestResult))]
[JsonSerializable(typeof(StreamResult))]
[JsonSerializable(typeof(CapabilitiesResult))]
[JsonSerializable(typeof(ErrorResult))]
[JsonSerializable(typeof(OpenAiChatRequest))]
[JsonSerializable(typeof(OpenAiChatResponse))]
[JsonSerializable(typeof(OpenAiModelsResponse))]
[JsonSerializable(typeof(OpenAiStreamChunk))]
public partial class JsonContext : JsonSerializerContext { }

public record PingResult(
    string Endpoint,
    bool Reachable,
    int? StatusCode,
    long LatencyMs,
    string? ServerHeader,
    string? Error);

public record ModelList(
    string Endpoint,
    int Count,
    string[] Models,
    long LatencyMs);

public record TestResult(
    string Endpoint,
    string Model,
    bool Ok,
    int? StatusCode,
    long LatencyMs,
    int PromptTokens,
    int CompletionTokens,
    int TotalTokens,
    string? FinishReason,
    string? ResponsePreview,
    string? Error);

public record StreamResult(
    string Endpoint,
    string Model,
    bool Ok,
    long TtftMs,
    long TotalMs,
    int Chunks,
    int OutputTokensApprox,
    double TokensPerSec,
    string? FinishReason,
    string? Error);

public record CapabilitiesResult(
    string Endpoint,
    bool Streaming,
    bool ToolCalls,
    bool Vision,
    bool JsonMode,
    bool LogProbs,
    string? ApiCompatibility,
    string? ServerSoftware,
    string[] AvailableModels,
    string? AuthNote = null);

public record ErrorResult(string Error, string? Hint);

public record OpenAiMessage(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] string Content);

public record OpenAiChatRequest(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("messages")] OpenAiMessage[] Messages,
    [property: JsonPropertyName("max_tokens")] int? MaxTokens = null,
    [property: JsonPropertyName("temperature")] double? Temperature = null,
    [property: JsonPropertyName("stream")] bool? Stream = null);

public record OpenAiChoice(
    [property: JsonPropertyName("message")] OpenAiMessage? Message,
    [property: JsonPropertyName("finish_reason")] string? FinishReason);

public record OpenAiUsage(
    [property: JsonPropertyName("prompt_tokens")] int PromptTokens,
    [property: JsonPropertyName("completion_tokens")] int CompletionTokens,
    [property: JsonPropertyName("total_tokens")] int TotalTokens);

public record OpenAiChatResponse(
    [property: JsonPropertyName("choices")] OpenAiChoice[] Choices,
    [property: JsonPropertyName("usage")] OpenAiUsage? Usage);

public record OpenAiModelEntry(
    [property: JsonPropertyName("id")] string Id);

public record OpenAiModelsResponse(
    [property: JsonPropertyName("data")] OpenAiModelEntry[] Data);

public record OpenAiStreamDelta(
    [property: JsonPropertyName("content")] string? Content,
    [property: JsonPropertyName("role")] string? Role);

public record OpenAiStreamChoice(
    [property: JsonPropertyName("delta")] OpenAiStreamDelta? Delta,
    [property: JsonPropertyName("finish_reason")] string? FinishReason);

public record OpenAiStreamChunk(
    [property: JsonPropertyName("choices")] OpenAiStreamChoice[]? Choices);

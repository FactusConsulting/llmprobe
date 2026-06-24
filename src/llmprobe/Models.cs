using System.Text.Json;
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
[JsonSerializable(typeof(EmbedResult))]
[JsonSerializable(typeof(RerankResult))]
[JsonSerializable(typeof(ErrorResult))]
[JsonSerializable(typeof(OpenAiChatRequest))]
[JsonSerializable(typeof(OpenAiChatResponse))]
[JsonSerializable(typeof(OpenAiModelsResponse))]
[JsonSerializable(typeof(OpenAiStreamChunk))]
[JsonSerializable(typeof(OpenAiEmbeddingRequest))]
[JsonSerializable(typeof(OpenAiEmbeddingResponse))]
[JsonSerializable(typeof(OpenAiRerankRequest))]
[JsonSerializable(typeof(OpenAiRerankResponse))]
[JsonSerializable(typeof(VisionResult))]
[JsonSerializable(typeof(ToolsResult))]
[JsonSerializable(typeof(OpenAiVisionRequest))]
[JsonSerializable(typeof(OpenAiToolsRequest))]
[JsonSerializable(typeof(OpenAiToolsResponse))]
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

public record EmbedResult(
    string Endpoint,
    string Model,
    bool Ok,
    int? StatusCode,
    long LatencyMs,
    int Inputs,
    int Dimensions,
    double Norm,
    int PromptTokens,
    int TotalTokens,
    string? Error);

public record RerankResult(
    string Endpoint,
    string Model,
    bool Ok,
    int? StatusCode,
    long LatencyMs,
    int Documents,
    RerankItem[] Ranking,
    int TotalTokens,
    string? Error);

public record RerankItem(
    int Index,
    double Score,
    string? DocumentPreview);

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

public record OpenAiEmbeddingRequest(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("input")] string[] Input);

public record OpenAiEmbeddingData(
    [property: JsonPropertyName("embedding")] float[] Embedding,
    [property: JsonPropertyName("index")] int Index);

public record OpenAiEmbeddingResponse(
    [property: JsonPropertyName("data")] OpenAiEmbeddingData[] Data,
    [property: JsonPropertyName("model")] string? Model,
    [property: JsonPropertyName("usage")] OpenAiUsage? Usage);

public record OpenAiRerankRequest(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("query")] string Query,
    [property: JsonPropertyName("documents")] string[] Documents,
    [property: JsonPropertyName("top_n")] int? TopN = null);

public record OpenAiRerankDocument(
    [property: JsonPropertyName("text")] string? Text);

public record OpenAiRerankResultEntry(
    [property: JsonPropertyName("index")] int Index,
    [property: JsonPropertyName("relevance_score")] double RelevanceScore,
    [property: JsonPropertyName("document")] OpenAiRerankDocument? Document);

public record OpenAiRerankUsage(
    [property: JsonPropertyName("total_tokens")] int TotalTokens);

public record OpenAiRerankResponse(
    [property: JsonPropertyName("results")] OpenAiRerankResultEntry[] Results,
    [property: JsonPropertyName("model")] string? Model,
    [property: JsonPropertyName("usage")] OpenAiRerankUsage? Usage);

public record VisionResult(
    string Endpoint,
    string Model,
    bool Ok,
    int? StatusCode,
    long LatencyMs,
    bool ImageAccepted,
    string ImageSource,
    string? FinishReason,
    int PromptTokens,
    int CompletionTokens,
    int TotalTokens,
    string? ResponsePreview,
    string? Error);

public record ToolsResult(
    string Endpoint,
    string Model,
    bool Ok,
    int? StatusCode,
    long LatencyMs,
    bool ToolCalled,
    string? FunctionName,
    string? FunctionArguments,
    string? FinishReason,
    int PromptTokens,
    int CompletionTokens,
    int TotalTokens,
    string? ResponsePreview,
    string? Error);

// --- Vision (multimodal chat) request shapes ---
// A user message whose content is an array of parts: one image_url part plus a
// short text part. Kept separate from OpenAiChatRequest (string content) so the
// existing chat commands are untouched.
public record OpenAiImageUrl(
    [property: JsonPropertyName("url")] string Url);

public record OpenAiContentPart(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("text")] string? Text = null,
    [property: JsonPropertyName("image_url")] OpenAiImageUrl? ImageUrl = null);

public record OpenAiVisionMessage(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] OpenAiContentPart[] Content);

public record OpenAiVisionRequest(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("messages")] OpenAiVisionMessage[] Messages,
    [property: JsonPropertyName("max_tokens")] int? MaxTokens = null,
    [property: JsonPropertyName("temperature")] double? Temperature = null);

// --- Tool / function calling request shapes ---
public record OpenAiFunctionDef(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("parameters")] JsonElement Parameters);

public record OpenAiToolDef(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("function")] OpenAiFunctionDef Function);

public record OpenAiToolsRequest(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("messages")] OpenAiMessage[] Messages,
    [property: JsonPropertyName("tools")] OpenAiToolDef[] Tools,
    [property: JsonPropertyName("tool_choice")] string? ToolChoice = null,
    [property: JsonPropertyName("max_tokens")] int? MaxTokens = null,
    [property: JsonPropertyName("temperature")] double? Temperature = null);

// --- Tool calling response shapes ---
public record OpenAiToolCallFunction(
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("arguments")] string? Arguments);

public record OpenAiToolCall(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("function")] OpenAiToolCallFunction? Function);

public record OpenAiToolsMessage(
    [property: JsonPropertyName("role")] string? Role,
    [property: JsonPropertyName("content")] string? Content,
    [property: JsonPropertyName("tool_calls")] OpenAiToolCall[]? ToolCalls);

public record OpenAiToolsChoice(
    [property: JsonPropertyName("message")] OpenAiToolsMessage? Message,
    [property: JsonPropertyName("finish_reason")] string? FinishReason);

public record OpenAiToolsResponse(
    [property: JsonPropertyName("choices")] OpenAiToolsChoice[] Choices,
    [property: JsonPropertyName("usage")] OpenAiUsage? Usage);

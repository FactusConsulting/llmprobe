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
[JsonSerializable(typeof(ReasoningResult))]
[JsonSerializable(typeof(StructuredResult))]
[JsonSerializable(typeof(OpenAiReasoningResponse))]
[JsonSerializable(typeof(OpenAiStructuredRequest))]
[JsonSerializable(typeof(CompletionsResult))]
[JsonSerializable(typeof(OpenAiCompletionsRequest))]
[JsonSerializable(typeof(OpenAiCompletionsResponse))]
[JsonSerializable(typeof(InfillResult))]
[JsonSerializable(typeof(LlamaInfillRequest))]
[JsonSerializable(typeof(LlamaInfillResponse))]
[JsonSerializable(typeof(TokenizeResult))]
[JsonSerializable(typeof(OpenAiTokenizeRequest))]
[JsonSerializable(typeof(OpenAiTokenizeResponse))]
[JsonSerializable(typeof(LlamaTokenizeRequest))]
[JsonSerializable(typeof(LlamaTokenizeResponse))]
[JsonSerializable(typeof(LogprobsResult))]
[JsonSerializable(typeof(LogprobItem))]
[JsonSerializable(typeof(LogprobAlternative))]
[JsonSerializable(typeof(OpenAiLogprobsRequest))]
[JsonSerializable(typeof(OpenAiLogprobsResponse))]
[JsonSerializable(typeof(ClassifyResult))]
[JsonSerializable(typeof(ClassifyLabel))]
[JsonSerializable(typeof(OpenAiClassifyRequest))]
[JsonSerializable(typeof(OpenAiClassifyResponse))]
[JsonSerializable(typeof(OpenAiScoreRequest))]
[JsonSerializable(typeof(OpenAiScoreResponse))]
[JsonSerializable(typeof(TranscribeResult))]
[JsonSerializable(typeof(OpenAiTranscriptionResponse))]
[JsonSerializable(typeof(SpeakResult))]
[JsonSerializable(typeof(OpenAiSpeechRequest))]
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

public record ReasoningResult(
    string Endpoint,
    string Model,
    bool Ok,
    int? StatusCode,
    long LatencyMs,
    bool ReasoningDetected,
    string? ReasoningChannel,
    int ReasoningTokens,
    int ReasoningCharsApprox,
    int AnswerCharsApprox,
    string? FinishReason,
    int PromptTokens,
    int CompletionTokens,
    int TotalTokens,
    string? AnswerPreview,
    string? Note,
    string? Error);

public record StructuredResult(
    string Endpoint,
    string Model,
    bool Ok,
    int? StatusCode,
    long LatencyMs,
    bool ParsedAsJson,
    bool SchemaConformant,
    string[] SchemaViolations,
    string? ObjectPreview,
    string? FinishReason,
    int PromptTokens,
    int CompletionTokens,
    int TotalTokens,
    string? Note,
    string? Error);

// --- Reasoning response shapes ---
// reasoning_content is exposed by some servers (e.g. vLLM, DeepSeek) on the
// message; reasoning_tokens lives under usage.completion_tokens_details.
public record OpenAiCompletionTokensDetails(
    [property: JsonPropertyName("reasoning_tokens")] int? ReasoningTokens);

public record OpenAiReasoningUsage(
    [property: JsonPropertyName("prompt_tokens")] int PromptTokens,
    [property: JsonPropertyName("completion_tokens")] int CompletionTokens,
    [property: JsonPropertyName("total_tokens")] int TotalTokens,
    [property: JsonPropertyName("completion_tokens_details")] OpenAiCompletionTokensDetails? CompletionTokensDetails);

public record OpenAiReasoningMessage(
    [property: JsonPropertyName("role")] string? Role,
    [property: JsonPropertyName("content")] string? Content,
    [property: JsonPropertyName("reasoning_content")] string? ReasoningContent);

public record OpenAiReasoningChoice(
    [property: JsonPropertyName("message")] OpenAiReasoningMessage? Message,
    [property: JsonPropertyName("finish_reason")] string? FinishReason);

public record OpenAiReasoningResponse(
    [property: JsonPropertyName("choices")] OpenAiReasoningChoice[] Choices,
    [property: JsonPropertyName("usage")] OpenAiReasoningUsage? Usage);

// --- Structured output request shape ---
// response_format: { "type": "json_schema", "json_schema": { name, schema, strict } }
public record OpenAiJsonSchema(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("schema")] JsonElement Schema,
    [property: JsonPropertyName("strict")] bool? Strict = null);

public record OpenAiResponseFormat(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("json_schema")] OpenAiJsonSchema? JsonSchema = null);

public record OpenAiStructuredRequest(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("messages")] OpenAiMessage[] Messages,
    [property: JsonPropertyName("response_format")] OpenAiResponseFormat ResponseFormat,
    [property: JsonPropertyName("max_tokens")] int? MaxTokens = null,
    [property: JsonPropertyName("temperature")] double? Temperature = null);

// --- completions (legacy text completion: /v1/completions) ---
// Unlike chat completions, the result lives on choices[0].text, not message.content.
public record CompletionsResult(
    string Endpoint,
    string Model,
    bool Ok,
    bool Supported,
    int? StatusCode,
    long LatencyMs,
    int PromptTokens,
    int CompletionTokens,
    int TotalTokens,
    string? FinishReason,
    string? TextPreview,
    string? Note,
    string? Error);

public record OpenAiCompletionsRequest(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("prompt")] string Prompt,
    [property: JsonPropertyName("max_tokens")] int? MaxTokens = null,
    [property: JsonPropertyName("temperature")] double? Temperature = null);

public record OpenAiCompletionsChoice(
    [property: JsonPropertyName("text")] string? Text,
    [property: JsonPropertyName("finish_reason")] string? FinishReason);

public record OpenAiCompletionsResponse(
    [property: JsonPropertyName("choices")] OpenAiCompletionsChoice[]? Choices,
    [property: JsonPropertyName("usage")] OpenAiUsage? Usage);

// --- infill (llama.cpp fill-in-the-middle: /infill) ---
public record InfillResult(
    string Endpoint,
    string Model,
    bool Ok,
    bool Supported,
    int? StatusCode,
    long LatencyMs,
    int PromptTokens,
    int CompletionTokens,
    int TotalTokens,
    string? ContentPreview,
    string? Note,
    string? Error);

public record LlamaInfillRequest(
    [property: JsonPropertyName("input_prefix")] string InputPrefix,
    [property: JsonPropertyName("input_suffix")] string InputSuffix,
    [property: JsonPropertyName("model")] string? Model = null,
    [property: JsonPropertyName("n_predict")] int? NPredict = null,
    [property: JsonPropertyName("temperature")] double? Temperature = null);

// llama.cpp /infill returns the infilled text on "content"; token counts (when
// present) live under "tokens_evaluated" / "tokens_predicted".
public record LlamaInfillResponse(
    [property: JsonPropertyName("content")] string? Content,
    [property: JsonPropertyName("tokens_evaluated")] int? TokensEvaluated,
    [property: JsonPropertyName("tokens_predicted")] int? TokensPredicted);

// --- tokenize (token count: /tokenize) ---
public record TokenizeResult(
    string Endpoint,
    string Model,
    bool Ok,
    bool Supported,
    int? StatusCode,
    long LatencyMs,
    int TokenCount,
    int[] FirstTokens,
    string? Note,
    string? Error);

// OpenAI/vLLM form: { model, prompt } -> { count, tokens }
public record OpenAiTokenizeRequest(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("prompt")] string Prompt);

public record OpenAiTokenizeResponse(
    [property: JsonPropertyName("count")] int? Count,
    [property: JsonPropertyName("tokens")] int[]? Tokens);

// llama.cpp form: { content } -> { tokens: [...] }
public record LlamaTokenizeRequest(
    [property: JsonPropertyName("content")] string Content);

public record LlamaTokenizeResponse(
    [property: JsonPropertyName("tokens")] int[]? Tokens);

// --- logprobs (functional logprobs probe over /v1/chat/completions) ---
public record LogprobsResult(
    string Endpoint,
    string Model,
    bool Ok,
    bool Supported,
    int? StatusCode,
    long LatencyMs,
    int SampledTokens,
    LogprobItem[] Tokens,
    string? FinishReason,
    int PromptTokens,
    int CompletionTokens,
    int TotalTokens,
    string? Note,
    string? Error);

public record LogprobItem(
    string Token,
    double Logprob,
    LogprobAlternative[] TopAlternatives);

public record LogprobAlternative(
    string Token,
    double Logprob);

public record OpenAiLogprobsRequest(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("messages")] OpenAiMessage[] Messages,
    [property: JsonPropertyName("logprobs")] bool Logprobs,
    [property: JsonPropertyName("top_logprobs")] int TopLogprobs,
    [property: JsonPropertyName("max_tokens")] int? MaxTokens = null,
    [property: JsonPropertyName("temperature")] double? Temperature = null);

// logprobs response: choices[].logprobs.content[] -> { token, logprob, top_logprobs[] }
public record OpenAiTopLogprob(
    [property: JsonPropertyName("token")] string? Token,
    [property: JsonPropertyName("logprob")] double Logprob);

public record OpenAiLogprobContent(
    [property: JsonPropertyName("token")] string? Token,
    [property: JsonPropertyName("logprob")] double Logprob,
    [property: JsonPropertyName("top_logprobs")] OpenAiTopLogprob[]? TopLogprobs);

public record OpenAiLogprobs(
    [property: JsonPropertyName("content")] OpenAiLogprobContent[]? Content);

public record OpenAiLogprobsChoice(
    [property: JsonPropertyName("message")] OpenAiMessage? Message,
    [property: JsonPropertyName("logprobs")] OpenAiLogprobs? Logprobs,
    [property: JsonPropertyName("finish_reason")] string? FinishReason);

public record OpenAiLogprobsResponse(
    [property: JsonPropertyName("choices")] OpenAiLogprobsChoice[]? Choices,
    [property: JsonPropertyName("usage")] OpenAiUsage? Usage);

// --- classify / score (vLLM classifier & cross-encoder scoring) ---
public record ClassifyResult(
    string Endpoint,
    string Model,
    bool Ok,
    bool Supported,
    int? StatusCode,
    long LatencyMs,
    string Mode,
    ClassifyLabel[] Labels,
    double? Score,
    string? Note,
    string? Error);

public record ClassifyLabel(
    string Label,
    double Probability);

public record OpenAiClassifyRequest(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("input")] string Input);

// vLLM /classify: data[] each has a predicted "label" and a "probs" array (one
// probability per label) plus optional "label_names".
public record OpenAiClassifyData(
    [property: JsonPropertyName("label")] string? Label,
    [property: JsonPropertyName("probs")] double[]? Probs,
    [property: JsonPropertyName("label_names")] string[]? LabelNames);

public record OpenAiClassifyResponse(
    [property: JsonPropertyName("data")] OpenAiClassifyData[]? Data);

public record OpenAiScoreRequest(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("text_1")] string Text1,
    [property: JsonPropertyName("text_2")] string Text2);

// vLLM /score: data[] each has a numeric "score".
public record OpenAiScoreData(
    [property: JsonPropertyName("score")] double Score);

public record OpenAiScoreResponse(
    [property: JsonPropertyName("data")] OpenAiScoreData[]? Data);

// --- transcribe (speech-to-text: /v1/audio/transcriptions, multipart upload) ---
public record TranscribeResult(
    string Endpoint,
    string Model,
    bool Ok,
    bool Supported,
    int? StatusCode,
    long LatencyMs,
    string AudioSource,
    int TextChars,
    string? TextPreview,
    double? DurationSeconds,
    string? Note,
    string? Error);

// /v1/audio/transcriptions returns the recognized text on "text"; some servers
// also echo the audio "duration".
public record OpenAiTranscriptionResponse(
    [property: JsonPropertyName("text")] string? Text,
    [property: JsonPropertyName("duration")] double? Duration);

// --- speak (text-to-speech: /v1/audio/speech, binary audio response) ---
public record SpeakResult(
    string Endpoint,
    string Model,
    bool Ok,
    bool Supported,
    int? StatusCode,
    long LatencyMs,
    string Voice,
    string Format,
    string? ContentType,
    int BytesReceived,
    string? OutputPath,
    string? Note,
    string? Error);

public record OpenAiSpeechRequest(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("input")] string Input,
    [property: JsonPropertyName("voice")] string Voice,
    [property: JsonPropertyName("response_format")] string ResponseFormat);

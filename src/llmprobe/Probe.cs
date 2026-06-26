using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace LlmProbe;

// Raised when a user-supplied @file (prompt/input/query/document/image) cannot be
// read. Commands catch this once and surface it as a clean config error (exit 78),
// distinct from a request/transport failure (exit 74).
public sealed class ConfigException : Exception
{
    public string? Hint { get; }

    public ConfigException(string message, string? hint = null) : base(message)
    {
        Hint = hint;
    }
}

public static class Probe
{
    private const string Post = "POST";

    // Read a user-supplied @file path, translating the unreadable-file exceptions
    // (missing/permission/security) into a ConfigException so callers can surface a
    // clean config error instead of an unhandled raw exception.
    internal static string ReadAtFile(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            throw new ConfigException($"could not read file '{path}': {ex.Message}",
                "Check the path and permissions, or pass the value inline / via @- (stdin)");
        }
    }

    // Byte-oriented counterpart of ReadAtFile for binary inputs (e.g. the audio file
    // the transcribe command uploads). Same ConfigException-on-failure contract so a
    // missing/unreadable file surfaces as a clean config error (exit 78).
    internal static byte[] ReadFileBytes(string path)
    {
        try
        {
            return File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            throw new ConfigException($"could not read file '{path}': {ex.Message}",
                "Check the path and permissions");
        }
    }

    public static HttpClient CreateClient(string? apiKey, TimeSpan timeout)
    {
        var c = new HttpClient { Timeout = timeout };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("llmprobe/0.1");
        c.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (!string.IsNullOrWhiteSpace(apiKey))
            c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        return c;
    }

    public static string Normalize(string endpoint)
    {
        var e = endpoint.TrimEnd('/');
        if (!e.StartsWith("http", StringComparison.OrdinalIgnoreCase)) e = "http://" + e;
        return e;
    }

    public static async Task<PingResult> PingAsync(HttpClient http, string endpoint, CancellationToken ct)
    {
        var e = Normalize(endpoint);
        var sw = Stopwatch.StartNew();
        foreach (var path in new[] { "/v1/models", "/health", "/" })
        {
            try
            {
                var req = new HttpRequestMessage(HttpMethod.Get, e + path);
                using var res = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
                sw.Stop();
                var server = res.Headers.TryGetValues("Server", out var s) ? string.Join(",", s) : null;
                return new PingResult(e, true, (int)res.StatusCode, sw.ElapsedMilliseconds, server, null);
            }
            catch (HttpRequestException) when (!ct.IsCancellationRequested) { }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested) { }
        }
        sw.Stop();
        return new PingResult(e, false, null, sw.ElapsedMilliseconds, null,
            "no reachable endpoint at /v1/models, /health, or /");
    }

    public static async Task<ModelList?> ListModelsAsync(HttpClient http, string endpoint, CancellationToken ct)
    {
        var e = Normalize(endpoint);
        var sw = Stopwatch.StartNew();
        using var res = await http.GetAsync($"{e}/v1/models", ct);
        sw.Stop();
        if (!res.IsSuccessStatusCode) return null;
        var json = await res.Content.ReadAsStringAsync(ct);
        var data = JsonSerializer.Deserialize(json, JsonContext.Default.OpenAiModelsResponse);
        var names = data?.Data.Select(m => m.Id).ToArray() ?? Array.Empty<string>();
        return new ModelList(e, names.Length, names, sw.ElapsedMilliseconds);
    }

    // Shared skeleton for every "POST JSON to one URL and map the response" probe.
    // Handles timing + request build + send + read + status/exception handling,
    // leaving each caller to map the raw response body into its own result shape.
    // The three delegates receive the same data the inline implementations used to:
    //   onError(status, elapsedMs, rawBody)   — non-2xx response
    //   onOk(status, elapsedMs, rawBody)       — 2xx response
    //   onException(elapsedMs, message)        — transport/cancellation failure
    private static async Task<TResult> PostJsonAsync<TResult>(
        HttpClient http, string url, string requestJson, CancellationToken ct,
        Func<int, long, string, TResult> onError,
        Func<int, long, string, TResult> onOk,
        Func<long, string, TResult> onException)
    {
        var sw = Stopwatch.StartNew();
        var req = BuildJsonPost(url, requestJson);
        try
        {
            using var res = await http.SendAsync(req, ct);
            sw.Stop();
            var raw = await res.Content.ReadAsStringAsync(ct);
            var status = (int)res.StatusCode;
            RawSink.Response(status, raw);
            return res.IsSuccessStatusCode
                ? onOk(status, sw.ElapsedMilliseconds, raw)
                : onError(status, sw.ElapsedMilliseconds, raw);
        }
        catch (Exception ex)
        {
            sw.Stop();
            RawSink.ResponseFailed(ex.Message);
            return onException(sw.ElapsedMilliseconds, ex.Message);
        }
    }

    // Build the POST request for a JSON body, mirroring it to the --raw sink first.
    // Shared by PostJsonAsync and the probes that can't use that skeleton (streaming
    // SSE, binary audio) but still send an ordinary JSON request.
    private static HttpRequestMessage BuildJsonPost(string url, string json)
    {
        RawSink.Request(Post, url, json);
        return new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
    }

    // Thin specialization of PostJsonAsync for the chat/completions probes
    // (chat, reasoning, structured, vision, tools), which all target the same
    // OpenAI /v1/chat/completions route off a bare endpoint.
    private static Task<TResult> PostChatAsync<TResult>(
        HttpClient http, string endpoint, string requestJson, CancellationToken ct,
        Func<int, long, string, TResult> onError,
        Func<int, long, string, TResult> onOk,
        Func<long, string, TResult> onException) =>
        PostJsonAsync(http, $"{Normalize(endpoint)}/v1/chat/completions", requestJson, ct,
            onError, onOk, onException);

    public static Task<TestResult> ChatTestAsync(
        HttpClient http, string endpoint, string model, string prompt, int maxTokens, CancellationToken ct)
    {
        var e = Normalize(endpoint);
        var body = new OpenAiChatRequest(model,
            new[] { new OpenAiMessage("user", prompt) }, MaxTokens: maxTokens, Temperature: 0);
        var json = JsonSerializer.Serialize(body, JsonContext.Default.OpenAiChatRequest);
        return PostChatAsync(http, e, json, ct,
            onError: (status, ms, raw) =>
                new TestResult(e, model, false, status, ms, 0, 0, 0, null, null, Trunc(raw, 200)),
            onOk: (status, ms, raw) =>
            {
                var resp = JsonSerializer.Deserialize(raw, JsonContext.Default.OpenAiChatResponse);
                var choice = resp?.Choices.FirstOrDefault();
                var content = choice?.Message?.Content ?? "";
                return new TestResult(e, model, true, status, ms,
                    resp?.Usage?.PromptTokens ?? 0,
                    resp?.Usage?.CompletionTokens ?? 0,
                    resp?.Usage?.TotalTokens ?? 0,
                    choice?.FinishReason,
                    Trunc(content, 160),
                    null);
            },
            onException: (ms, msg) =>
                new TestResult(e, model, false, null, ms, 0, 0, 0, null, null, msg));
    }

    public static async Task<StreamResult> StreamTestAsync(
        HttpClient http, string endpoint, string model, string prompt, int maxTokens, CancellationToken ct)
    {
        var e = Normalize(endpoint);
        var sw = Stopwatch.StartNew();
        long ttftMs = 0;
        int chunks = 0;
        var sb = new StringBuilder();
        string? finish = null;
        var body = new OpenAiChatRequest(model,
            new[] { new OpenAiMessage("user", prompt) }, MaxTokens: maxTokens, Temperature: 0, Stream: true);
        var json = JsonSerializer.Serialize(body, JsonContext.Default.OpenAiChatRequest);
        var url = $"{e}/v1/chat/completions";
        var req = BuildJsonPost(url, json);
        try
        {
            using var res = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!res.IsSuccessStatusCode)
            {
                sw.Stop();
                var err = await res.Content.ReadAsStringAsync(ct);
                RawSink.Response((int)res.StatusCode, err);
                return new StreamResult(e, model, false, 0, sw.ElapsedMilliseconds, 0, 0, 0, null, Trunc(err, 200));
            }
            RawSink.ResponseSummary((int)res.StatusCode, "(SSE stream; raw chunks below)");
            await using var stream = await res.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream);
            string? line;
            while ((line = await reader.ReadLineAsync(ct)) != null)
            {
                if (!line.StartsWith("data:")) continue;
                var payload = line[5..].Trim();
                if (payload == "[DONE]") break;
                if (payload.Length == 0) continue;
                RawSink.RawLine(payload);
                try
                {
                    var chunk = JsonSerializer.Deserialize(payload, JsonContext.Default.OpenAiStreamChunk);
                    var choice = chunk?.Choices?.FirstOrDefault();
                    var content = choice?.Delta?.Content;
                    if (!string.IsNullOrEmpty(content))
                    {
                        if (ttftMs == 0) ttftMs = sw.ElapsedMilliseconds;
                        sb.Append(content);
                        chunks++;
                    }
                    if (!string.IsNullOrEmpty(choice?.FinishReason)) finish = choice.FinishReason;
                }
                catch (JsonException) { }
            }
            sw.Stop();
            var output = sb.ToString();
            var approx = ApproxTokens(output);
            var elapsedSec = Math.Max(0.001, (sw.ElapsedMilliseconds - ttftMs) / 1000.0);
            return new StreamResult(e, model, true, ttftMs, sw.ElapsedMilliseconds, chunks, approx,
                approx / elapsedSec, finish, null);
        }
        catch (Exception ex)
        {
            sw.Stop();
            RawSink.ResponseFailed(ex.Message);
            return new StreamResult(e, model, false, ttftMs, sw.ElapsedMilliseconds, chunks, 0, 0, finish, ex.Message);
        }
    }

    public static async Task<CapabilitiesResult> CapabilitiesAsync(
        HttpClient http, string endpoint, CancellationToken ct)
    {
        var e = Normalize(endpoint);
        var models = await ListModelsAsync(http, e, ct);
        string? server = null;
        string? api = null;
        try
        {
            using var probe = await http.GetAsync($"{e}/v1/models", ct);
            if (probe.Headers.TryGetValues("Server", out var s)) server = string.Join(",", s);
            api = probe.IsSuccessStatusCode ? "openai-compatible" : null;
        }
        catch { }

        var firstModel = models?.Models.FirstOrDefault() ?? "default";
        var streaming = await TryFeatureAsync(http, e, firstModel, """{"stream":true}""", ct, expectSse: true);
        var jsonMode = await TryFeatureAsync(http, e, firstModel, """{"response_format":{"type":"json_object"}}""", ct);
        var logprobs = await TryFeatureAsync(http, e, firstModel, """{"logprobs":true}""", ct);

        // If all features look "false" because completions endpoint is auth-gated,
        // surface that more honestly via the auth_required field.
        var authRequired = streaming == FeatureState.AuthRequired
                        || jsonMode == FeatureState.AuthRequired
                        || logprobs == FeatureState.AuthRequired;

        return new CapabilitiesResult(e,
            streaming == FeatureState.Yes,
            false, false,
            jsonMode == FeatureState.Yes,
            logprobs == FeatureState.Yes,
            api, server,
            models?.Models ?? Array.Empty<string>(),
            authRequired ? "auth-required (some feature probes returned 401/403; set --api-key or OPENAI_API_KEY to detect properly)" : null);
    }

    public enum FeatureState { Yes, No, AuthRequired }

    private static async Task<FeatureState> TryFeatureAsync(
        HttpClient http, string endpoint, string model, string extraJson, CancellationToken ct, bool expectSse = false)
    {
        try
        {
            var body = $$"""
            {"model":"{{model}}","messages":[{"role":"user","content":"hi"}],"max_tokens":1{{(string.IsNullOrEmpty(extraJson) ? "" : "," + extraJson.Trim('{', '}'))}}}
            """;
            var req = new HttpRequestMessage(HttpMethod.Post, $"{endpoint}/v1/chat/completions")
            { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            using var res = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            if (res.StatusCode == System.Net.HttpStatusCode.Unauthorized
                || res.StatusCode == System.Net.HttpStatusCode.Forbidden)
                return FeatureState.AuthRequired;
            if (!res.IsSuccessStatusCode) return FeatureState.No;
            if (!expectSse) return FeatureState.Yes;
            await using var s = await res.Content.ReadAsStreamAsync(ct);
            using var r = new StreamReader(s);
            var line = await r.ReadLineAsync(ct);
            return line != null && line.StartsWith("data:") ? FeatureState.Yes : FeatureState.No;
        }
        catch { return FeatureState.No; }
    }

    public static Task<EmbedResult> EmbedAsync(
        HttpClient http, string endpoint, string model, string[] input, CancellationToken ct)
    {
        var e = Normalize(endpoint);
        var body = new OpenAiEmbeddingRequest(model, input);
        var json = JsonSerializer.Serialize(body, JsonContext.Default.OpenAiEmbeddingRequest);
        return PostJsonAsync(http, $"{e}/v1/embeddings", json, ct,
            onError: (status, ms, raw) =>
                new EmbedResult(e, model, false, status, ms, input.Length, 0, 0, 0, 0, Trunc(raw, 200)),
            onOk: (status, ms, raw) =>
            {
                var resp = JsonSerializer.Deserialize(raw, JsonContext.Default.OpenAiEmbeddingResponse);
                var first = resp?.Data.FirstOrDefault();
                var vec = first?.Embedding ?? Array.Empty<float>();
                return new EmbedResult(e, model, true, status, ms,
                    input.Length,
                    vec.Length,
                    L2Norm(vec),
                    resp?.Usage?.PromptTokens ?? 0,
                    resp?.Usage?.TotalTokens ?? 0,
                    null);
            },
            onException: (ms, msg) =>
                new EmbedResult(e, model, false, null, ms, input.Length, 0, 0, 0, 0, msg));
    }

    public static Task<RerankResult> RerankAsync(
        HttpClient http, string endpoint, string model, string query, string[] documents, int? topN, CancellationToken ct)
    {
        var e = Normalize(endpoint);
        var body = new OpenAiRerankRequest(model, query, documents, topN);
        var json = JsonSerializer.Serialize(body, JsonContext.Default.OpenAiRerankRequest);
        return PostJsonAsync(http, $"{e}/v1/rerank", json, ct,
            onError: (status, ms, raw) =>
                new RerankResult(e, model, false, status, ms, documents.Length, Array.Empty<RerankItem>(), 0, Trunc(raw, 200)),
            onOk: (status, ms, raw) =>
            {
                var resp = JsonSerializer.Deserialize(raw, JsonContext.Default.OpenAiRerankResponse);
                var ranking = (resp?.Results ?? Array.Empty<OpenAiRerankResultEntry>())
                    .Select(r => new RerankItem(
                        r.Index,
                        r.RelevanceScore,
                        // Prefer the doc text echoed by the server; fall back to the input we sent.
                        Trunc(r.Document?.Text ?? (r.Index >= 0 && r.Index < documents.Length ? documents[r.Index] : ""), 80)))
                    .ToArray();
                return new RerankResult(e, model, true, status, ms,
                    documents.Length, ranking, resp?.Usage?.TotalTokens ?? 0, null);
            },
            onException: (ms, msg) =>
                new RerankResult(e, model, false, null, ms, documents.Length, Array.Empty<RerankItem>(), 0, msg));
    }

    public static Task<ReasoningResult> ReasoningAsync(
        HttpClient http, string endpoint, string model, string prompt, int maxTokens, CancellationToken ct)
    {
        var e = Normalize(endpoint);
        var body = new OpenAiChatRequest(model,
            new[] { new OpenAiMessage("user", prompt) }, MaxTokens: maxTokens, Temperature: 0);
        var json = JsonSerializer.Serialize(body, JsonContext.Default.OpenAiChatRequest);
        return PostChatAsync(http, e, json, ct,
            onError: (status, ms, raw) =>
                new ReasoningResult(e, model, false, status, ms,
                    false, null, 0, 0, 0, null, 0, 0, 0, null, null, Trunc(raw, 200)),
            onOk: (status, ms, raw) =>
            {
                var resp = JsonSerializer.Deserialize(raw, JsonContext.Default.OpenAiReasoningResponse);
                var choice = resp?.Choices.FirstOrDefault();
                var content = choice?.Message?.Content ?? "";
                var reasoningContent = choice?.Message?.ReasoningContent;
                var reasoningTokens = resp?.Usage?.CompletionTokensDetails?.ReasoningTokens ?? 0;
                var (detected, channel, reasoningText, answer) = DetectReasoning(content, reasoningContent, reasoningTokens);
                return new ReasoningResult(e, model, true, status, ms,
                    detected, channel, reasoningTokens,
                    reasoningText.Length, answer.Length,
                    choice?.FinishReason,
                    resp?.Usage?.PromptTokens ?? 0,
                    resp?.Usage?.CompletionTokens ?? 0,
                    resp?.Usage?.TotalTokens ?? 0,
                    Trunc(answer.Trim(), 160),
                    "TTFT split between thinking and answer needs streaming (use 'stream').",
                    null);
            },
            onException: (ms, msg) =>
                new ReasoningResult(e, model, false, null, ms,
                    false, null, 0, 0, 0, null, 0, 0, 0, null, null, msg));
    }

    // Detect reasoning across the channels servers use: a dedicated
    // reasoning_content field, an inline <think>...</think> block in the content,
    // and/or a reasoning_tokens count in usage. Returns (detected, channel,
    // reasoningText, answerText) where answerText strips any <think> block.
    internal static (bool Detected, string? Channel, string ReasoningText, string AnswerText)
        DetectReasoning(string content, string? reasoningContent, int reasoningTokens)
    {
        if (!string.IsNullOrWhiteSpace(reasoningContent))
            return (true, "reasoning_content", reasoningContent!, content);

        var open = content.IndexOf("<think>", StringComparison.OrdinalIgnoreCase);
        if (open >= 0)
        {
            var close = content.IndexOf("</think>", open, StringComparison.OrdinalIgnoreCase);
            if (close >= 0)
            {
                var think = content.Substring(open + 7, close - (open + 7));
                var answer = content[..open] + content[(close + 8)..];
                return (true, "think_tag", think, answer);
            }
            // Unclosed <think>: treat the remainder as reasoning.
            return (true, "think_tag", content[(open + 7)..], "");
        }

        if (reasoningTokens > 0)
            return (true, "reasoning_tokens", "", content);

        return (false, null, "", content);
    }

    public static Task<StructuredResult> StructuredAsync(
        HttpClient http, string endpoint, string model, string prompt, int maxTokens, CancellationToken ct)
    {
        var e = Normalize(endpoint);
        var schema = JsonSerializer.Deserialize<JsonElement>("""
            {"type":"object","properties":{"name":{"type":"string"},"age":{"type":"integer"}},"required":["name","age"],"additionalProperties":false}
            """);
        var responseFormat = new OpenAiResponseFormat("json_schema",
            new OpenAiJsonSchema("person", schema, Strict: true));
        var body = new OpenAiStructuredRequest(model,
            new[] { new OpenAiMessage("user", prompt) }, responseFormat, MaxTokens: maxTokens, Temperature: 0);
        var json = JsonSerializer.Serialize(body, JsonContext.Default.OpenAiStructuredRequest);
        return PostChatAsync(http, e, json, ct,
            onError: (status, ms, raw) =>
                new StructuredResult(e, model, false, status, ms,
                    false, false, Array.Empty<string>(), null, null, 0, 0, 0,
                    "endpoint rejected the request (may not support response_format json_schema)", Trunc(raw, 200)),
            onOk: (status, ms, raw) =>
            {
                var resp = JsonSerializer.Deserialize(raw, JsonContext.Default.OpenAiChatResponse);
                var choice = resp?.Choices.FirstOrDefault();
                var content = choice?.Message?.Content ?? "";
                var (parsed, conformant, violations, preview) = ValidatePerson(content);
                string? note = null;
                if (!parsed)
                    note = "response was not valid JSON (endpoint may not support structured output)";
                else if (!conformant)
                    note = "returned JSON did not match the requested schema";
                return new StructuredResult(e, model, true, status, ms,
                    parsed, conformant, violations, preview, choice?.FinishReason,
                    resp?.Usage?.PromptTokens ?? 0,
                    resp?.Usage?.CompletionTokens ?? 0,
                    resp?.Usage?.TotalTokens ?? 0,
                    note, null);
            },
            onException: (ms, msg) =>
                new StructuredResult(e, model, false, null, ms,
                    false, false, Array.Empty<string>(), null, null, 0, 0, 0, null, msg));
    }

    // Validate a model response against the fixed { name: string, age: integer }
    // schema. Returns (parsedAsJson, schemaConformant, violations, objectPreview).
    internal static (bool Parsed, bool Conformant, string[] Violations, string? Preview)
        ValidatePerson(string content)
    {
        JsonElement root;
        try
        {
            using var doc = JsonDocument.Parse(content);
            root = doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return (false, false, Array.Empty<string>(), Trunc(content.Trim(), 120));
        }

        var violations = new List<string>();
        if (root.ValueKind != JsonValueKind.Object)
        {
            violations.Add($"root is {root.ValueKind}, expected object");
            return (true, false, violations.ToArray(), Trunc(content.Trim(), 120));
        }

        if (!root.TryGetProperty("name", out var name))
            violations.Add("missing required field 'name'");
        else if (name.ValueKind != JsonValueKind.String)
            violations.Add($"'name' is {name.ValueKind}, expected string");

        if (!root.TryGetProperty("age", out var age))
            violations.Add("missing required field 'age'");
        else if (age.ValueKind != JsonValueKind.Number || !age.TryGetInt64(out _))
            violations.Add($"'age' is not an integer ({age.ValueKind})");

        // The fixed schema is strict (additionalProperties:false): any key beyond
        // the {name, age} schema makes the object non-conformant.
        foreach (var prop in root.EnumerateObject())
            if (prop.Name != "name" && prop.Name != "age")
                violations.Add($"unexpected property '{prop.Name}'");

        var preview = Trunc(root.GetRawText().Replace("\n", " ").Replace("\r", ""), 160);
        return (true, violations.Count == 0, violations.ToArray(), preview);
    }

    public static Task<VisionResult> VisionAsync(
        HttpClient http, string endpoint, string model, (string Url, string Source) image,
        string prompt, int maxTokens, CancellationToken ct)
    {
        var e = Normalize(endpoint);
        var content = new[]
        {
            new OpenAiContentPart("text", Text: prompt),
            new OpenAiContentPart("image_url", ImageUrl: new OpenAiImageUrl(image.Url)),
        };
        var body = new OpenAiVisionRequest(model,
            new[] { new OpenAiVisionMessage("user", content) }, MaxTokens: maxTokens, Temperature: 0);
        var json = JsonSerializer.Serialize(body, JsonContext.Default.OpenAiVisionRequest);
        return PostChatAsync(http, e, json, ct,
            onError: (status, ms, raw) =>
                new VisionResult(e, model, false, status, ms,
                    false, image.Source, null, 0, 0, 0, null, Trunc(raw, 200)),
            onOk: (status, ms, raw) =>
            {
                var resp = JsonSerializer.Deserialize(raw, JsonContext.Default.OpenAiChatResponse);
                var choice = resp?.Choices.FirstOrDefault();
                var text = choice?.Message?.Content ?? "";
                return new VisionResult(e, model, true, status, ms,
                    true, image.Source, choice?.FinishReason,
                    resp?.Usage?.PromptTokens ?? 0,
                    resp?.Usage?.CompletionTokens ?? 0,
                    resp?.Usage?.TotalTokens ?? 0,
                    Trunc(text, 160), null);
            },
            onException: (ms, msg) =>
                new VisionResult(e, model, false, null, ms,
                    false, image.Source, null, 0, 0, 0, null, msg));
    }

    public static Task<ToolsResult> ToolsAsync(
        HttpClient http, string endpoint, string model, string prompt, int maxTokens, CancellationToken ct)
    {
        var e = Normalize(endpoint);
        var parameters = JsonSerializer.Deserialize<JsonElement>("""
            {"type":"object","properties":{"location":{"type":"string","description":"City name, e.g. Copenhagen"}},"required":["location"]}
            """);
        var tool = new OpenAiToolDef("function",
            new OpenAiFunctionDef("get_weather", "Get the current weather for a location.", parameters));
        var body = new OpenAiToolsRequest(model,
            new[] { new OpenAiMessage("user", prompt) },
            new[] { tool }, ToolChoice: "auto", MaxTokens: maxTokens, Temperature: 0);
        var json = JsonSerializer.Serialize(body, JsonContext.Default.OpenAiToolsRequest);
        return PostChatAsync(http, e, json, ct,
            onError: (status, ms, raw) =>
                new ToolsResult(e, model, false, status, ms,
                    false, null, null, null, 0, 0, 0, null, Trunc(raw, 200)),
            onOk: (status, ms, raw) =>
            {
                var resp = JsonSerializer.Deserialize(raw, JsonContext.Default.OpenAiToolsResponse);
                var choice = resp?.Choices.FirstOrDefault();
                var call = choice?.Message?.ToolCalls?.FirstOrDefault();
                var called = call?.Function != null;
                return new ToolsResult(e, model, true, status, ms,
                    called, call?.Function?.Name, call?.Function?.Arguments, choice?.FinishReason,
                    resp?.Usage?.PromptTokens ?? 0,
                    resp?.Usage?.CompletionTokens ?? 0,
                    resp?.Usage?.TotalTokens ?? 0,
                    Trunc(choice?.Message?.Content ?? "", 160), null);
            },
            onException: (ms, msg) =>
                new ToolsResult(e, model, false, null, ms,
                    false, null, null, null, 0, 0, 0, null, msg));
    }

    // Resolve an --image value into a (url, source-label) pair. An http(s):// value
    // is passed through as a remote URL; anything else is treated as a local file
    // path (with optional leading '@'), read and inlined as a base64 data: URL with
    // the mime type inferred from the extension. Throws on unreadable files so the
    // command can surface a clean config error (exit 78).
    public static (string Url, string Source) ResolveImage(string image)
    {
        if (image.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || image.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return (image, image);

        var path = image.StartsWith('@') ? image[1..] : image;
        var bytes = File.ReadAllBytes(path);
        var mime = MimeFromExtension(path);
        var dataUrl = $"data:{mime};base64,{Convert.ToBase64String(bytes)}";
        return (dataUrl, path);
    }

    internal static string MimeFromExtension(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            _ => "application/octet-stream",
        };
    }

    // Expand document/input values: a value of "@-" reads stdin, "@file" reads a
    // file; in both cases each non-empty line becomes a separate entry. Any other
    // value is taken literally. Used by the rerank command so a corpus can be fed
    // as `-d @docs.txt` instead of one -d flag per line.
    public static string[] ExpandLines(IEnumerable<string> values)
    {
        var outp = new List<string>();
        foreach (var v in values)
        {
            if (v == "@-")
                outp.AddRange(SplitLines(Console.In.ReadToEnd()));
            else if (v.StartsWith('@'))
                outp.AddRange(SplitLines(ReadAtFile(v[1..])));
            else
                outp.Add(v);
        }
        return outp.ToArray();
    }

    internal static IEnumerable<string> SplitLines(string text) =>
        text.Replace("\r\n", "\n").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0);

    internal static double L2Norm(float[] v)
    {
        double sum = 0;
        foreach (var x in v) sum += (double)x * x;
        return Math.Sqrt(sum);
    }

    private static int ApproxTokens(string text) => Math.Max(1, text.Length / 4);
    private static string Trunc(string s, int n) => s.Length > n ? s[..n] + "…" : s;

    // A probe detects support. When an endpoint route doesn't exist (404/501) or the
    // server rejects the route shape (400/405), that means "this endpoint doesn't
    // offer this feature" — not a transport failure. Callers report it cleanly via a
    // Supported=false field at exit 0, rather than treating it as an error (exit 74).
    internal static bool IsUnsupportedStatus(int status) =>
        status == 404 || status == 405 || status == 501 || status == 400;

    // --- completions: legacy text completion (POST /v1/completions) ---
    public static Task<CompletionsResult> CompletionsAsync(
        HttpClient http, string endpoint, string model, string prompt, int maxTokens, CancellationToken ct)
    {
        var e = Normalize(endpoint);
        var body = new OpenAiCompletionsRequest(model, prompt, MaxTokens: maxTokens, Temperature: 0);
        var json = JsonSerializer.Serialize(body, JsonContext.Default.OpenAiCompletionsRequest);
        return PostJsonAsync(http, $"{e}/v1/completions", json, ct,
            // A reachable endpoint that lacks the route is "not supported" — a clean
            // result (Ok=true so exit 0). Any other non-2xx (auth/5xx) is a genuine
            // error: Ok=false (exit 74) with the response body surfaced.
            onError: (status, ms, raw) =>
            {
                var unsupported = IsUnsupportedStatus(status);
                return new CompletionsResult(e, model, unsupported, !unsupported, status, ms,
                    0, 0, 0, null, null,
                    unsupported ? "not supported by this endpoint (no /v1/completions route)" : null,
                    unsupported ? null : Trunc(raw, 200));
            },
            onOk: (status, ms, raw) =>
            {
                var resp = JsonSerializer.Deserialize(raw, JsonContext.Default.OpenAiCompletionsResponse);
                var choice = resp?.Choices?.FirstOrDefault();
                return new CompletionsResult(e, model, true, true, status, ms,
                    resp?.Usage?.PromptTokens ?? 0,
                    resp?.Usage?.CompletionTokens ?? 0,
                    resp?.Usage?.TotalTokens ?? 0,
                    choice?.FinishReason,
                    Trunc(choice?.Text ?? "", 160),
                    null, null);
            },
            onException: (ms, msg) =>
                new CompletionsResult(e, model, false, true, null, ms, 0, 0, 0, null, null, null, msg));
    }

    // --- infill: llama.cpp fill-in-the-middle (POST /infill) ---
    public static Task<InfillResult> InfillAsync(
        HttpClient http, string endpoint, string model, string prefix, string suffix, int maxTokens, CancellationToken ct)
    {
        var e = Normalize(endpoint);
        var body = new LlamaInfillRequest(prefix, suffix,
            Model: string.IsNullOrEmpty(model) ? null : model, NPredict: maxTokens, Temperature: 0);
        var json = JsonSerializer.Serialize(body, JsonContext.Default.LlamaInfillRequest);
        return PostJsonAsync(http, $"{e}/infill", json, ct,
            onError: (status, ms, raw) =>
            {
                var unsupported = IsUnsupportedStatus(status);
                return new InfillResult(e, model, unsupported, !unsupported, status, ms,
                    0, 0, 0, null,
                    unsupported ? "not supported (llama.cpp /infill only)" : null,
                    unsupported ? null : Trunc(raw, 200));
            },
            onOk: (status, ms, raw) =>
            {
                var resp = JsonSerializer.Deserialize(raw, JsonContext.Default.LlamaInfillResponse);
                return new InfillResult(e, model, true, true, status, ms,
                    resp?.TokensEvaluated ?? 0,
                    resp?.TokensPredicted ?? 0,
                    (resp?.TokensEvaluated ?? 0) + (resp?.TokensPredicted ?? 0),
                    Trunc(resp?.Content ?? "", 160),
                    null, null);
            },
            onException: (ms, msg) =>
                new InfillResult(e, model, false, true, null, ms, 0, 0, 0, null, null, msg));
    }

    // --- tokenize: token count (POST /tokenize) ---
    // Prefer the OpenAI/vLLM form { model, prompt } -> { count, tokens }; if the
    // server returns the llama.cpp shape ({ tokens:[...] } with no count), use that.
    public static Task<TokenizeResult> TokenizeAsync(
        HttpClient http, string endpoint, string model, string input, CancellationToken ct)
    {
        var e = Normalize(endpoint);
        var body = new OpenAiTokenizeRequest(model, input);
        var json = JsonSerializer.Serialize(body, JsonContext.Default.OpenAiTokenizeRequest);
        return PostJsonAsync(http, $"{e}/tokenize", json, ct,
            onError: (status, ms, raw) =>
            {
                var unsupported = IsUnsupportedStatus(status);
                return new TokenizeResult(e, model, unsupported, !unsupported, status, ms,
                    0, Array.Empty<int>(),
                    unsupported ? "not supported by this endpoint (no /tokenize route)" : null,
                    unsupported ? null : Trunc(raw, 200));
            },
            onOk: (status, ms, raw) =>
            {
                // Both response shapes carry a "tokens" array; the OpenAI/vLLM form adds a
                // "count". Parse the richer shape first and fall back to the token array.
                var resp = JsonSerializer.Deserialize(raw, JsonContext.Default.OpenAiTokenizeResponse);
                var tokens = resp?.Tokens ?? Array.Empty<int>();
                var count = resp?.Count ?? tokens.Length;
                return new TokenizeResult(e, model, true, true, status, ms,
                    count, tokens.Take(10).ToArray(), null, null);
            },
            onException: (ms, msg) =>
                new TokenizeResult(e, model, false, true, null, ms, 0, Array.Empty<int>(), null, msg));
    }

    // --- logprobs: functional logprobs probe (POST /v1/chat/completions) ---
    public static Task<LogprobsResult> LogprobsAsync(
        HttpClient http, string endpoint, string model, string prompt, int maxTokens, CancellationToken ct)
    {
        var e = Normalize(endpoint);
        var body = new OpenAiLogprobsRequest(model,
            new[] { new OpenAiMessage("user", prompt) },
            Logprobs: true, TopLogprobs: 5, MaxTokens: maxTokens, Temperature: 0);
        var json = JsonSerializer.Serialize(body, JsonContext.Default.OpenAiLogprobsRequest);
        return PostChatAsync(http, e, json, ct,
            onError: (status, ms, raw) =>
            {
                // A rejected logprobs request on a reachable endpoint means logprobs
                // aren't supported (Ok=true, exit 0); other non-2xx is a real error.
                var unsupported = IsUnsupportedStatus(status);
                return new LogprobsResult(e, model, unsupported, false, status, ms,
                    0, Array.Empty<LogprobItem>(), null, 0, 0, 0,
                    unsupported ? "not supported by this endpoint (rejected logprobs request)" : null,
                    unsupported ? null : Trunc(raw, 200));
            },
            onOk: (status, ms, raw) =>
            {
                var resp = JsonSerializer.Deserialize(raw, JsonContext.Default.OpenAiLogprobsResponse);
                var choice = resp?.Choices?.FirstOrDefault();
                var content = choice?.Logprobs?.Content ?? Array.Empty<OpenAiLogprobContent>();
                var items = content.Take(5).Select(c => new LogprobItem(
                    c.Token ?? "",
                    c.Logprob,
                    (c.TopLogprobs ?? Array.Empty<OpenAiTopLogprob>())
                        .Select(a => new LogprobAlternative(a.Token ?? "", a.Logprob)).ToArray()))
                    .ToArray();
                var supported = items.Length > 0;
                return new LogprobsResult(e, model, true, supported, status, ms,
                    items.Length, items, choice?.FinishReason,
                    resp?.Usage?.PromptTokens ?? 0,
                    resp?.Usage?.CompletionTokens ?? 0,
                    resp?.Usage?.TotalTokens ?? 0,
                    supported ? null : "request succeeded but no logprobs were returned (not supported)",
                    null);
            },
            onException: (ms, msg) =>
                new LogprobsResult(e, model, false, false, null, ms, 0, Array.Empty<LogprobItem>(), null, 0, 0, 0, null, msg));
    }

    // --- classify: sequence classification (POST /classify) ---
    public static Task<ClassifyResult> ClassifyAsync(
        HttpClient http, string endpoint, string model, string input, CancellationToken ct)
    {
        var e = Normalize(endpoint);
        var body = new OpenAiClassifyRequest(model, input);
        var json = JsonSerializer.Serialize(body, JsonContext.Default.OpenAiClassifyRequest);
        return PostJsonAsync(http, $"{e}/classify", json, ct,
            onError: (status, ms, raw) =>
                ClassifyError(e, model, "classify", status, ms, raw),
            onOk: (status, ms, raw) =>
            {
                var resp = JsonSerializer.Deserialize(raw, JsonContext.Default.OpenAiClassifyResponse);
                var labels = BuildLabels(resp?.Data?.FirstOrDefault());
                return new ClassifyResult(e, model, true, true, status, ms,
                    "classify", labels, null, null, null);
            },
            onException: (ms, msg) =>
                new ClassifyResult(e, model, false, true, null, ms,
                    "classify", Array.Empty<ClassifyLabel>(), null, null, msg));
    }

    // --- score: cross-encoder similarity of a text pair (POST /score) ---
    public static Task<ClassifyResult> ScoreAsync(
        HttpClient http, string endpoint, string model, string text1, string text2, CancellationToken ct)
    {
        var e = Normalize(endpoint);
        var body = new OpenAiScoreRequest(model, text1, text2);
        var json = JsonSerializer.Serialize(body, JsonContext.Default.OpenAiScoreRequest);
        return PostJsonAsync(http, $"{e}/score", json, ct,
            onError: (status, ms, raw) =>
                ClassifyError(e, model, "score", status, ms, raw),
            onOk: (status, ms, raw) =>
            {
                var resp = JsonSerializer.Deserialize(raw, JsonContext.Default.OpenAiScoreResponse);
                var score = resp?.Data?.FirstOrDefault()?.Score;
                return new ClassifyResult(e, model, true, true, status, ms,
                    "score", Array.Empty<ClassifyLabel>(), score, null, null);
            },
            onException: (ms, msg) =>
                new ClassifyResult(e, model, false, true, null, ms,
                    "score", Array.Empty<ClassifyLabel>(), null, null, msg));
    }

    // classify and score share the same non-2xx mapping: an unsupported route is a
    // clean "not supported" result (exit 0); anything else surfaces the body as an error.
    private static ClassifyResult ClassifyError(string endpoint, string model, string mode, int status, long ms, string raw)
    {
        var unsupported = IsUnsupportedStatus(status);
        return new ClassifyResult(endpoint, model, unsupported, !unsupported, status, ms,
            mode, Array.Empty<ClassifyLabel>(), null,
            unsupported ? "not supported (vLLM classifier/score models only)" : null,
            unsupported ? null : Trunc(raw, 200));
    }

    // Pair each probability from a /classify result with its label name (using the
    // server's label_names when present, else synthetic LABEL_n), ordered by
    // descending probability. Always surfaces the predicted top label first.
    internal static ClassifyLabel[] BuildLabels(OpenAiClassifyData? data)
    {
        if (data == null) return Array.Empty<ClassifyLabel>();
        var probs = data.Probs ?? Array.Empty<double>();
        if (probs.Length == 0)
            return data.Label != null ? new[] { new ClassifyLabel(data.Label, 1.0) } : Array.Empty<ClassifyLabel>();
        var names = data.LabelNames;
        return probs
            .Select((p, i) => new ClassifyLabel(
                names != null && i < names.Length ? names[i] : $"LABEL_{i}", p))
            .OrderByDescending(l => l.Probability)
            .ToArray();
    }

    // Infer an audio content type from a file extension, mirroring MimeFromExtension
    // for images. Used to label the multipart 'file' part so the server can pick the
    // right decoder. Unknown extensions fall back to the generic binary type.
    internal static string AudioMimeFromExtension(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".wav" => "audio/wav",
            ".mp3" => "audio/mpeg",
            ".m4a" => "audio/mp4",
            ".flac" => "audio/flac",
            ".ogg" => "audio/ogg",
            ".webm" => "audio/webm",
            _ => "application/octet-stream",
        };
    }

    // Build the multipart/form-data body for a transcription request: a binary
    // 'file' part (labelled with the inferred audio content type + original
    // filename) plus a 'model' field. Factored out so a test can assert the parts
    // without a live server. The caller owns disposing the returned content.
    internal static MultipartFormDataContent BuildTranscriptionContent(byte[] audio, string fileName, string model)
    {
        var content = new MultipartFormDataContent();
        var filePart = new ByteArrayContent(audio);
        filePart.Headers.ContentType = new MediaTypeHeaderValue(AudioMimeFromExtension(fileName));
        content.Add(filePart, "file", fileName);
        content.Add(new StringContent(model), "model");
        return content;
    }

    // --- transcribe: speech-to-text (POST /v1/audio/transcriptions, multipart) ---
    // Doesn't fit the JSON PostJsonAsync skeleton (the body is multipart/form-data),
    // but keeps the same timing / try-catch / unsupported-status mapping shape.
    public static async Task<TranscribeResult> TranscribeAsync(
        HttpClient http, string endpoint, string model, byte[] audio, string fileName, string source, CancellationToken ct)
    {
        var e = Normalize(endpoint);
        var sw = Stopwatch.StartNew();
        var url = $"{e}/v1/audio/transcriptions";
        using var content = BuildTranscriptionContent(audio, fileName, model);
        RawSink.RequestDescription(Post, url,
            $"multipart/form-data: model={model}, file={fileName} ({audio.Length} bytes)");
        var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
        try
        {
            using var res = await http.SendAsync(req, ct);
            sw.Stop();
            var raw = await res.Content.ReadAsStringAsync(ct);
            var status = (int)res.StatusCode;
            RawSink.Response(status, raw);
            if (!res.IsSuccessStatusCode)
            {
                var unsupported = IsUnsupportedStatus(status);
                return new TranscribeResult(e, model, unsupported, !unsupported, status, sw.ElapsedMilliseconds,
                    source, 0, null, null,
                    unsupported ? "not supported by this endpoint (no /v1/audio/transcriptions route)" : null,
                    unsupported ? null : Trunc(raw, 200));
            }
            var resp = JsonSerializer.Deserialize(raw, JsonContext.Default.OpenAiTranscriptionResponse);
            var text = resp?.Text ?? "";
            return new TranscribeResult(e, model, true, true, status, sw.ElapsedMilliseconds,
                source, text.Length, Trunc(text, 160), resp?.Duration, null, null);
        }
        catch (Exception ex)
        {
            sw.Stop();
            RawSink.ResponseFailed(ex.Message);
            return new TranscribeResult(e, model, false, true, null, sw.ElapsedMilliseconds,
                source, 0, null, null, null, ex.Message);
        }
    }

    // --- speak: text-to-speech (POST /v1/audio/speech, binary audio response) ---
    // The response body is audio bytes, not JSON, so this also can't use the JSON
    // skeleton. When outputPath is set the bytes are written there (a write failure
    // surfaces as a ConfigException -> exit 78); otherwise only metadata is reported.
    public static async Task<SpeakResult> SpeakAsync(
        HttpClient http, string endpoint, OpenAiSpeechRequest body,
        string? outputPath, CancellationToken ct)
    {
        var e = Normalize(endpoint);
        var sw = Stopwatch.StartNew();
        var json = JsonSerializer.Serialize(body, JsonContext.Default.OpenAiSpeechRequest);
        var url = $"{e}/v1/audio/speech";
        var req = BuildJsonPost(url, json);
        try
        {
            using var res = await http.SendAsync(req, ct);
            sw.Stop();
            var status = (int)res.StatusCode;
            if (!res.IsSuccessStatusCode)
            {
                var raw = await res.Content.ReadAsStringAsync(ct);
                RawSink.Response(status, raw);
                var unsupported = IsUnsupportedStatus(status);
                return new SpeakResult(e, body.Model, unsupported, !unsupported, status, sw.ElapsedMilliseconds,
                    body.Voice, body.ResponseFormat, null, 0, null,
                    unsupported ? "not supported by this endpoint (no /v1/audio/speech route)" : null,
                    unsupported ? null : Trunc(raw, 200));
            }
            var bytes = await res.Content.ReadAsByteArrayAsync(ct);
            var contentType = res.Content.Headers.ContentType?.ToString();
            RawSink.ResponseSummary(status, $"binary audio: content-type={contentType ?? "?"}, {bytes.Length} bytes");
            if (outputPath != null) WriteAudioFile(outputPath, bytes);
            var note = outputPath == null
                ? "no -o/--output given; audio not written (metadata only)"
                : null;
            return new SpeakResult(e, body.Model, true, true, status, sw.ElapsedMilliseconds,
                body.Voice, body.ResponseFormat, contentType, bytes.Length, outputPath, note, null);
        }
        catch (Exception ex)
        {
            sw.Stop();
            RawSink.ResponseFailed(ex.Message);
            return new SpeakResult(e, body.Model, false, true, null, sw.ElapsedMilliseconds,
                body.Voice, body.ResponseFormat, null, 0, outputPath, null, ex.Message);
        }
    }

    // Write synthesized audio to disk, translating the unwritable-file exceptions
    // into a ConfigException so the command surfaces a clean config error (exit 78)
    // rather than an unhandled raw exception, matching the @file read convention.
    internal static void WriteAudioFile(string path, byte[] bytes)
    {
        try
        {
            File.WriteAllBytes(path, bytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            throw new ConfigException($"could not write audio to '{path}': {ex.Message}",
                "Check the path and permissions, or omit -o/--output to report metadata only");
        }
    }
}

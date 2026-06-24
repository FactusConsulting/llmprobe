using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace LlmProbe;

public static class Probe
{
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

    public static async Task<TestResult> ChatTestAsync(
        HttpClient http, string endpoint, string model, string prompt, int maxTokens, CancellationToken ct)
    {
        var e = Normalize(endpoint);
        var sw = Stopwatch.StartNew();
        var body = new OpenAiChatRequest(model,
            new[] { new OpenAiMessage("user", prompt) }, MaxTokens: maxTokens, Temperature: 0);
        var json = JsonSerializer.Serialize(body, JsonContext.Default.OpenAiChatRequest);
        var req = new HttpRequestMessage(HttpMethod.Post, $"{e}/v1/chat/completions")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        try
        {
            using var res = await http.SendAsync(req, ct);
            sw.Stop();
            var raw = await res.Content.ReadAsStringAsync(ct);
            if (!res.IsSuccessStatusCode)
                return new TestResult(e, model, false, (int)res.StatusCode, sw.ElapsedMilliseconds, 0, 0, 0, null, null, Trunc(raw, 200));
            var resp = JsonSerializer.Deserialize(raw, JsonContext.Default.OpenAiChatResponse);
            var choice = resp?.Choices.FirstOrDefault();
            var content = choice?.Message?.Content ?? "";
            return new TestResult(e, model, true, (int)res.StatusCode, sw.ElapsedMilliseconds,
                resp?.Usage?.PromptTokens ?? 0,
                resp?.Usage?.CompletionTokens ?? 0,
                resp?.Usage?.TotalTokens ?? 0,
                choice?.FinishReason,
                Trunc(content, 160),
                null);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new TestResult(e, model, false, null, sw.ElapsedMilliseconds, 0, 0, 0, null, null, ex.Message);
        }
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
        var req = new HttpRequestMessage(HttpMethod.Post, $"{e}/v1/chat/completions")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        try
        {
            using var res = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!res.IsSuccessStatusCode)
            {
                sw.Stop();
                var err = await res.Content.ReadAsStringAsync(ct);
                return new StreamResult(e, model, false, 0, sw.ElapsedMilliseconds, 0, 0, 0, null, Trunc(err, 200));
            }
            await using var stream = await res.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream);
            string? line;
            while ((line = await reader.ReadLineAsync(ct)) != null)
            {
                if (!line.StartsWith("data:")) continue;
                var payload = line[5..].Trim();
                if (payload == "[DONE]") break;
                if (payload.Length == 0) continue;
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

    public static async Task<EmbedResult> EmbedAsync(
        HttpClient http, string endpoint, string model, string[] input, CancellationToken ct)
    {
        var e = Normalize(endpoint);
        var sw = Stopwatch.StartNew();
        var body = new OpenAiEmbeddingRequest(model, input);
        var json = JsonSerializer.Serialize(body, JsonContext.Default.OpenAiEmbeddingRequest);
        var req = new HttpRequestMessage(HttpMethod.Post, $"{e}/v1/embeddings")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        try
        {
            using var res = await http.SendAsync(req, ct);
            sw.Stop();
            var raw = await res.Content.ReadAsStringAsync(ct);
            if (!res.IsSuccessStatusCode)
                return new EmbedResult(e, model, false, (int)res.StatusCode, sw.ElapsedMilliseconds, input.Length, 0, 0, 0, 0, Trunc(raw, 200));
            var resp = JsonSerializer.Deserialize(raw, JsonContext.Default.OpenAiEmbeddingResponse);
            var first = resp?.Data.FirstOrDefault();
            var vec = first?.Embedding ?? Array.Empty<float>();
            return new EmbedResult(e, model, true, (int)res.StatusCode, sw.ElapsedMilliseconds,
                input.Length,
                vec.Length,
                L2Norm(vec),
                resp?.Usage?.PromptTokens ?? 0,
                resp?.Usage?.TotalTokens ?? 0,
                null);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new EmbedResult(e, model, false, null, sw.ElapsedMilliseconds, input.Length, 0, 0, 0, 0, ex.Message);
        }
    }

    public static async Task<RerankResult> RerankAsync(
        HttpClient http, string endpoint, string model, string query, string[] documents, int? topN, CancellationToken ct)
    {
        var e = Normalize(endpoint);
        var sw = Stopwatch.StartNew();
        var body = new OpenAiRerankRequest(model, query, documents, topN);
        var json = JsonSerializer.Serialize(body, JsonContext.Default.OpenAiRerankRequest);
        var req = new HttpRequestMessage(HttpMethod.Post, $"{e}/v1/rerank")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        try
        {
            using var res = await http.SendAsync(req, ct);
            sw.Stop();
            var raw = await res.Content.ReadAsStringAsync(ct);
            if (!res.IsSuccessStatusCode)
                return new RerankResult(e, model, false, (int)res.StatusCode, sw.ElapsedMilliseconds, documents.Length, Array.Empty<RerankItem>(), 0, Trunc(raw, 200));
            var resp = JsonSerializer.Deserialize(raw, JsonContext.Default.OpenAiRerankResponse);
            var ranking = (resp?.Results ?? Array.Empty<OpenAiRerankResultEntry>())
                .Select(r => new RerankItem(
                    r.Index,
                    r.RelevanceScore,
                    // Prefer the doc text echoed by the server; fall back to the input we sent.
                    Trunc(r.Document?.Text ?? (r.Index >= 0 && r.Index < documents.Length ? documents[r.Index] : ""), 80)))
                .ToArray();
            return new RerankResult(e, model, true, (int)res.StatusCode, sw.ElapsedMilliseconds,
                documents.Length, ranking, resp?.Usage?.TotalTokens ?? 0, null);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new RerankResult(e, model, false, null, sw.ElapsedMilliseconds, documents.Length, Array.Empty<RerankItem>(), 0, ex.Message);
        }
    }

    public static async Task<ReasoningResult> ReasoningAsync(
        HttpClient http, string endpoint, string model, string prompt, int maxTokens, CancellationToken ct)
    {
        var e = Normalize(endpoint);
        var sw = Stopwatch.StartNew();
        var body = new OpenAiChatRequest(model,
            new[] { new OpenAiMessage("user", prompt) }, MaxTokens: maxTokens, Temperature: 0);
        var json = JsonSerializer.Serialize(body, JsonContext.Default.OpenAiChatRequest);
        var req = new HttpRequestMessage(HttpMethod.Post, $"{e}/v1/chat/completions")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        try
        {
            using var res = await http.SendAsync(req, ct);
            sw.Stop();
            var raw = await res.Content.ReadAsStringAsync(ct);
            if (!res.IsSuccessStatusCode)
                return new ReasoningResult(e, model, false, (int)res.StatusCode, sw.ElapsedMilliseconds,
                    false, null, 0, 0, 0, null, 0, 0, 0, null, null, Trunc(raw, 200));
            var resp = JsonSerializer.Deserialize(raw, JsonContext.Default.OpenAiReasoningResponse);
            var choice = resp?.Choices.FirstOrDefault();
            var content = choice?.Message?.Content ?? "";
            var reasoningContent = choice?.Message?.ReasoningContent;
            var reasoningTokens = resp?.Usage?.CompletionTokensDetails?.ReasoningTokens ?? 0;
            var (detected, channel, reasoningText, answer) = DetectReasoning(content, reasoningContent, reasoningTokens);
            return new ReasoningResult(e, model, true, (int)res.StatusCode, sw.ElapsedMilliseconds,
                detected, channel, reasoningTokens,
                reasoningText.Length, answer.Length,
                choice?.FinishReason,
                resp?.Usage?.PromptTokens ?? 0,
                resp?.Usage?.CompletionTokens ?? 0,
                resp?.Usage?.TotalTokens ?? 0,
                Trunc(answer.Trim(), 160),
                "TTFT split between thinking and answer needs streaming (use 'stream').",
                null);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new ReasoningResult(e, model, false, null, sw.ElapsedMilliseconds,
                false, null, 0, 0, 0, null, 0, 0, 0, null, null, ex.Message);
        }
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

    public static async Task<StructuredResult> StructuredAsync(
        HttpClient http, string endpoint, string model, string prompt, int maxTokens, CancellationToken ct)
    {
        var e = Normalize(endpoint);
        var sw = Stopwatch.StartNew();
        var schema = JsonSerializer.Deserialize<JsonElement>("""
            {"type":"object","properties":{"name":{"type":"string"},"age":{"type":"integer"}},"required":["name","age"],"additionalProperties":false}
            """);
        var responseFormat = new OpenAiResponseFormat("json_schema",
            new OpenAiJsonSchema("person", schema, Strict: true));
        var body = new OpenAiStructuredRequest(model,
            new[] { new OpenAiMessage("user", prompt) }, responseFormat, MaxTokens: maxTokens, Temperature: 0);
        var json = JsonSerializer.Serialize(body, JsonContext.Default.OpenAiStructuredRequest);
        var req = new HttpRequestMessage(HttpMethod.Post, $"{e}/v1/chat/completions")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        try
        {
            using var res = await http.SendAsync(req, ct);
            sw.Stop();
            var raw = await res.Content.ReadAsStringAsync(ct);
            if (!res.IsSuccessStatusCode)
                return new StructuredResult(e, model, false, (int)res.StatusCode, sw.ElapsedMilliseconds,
                    false, false, Array.Empty<string>(), null, null, 0, 0, 0,
                    "endpoint rejected the request (may not support response_format json_schema)", Trunc(raw, 200));
            var resp = JsonSerializer.Deserialize(raw, JsonContext.Default.OpenAiChatResponse);
            var choice = resp?.Choices.FirstOrDefault();
            var content = choice?.Message?.Content ?? "";
            var (parsed, conformant, violations, preview) = ValidatePerson(content);
            string? note = parsed
                ? (conformant ? null : "returned JSON did not match the requested schema")
                : "response was not valid JSON (endpoint may not support structured output)";
            return new StructuredResult(e, model, true, (int)res.StatusCode, sw.ElapsedMilliseconds,
                parsed, conformant, violations, preview, choice?.FinishReason,
                resp?.Usage?.PromptTokens ?? 0,
                resp?.Usage?.CompletionTokens ?? 0,
                resp?.Usage?.TotalTokens ?? 0,
                note, null);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new StructuredResult(e, model, false, null, sw.ElapsedMilliseconds,
                false, false, Array.Empty<string>(), null, null, 0, 0, 0, null, ex.Message);
        }
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

        var preview = Trunc(root.GetRawText().Replace("\n", " ").Replace("\r", ""), 160);
        return (true, violations.Count == 0, violations.ToArray(), preview);
    }

    public static async Task<VisionResult> VisionAsync(
        HttpClient http, string endpoint, string model, string imageUrl, string imageSource,
        string prompt, int maxTokens, CancellationToken ct)
    {
        var e = Normalize(endpoint);
        var sw = Stopwatch.StartNew();
        var content = new[]
        {
            new OpenAiContentPart("text", Text: prompt),
            new OpenAiContentPart("image_url", ImageUrl: new OpenAiImageUrl(imageUrl)),
        };
        var body = new OpenAiVisionRequest(model,
            new[] { new OpenAiVisionMessage("user", content) }, MaxTokens: maxTokens, Temperature: 0);
        var json = JsonSerializer.Serialize(body, JsonContext.Default.OpenAiVisionRequest);
        var req = new HttpRequestMessage(HttpMethod.Post, $"{e}/v1/chat/completions")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        try
        {
            using var res = await http.SendAsync(req, ct);
            sw.Stop();
            var raw = await res.Content.ReadAsStringAsync(ct);
            if (!res.IsSuccessStatusCode)
                return new VisionResult(e, model, false, (int)res.StatusCode, sw.ElapsedMilliseconds,
                    false, imageSource, null, 0, 0, 0, null, Trunc(raw, 200));
            var resp = JsonSerializer.Deserialize(raw, JsonContext.Default.OpenAiChatResponse);
            var choice = resp?.Choices.FirstOrDefault();
            var text = choice?.Message?.Content ?? "";
            return new VisionResult(e, model, true, (int)res.StatusCode, sw.ElapsedMilliseconds,
                true, imageSource, choice?.FinishReason,
                resp?.Usage?.PromptTokens ?? 0,
                resp?.Usage?.CompletionTokens ?? 0,
                resp?.Usage?.TotalTokens ?? 0,
                Trunc(text, 160), null);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new VisionResult(e, model, false, null, sw.ElapsedMilliseconds,
                false, imageSource, null, 0, 0, 0, null, ex.Message);
        }
    }

    public static async Task<ToolsResult> ToolsAsync(
        HttpClient http, string endpoint, string model, string prompt, int maxTokens, CancellationToken ct)
    {
        var e = Normalize(endpoint);
        var sw = Stopwatch.StartNew();
        var parameters = JsonSerializer.Deserialize<JsonElement>("""
            {"type":"object","properties":{"location":{"type":"string","description":"City name, e.g. Copenhagen"}},"required":["location"]}
            """);
        var tool = new OpenAiToolDef("function",
            new OpenAiFunctionDef("get_weather", "Get the current weather for a location.", parameters));
        var body = new OpenAiToolsRequest(model,
            new[] { new OpenAiMessage("user", prompt) },
            new[] { tool }, ToolChoice: "auto", MaxTokens: maxTokens, Temperature: 0);
        var json = JsonSerializer.Serialize(body, JsonContext.Default.OpenAiToolsRequest);
        var req = new HttpRequestMessage(HttpMethod.Post, $"{e}/v1/chat/completions")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        try
        {
            using var res = await http.SendAsync(req, ct);
            sw.Stop();
            var raw = await res.Content.ReadAsStringAsync(ct);
            if (!res.IsSuccessStatusCode)
                return new ToolsResult(e, model, false, (int)res.StatusCode, sw.ElapsedMilliseconds,
                    false, null, null, null, 0, 0, 0, null, Trunc(raw, 200));
            var resp = JsonSerializer.Deserialize(raw, JsonContext.Default.OpenAiToolsResponse);
            var choice = resp?.Choices.FirstOrDefault();
            var call = choice?.Message?.ToolCalls?.FirstOrDefault();
            var called = call?.Function != null;
            return new ToolsResult(e, model, true, (int)res.StatusCode, sw.ElapsedMilliseconds,
                called, call?.Function?.Name, call?.Function?.Arguments, choice?.FinishReason,
                resp?.Usage?.PromptTokens ?? 0,
                resp?.Usage?.CompletionTokens ?? 0,
                resp?.Usage?.TotalTokens ?? 0,
                Trunc(choice?.Message?.Content ?? "", 160), null);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new ToolsResult(e, model, false, null, sw.ElapsedMilliseconds,
                false, null, null, null, 0, 0, 0, null, ex.Message);
        }
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
                outp.AddRange(SplitLines(File.ReadAllText(v[1..])));
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
}

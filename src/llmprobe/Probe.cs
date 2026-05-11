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

    private static int ApproxTokens(string text) => Math.Max(1, text.Length / 4);
    private static string Trunc(string s, int n) => s.Length > n ? s[..n] + "…" : s;
}

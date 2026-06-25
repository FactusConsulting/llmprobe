using Xunit;

namespace LlmProbe.Tests;

public class ProbeNormalizeTests
{
    [Theory]
    [InlineData("localhost:11434", "http://localhost:11434")]
    [InlineData("infer.local:8080", "http://infer.local:8080")]
    [InlineData("192.168.1.10:8000", "http://192.168.1.10:8000")]
    public void Normalize_AddsHttpScheme_WhenMissing(string input, string expected)
    {
        Assert.Equal(expected, Probe.Normalize(input));
    }

    [Theory]
    [InlineData("https://api.openai.com")]
    [InlineData("http://infer:8080")]
    [InlineData("HTTPS://API.OPENAI.COM")]
    public void Normalize_PreservesExistingScheme(string input)
    {
        var normalized = Probe.Normalize(input);
        Assert.StartsWith(input[..4].ToLowerInvariant(), normalized, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("http://infer:8080/", "http://infer:8080")]
    [InlineData("https://api.openai.com/v1/", "https://api.openai.com/v1")]
    [InlineData("http://localhost/", "http://localhost")]
    public void Normalize_StripsTrailingSlash(string input, string expected)
    {
        Assert.Equal(expected, Probe.Normalize(input));
    }

    [Fact]
    public void Normalize_KeepsPathSegments()
    {
        Assert.Equal("https://api.openai.com/v1", Probe.Normalize("https://api.openai.com/v1"));
    }
}

public class JsonSerializationTests
{
    [Fact]
    public void PingResult_SerializesWithSnakeCaseFields()
    {
        var result = new PingResult("http://infer:8080", true, 200, 42, "uvicorn", null);
        var json = System.Text.Json.JsonSerializer.Serialize(result, JsonContext.Default.PingResult);

        Assert.Contains("\"endpoint\"", json);
        Assert.Contains("\"reachable\"", json);
        Assert.Contains("\"status_code\"", json);
        Assert.Contains("\"latency_ms\"", json);
        Assert.Contains("\"server_header\"", json);
        Assert.DoesNotContain("\"Endpoint\"", json);
        Assert.DoesNotContain("\"statusCode\"", json);
    }

    [Fact]
    public void PingResult_OmitsNullFields()
    {
        var result = new PingResult("http://infer:8080", true, 200, 42, null, null);
        var json = System.Text.Json.JsonSerializer.Serialize(result, JsonContext.Default.PingResult);

        Assert.DoesNotContain("\"server_header\"", json);
        Assert.DoesNotContain("\"error\"", json);
    }

    [Fact]
    public void StreamResult_HasStableFieldNames()
    {
        var result = new StreamResult("http://infer:8080", "gemma4-26b", true,
            38, 512, 27, 115, 242.7, "stop", null);
        var json = System.Text.Json.JsonSerializer.Serialize(result, JsonContext.Default.StreamResult);

        Assert.Contains("\"ttft_ms\"", json);
        Assert.Contains("\"total_ms\"", json);
        Assert.Contains("\"output_tokens_approx\"", json);
        Assert.Contains("\"tokens_per_sec\"", json);
        Assert.Contains("\"finish_reason\"", json);
    }

    [Fact]
    public void EmbedResult_HasStableFieldNames()
    {
        var result = new EmbedResult("http://infer:8080", "qwen3-embedding-8b", true,
            200, 21, 1, 4096, 1.0, 7, 7, null);
        var json = System.Text.Json.JsonSerializer.Serialize(result, JsonContext.Default.EmbedResult);

        Assert.Contains("\"dimensions\"", json);
        Assert.Contains("\"norm\"", json);
        Assert.Contains("\"inputs\"", json);
        Assert.Contains("\"total_tokens\"", json);
    }

    [Fact]
    public void RerankResult_HasStableFieldNames()
    {
        var result = new RerankResult("http://infer:8080", "qwen3-reranker-8b", true,
            200, 33, 2, new[] { new RerankItem(1, 0.92, "doc b"), new RerankItem(0, 0.10, "doc a") }, 12, null);
        var json = System.Text.Json.JsonSerializer.Serialize(result, JsonContext.Default.RerankResult);

        Assert.Contains("\"ranking\"", json);
        Assert.Contains("\"score\"", json);
        Assert.Contains("\"document_preview\"", json);
        Assert.Contains("\"index\"", json);
    }
}

public class ReasoningTests
{
    [Fact]
    public void ReasoningResult_HasStableFieldNames()
    {
        var result = new ReasoningResult("http://infer:8080", "deepseek-r1", true,
            200, 1500, true, "reasoning_content", 240, 980, 12, "stop", 30, 250, 280, "9", null, null);
        var json = System.Text.Json.JsonSerializer.Serialize(result, JsonContext.Default.ReasoningResult);

        Assert.Contains("\"reasoning_detected\"", json);
        Assert.Contains("\"reasoning_channel\"", json);
        Assert.Contains("\"reasoning_tokens\"", json);
        Assert.Contains("\"answer_preview\"", json);
        Assert.DoesNotContain("\"ReasoningDetected\"", json);
    }

    [Fact]
    public void DetectReasoning_DetectsReasoningContentChannel()
    {
        var (detected, channel, reasoning, answer) = Probe.DetectReasoning("The answer is 9.", "let me think...", 0);
        Assert.True(detected);
        Assert.Equal("reasoning_content", channel);
        Assert.Equal("let me think...", reasoning);
        Assert.Equal("The answer is 9.", answer);
    }

    [Fact]
    public void DetectReasoning_ExtractsThinkTagAndStripsItFromAnswer()
    {
        var (detected, channel, reasoning, answer) =
            Probe.DetectReasoning("<think>17 - 8 = 9</think>The answer is 9.", null, 0);
        Assert.True(detected);
        Assert.Equal("think_tag", channel);
        Assert.Equal("17 - 8 = 9", reasoning);
        Assert.Equal("The answer is 9.", answer);
    }

    [Fact]
    public void DetectReasoning_FallsBackToReasoningTokenCount()
    {
        var (detected, channel, _, answer) = Probe.DetectReasoning("9", null, 240);
        Assert.True(detected);
        Assert.Equal("reasoning_tokens", channel);
        Assert.Equal("9", answer);
    }

    [Fact]
    public void DetectReasoning_ReportsNoReasoningForPlainAnswer()
    {
        var (detected, channel, _, _) = Probe.DetectReasoning("9", null, 0);
        Assert.False(detected);
        Assert.Null(channel);
    }

    [Fact]
    public void OpenAiReasoningResponse_ParsesReasoningTokensFromUsageDetails()
    {
        const string raw = """
        {
          "choices": [{
            "message": { "role": "assistant", "content": "9", "reasoning_content": "17 minus 8 is 9" },
            "finish_reason": "stop"
          }],
          "usage": {
            "prompt_tokens": 30, "completion_tokens": 250, "total_tokens": 280,
            "completion_tokens_details": { "reasoning_tokens": 240 }
          }
        }
        """;
        var resp = System.Text.Json.JsonSerializer.Deserialize(raw, JsonContext.Default.OpenAiReasoningResponse);
        Assert.Equal("17 minus 8 is 9", resp!.Choices[0].Message!.ReasoningContent);
        Assert.Equal(240, resp.Usage!.CompletionTokensDetails!.ReasoningTokens);
    }
}

public class StructuredTests
{
    [Fact]
    public void StructuredResult_HasStableFieldNames()
    {
        var result = new StructuredResult("http://infer:8080", "gpt-4o", true,
            200, 200, true, true, Array.Empty<string>(), "{\"name\":\"Alice\",\"age\":30}", "stop", 20, 12, 32, null, null);
        var json = System.Text.Json.JsonSerializer.Serialize(result, JsonContext.Default.StructuredResult);

        Assert.Contains("\"parsed_as_json\"", json);
        Assert.Contains("\"schema_conformant\"", json);
        Assert.Contains("\"schema_violations\"", json);
        Assert.Contains("\"object_preview\"", json);
        Assert.DoesNotContain("\"ParsedAsJson\"", json);
    }

    [Fact]
    public void ValidatePerson_AcceptsConformantObject()
    {
        var (parsed, conformant, violations, preview) = Probe.ValidatePerson("{\"name\":\"Alice\",\"age\":30}");
        Assert.True(parsed);
        Assert.True(conformant);
        Assert.Empty(violations);
        Assert.Contains("Alice", preview!);
    }

    [Fact]
    public void ValidatePerson_FlagsMissingAndMistypedFields()
    {
        var (parsed, conformant, violations, _) = Probe.ValidatePerson("{\"name\":42}");
        Assert.True(parsed);
        Assert.False(conformant);
        Assert.Contains(violations, v => v.Contains("'name'"));
        Assert.Contains(violations, v => v.Contains("age"));
    }

    [Fact]
    public void ValidatePerson_RejectsNonIntegerAge()
    {
        var (parsed, conformant, violations, _) = Probe.ValidatePerson("{\"name\":\"Bob\",\"age\":3.5}");
        Assert.True(parsed);
        Assert.False(conformant);
        Assert.Contains(violations, v => v.Contains("age"));
    }

    [Fact]
    public void ValidatePerson_ReportsNonJsonAsNotParsed()
    {
        var (parsed, conformant, _, _) = Probe.ValidatePerson("I cannot do that.");
        Assert.False(parsed);
        Assert.False(conformant);
    }

    [Fact]
    public void ValidatePerson_FlagsNonObjectRoot()
    {
        var (parsed, conformant, violations, _) = Probe.ValidatePerson("[1,2,3]");
        Assert.True(parsed);
        Assert.False(conformant);
        Assert.Contains(violations, v => v.Contains("expected object"));
    }

    [Fact]
    public void ValidatePerson_RejectsExtraProperties()
    {
        var (parsed, conformant, violations, _) =
            Probe.ValidatePerson("{\"name\":\"Alice\",\"age\":30,\"email\":\"a@x.dk\"}");
        Assert.True(parsed);
        Assert.False(conformant);
        Assert.Contains(violations, v => v.Contains("unexpected property 'email'"));
    }

    [Fact]
    public void ValidatePerson_AcceptsExactSchemaWithoutExtras()
    {
        var (_, conformant, violations, _) =
            Probe.ValidatePerson("{\"name\":\"Alice\",\"age\":30}");
        Assert.True(conformant);
        Assert.Empty(violations);
    }
}

public class VisionTests
{
    [Fact]
    public void VisionResult_HasStableFieldNames()
    {
        var result = new VisionResult("http://infer:8080", "qwen2.5-vl", true,
            200, 88, true, "https://example.com/cat.png", "stop", 1024, 3, 1027, "cat", null);
        var json = System.Text.Json.JsonSerializer.Serialize(result, JsonContext.Default.VisionResult);

        Assert.Contains("\"image_accepted\"", json);
        Assert.Contains("\"image_source\"", json);
        Assert.Contains("\"finish_reason\"", json);
        Assert.Contains("\"completion_tokens\"", json);
        Assert.Contains("\"response_preview\"", json);
        Assert.DoesNotContain("\"ImageAccepted\"", json);
    }

    [Fact]
    public void ResolveImage_PassesThroughHttpUrls()
    {
        var (url, source) = Probe.ResolveImage("https://example.com/cat.png");
        Assert.Equal("https://example.com/cat.png", url);
        Assert.Equal("https://example.com/cat.png", source);
    }

    [Fact]
    public void ResolveImage_InlinesLocalFileAsBase64DataUrl()
    {
        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 }; // PNG magic-ish bytes
        var path = Path.Combine(Path.GetTempPath(), $"llmprobe-test-{Guid.NewGuid():N}.png");
        File.WriteAllBytes(path, bytes);
        try
        {
            var (url, source) = Probe.ResolveImage(path);
            Assert.Equal($"data:image/png;base64,{Convert.ToBase64String(bytes)}", url);
            Assert.Equal(path, source);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ResolveImage_StripsLeadingAtFromLocalPath()
    {
        var bytes = new byte[] { 0xFF, 0xD8, 0xFF };
        var path = Path.Combine(Path.GetTempPath(), $"llmprobe-test-{Guid.NewGuid():N}.jpg");
        File.WriteAllBytes(path, bytes);
        try
        {
            var (url, source) = Probe.ResolveImage("@" + path);
            Assert.StartsWith("data:image/jpeg;base64,", url);
            Assert.Equal(path, source);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("a.png", "image/png")]
    [InlineData("a.PNG", "image/png")]
    [InlineData("a.jpg", "image/jpeg")]
    [InlineData("a.jpeg", "image/jpeg")]
    [InlineData("a.webp", "image/webp")]
    [InlineData("a.gif", "image/gif")]
    [InlineData("a.bmp", "application/octet-stream")]
    public void MimeFromExtension_InfersFromExtension(string path, string expected)
    {
        Assert.Equal(expected, Probe.MimeFromExtension(path));
    }
}

public class ToolsParsingTests
{
    [Fact]
    public void ToolsResult_HasStableFieldNames()
    {
        var result = new ToolsResult("http://infer:8080", "gpt-4o", true,
            200, 120, true, "get_weather", "{\"location\":\"Copenhagen\"}", "tool_calls",
            50, 12, 62, null, null);
        var json = System.Text.Json.JsonSerializer.Serialize(result, JsonContext.Default.ToolsResult);

        Assert.Contains("\"tool_called\"", json);
        Assert.Contains("\"function_name\"", json);
        Assert.Contains("\"function_arguments\"", json);
        Assert.Contains("\"finish_reason\"", json);
        Assert.DoesNotContain("\"ToolCalled\"", json);
    }

    [Fact]
    public void OpenAiToolsResponse_ParsesToolCall()
    {
        const string raw = """
        {
          "choices": [{
            "message": {
              "role": "assistant",
              "content": null,
              "tool_calls": [{
                "id": "call_1",
                "type": "function",
                "function": { "name": "get_weather", "arguments": "{\"location\":\"Copenhagen\"}" }
              }]
            },
            "finish_reason": "tool_calls"
          }],
          "usage": { "prompt_tokens": 50, "completion_tokens": 12, "total_tokens": 62 }
        }
        """;
        var resp = System.Text.Json.JsonSerializer.Deserialize(raw, JsonContext.Default.OpenAiToolsResponse);
        var call = resp!.Choices[0].Message!.ToolCalls![0];

        Assert.Equal("tool_calls", resp.Choices[0].FinishReason);
        Assert.Equal("get_weather", call.Function!.Name);
        Assert.Equal("{\"location\":\"Copenhagen\"}", call.Function.Arguments);
        Assert.Equal(62, resp.Usage!.TotalTokens);
    }

    [Fact]
    public void OpenAiToolsResponse_ParsesDirectAnswerWithoutToolCall()
    {
        const string raw = """
        {
          "choices": [{
            "message": { "role": "assistant", "content": "It is sunny." },
            "finish_reason": "stop"
          }]
        }
        """;
        var resp = System.Text.Json.JsonSerializer.Deserialize(raw, JsonContext.Default.OpenAiToolsResponse);
        var msg = resp!.Choices[0].Message!;

        Assert.Null(msg.ToolCalls);
        Assert.Equal("It is sunny.", msg.Content);
    }
}

public class InputExpansionTests
{
    [Fact]
    public void ExpandLines_TakesLiteralValuesAsIs()
    {
        var result = Probe.ExpandLines(new[] { "doc one", "doc two" });
        Assert.Equal(new[] { "doc one", "doc two" }, result);
    }

    [Fact]
    public void SplitLines_TrimsAndDropsBlankLines()
    {
        var result = Probe.SplitLines("a\r\n\n  b  \nc\n").ToArray();
        Assert.Equal(new[] { "a", "b", "c" }, result);
    }

    [Fact]
    public void L2Norm_ComputesEuclideanLength()
    {
        Assert.Equal(5.0, Probe.L2Norm(new[] { 3f, 4f }), 5);
    }

    [Fact]
    public void ReadAtFile_ReturnsContents_ForReadableFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"llmprobe-test-{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, "hello world");
        try { Assert.Equal("hello world", Probe.ReadAtFile(path)); }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ReadAtFile_ThrowsConfigException_ForMissingFile()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"llmprobe-missing-{Guid.NewGuid():N}.txt");
        var ex = Assert.Throws<ConfigException>(() => Probe.ReadAtFile(missing));
        Assert.Contains(missing, ex.Message);
        Assert.NotNull(ex.Hint);
    }

    [Fact]
    public void ExpandLines_ThrowsConfigException_ForMissingAtFile()
    {
        var missing = "@" + Path.Combine(Path.GetTempPath(), $"llmprobe-missing-{Guid.NewGuid():N}.txt");
        Assert.Throws<ConfigException>(() => Probe.ExpandLines(new[] { missing }));
    }

    [Fact]
    public void ExpandLines_ReadsAtFile_LineByLine()
    {
        var path = Path.Combine(Path.GetTempPath(), $"llmprobe-test-{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, "alpha\nbeta\n\ngamma\n");
        try
        {
            var result = Probe.ExpandLines(new[] { "@" + path });
            Assert.Equal(new[] { "alpha", "beta", "gamma" }, result);
        }
        finally { File.Delete(path); }
    }
}

public class SupportDetectionTests
{
    [Theory]
    [InlineData(400)]
    [InlineData(404)]
    [InlineData(405)]
    [InlineData(501)]
    public void IsUnsupportedStatus_TreatsMissingRoutesAsUnsupported(int status)
    {
        Assert.True(Probe.IsUnsupportedStatus(status));
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(503)]
    public void IsUnsupportedStatus_TreatsAuthAndServerErrorsAsSupportedButFailing(int status)
    {
        // 401/403/5xx mean the route exists but the request failed — not "unsupported".
        Assert.False(Probe.IsUnsupportedStatus(status));
    }
}

public class CompletionsTests
{
    [Fact]
    public void CompletionsResult_HasStableFieldNames()
    {
        var result = new CompletionsResult("http://infer:8080", "gpt-3.5-turbo-instruct", true, true,
            200, 42, 6, 3, 9, "stop", "Paris", null, null);
        var json = System.Text.Json.JsonSerializer.Serialize(result, JsonContext.Default.CompletionsResult);

        Assert.Contains("\"supported\"", json);
        Assert.Contains("\"text_preview\"", json);
        Assert.Contains("\"finish_reason\"", json);
        Assert.Contains("\"total_tokens\"", json);
        Assert.DoesNotContain("\"TextPreview\"", json);
    }

    [Fact]
    public void OpenAiCompletionsResponse_ParsesTextFromChoices()
    {
        const string raw = """
        {
          "choices": [{ "text": " Paris.", "finish_reason": "stop" }],
          "usage": { "prompt_tokens": 6, "completion_tokens": 3, "total_tokens": 9 }
        }
        """;
        var resp = System.Text.Json.JsonSerializer.Deserialize(raw, JsonContext.Default.OpenAiCompletionsResponse);
        Assert.Equal(" Paris.", resp!.Choices![0].Text);
        Assert.Equal("stop", resp.Choices[0].FinishReason);
        Assert.Equal(9, resp.Usage!.TotalTokens);
    }
}

public class InfillTests
{
    [Fact]
    public void InfillResult_HasStableFieldNames()
    {
        var result = new InfillResult("http://infer:8080", "qwen2.5-coder", true, true,
            200, 55, 10, 8, 18, "a + b", null, null);
        var json = System.Text.Json.JsonSerializer.Serialize(result, JsonContext.Default.InfillResult);

        Assert.Contains("\"content_preview\"", json);
        Assert.Contains("\"supported\"", json);
        Assert.DoesNotContain("\"ContentPreview\"", json);
    }

    [Fact]
    public void LlamaInfillResponse_ParsesContentAndTokenCounts()
    {
        const string raw = """
        { "content": "a + b", "tokens_evaluated": 10, "tokens_predicted": 8 }
        """;
        var resp = System.Text.Json.JsonSerializer.Deserialize(raw, JsonContext.Default.LlamaInfillResponse);
        Assert.Equal("a + b", resp!.Content);
        Assert.Equal(10, resp.TokensEvaluated);
        Assert.Equal(8, resp.TokensPredicted);
    }

    [Fact]
    public void LlamaInfillRequest_SerializesFimFieldNames()
    {
        var req = new LlamaInfillRequest("pre", "suf", Model: "m", NPredict: 64, Temperature: 0);
        var json = System.Text.Json.JsonSerializer.Serialize(req, JsonContext.Default.LlamaInfillRequest);
        Assert.Contains("\"input_prefix\"", json);
        Assert.Contains("\"input_suffix\"", json);
        Assert.Contains("\"n_predict\"", json);
    }
}

public class TokenizeTests
{
    [Fact]
    public void TokenizeResult_HasStableFieldNames()
    {
        var result = new TokenizeResult("http://infer:8080", "gpt-4o", true, true,
            200, 12, 9, new[] { 1, 2, 3 }, null, null);
        var json = System.Text.Json.JsonSerializer.Serialize(result, JsonContext.Default.TokenizeResult);

        Assert.Contains("\"token_count\"", json);
        Assert.Contains("\"first_tokens\"", json);
        Assert.DoesNotContain("\"TokenCount\"", json);
    }

    [Fact]
    public void OpenAiTokenizeResponse_ParsesCountAndTokens_VllmForm()
    {
        const string raw = """{ "count": 4, "tokens": [9906, 1917, 0, 1] }""";
        var resp = System.Text.Json.JsonSerializer.Deserialize(raw, JsonContext.Default.OpenAiTokenizeResponse);
        Assert.Equal(4, resp!.Count);
        Assert.Equal(new[] { 9906, 1917, 0, 1 }, resp.Tokens);
    }

    [Fact]
    public void OpenAiTokenizeResponse_ParsesTokensOnly_LlamaCppForm()
    {
        // llama.cpp returns { tokens:[...] } with no count; callers fall back to length.
        const string raw = """{ "tokens": [1, 2, 3] }""";
        var resp = System.Text.Json.JsonSerializer.Deserialize(raw, JsonContext.Default.OpenAiTokenizeResponse);
        Assert.Null(resp!.Count);
        Assert.Equal(3, resp.Tokens!.Length);
    }
}

public class LogprobsParsingTests
{
    [Fact]
    public void LogprobsResult_HasStableFieldNames()
    {
        var result = new LogprobsResult("http://infer:8080", "gpt-4o", true, true,
            200, 88, 1,
            new[] { new LogprobItem("ok", -0.01, new[] { new LogprobAlternative("OK", -3.2) }) },
            "stop", 10, 1, 11, null, null);
        var json = System.Text.Json.JsonSerializer.Serialize(result, JsonContext.Default.LogprobsResult);

        Assert.Contains("\"sampled_tokens\"", json);
        Assert.Contains("\"top_alternatives\"", json);
        Assert.Contains("\"logprob\"", json);
        Assert.DoesNotContain("\"SampledTokens\"", json);
    }

    [Fact]
    public void OpenAiLogprobsResponse_ParsesContentAndTopLogprobs()
    {
        const string raw = """
        {
          "choices": [{
            "message": { "role": "assistant", "content": "ok" },
            "logprobs": { "content": [
              { "token": "ok", "logprob": -0.01,
                "top_logprobs": [ { "token": "ok", "logprob": -0.01 }, { "token": "OK", "logprob": -3.2 } ] }
            ] },
            "finish_reason": "stop"
          }],
          "usage": { "prompt_tokens": 10, "completion_tokens": 1, "total_tokens": 11 }
        }
        """;
        var resp = System.Text.Json.JsonSerializer.Deserialize(raw, JsonContext.Default.OpenAiLogprobsResponse);
        var content = resp!.Choices![0].Logprobs!.Content!;
        Assert.Single(content);
        Assert.Equal("ok", content[0].Token);
        Assert.Equal(-0.01, content[0].Logprob, 5);
        Assert.Equal(2, content[0].TopLogprobs!.Length);
        Assert.Equal("OK", content[0].TopLogprobs![1].Token);
    }

    [Fact]
    public void OpenAiLogprobsResponse_HandlesMissingLogprobsAsNotReturned()
    {
        const string raw = """
        { "choices": [{ "message": { "role": "assistant", "content": "ok" }, "finish_reason": "stop" }] }
        """;
        var resp = System.Text.Json.JsonSerializer.Deserialize(raw, JsonContext.Default.OpenAiLogprobsResponse);
        Assert.Null(resp!.Choices![0].Logprobs);
    }
}

public class ClassifyTests
{
    [Fact]
    public void ClassifyResult_HasStableFieldNames()
    {
        var result = new ClassifyResult("http://infer:8080", "bert-classifier", true, true,
            200, 33, "classify", new[] { new ClassifyLabel("POSITIVE", 0.98) }, null, null, null);
        var json = System.Text.Json.JsonSerializer.Serialize(result, JsonContext.Default.ClassifyResult);

        Assert.Contains("\"labels\"", json);
        Assert.Contains("\"probability\"", json);
        Assert.Contains("\"mode\"", json);
        Assert.DoesNotContain("\"Labels\"", json);
    }

    [Fact]
    public void OpenAiClassifyResponse_ParsesLabelAndProbs()
    {
        const string raw = """
        { "data": [ { "label": "POSITIVE", "probs": [0.02, 0.98], "label_names": ["NEGATIVE", "POSITIVE"] } ] }
        """;
        var resp = System.Text.Json.JsonSerializer.Deserialize(raw, JsonContext.Default.OpenAiClassifyResponse);
        Assert.Equal("POSITIVE", resp!.Data![0].Label);
        Assert.Equal(new[] { 0.02, 0.98 }, resp.Data[0].Probs);
    }

    [Fact]
    public void OpenAiScoreResponse_ParsesScore()
    {
        const string raw = """{ "data": [ { "score": 0.873 } ] }""";
        var resp = System.Text.Json.JsonSerializer.Deserialize(raw, JsonContext.Default.OpenAiScoreResponse);
        Assert.Equal(0.873, resp!.Data![0].Score, 5);
    }

    [Fact]
    public void BuildLabels_PairsNamesWithProbsOrderedByProbability()
    {
        var data = new OpenAiClassifyData("POSITIVE", new[] { 0.02, 0.98 }, new[] { "NEGATIVE", "POSITIVE" });
        var labels = Probe.BuildLabels(data);
        Assert.Equal(2, labels.Length);
        Assert.Equal("POSITIVE", labels[0].Label);
        Assert.Equal(0.98, labels[0].Probability, 5);
        Assert.Equal("NEGATIVE", labels[1].Label);
    }

    [Fact]
    public void BuildLabels_SynthesizesLabelNamesWhenAbsent()
    {
        var data = new OpenAiClassifyData(null, new[] { 0.3, 0.7 }, null);
        var labels = Probe.BuildLabels(data);
        Assert.Equal("LABEL_1", labels[0].Label);
        Assert.Equal(0.7, labels[0].Probability, 5);
    }

    [Fact]
    public void BuildLabels_FallsBackToSingleLabelWhenNoProbs()
    {
        var data = new OpenAiClassifyData("spam", null, null);
        var labels = Probe.BuildLabels(data);
        Assert.Single(labels);
        Assert.Equal("spam", labels[0].Label);
    }

    [Fact]
    public void BuildLabels_ReturnsEmptyForNullData()
    {
        Assert.Empty(Probe.BuildLabels(null));
    }
}

public class TranscribeTests
{
    [Fact]
    public void TranscribeResult_HasStableFieldNames()
    {
        var result = new TranscribeResult("http://infer:8080", "whisper-1", true, true,
            200, 540, "./speech.wav", 11, "hello world", 1.5, null, null);
        var json = System.Text.Json.JsonSerializer.Serialize(result, JsonContext.Default.TranscribeResult);

        Assert.Contains("\"supported\"", json);
        Assert.Contains("\"audio_source\"", json);
        Assert.Contains("\"text_chars\"", json);
        Assert.Contains("\"text_preview\"", json);
        Assert.Contains("\"duration_seconds\"", json);
        Assert.DoesNotContain("\"TextPreview\"", json);
    }

    [Theory]
    [InlineData("a.wav", "audio/wav")]
    [InlineData("a.WAV", "audio/wav")]
    [InlineData("a.mp3", "audio/mpeg")]
    [InlineData("a.m4a", "audio/mp4")]
    [InlineData("a.flac", "audio/flac")]
    [InlineData("a.ogg", "audio/ogg")]
    [InlineData("a.webm", "audio/webm")]
    [InlineData("a.txt", "application/octet-stream")]
    public void AudioMimeFromExtension_InfersFromExtension(string path, string expected)
    {
        Assert.Equal(expected, Probe.AudioMimeFromExtension(path));
    }

    [Fact]
    public void OpenAiTranscriptionResponse_ParsesTextAndDuration()
    {
        const string raw = """{ "text": "hello world", "duration": 1.5 }""";
        var resp = System.Text.Json.JsonSerializer.Deserialize(raw, JsonContext.Default.OpenAiTranscriptionResponse);
        Assert.Equal("hello world", resp!.Text);
        Assert.Equal(1.5, resp.Duration);
    }

    [Fact]
    public async Task BuildTranscriptionContent_IncludesFileAndModelParts()
    {
        var audio = new byte[] { 0x52, 0x49, 0x46, 0x46 }; // "RIFF"
        using var content = Probe.BuildTranscriptionContent(audio, "speech.mp3", "whisper-1");

        var parts = content.ToList();
        Assert.Equal(2, parts.Count);

        var filePart = parts.Single(p => p.Headers.ContentDisposition!.Name!.Trim('"') == "file");
        Assert.Equal("speech.mp3", filePart.Headers.ContentDisposition!.FileName!.Trim('"'));
        Assert.Equal("audio/mpeg", filePart.Headers.ContentType!.ToString());

        var modelPart = parts.Single(p => p.Headers.ContentDisposition!.Name!.Trim('"') == "model");
        Assert.Equal("whisper-1", await modelPart.ReadAsStringAsync());
    }

    [Fact]
    public void ReadFileBytes_ThrowsConfigException_ForMissingFile()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"llmprobe-missing-{Guid.NewGuid():N}.wav");
        var ex = Assert.Throws<ConfigException>(() => Probe.ReadFileBytes(missing));
        Assert.Contains(missing, ex.Message);
        Assert.NotNull(ex.Hint);
    }

    [Fact]
    public void ReadFileBytes_ReturnsBytes_ForReadableFile()
    {
        var bytes = new byte[] { 1, 2, 3, 4 };
        var path = Path.Combine(Path.GetTempPath(), $"llmprobe-test-{Guid.NewGuid():N}.wav");
        File.WriteAllBytes(path, bytes);
        try { Assert.Equal(bytes, Probe.ReadFileBytes(path)); }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task TranscribeAsync_ParsesTextResponse()
    {
        var handler = new StubHandler(_ =>
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""{ "text": "hello world", "duration": 2.0 }"""),
            });
        using var http = new HttpClient(handler);
        var r = await Probe.TranscribeAsync(http, "http://infer:8080", "whisper-1",
            new byte[] { 1, 2, 3 }, "speech.wav", "./speech.wav", default);
        Assert.True(r.Ok);
        Assert.True(r.Supported);
        Assert.Equal("hello world", r.TextPreview);
        Assert.Equal(11, r.TextChars);
        Assert.Equal(2.0, r.DurationSeconds);
    }

    [Fact]
    public async Task TranscribeAsync_ReportsUnsupported_OnNotFound()
    {
        var handler = new StubHandler(_ =>
            new HttpResponseMessage(System.Net.HttpStatusCode.NotFound)
            {
                Content = new StringContent("no such route"),
            });
        using var http = new HttpClient(handler);
        var r = await Probe.TranscribeAsync(http, "http://infer:8080", "whisper-1",
            new byte[] { 1 }, "a.wav", "a.wav", default);
        Assert.True(r.Ok);
        Assert.False(r.Supported);
        Assert.Null(r.Error);
    }
}

public class SpeakTests
{
    [Fact]
    public void SpeakResult_HasStableFieldNames()
    {
        var result = new SpeakResult("http://infer:8080", "tts-1", true, true,
            200, 320, "alloy", "mp3", "audio/mpeg", 20480, "out.mp3", null, null);
        var json = System.Text.Json.JsonSerializer.Serialize(result, JsonContext.Default.SpeakResult);

        Assert.Contains("\"supported\"", json);
        Assert.Contains("\"content_type\"", json);
        Assert.Contains("\"bytes_received\"", json);
        Assert.Contains("\"output_path\"", json);
        Assert.Contains("\"voice\"", json);
        Assert.DoesNotContain("\"BytesReceived\"", json);
    }

    [Fact]
    public void OpenAiSpeechRequest_SerializesTtsFieldNames()
    {
        var req = new OpenAiSpeechRequest("tts-1", "Hej", "alloy", "mp3");
        var json = System.Text.Json.JsonSerializer.Serialize(req, JsonContext.Default.OpenAiSpeechRequest);
        Assert.Contains("\"model\"", json);
        Assert.Contains("\"input\"", json);
        Assert.Contains("\"voice\"", json);
        Assert.Contains("\"response_format\"", json);
        Assert.Contains("\"mp3\"", json);
    }

    [Fact]
    public void WriteAudioFile_WritesBytesToOutputPath()
    {
        var bytes = new byte[] { 0x49, 0x44, 0x33, 0x04 }; // "ID3" + version
        var path = Path.Combine(Path.GetTempPath(), $"llmprobe-test-{Guid.NewGuid():N}.mp3");
        try
        {
            Probe.WriteAudioFile(path, bytes);
            Assert.Equal(bytes, File.ReadAllBytes(path));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void WriteAudioFile_ThrowsConfigException_ForUnwritablePath()
    {
        // A path whose parent directory does not exist is unwritable.
        var bad = Path.Combine(Path.GetTempPath(), $"llmprobe-nodir-{Guid.NewGuid():N}", "out.mp3");
        var ex = Assert.Throws<ConfigException>(() => Probe.WriteAudioFile(bad, new byte[] { 1 }));
        Assert.Contains(bad, ex.Message);
        Assert.NotNull(ex.Hint);
    }

    [Fact]
    public async Task SpeakAsync_HandlesBinaryResponse_AndWritesOutput()
    {
        var audioBytes = new byte[] { 0x49, 0x44, 0x33, 0x04, 0x00 };
        var handler = new StubHandler(_ =>
        {
            var res = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(audioBytes),
            };
            res.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("audio/mpeg");
            return res;
        });
        using var http = new HttpClient(handler);
        var outPath = Path.Combine(Path.GetTempPath(), $"llmprobe-test-{Guid.NewGuid():N}.mp3");
        try
        {
            var r = await Probe.SpeakAsync(http, "http://infer:8080", "tts-1", "Hej", "alloy", "mp3", outPath, default);
            Assert.True(r.Ok);
            Assert.True(r.Supported);
            Assert.Equal(audioBytes.Length, r.BytesReceived);
            Assert.Equal("audio/mpeg", r.ContentType);
            Assert.Equal(outPath, r.OutputPath);
            Assert.Equal(audioBytes, File.ReadAllBytes(outPath));
        }
        finally { if (File.Exists(outPath)) File.Delete(outPath); }
    }

    [Fact]
    public async Task SpeakAsync_ReportsMetadataOnly_WhenNoOutputGiven()
    {
        var audioBytes = new byte[] { 0x00, 0x01, 0x02 };
        var handler = new StubHandler(_ =>
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(audioBytes),
            });
        using var http = new HttpClient(handler);
        var r = await Probe.SpeakAsync(http, "http://infer:8080", "tts-1", "Hej", "alloy", "mp3", null, default);
        Assert.True(r.Ok);
        Assert.Equal(audioBytes.Length, r.BytesReceived);
        Assert.Null(r.OutputPath);
        Assert.NotNull(r.Note);
    }

    [Fact]
    public async Task SpeakAsync_ReportsUnsupported_OnNotFound()
    {
        var handler = new StubHandler(_ =>
            new HttpResponseMessage(System.Net.HttpStatusCode.NotFound)
            {
                Content = new StringContent("no such route"),
            });
        using var http = new HttpClient(handler);
        var r = await Probe.SpeakAsync(http, "http://infer:8080", "tts-1", "Hej", "alloy", "mp3", null, default);
        Assert.True(r.Ok);          // unsupported is still a clean result (exit 0)
        Assert.False(r.Supported);
        Assert.Null(r.Error);
        Assert.NotNull(r.Note);
    }
}

// Minimal stubbed HttpMessageHandler so the audio probes can be exercised against
// an in-memory response without a live server.
internal sealed class StubHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

    public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => Task.FromResult(_responder(request));
}

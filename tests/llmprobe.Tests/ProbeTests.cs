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
}

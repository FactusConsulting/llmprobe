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
}

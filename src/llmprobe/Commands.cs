using System.ComponentModel;
using Spectre.Console.Cli;

namespace LlmProbe;

public class GlobalSettings : CommandSettings
{
    [CommandOption("--json")]
    [Description("Emit machine-readable JSON to stdout. Field names are snake_case and stable.")]
    public bool Json { get; init; }

    [CommandOption("--quiet")]
    [Description("Minimal output (status only). Useful when chaining via && or in scripts.")]
    public bool Quiet { get; init; }

    [CommandOption("--timeout <SECONDS>")]
    [DefaultValue(30)]
    [Description("HTTP timeout in seconds.")]
    public int TimeoutSeconds { get; init; } = 30;

    [CommandOption("--api-key <TOKEN>")]
    [Description("Bearer token. Falls back to OPENAI_API_KEY env var if unset.")]
    public string? ApiKey { get; init; }

    public string ResolvedApiKey() =>
        ApiKey ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? "";

    public TimeSpan Timeout => TimeSpan.FromSeconds(TimeoutSeconds);

    public void ApplyToRender() { Render.Format = Json ? OutputFormat.Json : OutputFormat.Text; Render.Quiet = Quiet; }
}

public class EndpointSettings : GlobalSettings
{
    [CommandArgument(0, "<endpoint>")]
    [Description("API base URL (e.g. http://localhost:11434/v1, https://api.openai.com).")]
    public required string Endpoint { get; init; }
}

public sealed class ChatSettings : EndpointSettings
{
    [CommandOption("-m|--model <MODEL>")]
    [DefaultValue("default")]
    [Description("Model identifier (use 'llmprobe models <endpoint>' to list).")]
    public string Model { get; init; } = "default";

    [CommandOption("-p|--prompt <PROMPT>")]
    [DefaultValue("Reply with the single word: ok.")]
    [Description("Prompt text. Use @file.txt to read from file, or @- for stdin.")]
    public string Prompt { get; init; } = "Reply with the single word: ok.";

    [CommandOption("--max-tokens <N>")]
    [DefaultValue(16)]
    [Description("Maximum completion tokens.")]
    public int MaxTokens { get; init; } = 16;

    public string ResolvedPrompt()
    {
        if (Prompt == "@-") return Console.In.ReadToEnd().Trim();
        if (Prompt.StartsWith('@')) return File.ReadAllText(Prompt[1..]).Trim();
        return Prompt;
    }
}

public sealed class PingCommand : AsyncCommand<EndpointSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext ctx, EndpointSettings s)
    {
        s.ApplyToRender();
        using var http = Probe.CreateClient(s.ResolvedApiKey(), s.Timeout);
        var r = await Probe.PingAsync(http, s.Endpoint, default);
        Render.Ping(r);
        return r.Reachable ? 0 : 74;
    }
}

public sealed class ModelsCommand : AsyncCommand<EndpointSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext ctx, EndpointSettings s)
    {
        s.ApplyToRender();
        using var http = Probe.CreateClient(s.ResolvedApiKey(), s.Timeout);
        var r = await Probe.ListModelsAsync(http, s.Endpoint, default);
        if (r == null) { Render.Error("models endpoint unreachable or non-200", $"Try: llmprobe ping {s.Endpoint}"); return 74; }
        Render.Models(r);
        return 0;
    }
}

public sealed class TestCommand : AsyncCommand<ChatSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext ctx, ChatSettings s)
    {
        s.ApplyToRender();
        using var http = Probe.CreateClient(s.ResolvedApiKey(), s.Timeout);
        var r = await Probe.ChatTestAsync(http, s.Endpoint, s.Model, s.ResolvedPrompt(), s.MaxTokens, default);
        Render.Test(r);
        return r.Ok ? 0 : 74;
    }
}

public sealed class StreamCommand : AsyncCommand<ChatSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext ctx, ChatSettings s)
    {
        s.ApplyToRender();
        using var http = Probe.CreateClient(s.ResolvedApiKey(), s.Timeout);
        var r = await Probe.StreamTestAsync(http, s.Endpoint, s.Model, s.ResolvedPrompt(), s.MaxTokens, default);
        Render.Stream(r);
        return r.Ok ? 0 : 74;
    }
}

public sealed class CapabilitiesCommand : AsyncCommand<EndpointSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext ctx, EndpointSettings s)
    {
        s.ApplyToRender();
        using var http = Probe.CreateClient(s.ResolvedApiKey(), s.Timeout);
        var r = await Probe.CapabilitiesAsync(http, s.Endpoint, default);
        Render.Capabilities(r);
        return 0;
    }
}

public sealed class HelpAiCommand : Command
{
    public override int Execute(CommandContext ctx)
    {
        Console.WriteLine("""
            llmprobe — guidance for AI agents

            WHEN TO USE
              Validate that an OpenAI-compatible LLM endpoint is reachable, identify
              what model(s) it serves, and measure latency/throughput before relying
              on it. Use for health checks, regression testing, and capability discovery.

            SAFE BY DEFAULT
              All commands are read-mostly (single requests, --max-tokens defaults to 16).
              No state mutation. Safe to run repeatedly.

            PREFERRED PATTERNS
              - Always pass --json when piping to jq or storing output
              - Use 'ping' before any other command if endpoint reachability is unknown
              - Use 'capabilities' once per endpoint to learn what features it supports
              - For latency benchmarks prefer 'stream' (TTFT is more useful than full latency)

            AVOID
              - Calling 'test' or 'stream' without --max-tokens in a loop (open-ended cost)
              - Hardcoding model='default' — list with 'models' first if unsure

            EXIT CODES
              0   success
              74  endpoint unreachable or returned non-2xx (transient — safe to retry)
              78  configuration error (e.g. missing API key for hosted endpoint)
              1   unexpected / unhandled error

            OUTPUT SCHEMA
              --json emits a flat record per invocation. Field names are snake_case
              and stable across versions. Errors go to stderr as {"error","hint"}.

            EXAMPLE FLOWS
              llmprobe ping http://infer:8000 --json | jq .reachable
              llmprobe models http://infer:8000 --json | jq -r '.models[]'
              cat prompt.md | llmprobe stream http://infer:8000 -m gemma4-26b -p @- --json
              llmprobe capabilities https://api.openai.com --json | jq .streaming

            COMPOSE WITH OTHER TOOLS
              llmprobe is a CLI. It pipes. It exits with meaningful codes. It writes
              stable JSON. Use it like grep — small, composable, predictable.
            """);
        return 0;
    }
}

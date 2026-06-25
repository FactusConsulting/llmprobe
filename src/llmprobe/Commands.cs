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

    // Resolve a single-value option that may use the @-/@file convention shared by
    // the prompt/query/input options: "@-" reads (trimmed) stdin, "@file" reads a
    // (trimmed) file, anything else is taken literally.
    protected static string ResolveAtValue(string value)
    {
        if (value == "@-") return Console.In.ReadToEnd().Trim();
        if (value.StartsWith('@')) return Probe.ReadAtFile(value[1..]).Trim();
        return value;
    }
}

// Run a command body that may resolve a user-supplied @file (prompt/input/query/
// document), translating a ConfigException (unreadable file) into a clean rendered
// config error with exit 78 — distinct from request/transport failures (exit 74).
// Keeps the @file try/catch in one place rather than duplicated across commands.
public static class CommandRunner
{
    public static async Task<int> GuardConfig(Func<Task<int>> body)
    {
        try
        {
            return await body();
        }
        catch (ConfigException ex)
        {
            Render.Error(ex.Message, ex.Hint);
            return 78;
        }
    }
}

public class EndpointSettings : GlobalSettings
{
    [CommandArgument(0, "<endpoint>")]
    [Description("API base URL (e.g. http://localhost:11434/v1, https://api.openai.com).")]
    public required string Endpoint { get; init; }
}

// Shared base for the prompt-driven chat probes (test, reasoning, structured).
// Each only differs in the default prompt/max-tokens and the option wording, so
// the common -m/--model option and the @-/@file prompt resolution live here.
public abstract class PromptSettings : EndpointSettings
{
    [CommandOption("-m|--model <MODEL>")]
    [DefaultValue("default")]
    [Description("Model identifier (use 'llmprobe models <endpoint>' to list).")]
    public string Model { get; init; } = "default";

    public abstract string Prompt { get; init; }
    public abstract int MaxTokens { get; init; }

    public string ResolvedPrompt() => ResolveAtValue(Prompt);
}

public sealed class ChatSettings : PromptSettings
{
    [CommandOption("-p|--prompt <PROMPT>")]
    [DefaultValue("Reply with the single word: ok.")]
    [Description("Prompt text. Use @file.txt to read from file, or @- for stdin.")]
    public override string Prompt { get; init; } = "Reply with the single word: ok.";

    [CommandOption("--max-tokens <N>")]
    [DefaultValue(16)]
    [Description("Maximum completion tokens.")]
    public override int MaxTokens { get; init; } = 16;
}

public sealed class EmbedSettings : EndpointSettings
{
    [CommandOption("-m|--model <MODEL>")]
    [DefaultValue("default")]
    [Description("Embedding model identifier (use 'llmprobe models <endpoint>' to list).")]
    public string Model { get; init; } = "default";

    [CommandOption("-i|--input <INPUT>")]
    [DefaultValue("The quick brown fox jumps over the lazy dog.")]
    [Description("Text to embed. Use @file.txt to read from file, or @- for stdin.")]
    public string Input { get; init; } = "The quick brown fox jumps over the lazy dog.";

    public string ResolvedInput() => ResolveAtValue(Input);
}

public sealed class RerankSettings : EndpointSettings
{
    [CommandOption("-m|--model <MODEL>")]
    [DefaultValue("default")]
    [Description("Reranker model identifier (use 'llmprobe models <endpoint>' to list).")]
    public string Model { get; init; } = "default";

    [CommandOption("-q|--query <QUERY>")]
    [Description("Query to rank documents against. Use @file.txt or @- for stdin.")]
    public required string Query { get; init; }

    [CommandOption("-d|--document <DOC>")]
    [Description("A candidate document. Repeatable. A @file or @- value is split into one document per line.")]
    public string[] Documents { get; init; } = Array.Empty<string>();

    [CommandOption("--top-n <N>")]
    [Description("Return only the top N documents (server-side). Default: all.")]
    public int? TopN { get; init; }

    public string ResolvedQuery() => ResolveAtValue(Query);

    public string[] ResolvedDocuments() => Probe.ExpandLines(Documents);
}

public sealed class ReasoningSettings : PromptSettings
{
    [CommandOption("-p|--prompt <PROMPT>")]
    [DefaultValue("A farmer has 17 sheep. All but 9 run away. How many are left? Think step by step, then give the final number.")]
    [Description("Reasoning prompt. Use @file.txt to read from file, or @- for stdin.")]
    public override string Prompt { get; init; } = "A farmer has 17 sheep. All but 9 run away. How many are left? Think step by step, then give the final number.";

    [CommandOption("--max-tokens <N>")]
    [DefaultValue(512)]
    [Description("Maximum completion tokens (high enough to allow a thinking phase).")]
    public override int MaxTokens { get; init; } = 512;
}

public sealed class StructuredSettings : PromptSettings
{
    [CommandOption("-p|--prompt <PROMPT>")]
    [DefaultValue("Extract a person from: 'Alice is 30 years old.' Respond only with the JSON object.")]
    [Description("Prompt that should populate the {name, age} schema. Use @file.txt or @- for stdin.")]
    public override string Prompt { get; init; } = "Extract a person from: 'Alice is 30 years old.' Respond only with the JSON object.";

    [CommandOption("--max-tokens <N>")]
    [DefaultValue(128)]
    [Description("Maximum completion tokens.")]
    public override int MaxTokens { get; init; } = 128;
}

public sealed class ReasoningCommand : AsyncCommand<ReasoningSettings>
{
    public override Task<int> ExecuteAsync(CommandContext context, ReasoningSettings s)
    {
        s.ApplyToRender();
        return CommandRunner.GuardConfig(async () =>
        {
            using var http = Probe.CreateClient(s.ResolvedApiKey(), s.Timeout);
            var r = await Probe.ReasoningAsync(http, s.Endpoint, s.Model, s.ResolvedPrompt(), s.MaxTokens, default);
            Render.Reasoning(r);
            return r.Ok ? 0 : 74;
        });
    }
}

public sealed class StructuredCommand : AsyncCommand<StructuredSettings>
{
    public override Task<int> ExecuteAsync(CommandContext context, StructuredSettings s)
    {
        s.ApplyToRender();
        return CommandRunner.GuardConfig(async () =>
        {
            using var http = Probe.CreateClient(s.ResolvedApiKey(), s.Timeout);
            var r = await Probe.StructuredAsync(http, s.Endpoint, s.Model, s.ResolvedPrompt(), s.MaxTokens, default);
            Render.Structured(r);
            // A successful HTTP call that returns bad structure is still exit 0 — only
            // transport/HTTP failure is 74. (Render distinguishes the two via fields.)
            return r.Ok ? 0 : 74;
        });
    }
}

public sealed class VisionSettings : PromptSettings
{
    [CommandOption("-i|--image <IMAGE>")]
    [Description("Image to send: an http(s):// URL, or a local file path / @file inlined as a base64 data: URL (png/jpeg/webp/gif).")]
    public required string Image { get; init; }

    [CommandOption("-p|--prompt <PROMPT>")]
    [DefaultValue("Describe this image in one word.")]
    [Description("Prompt sent alongside the image. Use @file.txt to read from file, or @- for stdin.")]
    public override string Prompt { get; init; } = "Describe this image in one word.";

    [CommandOption("--max-tokens <N>")]
    [DefaultValue(32)]
    [Description("Maximum completion tokens.")]
    public override int MaxTokens { get; init; } = 32;
}

public sealed class ToolsSettings : PromptSettings
{
    [CommandOption("-p|--prompt <PROMPT>")]
    [DefaultValue("What's the weather in Copenhagen? Use the tool.")]
    [Description("Prompt designed to trigger a tool call. Use @file.txt to read from file, or @- for stdin.")]
    public override string Prompt { get; init; } = "What's the weather in Copenhagen? Use the tool.";

    [CommandOption("--max-tokens <N>")]
    [DefaultValue(128)]
    [Description("Maximum completion tokens.")]
    public override int MaxTokens { get; init; } = 128;
}

public sealed class VisionCommand : AsyncCommand<VisionSettings>
{
    public override Task<int> ExecuteAsync(CommandContext context, VisionSettings s)
    {
        s.ApplyToRender();
        // Spectre does not enforce the C# `required` modifier, so a missing -i
        // leaves Image null. Validate before use instead of throwing a raw NRE.
        if (string.IsNullOrWhiteSpace(s.Image))
        {
            Render.Error("no image provided", "Pass an image URL or local path with -i/--image (e.g. -i https://… or -i ./cat.png)");
            return Task.FromResult(78);
        }
        return CommandRunner.GuardConfig(async () =>
        {
            (string Url, string Source) image;
            try
            {
                image = Probe.ResolveImage(s.Image);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                Render.Error($"could not read image: {ex.Message}", "Check the path, or pass an http(s):// URL instead");
                return 78;
            }
            // ResolvedPrompt() may throw ConfigException for an unreadable -p @file;
            // the guard turns that into the same clean config error (exit 78).
            var prompt = s.ResolvedPrompt();
            using var http = Probe.CreateClient(s.ResolvedApiKey(), s.Timeout);
            var r = await Probe.VisionAsync(http, s.Endpoint, s.Model, image, prompt, s.MaxTokens, default);
            Render.Vision(r);
            return r.Ok ? 0 : 74;
        });
    }
}

public sealed class ToolsCommand : AsyncCommand<ToolsSettings>
{
    public override Task<int> ExecuteAsync(CommandContext context, ToolsSettings s)
    {
        s.ApplyToRender();
        return CommandRunner.GuardConfig(async () =>
        {
            using var http = Probe.CreateClient(s.ResolvedApiKey(), s.Timeout);
            var r = await Probe.ToolsAsync(http, s.Endpoint, s.Model, s.ResolvedPrompt(), s.MaxTokens, default);
            Render.Tools(r);
            return r.Ok ? 0 : 74;
        });
    }
}

public sealed class PingCommand : AsyncCommand<EndpointSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, EndpointSettings s)
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
    public override async Task<int> ExecuteAsync(CommandContext context, EndpointSettings s)
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
    public override Task<int> ExecuteAsync(CommandContext context, ChatSettings s)
    {
        s.ApplyToRender();
        return CommandRunner.GuardConfig(async () =>
        {
            using var http = Probe.CreateClient(s.ResolvedApiKey(), s.Timeout);
            var r = await Probe.ChatTestAsync(http, s.Endpoint, s.Model, s.ResolvedPrompt(), s.MaxTokens, default);
            Render.Test(r);
            return r.Ok ? 0 : 74;
        });
    }
}

public sealed class StreamCommand : AsyncCommand<ChatSettings>
{
    public override Task<int> ExecuteAsync(CommandContext context, ChatSettings s)
    {
        s.ApplyToRender();
        return CommandRunner.GuardConfig(async () =>
        {
            using var http = Probe.CreateClient(s.ResolvedApiKey(), s.Timeout);
            var r = await Probe.StreamTestAsync(http, s.Endpoint, s.Model, s.ResolvedPrompt(), s.MaxTokens, default);
            Render.Stream(r);
            return r.Ok ? 0 : 74;
        });
    }
}

public sealed class EmbedCommand : AsyncCommand<EmbedSettings>
{
    public override Task<int> ExecuteAsync(CommandContext context, EmbedSettings s)
    {
        s.ApplyToRender();
        return CommandRunner.GuardConfig(async () =>
        {
            using var http = Probe.CreateClient(s.ResolvedApiKey(), s.Timeout);
            var r = await Probe.EmbedAsync(http, s.Endpoint, s.Model, new[] { s.ResolvedInput() }, default);
            Render.Embed(r);
            return r.Ok ? 0 : 74;
        });
    }
}

public sealed class RerankCommand : AsyncCommand<RerankSettings>
{
    public override Task<int> ExecuteAsync(CommandContext context, RerankSettings s)
    {
        s.ApplyToRender();
        // Spectre does not enforce the C# `required` modifier, so a missing -q
        // leaves Query null. Validate (and resolve it before documents, so two
        // @- values don't silently drain stdin into the query, leaving none for
        // the docs) instead of letting ResolvedQuery() throw a raw NRE.
        if (string.IsNullOrWhiteSpace(s.Query))
        {
            Render.Error("no query provided", "Pass a query with -q/--query, or -q @query.txt / -q @-");
            return Task.FromResult(78);
        }
        return CommandRunner.GuardConfig(async () =>
        {
            // ResolvedQuery()/ResolvedDocuments() may throw ConfigException for an
            // unreadable @file; the guard turns that into a clean config error (78).
            var query = s.ResolvedQuery();
            var docs = s.ResolvedDocuments();
            if (docs.Length == 0)
            {
                Render.Error("no documents provided", "Pass one or more with -d/--document (repeatable), or -d @docs.txt");
                return 78;
            }
            using var http = Probe.CreateClient(s.ResolvedApiKey(), s.Timeout);
            var r = await Probe.RerankAsync(http, s.Endpoint, s.Model, query, docs, s.TopN, default);
            Render.Rerank(r);
            return r.Ok ? 0 : 74;
        });
    }
}

public sealed class CapabilitiesCommand : AsyncCommand<EndpointSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, EndpointSettings s)
    {
        s.ApplyToRender();
        using var http = Probe.CreateClient(s.ResolvedApiKey(), s.Timeout);
        var r = await Probe.CapabilitiesAsync(http, s.Endpoint, default);
        Render.Capabilities(r);
        return 0;
    }
}

public sealed class CompletionsSettings : PromptSettings
{
    [CommandOption("-p|--prompt <PROMPT>")]
    [DefaultValue("The capital of France is")]
    [Description("Prompt to complete. Use @file.txt to read from file, or @- for stdin.")]
    public override string Prompt { get; init; } = "The capital of France is";

    [CommandOption("--max-tokens <N>")]
    [DefaultValue(16)]
    [Description("Maximum completion tokens.")]
    public override int MaxTokens { get; init; } = 16;
}

public sealed class InfillSettings : EndpointSettings
{
    [CommandOption("-m|--model <MODEL>")]
    [DefaultValue("default")]
    [Description("Model identifier (use 'llmprobe models <endpoint>' to list).")]
    public string Model { get; init; } = "default";

    [CommandOption("--prefix <TEXT>")]
    [DefaultValue("def add(a, b):\n    return ")]
    [Description("Text before the cursor (input_prefix). Use @file.txt or @- for stdin.")]
    public string Prefix { get; init; } = "def add(a, b):\n    return ";

    [CommandOption("--suffix <TEXT>")]
    [DefaultValue("\n\nprint(add(2, 3))")]
    [Description("Text after the cursor (input_suffix). Use @file.txt or @- for stdin.")]
    public string Suffix { get; init; } = "\n\nprint(add(2, 3))";

    [CommandOption("--max-tokens <N>")]
    [DefaultValue(64)]
    [Description("Maximum predicted tokens (n_predict).")]
    public int MaxTokens { get; init; } = 64;

    public string ResolvedPrefix() => ResolveAtValue(Prefix);
    public string ResolvedSuffix() => ResolveAtValue(Suffix);
}

public sealed class TokenizeSettings : EndpointSettings
{
    [CommandOption("-m|--model <MODEL>")]
    [DefaultValue("default")]
    [Description("Model identifier (use 'llmprobe models <endpoint>' to list).")]
    public string Model { get; init; } = "default";

    [CommandOption("-i|--input <INPUT>")]
    [DefaultValue("The quick brown fox jumps over the lazy dog.")]
    [Description("Text to tokenize. Use @file.txt to read from file, or @- for stdin.")]
    public string Input { get; init; } = "The quick brown fox jumps over the lazy dog.";

    public string ResolvedInput() => ResolveAtValue(Input);
}

public sealed class LogprobsSettings : PromptSettings
{
    [CommandOption("-p|--prompt <PROMPT>")]
    [DefaultValue("Reply with the single word: ok.")]
    [Description("Prompt sent to elicit logprobs. Use @file.txt or @- for stdin.")]
    public override string Prompt { get; init; } = "Reply with the single word: ok.";

    [CommandOption("--max-tokens <N>")]
    [DefaultValue(16)]
    [Description("Maximum completion tokens (kept small; only the first few are reported).")]
    public override int MaxTokens { get; init; } = 16;
}

public sealed class ClassifySettings : EndpointSettings
{
    [CommandOption("-m|--model <MODEL>")]
    [DefaultValue("default")]
    [Description("Classifier/scoring model identifier (use 'llmprobe models <endpoint>' to list).")]
    public string Model { get; init; } = "default";

    [CommandOption("-i|--input <INPUT>")]
    [DefaultValue("I really enjoyed this movie, it was fantastic!")]
    [Description("Text to classify, or the first text when scoring. Use @file.txt or @- for stdin.")]
    public string Input { get; init; } = "I really enjoyed this movie, it was fantastic!";

    [CommandOption("--score <TEXT>")]
    [Description("Switch to scoring mode: similarity of --input vs this text via /score. Use @file.txt or @- for stdin.")]
    public string? Score { get; init; }

    public string ResolvedInput() => ResolveAtValue(Input);
    public string ResolvedScore() => ResolveAtValue(Score!);
}

public sealed class CompletionsCommand : AsyncCommand<CompletionsSettings>
{
    public override Task<int> ExecuteAsync(CommandContext context, CompletionsSettings s)
    {
        s.ApplyToRender();
        return CommandRunner.GuardConfig(async () =>
        {
            using var http = Probe.CreateClient(s.ResolvedApiKey(), s.Timeout);
            var r = await Probe.CompletionsAsync(http, s.Endpoint, s.Model, s.ResolvedPrompt(), s.MaxTokens, default);
            Render.Completions(r);
            // A reachable endpoint missing this route still counts as success
            // (reported as unsupported, exit code 0). Only a transport or an
            // HTTP-level failure returns exit code 74.
            return r.Ok ? 0 : 74;
        });
    }
}

public sealed class InfillCommand : AsyncCommand<InfillSettings>
{
    public override Task<int> ExecuteAsync(CommandContext context, InfillSettings s)
    {
        s.ApplyToRender();
        return CommandRunner.GuardConfig(async () =>
        {
            var prefix = s.ResolvedPrefix();
            var suffix = s.ResolvedSuffix();
            using var http = Probe.CreateClient(s.ResolvedApiKey(), s.Timeout);
            var r = await Probe.InfillAsync(http, s.Endpoint, s.Model, prefix, suffix, s.MaxTokens, default);
            Render.Infill(r);
            return r.Ok ? 0 : 74;
        });
    }
}

public sealed class TokenizeCommand : AsyncCommand<TokenizeSettings>
{
    public override Task<int> ExecuteAsync(CommandContext context, TokenizeSettings s)
    {
        s.ApplyToRender();
        return CommandRunner.GuardConfig(async () =>
        {
            using var http = Probe.CreateClient(s.ResolvedApiKey(), s.Timeout);
            var r = await Probe.TokenizeAsync(http, s.Endpoint, s.Model, s.ResolvedInput(), default);
            Render.Tokenize(r);
            return r.Ok ? 0 : 74;
        });
    }
}

public sealed class LogprobsCommand : AsyncCommand<LogprobsSettings>
{
    public override Task<int> ExecuteAsync(CommandContext context, LogprobsSettings s)
    {
        s.ApplyToRender();
        return CommandRunner.GuardConfig(async () =>
        {
            using var http = Probe.CreateClient(s.ResolvedApiKey(), s.Timeout);
            var r = await Probe.LogprobsAsync(http, s.Endpoint, s.Model, s.ResolvedPrompt(), s.MaxTokens, default);
            Render.Logprobs(r);
            return r.Ok ? 0 : 74;
        });
    }
}

public sealed class ClassifyCommand : AsyncCommand<ClassifySettings>
{
    public override Task<int> ExecuteAsync(CommandContext context, ClassifySettings s)
    {
        s.ApplyToRender();
        return CommandRunner.GuardConfig(async () =>
        {
            using var http = Probe.CreateClient(s.ResolvedApiKey(), s.Timeout);
            var input = s.ResolvedInput();
            // When a score value is given, run cross-encoder pair scoring against
            // the score route. Otherwise classify the single input against the
            // classify route.
            var r = s.Score != null
                ? await Probe.ScoreAsync(http, s.Endpoint, s.Model, input, s.ResolvedScore(), default)
                : await Probe.ClassifyAsync(http, s.Endpoint, s.Model, input, default);
            Render.Classify(r);
            return r.Ok ? 0 : 74;
        });
    }
}

public sealed class TranscribeSettings : EndpointSettings
{
    [CommandOption("-m|--model <MODEL>")]
    [DefaultValue("whisper-1")]
    [Description("Speech-to-text model identifier (use 'llmprobe models <endpoint>' to list).")]
    public string Model { get; init; } = "whisper-1";

    [CommandOption("-f|--file <PATH>")]
    [Description("Local audio file to transcribe (wav/mp3/m4a/flac/ogg/webm).")]
    public required string File { get; init; }
}

public sealed class SpeakSettings : EndpointSettings
{
    [CommandOption("-m|--model <MODEL>")]
    [DefaultValue("tts-1")]
    [Description("Text-to-speech model identifier (use 'llmprobe models <endpoint>' to list).")]
    public string Model { get; init; } = "tts-1";

    [CommandOption("-i|--input <TEXT>")]
    [DefaultValue("Hello from llmprobe.")]
    [Description("Text to synthesize. Use @file.txt to read from file, or @- for stdin.")]
    public string Input { get; init; } = "Hello from llmprobe.";

    [CommandOption("--voice <NAME>")]
    [DefaultValue("alloy")]
    [Description("Voice name to synthesize with.")]
    public string Voice { get; init; } = "alloy";

    [CommandOption("--format <FMT>")]
    [DefaultValue("mp3")]
    [Description("Audio response_format (e.g. mp3, wav, opus, flac).")]
    public string Format { get; init; } = "mp3";

    [CommandOption("-o|--output <PATH>")]
    [Description("Write the synthesized audio to this file. Omit to report metadata only (no binary dumped to the terminal).")]
    public string? Output { get; init; }

    public string ResolvedInput() => ResolveAtValue(Input);
}

public sealed class TranscribeCommand : AsyncCommand<TranscribeSettings>
{
    public override Task<int> ExecuteAsync(CommandContext context, TranscribeSettings s)
    {
        s.ApplyToRender();
        // Spectre does not enforce the C# `required` modifier, so a missing -f
        // leaves File null. Validate before use instead of throwing a raw NRE.
        if (string.IsNullOrWhiteSpace(s.File))
        {
            Render.Error("no audio file provided", "Pass a local audio file with -f/--file (e.g. -f ./speech.wav)");
            return Task.FromResult(78);
        }
        return CommandRunner.GuardConfig(async () =>
        {
            // ReadFileBytes throws ConfigException for a missing/unreadable file;
            // the guard turns that into a clean config error (exit 78).
            var audio = Probe.ReadFileBytes(s.File);
            var fileName = Path.GetFileName(s.File);
            using var http = Probe.CreateClient(s.ResolvedApiKey(), s.Timeout);
            var r = await Probe.TranscribeAsync(http, s.Endpoint, s.Model, audio, fileName, s.File, default);
            Render.Transcribe(r);
            return r.Ok ? 0 : 74;
        });
    }
}

public sealed class SpeakCommand : AsyncCommand<SpeakSettings>
{
    public override Task<int> ExecuteAsync(CommandContext context, SpeakSettings s)
    {
        s.ApplyToRender();
        return CommandRunner.GuardConfig(async () =>
        {
            // ResolvedInput() may throw ConfigException for an unreadable @file, and
            // SpeakAsync throws it for an unwritable -o; the guard maps both to 78.
            var input = s.ResolvedInput();
            using var http = Probe.CreateClient(s.ResolvedApiKey(), s.Timeout);
            var r = await Probe.SpeakAsync(http, s.Endpoint, s.Model, input, s.Voice, s.Format, s.Output, default);
            Render.Speak(r);
            return r.Ok ? 0 : 74;
        });
    }
}

public static class AgentGuidance
{
    public const string Text = """
            llmprobe — guidance for AI agents

            WHEN TO USE
              Validate that an OpenAI-compatible LLM endpoint is reachable, identify
              what model(s) it serves, and measure latency/throughput before relying
              on it. Use for health checks, regression testing, and capability discovery.
              Covers chat (test/stream), embeddings (embed), rerankers (rerank),
              vision/multimodal input (vision), function/tool calling (tools),
              reasoning/thinking models (reasoning), structured/json-schema
              output (structured), legacy text completion (completions),
              fill-in-the-middle (infill), tokenization (tokenize), token
              logprobs (logprobs), classification/scoring (classify),
              speech-to-text (transcribe) and text-to-speech (speak).

            SUPPORT DETECTION
              completions/infill/tokenize/logprobs/classify/transcribe/speak are support probes:
              when an endpoint lacks the route (404/400/405/501) or returns no
              logprobs, that is reported as "supported": false at exit 0 — it is
              NOT a failure. Only a transport/connection error is exit 74.

            SAFE BY DEFAULT
              All commands are read-mostly (single requests, --max-tokens defaults to 16).
              No state mutation. Safe to run repeatedly.

            COMMANDS BY ENDPOINT
              ping/models      -> /v1/models (+ /health)
              test/stream/caps -> /v1/chat/completions
              vision/tools     -> /v1/chat/completions (image input / tool calling)
              reasoning        -> /v1/chat/completions (detects thinking/reasoning)
              structured       -> /v1/chat/completions (json_schema adherence)
              embed            -> /v1/embeddings   (reports dimensions + L2 norm)
              rerank           -> /v1/rerank       (reports ordering + relevance scores)
              completions      -> /v1/completions  (legacy text completion; choices[].text)
              logprobs         -> /v1/chat/completions (logprobs + top_logprobs)
              tokenize         -> /tokenize        (OpenAI/vLLM or llama.cpp form)
              infill           -> /infill          (llama.cpp fill-in-the-middle)
              classify         -> /classify, /score (vLLM classifier / cross-encoder)
              transcribe       -> /v1/audio/transcriptions (speech-to-text, multipart upload)
              speak            -> /v1/audio/speech (text-to-speech, binary audio response)
              A model-aware gateway routes by the request's model name, so pass -m
              to reach the intended backend.

            AUTHENTICATION
              For hosted endpoints (OpenAI, Anthropic, OpenRouter, secured vLLM):
                - Pass --api-key <token> on the command line, OR
                - Set OPENAI_API_KEY in the environment (the env var name is generic;
                  it's just used as a bearer token for any OpenAI-compatible endpoint)
              The --api-key flag wins if both are present. Local llama.cpp/Ollama
              instances typically need no key. A missing key on a hosted endpoint
              shows up as 401/403 in the result.error field, not exit code 78.

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
              llmprobe ping https://infer:8000 --json | jq .reachable
              llmprobe models https://infer:8000 --json | jq -r '.models[]'
              cat prompt.md | llmprobe stream https://infer:8000 -m gemma4-26b -p @- --json
              llmprobe capabilities https://api.openai.com --json | jq .streaming
              llmprobe embed https://infer:8000 -m <embedding-model> -i "hello" --json | jq .dimensions
              llmprobe rerank https://infer:8000 -q "@q.txt" -d @docs.txt --json | jq '.ranking[0]'
              llmprobe vision https://infer:8000 -i ./cat.png --json | jq .image_accepted
              llmprobe tools https://infer:8000 --json | jq .tool_called
              llmprobe reasoning https://infer:8000 --json | jq '{ok:.reasoning_detected,via:.reasoning_channel}'
              llmprobe structured https://infer:8000 --json | jq .schema_conformant
              llmprobe completions https://infer:8000 -m <model> --json | jq '{ok:.supported,text:.text_preview}'
              llmprobe tokenize https://infer:8000 -i "hello world" --json | jq .token_count
              llmprobe logprobs https://infer:8000 --json | jq '{ok:.supported,first:.tokens[0]}'
              llmprobe classify https://infer:8000 -i "great!" --json | jq '.labels[0]'
              llmprobe infill https://infer:8000 --prefix "@a" --suffix "@b" --json | jq .content_preview
              llmprobe transcribe https://infer:8000 -f ./speech.wav --json | jq '{ok:.supported,text:.text_preview}'
              llmprobe speak https://infer:8000 -i "Hej" -o out.mp3 --json | jq '{ok:.supported,bytes:.bytes_received}'

            COMPOSE WITH OTHER TOOLS
              llmprobe is a CLI. It pipes. It exits with meaningful codes. It writes
              stable JSON. Use it like grep — small, composable, predictable.
            """;
}

public sealed class HelpAiCommand : Command
{
    public override int Execute(CommandContext ctx)
    {
        Console.WriteLine(AgentGuidance.Text);
        return 0;
    }
}

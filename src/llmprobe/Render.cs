using System.Text.Json;
using Spectre.Console;

namespace LlmProbe;

public enum OutputFormat { Text, Json }

public static class Render
{
    public static OutputFormat Format { get; set; } = OutputFormat.Text;
    public static bool Quiet { get; set; } = false;

    private const string IconOk = "[green]✓[/]";
    private const string IconFail = "[red]✗[/]";
    private const string IconYes = "[green]yes[/]";
    private const string ErrorLabel = "[red]error[/]";
    private const string KeyLatency = "latency";
    private const string KeyTokens = "tokens";
    private const string KeyFinish = "finish";

    public static void Ping(PingResult r)
    {
        if (ShortCircuit(r.Reachable, () => JsonSerializer.Serialize(r, JsonContext.Default.PingResult))) return;
        var status = r.Reachable ? IconOk : IconFail;
        AnsiConsole.MarkupLine($"{status} [bold]{r.Endpoint}[/]");
        var t = KvTable();
        t.AddRow("status", r.StatusCode?.ToString() ?? "—");
        t.AddRow(KeyLatency, $"{r.LatencyMs} ms");
        if (r.ServerHeader != null) t.AddRow("server", Markup.Escape(r.ServerHeader));
        if (r.Error != null) t.AddRow(ErrorLabel, Markup.Escape(r.Error));
        AnsiConsole.Write(t);
    }

    public static void Models(ModelList r)
    {
        if (Format == OutputFormat.Json) { Json(JsonSerializer.Serialize(r, JsonContext.Default.ModelList)); return; }
        if (Quiet) { foreach (var m in r.Models) Console.WriteLine(m); return; }
        AnsiConsole.MarkupLine($"[bold]{r.Count}[/] model(s) available on [cyan]{r.Endpoint}[/] [grey]({r.LatencyMs} ms)[/]");
        foreach (var m in r.Models) AnsiConsole.MarkupLine($"  [green]•[/] {m}");
    }

    public static void Test(TestResult r)
    {
        if (ShortCircuit(r.Ok, () => JsonSerializer.Serialize(r, JsonContext.Default.TestResult))) return;
        var status = r.Ok ? IconOk : IconFail;
        AnsiConsole.MarkupLine($"{status} [bold]{r.Model}[/] @ [cyan]{r.Endpoint}[/]");
        var t = KvTable();
        t.AddRow(KeyLatency, $"{r.LatencyMs} ms");
        t.AddRow(KeyTokens, TokenRow(r.PromptTokens, r.CompletionTokens, r.TotalTokens));
        if (r.FinishReason != null) t.AddRow(KeyFinish, Markup.Escape(r.FinishReason));
        if (r.ResponsePreview != null) t.AddRow("response", $"[italic]{Markup.Escape(r.ResponsePreview)}[/]");
        if (r.Error != null) t.AddRow(ErrorLabel, Markup.Escape(r.Error));
        AnsiConsole.Write(t);
    }

    public static void Stream(StreamResult r)
    {
        if (ShortCircuit(r.Ok, () => JsonSerializer.Serialize(r, JsonContext.Default.StreamResult))) return;
        var status = r.Ok ? IconOk : IconFail;
        AnsiConsole.MarkupLine($"{status} streaming [bold]{r.Model}[/] @ [cyan]{r.Endpoint}[/]");
        var t = KvTable();
        t.AddRow("TTFT", $"[yellow]{r.TtftMs}[/] ms");
        t.AddRow("total", $"{r.TotalMs} ms");
        t.AddRow("chunks", r.Chunks.ToString());
        t.AddRow(KeyTokens, $"~{r.OutputTokensApprox}");
        t.AddRow("throughput", $"[yellow]{r.TokensPerSec:F1}[/] tok/s");
        AddFinishAndError(t, r.FinishReason, r.Error);
        AnsiConsole.Write(t);
    }

    public static void Capabilities(CapabilitiesResult r)
    {
        if (Format == OutputFormat.Json) { Json(JsonSerializer.Serialize(r, JsonContext.Default.CapabilitiesResult)); return; }
        AnsiConsole.MarkupLine($"Capabilities of [cyan]{r.Endpoint}[/]");
        var t = new Table().Border(TableBorder.Minimal).HideHeaders().AddColumn("feature").AddColumn("supported");
        if (r.ServerSoftware != null) t.AddRow("server", Markup.Escape(r.ServerSoftware));
        if (r.ApiCompatibility != null) t.AddRow("api", Markup.Escape(r.ApiCompatibility));
        t.AddRow("streaming", Yn(r.Streaming));
        t.AddRow("tool calls", Yn(r.ToolCalls));
        t.AddRow("vision", Yn(r.Vision));
        t.AddRow("json mode", Yn(r.JsonMode));
        t.AddRow("logprobs", Yn(r.LogProbs));
        t.AddRow("models", r.AvailableModels.Length.ToString());
        AnsiConsole.Write(t);
        foreach (var m in r.AvailableModels) AnsiConsole.MarkupLine($"  [green]•[/] {m}");
        if (r.AuthNote != null)
        {
            AnsiConsole.MarkupLine($"[yellow]note:[/] {Markup.Escape(r.AuthNote)}");
        }
    }

    public static void Embed(EmbedResult r)
    {
        if (ShortCircuit(r.Ok, () => JsonSerializer.Serialize(r, JsonContext.Default.EmbedResult))) return;
        var status = r.Ok ? IconOk : IconFail;
        AnsiConsole.MarkupLine($"{status} embed [bold]{r.Model}[/] @ [cyan]{r.Endpoint}[/]");
        var t = KvTable();
        t.AddRow(KeyLatency, $"{r.LatencyMs} ms");
        t.AddRow("inputs", r.Inputs.ToString());
        t.AddRow("dimensions", $"[yellow]{r.Dimensions}[/]");
        t.AddRow("norm", $"{r.Norm:F4}");
        if (r.TotalTokens > 0) t.AddRow(KeyTokens, $"prompt=[yellow]{r.PromptTokens}[/] total=[yellow]{r.TotalTokens}[/]");
        if (r.Error != null) t.AddRow(ErrorLabel, Markup.Escape(r.Error));
        AnsiConsole.Write(t);
    }

    public static void Rerank(RerankResult r)
    {
        if (ShortCircuit(r.Ok, () => JsonSerializer.Serialize(r, JsonContext.Default.RerankResult))) return;
        var status = r.Ok ? IconOk : IconFail;
        AnsiConsole.MarkupLine($"{status} rerank [bold]{r.Model}[/] @ [cyan]{r.Endpoint}[/] [grey]({r.LatencyMs} ms, {r.Documents} docs)[/]");
        if (r.Error != null) { AnsiConsole.MarkupLine($"[red]error:[/] {Markup.Escape(r.Error)}"); return; }
        var t = new Table().Border(TableBorder.Minimal).AddColumn("rank").AddColumn("idx").AddColumn("score").AddColumn("document");
        var rank = 1;
        foreach (var item in r.Ranking)
        {
            t.AddRow(rank.ToString(), item.Index.ToString(), $"[yellow]{item.Score:F4}[/]",
                item.DocumentPreview != null ? $"[italic]{Markup.Escape(item.DocumentPreview)}[/]" : "—");
            rank++;
        }
        AnsiConsole.Write(t);
    }

    public static void Reasoning(ReasoningResult r)
    {
        if (ShortCircuit(r.Ok, () => JsonSerializer.Serialize(r, JsonContext.Default.ReasoningResult))) return;
        var status = r.Ok ? IconOk : IconFail;
        AnsiConsole.MarkupLine($"{status} reasoning [bold]{r.Model}[/] @ [cyan]{r.Endpoint}[/]");
        var t = KvTable();
        t.AddRow(KeyLatency, $"{r.LatencyMs} ms");
        t.AddRow("reasoning", r.ReasoningDetected ? "[green]detected[/]" : "[grey]not detected[/]");
        if (r.ReasoningChannel != null) t.AddRow("channel", Markup.Escape(r.ReasoningChannel));
        if (r.ReasoningTokens > 0) t.AddRow("reasoning tokens", $"[yellow]{r.ReasoningTokens}[/]");
        if (r.ReasoningDetected) t.AddRow("split (chars)", $"thinking=[yellow]{r.ReasoningCharsApprox}[/] answer=[yellow]{r.AnswerCharsApprox}[/]");
        t.AddRow(KeyTokens, TokenRow(r.PromptTokens, r.CompletionTokens, r.TotalTokens));
        if (r.FinishReason != null) t.AddRow(KeyFinish, Markup.Escape(r.FinishReason));
        if (r.AnswerPreview != null) t.AddRow("answer", $"[italic]{Markup.Escape(r.AnswerPreview)}[/]");
        if (r.Error != null) t.AddRow(ErrorLabel, Markup.Escape(r.Error));
        AnsiConsole.Write(t);
        if (r.Note != null && r.Error == null) AnsiConsole.MarkupLine($"[grey]note:[/]  {Markup.Escape(r.Note)}");
    }

    public static void Structured(StructuredResult r)
    {
        if (ShortCircuit(r.Ok, () => JsonSerializer.Serialize(r, JsonContext.Default.StructuredResult))) return;
        var status = r.Ok ? IconOk : IconFail;
        AnsiConsole.MarkupLine($"{status} structured [bold]{r.Model}[/] @ [cyan]{r.Endpoint}[/]");
        var t = KvTable();
        t.AddRow(KeyLatency, $"{r.LatencyMs} ms");
        t.AddRow("parsed json", r.ParsedAsJson ? IconYes : "[red]no[/]");
        t.AddRow("schema conform", r.SchemaConformant ? IconYes : "[red]no[/]");
        if (r.SchemaViolations.Length > 0) t.AddRow("violations", Markup.Escape(string.Join("; ", r.SchemaViolations)));
        if (r.ObjectPreview != null) t.AddRow("object", $"[italic]{Markup.Escape(r.ObjectPreview)}[/]");
        t.AddRow(KeyTokens, TokenRow(r.PromptTokens, r.CompletionTokens, r.TotalTokens));
        AddFinishAndError(t, r.FinishReason, r.Error);
        AnsiConsole.Write(t);
        if (r.Note != null && r.Error == null) AnsiConsole.MarkupLine($"[grey]note:[/]  {Markup.Escape(r.Note)}");
    }

    public static void Vision(VisionResult r)
    {
        if (ShortCircuit(r.Ok, () => JsonSerializer.Serialize(r, JsonContext.Default.VisionResult))) return;
        var status = r.Ok ? IconOk : IconFail;
        AnsiConsole.MarkupLine($"{status} vision [bold]{r.Model}[/] @ [cyan]{r.Endpoint}[/]");
        var t = KvTable();
        t.AddRow(KeyLatency, $"{r.LatencyMs} ms");
        t.AddRow("image", Markup.Escape(r.ImageSource));
        t.AddRow("accepted", r.ImageAccepted ? IconYes : "[red]no[/]");
        t.AddRow(KeyTokens, TokenRow(r.PromptTokens, r.CompletionTokens, r.TotalTokens));
        if (r.FinishReason != null) t.AddRow(KeyFinish, Markup.Escape(r.FinishReason));
        if (r.ResponsePreview != null) t.AddRow("response", $"[italic]{Markup.Escape(r.ResponsePreview)}[/]");
        if (r.Error != null) t.AddRow(ErrorLabel, Markup.Escape(r.Error));
        AnsiConsole.Write(t);
    }

    public static void Tools(ToolsResult r)
    {
        if (ShortCircuit(r.Ok, () => JsonSerializer.Serialize(r, JsonContext.Default.ToolsResult))) return;
        var status = r.Ok ? IconOk : IconFail;
        AnsiConsole.MarkupLine($"{status} tools [bold]{r.Model}[/] @ [cyan]{r.Endpoint}[/]");
        var t = KvTable();
        t.AddRow(KeyLatency, $"{r.LatencyMs} ms");
        t.AddRow("tool call", r.ToolCalled ? IconYes : "[grey]no[/]");
        AddToolCallRows(t, r);
        t.AddRow(KeyTokens, TokenRow(r.PromptTokens, r.CompletionTokens, r.TotalTokens));
        AddFinishAndError(t, r.FinishReason, r.Error);
        AnsiConsole.Write(t);
    }

    private static void AddToolCallRows(Table t, ToolsResult r)
    {
        if (r.ToolCalled)
        {
            if (r.FunctionName != null) t.AddRow("function", $"[yellow]{Markup.Escape(r.FunctionName)}[/]");
            if (r.FunctionArguments != null) t.AddRow("arguments", $"[italic]{Markup.Escape(r.FunctionArguments)}[/]");
        }
        else if (r.Ok && r.Error == null)
        {
            t.AddRow("note", "no tool call (model answered directly)");
            if (!string.IsNullOrEmpty(r.ResponsePreview)) t.AddRow("response", $"[italic]{Markup.Escape(r.ResponsePreview)}[/]");
        }
    }

    public static void Completions(CompletionsResult r)
    {
        var t = BeginSupported(r.Ok, r.Supported, r.Error,
            () => JsonSerializer.Serialize(r, JsonContext.Default.CompletionsResult),
            "completions", r.Model, r.Endpoint, r.LatencyMs);
        if (t == null) return;
        if (r.Supported && r.Error == null)
        {
            if (r.FinishReason != null) t.AddRow(KeyFinish, Markup.Escape(r.FinishReason));
            t.AddRow(KeyTokens, TokenRow(r.PromptTokens, r.CompletionTokens, r.TotalTokens));
            if (r.TextPreview != null) t.AddRow("text", $"[italic]{Markup.Escape(r.TextPreview)}[/]");
        }
        EndSupported(t, r.Error, r.Note);
    }

    public static void Infill(InfillResult r)
    {
        var t = BeginSupported(r.Ok, r.Supported, r.Error,
            () => JsonSerializer.Serialize(r, JsonContext.Default.InfillResult),
            "infill", r.Model, r.Endpoint, r.LatencyMs);
        if (t == null) return;
        if (r.Supported && r.Error == null)
        {
            t.AddRow(KeyTokens, TokenRow(r.PromptTokens, r.CompletionTokens, r.TotalTokens));
            if (r.ContentPreview != null) t.AddRow("infilled", $"[italic]{Markup.Escape(r.ContentPreview)}[/]");
        }
        EndSupported(t, r.Error, r.Note);
    }

    public static void Tokenize(TokenizeResult r)
    {
        var t = BeginSupported(r.Ok, r.Supported, r.Error,
            () => JsonSerializer.Serialize(r, JsonContext.Default.TokenizeResult),
            "tokenize", r.Model, r.Endpoint, r.LatencyMs);
        if (t == null) return;
        if (r.Supported && r.Error == null)
        {
            t.AddRow("token count", $"[yellow]{r.TokenCount}[/]");
            if (r.FirstTokens.Length > 0)
                t.AddRow("first tokens", Markup.Escape("[" + string.Join(", ", r.FirstTokens) + (r.TokenCount > r.FirstTokens.Length ? ", …" : "") + "]"));
        }
        EndSupported(t, r.Error, r.Note);
    }

    public static void Logprobs(LogprobsResult r)
    {
        var t = BeginSupported(r.Ok, r.Supported, r.Error,
            () => JsonSerializer.Serialize(r, JsonContext.Default.LogprobsResult),
            "logprobs", r.Model, r.Endpoint, r.LatencyMs);
        if (t == null) return;
        if (r.Supported && r.Error == null)
        {
            t.AddRow("sampled", r.SampledTokens.ToString());
            foreach (var tok in r.Tokens)
            {
                var alts = string.Join(", ", tok.TopAlternatives.Select(a => $"{Markup.Escape(a.Token)} ({a.Logprob:F2})"));
                t.AddRow($"[yellow]{Markup.Escape(tok.Token)}[/] [grey]({tok.Logprob:F2})[/]", alts.Length > 0 ? alts : "—");
            }
            t.AddRow(KeyTokens, TokenRow(r.PromptTokens, r.CompletionTokens, r.TotalTokens));
        }
        if (r.FinishReason != null) t.AddRow(KeyFinish, Markup.Escape(r.FinishReason));
        EndSupported(t, r.Error, r.Note);
    }

    public static void Classify(ClassifyResult r)
    {
        var t = BeginSupported(r.Ok, r.Supported, r.Error,
            () => JsonSerializer.Serialize(r, JsonContext.Default.ClassifyResult),
            r.Mode, r.Model, r.Endpoint, r.LatencyMs);
        if (t == null) return;
        if (r.Supported && r.Error == null)
        {
            if (r.Score != null) t.AddRow("score", $"[yellow]{r.Score:F4}[/]");
            foreach (var l in r.Labels)
                t.AddRow(Markup.Escape(l.Label), $"[yellow]{l.Probability:F4}[/]");
        }
        EndSupported(t, r.Error, r.Note);
    }

    public static void Error(string error, string? hint = null)
    {
        if (Format == OutputFormat.Json)
        {
            Console.Error.WriteLine(JsonSerializer.Serialize(new ErrorResult(error, hint), JsonContext.Default.ErrorResult));
            return;
        }
        AnsiConsole.MarkupLine($"[red]error:[/] {error}");
        if (hint != null) AnsiConsole.MarkupLine($"[grey]hint:[/]  {hint}");
    }

    private static void Json(string s) => Console.WriteLine(s);
    private static string Yn(bool b) => b ? IconYes : "[grey]no[/]";

    // Shared preamble for the result renderers: emit the serialized JSON in --json
    // mode, or a bare ok/fail line in --quiet mode. Returns true when the output
    // was fully handled and the caller should return. serialize is a Func so the
    // concrete JsonContext.Default.XResult is only invoked when actually needed.
    private static bool ShortCircuit(bool ok, Func<string> serialize)
    {
        if (Format == OutputFormat.Json) { Json(serialize()); return true; }
        if (Quiet) { Console.WriteLine(ok ? "ok" : "fail"); return true; }
        return false;
    }

    // Shared open for the "supported-gated" renderers (completions, infill,
    // tokenize, logprobs, classify): handle --json/--quiet, print the
    // "{ok/fail} {label} {model} @ {endpoint}" header, and return a KvTable
    // pre-seeded with latency plus a "supported" row (omitted on error). Returns
    // null when ShortCircuit already produced the output and the caller should stop.
    private static Table? BeginSupported(
        bool ok, bool supported, string? error, Func<string> serialize,
        string label, string model, string endpoint, long latencyMs)
    {
        if (ShortCircuit(ok, serialize)) return null;
        AnsiConsole.MarkupLine($"{(ok ? IconOk : IconFail)} {label} [bold]{model}[/] @ [cyan]{endpoint}[/]");
        var t = KvTable();
        t.AddRow(KeyLatency, $"{latencyMs} ms");
        if (error == null) t.AddRow("supported", supported ? IconYes : "[grey]no[/]");
        return t;
    }

    // Shared close for the supported-gated renderers: append the error row, write
    // the table, then print the trailing note (suppressed when there was an error).
    private static void EndSupported(Table t, string? error, string? note)
    {
        if (error != null) t.AddRow(ErrorLabel, Markup.Escape(error));
        AnsiConsole.Write(t);
        if (note != null && error == null) AnsiConsole.MarkupLine($"[grey]note:[/]  {Markup.Escape(note)}");
    }

    // The minimal two-column key/value table shared by most renderers.
    private static Table KvTable() =>
        new Table().Border(TableBorder.Minimal).HideHeaders().AddColumn("k").AddColumn("v");

    private static string TokenRow(int prompt, int completion, int total) =>
        $"prompt=[yellow]{prompt}[/] completion=[yellow]{completion}[/] total=[yellow]{total}[/]";

    // The trailing finish-reason / error rows shared by chat-style renderers.
    private static void AddFinishAndError(Table t, string? finishReason, string? error)
    {
        if (finishReason != null) t.AddRow(KeyFinish, Markup.Escape(finishReason));
        if (error != null) t.AddRow(ErrorLabel, Markup.Escape(error));
    }
}

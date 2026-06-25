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
        if (Format == OutputFormat.Json) { Json(JsonSerializer.Serialize(r, JsonContext.Default.PingResult)); return; }
        if (Quiet) { Console.WriteLine(r.Reachable ? "ok" : "fail"); return; }
        var status = r.Reachable ? IconOk : IconFail;
        AnsiConsole.MarkupLine($"{status} [bold]{r.Endpoint}[/]");
        var t = new Table().Border(TableBorder.Minimal).HideHeaders().AddColumn("k").AddColumn("v");
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
        if (Format == OutputFormat.Json) { Json(JsonSerializer.Serialize(r, JsonContext.Default.TestResult)); return; }
        if (Quiet) { Console.WriteLine(r.Ok ? "ok" : "fail"); return; }
        var status = r.Ok ? IconOk : IconFail;
        AnsiConsole.MarkupLine($"{status} [bold]{r.Model}[/] @ [cyan]{r.Endpoint}[/]");
        var t = new Table().Border(TableBorder.Minimal).HideHeaders().AddColumn("k").AddColumn("v");
        t.AddRow(KeyLatency, $"{r.LatencyMs} ms");
        t.AddRow(KeyTokens, $"prompt=[yellow]{r.PromptTokens}[/] completion=[yellow]{r.CompletionTokens}[/] total=[yellow]{r.TotalTokens}[/]");
        if (r.FinishReason != null) t.AddRow(KeyFinish, Markup.Escape(r.FinishReason));
        if (r.ResponsePreview != null) t.AddRow("response", $"[italic]{Markup.Escape(r.ResponsePreview)}[/]");
        if (r.Error != null) t.AddRow(ErrorLabel, Markup.Escape(r.Error));
        AnsiConsole.Write(t);
    }

    public static void Stream(StreamResult r)
    {
        if (Format == OutputFormat.Json) { Json(JsonSerializer.Serialize(r, JsonContext.Default.StreamResult)); return; }
        if (Quiet) { Console.WriteLine(r.Ok ? "ok" : "fail"); return; }
        var status = r.Ok ? IconOk : IconFail;
        AnsiConsole.MarkupLine($"{status} streaming [bold]{r.Model}[/] @ [cyan]{r.Endpoint}[/]");
        var t = new Table().Border(TableBorder.Minimal).HideHeaders().AddColumn("k").AddColumn("v");
        t.AddRow("TTFT", $"[yellow]{r.TtftMs}[/] ms");
        t.AddRow("total", $"{r.TotalMs} ms");
        t.AddRow("chunks", r.Chunks.ToString());
        t.AddRow(KeyTokens, $"~{r.OutputTokensApprox}");
        t.AddRow("throughput", $"[yellow]{r.TokensPerSec:F1}[/] tok/s");
        if (r.FinishReason != null) t.AddRow(KeyFinish, Markup.Escape(r.FinishReason));
        if (r.Error != null) t.AddRow(ErrorLabel, Markup.Escape(r.Error));
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
        if (Format == OutputFormat.Json) { Json(JsonSerializer.Serialize(r, JsonContext.Default.EmbedResult)); return; }
        if (Quiet) { Console.WriteLine(r.Ok ? "ok" : "fail"); return; }
        var status = r.Ok ? IconOk : IconFail;
        AnsiConsole.MarkupLine($"{status} embed [bold]{r.Model}[/] @ [cyan]{r.Endpoint}[/]");
        var t = new Table().Border(TableBorder.Minimal).HideHeaders().AddColumn("k").AddColumn("v");
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
        if (Format == OutputFormat.Json) { Json(JsonSerializer.Serialize(r, JsonContext.Default.RerankResult)); return; }
        if (Quiet) { Console.WriteLine(r.Ok ? "ok" : "fail"); return; }
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
        if (Format == OutputFormat.Json) { Json(JsonSerializer.Serialize(r, JsonContext.Default.ReasoningResult)); return; }
        if (Quiet) { Console.WriteLine(r.Ok ? "ok" : "fail"); return; }
        var status = r.Ok ? IconOk : IconFail;
        AnsiConsole.MarkupLine($"{status} reasoning [bold]{r.Model}[/] @ [cyan]{r.Endpoint}[/]");
        var t = new Table().Border(TableBorder.Minimal).HideHeaders().AddColumn("k").AddColumn("v");
        t.AddRow(KeyLatency, $"{r.LatencyMs} ms");
        t.AddRow("reasoning", r.ReasoningDetected ? "[green]detected[/]" : "[grey]not detected[/]");
        if (r.ReasoningChannel != null) t.AddRow("channel", Markup.Escape(r.ReasoningChannel));
        if (r.ReasoningTokens > 0) t.AddRow("reasoning tokens", $"[yellow]{r.ReasoningTokens}[/]");
        if (r.ReasoningDetected) t.AddRow("split (chars)", $"thinking=[yellow]{r.ReasoningCharsApprox}[/] answer=[yellow]{r.AnswerCharsApprox}[/]");
        t.AddRow(KeyTokens, $"prompt=[yellow]{r.PromptTokens}[/] completion=[yellow]{r.CompletionTokens}[/] total=[yellow]{r.TotalTokens}[/]");
        if (r.FinishReason != null) t.AddRow(KeyFinish, Markup.Escape(r.FinishReason));
        if (r.AnswerPreview != null) t.AddRow("answer", $"[italic]{Markup.Escape(r.AnswerPreview)}[/]");
        if (r.Error != null) t.AddRow(ErrorLabel, Markup.Escape(r.Error));
        AnsiConsole.Write(t);
        if (r.Note != null && r.Error == null) AnsiConsole.MarkupLine($"[grey]note:[/]  {Markup.Escape(r.Note)}");
    }

    public static void Structured(StructuredResult r)
    {
        if (Format == OutputFormat.Json) { Json(JsonSerializer.Serialize(r, JsonContext.Default.StructuredResult)); return; }
        if (Quiet) { Console.WriteLine(r.Ok ? "ok" : "fail"); return; }
        var status = r.Ok ? IconOk : IconFail;
        AnsiConsole.MarkupLine($"{status} structured [bold]{r.Model}[/] @ [cyan]{r.Endpoint}[/]");
        var t = new Table().Border(TableBorder.Minimal).HideHeaders().AddColumn("k").AddColumn("v");
        t.AddRow(KeyLatency, $"{r.LatencyMs} ms");
        t.AddRow("parsed json", r.ParsedAsJson ? IconYes : "[red]no[/]");
        t.AddRow("schema conform", r.SchemaConformant ? IconYes : "[red]no[/]");
        if (r.SchemaViolations.Length > 0) t.AddRow("violations", Markup.Escape(string.Join("; ", r.SchemaViolations)));
        if (r.ObjectPreview != null) t.AddRow("object", $"[italic]{Markup.Escape(r.ObjectPreview)}[/]");
        t.AddRow(KeyTokens, $"prompt=[yellow]{r.PromptTokens}[/] completion=[yellow]{r.CompletionTokens}[/] total=[yellow]{r.TotalTokens}[/]");
        if (r.FinishReason != null) t.AddRow(KeyFinish, Markup.Escape(r.FinishReason));
        if (r.Error != null) t.AddRow(ErrorLabel, Markup.Escape(r.Error));
        AnsiConsole.Write(t);
        if (r.Note != null && r.Error == null) AnsiConsole.MarkupLine($"[grey]note:[/]  {Markup.Escape(r.Note)}");
    }

    public static void Vision(VisionResult r)
    {
        if (Format == OutputFormat.Json) { Json(JsonSerializer.Serialize(r, JsonContext.Default.VisionResult)); return; }
        if (Quiet) { Console.WriteLine(r.Ok ? "ok" : "fail"); return; }
        var status = r.Ok ? IconOk : IconFail;
        AnsiConsole.MarkupLine($"{status} vision [bold]{r.Model}[/] @ [cyan]{r.Endpoint}[/]");
        var t = new Table().Border(TableBorder.Minimal).HideHeaders().AddColumn("k").AddColumn("v");
        t.AddRow(KeyLatency, $"{r.LatencyMs} ms");
        t.AddRow("image", Markup.Escape(r.ImageSource));
        t.AddRow("accepted", r.ImageAccepted ? IconYes : "[red]no[/]");
        t.AddRow(KeyTokens, $"prompt=[yellow]{r.PromptTokens}[/] completion=[yellow]{r.CompletionTokens}[/] total=[yellow]{r.TotalTokens}[/]");
        if (r.FinishReason != null) t.AddRow(KeyFinish, Markup.Escape(r.FinishReason));
        if (r.ResponsePreview != null) t.AddRow("response", $"[italic]{Markup.Escape(r.ResponsePreview)}[/]");
        if (r.Error != null) t.AddRow(ErrorLabel, Markup.Escape(r.Error));
        AnsiConsole.Write(t);
    }

    public static void Tools(ToolsResult r)
    {
        if (Format == OutputFormat.Json) { Json(JsonSerializer.Serialize(r, JsonContext.Default.ToolsResult)); return; }
        if (Quiet) { Console.WriteLine(r.Ok ? "ok" : "fail"); return; }
        var status = r.Ok ? IconOk : IconFail;
        AnsiConsole.MarkupLine($"{status} tools [bold]{r.Model}[/] @ [cyan]{r.Endpoint}[/]");
        var t = new Table().Border(TableBorder.Minimal).HideHeaders().AddColumn("k").AddColumn("v");
        t.AddRow(KeyLatency, $"{r.LatencyMs} ms");
        t.AddRow("tool call", r.ToolCalled ? IconYes : "[grey]no[/]");
        AddToolCallRows(t, r);
        t.AddRow(KeyTokens, $"prompt=[yellow]{r.PromptTokens}[/] completion=[yellow]{r.CompletionTokens}[/] total=[yellow]{r.TotalTokens}[/]");
        if (r.FinishReason != null) t.AddRow(KeyFinish, Markup.Escape(r.FinishReason));
        if (r.Error != null) t.AddRow(ErrorLabel, Markup.Escape(r.Error));
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
}

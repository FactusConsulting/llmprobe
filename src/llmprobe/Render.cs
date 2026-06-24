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
    private const string ErrorLabel = "[red]error[/]";

    public static void Ping(PingResult r)
    {
        if (Format == OutputFormat.Json) { Json(JsonSerializer.Serialize(r, JsonContext.Default.PingResult)); return; }
        if (Quiet) { Console.WriteLine(r.Reachable ? "ok" : "fail"); return; }
        var status = r.Reachable ? IconOk : IconFail;
        AnsiConsole.MarkupLine($"{status} [bold]{r.Endpoint}[/]");
        var t = new Table().Border(TableBorder.Minimal).HideHeaders().AddColumn("k").AddColumn("v");
        t.AddRow("status", r.StatusCode?.ToString() ?? "—");
        t.AddRow("latency", $"{r.LatencyMs} ms");
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
        t.AddRow("latency", $"{r.LatencyMs} ms");
        t.AddRow("tokens", $"prompt=[yellow]{r.PromptTokens}[/] completion=[yellow]{r.CompletionTokens}[/] total=[yellow]{r.TotalTokens}[/]");
        if (r.FinishReason != null) t.AddRow("finish", Markup.Escape(r.FinishReason));
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
        t.AddRow("tokens", $"~{r.OutputTokensApprox}");
        t.AddRow("throughput", $"[yellow]{r.TokensPerSec:F1}[/] tok/s");
        if (r.FinishReason != null) t.AddRow("finish", Markup.Escape(r.FinishReason));
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
        t.AddRow("latency", $"{r.LatencyMs} ms");
        t.AddRow("inputs", r.Inputs.ToString());
        t.AddRow("dimensions", $"[yellow]{r.Dimensions}[/]");
        t.AddRow("norm", $"{r.Norm:F4}");
        if (r.TotalTokens > 0) t.AddRow("tokens", $"prompt=[yellow]{r.PromptTokens}[/] total=[yellow]{r.TotalTokens}[/]");
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
    private static string Yn(bool b) => b ? "[green]yes[/]" : "[grey]no[/]";
}

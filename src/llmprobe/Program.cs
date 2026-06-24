using System.Reflection;
using LlmProbe;
using Spectre.Console.Cli;

// Honor --help-ai as a global flag before Spectre.Console.Cli routing. This
// matches the convention preached in the ai-ops.dk blog post about agent-
// friendly CLI design. The subcommand 'help-ai' remains as an alias for
// discoverability in --help output.
if (args.Any(a => a == "--help-ai"))
{
    Console.WriteLine(AgentGuidance.Text);
    return 0;
}

var version = Assembly.GetExecutingAssembly()
    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
    ?.InformationalVersion ?? "0.0.0-dev";

var app = new CommandApp();
app.Configure(config =>
{
    config.SetApplicationName("llmprobe");
    config.SetApplicationVersion(version);
    config.AddCommand<PingCommand>("ping")
        .WithDescription("Reachability and latency check (no model invocation).")
        .WithExample("ping", "http://localhost:11434")
        .WithExample("ping", "https://api.openai.com", "--json");
    config.AddCommand<ModelsCommand>("models")
        .WithDescription("List available models from /v1/models.")
        .WithExample("models", "https://infer:8000")
        .WithExample("models", "https://infer:8000", "--json");
    config.AddCommand<TestCommand>("test")
        .WithDescription("Send a single chat completion; report tokens, latency, finish reason.")
        .WithExample("test", "https://infer:8000", "-m", "gemma4-26b", "-p", "Hej")
        .WithExample("test", "https://infer:8000", "-p", "@prompt.md", "--json");
    config.AddCommand<StreamCommand>("stream")
        .WithDescription("Stream a chat completion; measure TTFT and tokens/sec.")
        .WithExample("stream", "https://infer:8000", "-m", "gemma4-26b")
        .WithExample("stream", "https://infer:8000", "-p", "@-", "--json");
    config.AddCommand<EmbedCommand>("embed")
        .WithDescription("Request an embedding from /v1/embeddings; report dimensions, vector norm, latency.")
        .WithExample("embed", "https://infer:8000", "-m", "<embedding-model>", "-i", "hello world")
        .WithExample("embed", "https://infer:8000", "-i", "@doc.txt", "--json");
    config.AddCommand<RerankCommand>("rerank")
        .WithDescription("Rank documents against a query via /v1/rerank; report ordering and scores.")
        .WithExample("rerank", "https://infer:8000", "-m", "<reranker-model>", "-q", "what is the capital?", "-d", "Copenhagen is the capital", "-d", "a cat is sleeping")
        .WithExample("rerank", "https://infer:8000", "-q", "@query.txt", "-d", "@docs.txt", "--json");
    config.AddCommand<CapabilitiesCommand>("capabilities")
        .WithAlias("caps")
        .WithDescription("Detect features the endpoint supports (streaming, json mode, logprobs, ...).")
        .WithExample("capabilities", "https://infer:8000");
    config.AddCommand<HelpAiCommand>("help-ai")
        .WithDescription("Print guidance specifically for AI agents invoking this tool.");
});
return await app.RunAsync(args);

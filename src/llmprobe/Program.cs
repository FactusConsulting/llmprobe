using System.Reflection;
using LlmProbe;
using Spectre.Console.Cli;

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
        .WithExample("models", "http://infer:8000")
        .WithExample("models", "http://infer:8000", "--json");
    config.AddCommand<TestCommand>("test")
        .WithDescription("Send a single chat completion; report tokens, latency, finish reason.")
        .WithExample("test", "http://infer:8000", "-m", "gemma4-26b", "-p", "Hej")
        .WithExample("test", "http://infer:8000", "-p", "@prompt.md", "--json");
    config.AddCommand<StreamCommand>("stream")
        .WithDescription("Stream a chat completion; measure TTFT and tokens/sec.")
        .WithExample("stream", "http://infer:8000", "-m", "gemma4-26b")
        .WithExample("stream", "http://infer:8000", "-p", "@-", "--json");
    config.AddCommand<CapabilitiesCommand>("capabilities")
        .WithAlias("caps")
        .WithDescription("Detect features the endpoint supports (streaming, json mode, logprobs, ...).")
        .WithExample("capabilities", "http://infer:8000");
    config.AddCommand<HelpAiCommand>("help-ai")
        .WithDescription("Print guidance specifically for AI agents invoking this tool.");
});
return await app.RunAsync(args);

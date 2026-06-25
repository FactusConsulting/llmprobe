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

// Repeated CLI-example placeholders, hoisted to satisfy the no-duplicate-literal
// rule and keep the example wording consistent across commands.
const string ModelArg = "<model>";
const string ClassifyCmd = "classify";

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
    config.AddCommand<VisionCommand>("vision")
        .WithDescription("Probe whether the model accepts image input (multimodal chat).")
        .WithExample("vision", "https://infer:8000", "-m", "<vision-model>", "-i", "https://example.com/cat.png")
        .WithExample("vision", "https://infer:8000", "-i", "./diagram.png", "--json");
    config.AddCommand<ToolsCommand>("tools")
        .WithDescription("Probe whether the model performs function/tool calling.")
        .WithExample("tools", "https://infer:8000", "-m", ModelArg)
        .WithExample("tools", "https://infer:8000", "-p", "What's the weather in Paris? Use the tool.", "--json");
    config.AddCommand<ReasoningCommand>("reasoning")
        .WithDescription("Probe a thinking/reasoning model; detect reasoning_content, <think> blocks, reasoning tokens.")
        .WithExample("reasoning", "https://infer:8000", "-m", "<reasoning-model>")
        .WithExample("reasoning", "https://infer:8000", "-p", "@puzzle.txt", "--json");
    config.AddCommand<StructuredCommand>("structured")
        .WithDescription("Probe structured output: request a json_schema response and validate schema adherence.")
        .WithExample("structured", "https://infer:8000", "-m", ModelArg)
        .WithExample("structured", "https://infer:8000", "--json");
    config.AddCommand<CompletionsCommand>("completions")
        .WithDescription("Legacy text completion via /v1/completions; report finish reason, tokens, text.")
        .WithExample("completions", "https://infer:8000", "-m", ModelArg, "-p", "The capital of France is")
        .WithExample("completions", "https://infer:8000", "-p", "@prompt.txt", "--json");
    config.AddCommand<InfillCommand>("infill")
        .WithDescription("Fill-in-the-middle via llama.cpp /infill; report the infilled content.")
        .WithExample("infill", "https://infer:8000", "--prefix", "def add(a, b):\n    return ", "--suffix", "\nprint(add(2,3))")
        .WithExample("infill", "https://infer:8000", "--prefix", "@head.py", "--suffix", "@tail.py", "--json");
    config.AddCommand<TokenizeCommand>("tokenize")
        .WithDescription("Count tokens via /tokenize (OpenAI/vLLM or llama.cpp form).")
        .WithExample("tokenize", "https://infer:8000", "-m", ModelArg, "-i", "hello world")
        .WithExample("tokenize", "https://infer:8000", "-i", "@doc.txt", "--json");
    config.AddCommand<LogprobsCommand>("logprobs")
        .WithDescription("Probe token logprobs: report chosen tokens, their logprob, and top alternatives.")
        .WithExample("logprobs", "https://infer:8000", "-m", ModelArg)
        .WithExample("logprobs", "https://infer:8000", "-p", "Reply with: ok.", "--json");
    config.AddCommand<ClassifyCommand>(ClassifyCmd)
        .WithDescription("Sequence classification via /classify, or text-pair scoring via /score (vLLM).")
        .WithExample(ClassifyCmd, "https://infer:8000", "-m", "<classifier-model>", "-i", "I loved this movie!")
        .WithExample(ClassifyCmd, "https://infer:8000", "-m", "<reranker-model>", "-i", "what is the capital?", "--score", "Copenhagen is the capital")
        .WithExample(ClassifyCmd, "https://infer:8000", "-i", "@review.txt", "--json");
    config.AddCommand<CapabilitiesCommand>("capabilities")
        .WithAlias("caps")
        .WithDescription("Detect features the endpoint supports (streaming, json mode, logprobs, ...).")
        .WithExample("capabilities", "https://infer:8000");
    config.AddCommand<HelpAiCommand>("help-ai")
        .WithDescription("Print guidance specifically for AI agents invoking this tool.");
});
return await app.RunAsync(args);

# llmprobe

[![Build](https://github.com/FactusConsulting/llmprobe/actions/workflows/release.yml/badge.svg)](https://github.com/FactusConsulting/llmprobe/actions)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4.svg)](https://dotnet.microsoft.com/)

**Agent-friendly CLI for probing OpenAI-compatible LLM endpoints.** Health checks, model listing, latency measurement, streaming TTFT, capability detection — composable through pipes, colored output for humans, stable JSON for agents.

This is a reference implementation of the principles laid out in [Hvorfor en god CLI ofte slår MCP for AI-agenter](https://ai-ops.dk/blog/cli-vs-mcp-for-ai-agenter) — built explicitly to demonstrate what *agent-friendly CLI design* looks like in practice.

## What it does

```sh
llmprobe ping http://localhost:11434           # reachability + latency
llmprobe models http://infer:8000              # list available models
llmprobe test http://infer:8000 -m gemma4-26b  # one-shot chat completion
llmprobe stream http://infer:8000 -m gemma4-26b # streaming with TTFT measurement
llmprobe embed http://infer:8000 -i "hello"    # embedding: dimensions + vector norm
llmprobe rerank http://infer:8000 -q "q" -d a -d b # rank documents against a query
llmprobe vision http://infer:8000 -i ./cat.png # probe image (multimodal) input support
llmprobe tools http://infer:8000               # probe function/tool calling support
llmprobe capabilities http://infer:8000        # detect features (streaming, JSON, vision)
llmprobe help-ai                               # guidance for AI agents using this tool
```

Works against any OpenAI-compatible endpoint: **vLLM**, **llama.cpp** (`llama-server`), **Ollama**, **OpenAI**, **Anthropic** (via gateways), **OpenRouter**, **Mistral**, custom RAG-gateways.

Endpoint coverage: `ping`/`models` → `/v1/models`; `test`/`stream`/`vision`/`tools`/`capabilities` → `/v1/chat/completions`; `embed` → `/v1/embeddings`; `rerank` → `/v1/rerank`.

## Why this exists (the agent angle)

Most CLIs are designed for humans first. This one is designed to be **equally good for AI agents** — which matters more and more as agents become routine consumers of dev tooling. Concretely:

- **`--json`** on every command, with **stable snake_case field names** documented in the help
- **`--quiet`** mode for silent scripting (just "ok" or "fail" + exit code)
- **Meaningful exit codes**: `0` success, `74` unreachable, `78` config error
- **`help-ai`** subcommand with explicit agent guidance (what's safe, what to avoid, common patterns)
- **Composable**: every command works via pipes (`cat prompt.md | llmprobe stream … -p @-`)
- **Read by default**: low max_tokens defaults, no destructive operations

For humans, you get rich colored output via Spectre.Console — tables, status indicators, syntax-highlighted markup.

## Install

### Prebuilt binaries

Download from [Releases](https://github.com/FactusConsulting/llmprobe/releases) — single-file binaries for:

- Linux x64 / arm64
- macOS x64 / arm64 (Apple Silicon)
- Windows x64

Unpack and move to `~/bin/` or `/usr/local/bin/`. No runtime needed (AOT-compiled).

### Build from source

```sh
git clone https://github.com/FactusConsulting/llmprobe.git
cd llmprobe
dotnet publish src/llmprobe -c Release -o ./publish
./publish/llmprobe --help
```

Requires .NET 10 SDK.

## Examples

### Health-check a local llama.cpp server

```sh
$ llmprobe ping http://localhost:8080
✓ http://localhost:8080
status   200
latency  4 ms
```

### List models with JSON output for piping

```sh
$ llmprobe models http://infer:8000 --json | jq -r '.models[]'
gemma4-26b-q6k
gemma4-4b-bf16
```

### Measure time-to-first-token (TTFT)

```sh
$ llmprobe stream http://infer:8000 -m gemma4-26b -p "Skriv en haiku på dansk"
✓ streaming gemma4-26b @ http://infer:8000
TTFT        38 ms
total       512 ms
chunks      27
tokens      ~115 (approx)
throughput  242.7 tok/s
finish      stop
```

### Probe multimodal (image) input

```sh
# Remote image URL
$ llmprobe vision http://infer:8000 -m qwen2.5-vl -i https://example.com/cat.png
✓ vision qwen2.5-vl @ http://infer:8000
latency   880 ms
image     https://example.com/cat.png
accepted  yes
tokens    prompt=1024 completion=2 total=1026
finish    stop
response  cat

# Local file is read and inlined as a base64 data: URL (png/jpeg/webp/gif)
llmprobe vision http://infer:8000 -i ./diagram.png -p "What does this show?" --json
```

If the model can't accept images, `accepted` is `no` and the server's error
(e.g. "image input not supported") is reported in the `error` field.

### Probe function / tool calling

```sh
$ llmprobe tools http://infer:8000 -m gpt-4o
✓ tools gpt-4o @ http://infer:8000
latency    640 ms
tool call  yes
function   get_weather
arguments  {"location":"Copenhagen"}
tokens     prompt=80 completion=18 total=98
finish     tool_calls
```

It sends a single `get_weather(location)` tool definition with `tool_choice: auto`
and a prompt that should trigger it. If the model answers directly instead, `tool
call` is `no` and the direct response is shown.

### Compose with shell

```sh
# Send a long prompt from a file
cat ./prompts/regression-test.md | llmprobe stream http://infer:8000 -p @- --json > result.json

# Quick smoke test in CI
if llmprobe ping http://infer:8000 --quiet >/dev/null; then
  echo "infer up"
else
  exit 1
fi

# Compare TTFT across two endpoints
for ep in vllm:8000 llamacpp:8081; do
  llmprobe stream "http://$ep" -m default -p "hej" --json | jq "{ep:\"$ep\",ttft:.ttft_ms}"
done
```

## Authentication

For hosted endpoints (OpenAI, Anthropic via gateway, OpenRouter, vLLM with `--api-key`),
provide a bearer token via flag or environment variable. The flag wins if both are set:

```sh
# Flag-based (overrides env)
llmprobe ping https://api.openai.com --api-key sk-proj-...
llmprobe test https://api.openai.com -m gpt-5 -p "Hej" --api-key sk-proj-...

# Environment variable (preferred for scripts and CI — keeps secrets out of shell history)
export OPENAI_API_KEY=sk-proj-...
llmprobe models https://api.openai.com
llmprobe test https://api.openai.com -m gpt-5 -p "Hej"

# Per-call without leaving in env or history
OPENAI_API_KEY=sk-proj-... llmprobe ping https://api.openai.com
```

Despite the env var name `OPENAI_API_KEY`, it's used as a generic bearer token — works
with any OpenAI-compatible endpoint:

```sh
# OpenRouter (one key, many models)
OPENAI_API_KEY=sk-or-v1-... llmprobe test https://openrouter.ai/api -m anthropic/claude-sonnet-4-6 -p "Hej"

# vLLM secured with --api-key
OPENAI_API_KEY=my-vllm-token llmprobe stream http://infer:8000 -m gemma4-26b -p "Test"

# Anthropic via OpenAI-compatible gateway
OPENAI_API_KEY=sk-ant-... llmprobe test https://gateway.example.com -m claude-sonnet-4-6 -p "Hej"

# Local llama.cpp / Ollama (no auth needed)
llmprobe models http://localhost:11434
```

If you forget the key on a hosted endpoint, `llmprobe` reports the 401/403 response
verbatim in the `error` field so you can tell auth failures from network failures.

## JSON schema

All `--json` output uses stable snake_case field names. Examples:

**`ping --json`:**
```json
{
  "endpoint": "http://infer:8000",
  "reachable": true,
  "status_code": 200,
  "latency_ms": 4,
  "server_header": "uvicorn",
  "error": null
}
```

**`stream --json`:**
```json
{
  "endpoint": "http://infer:8000",
  "model": "gemma4-26b",
  "ok": true,
  "ttft_ms": 38,
  "total_ms": 512,
  "chunks": 27,
  "output_tokens_approx": 115,
  "tokens_per_sec": 242.7,
  "finish_reason": "stop",
  "error": null
}
```

Field names are part of the public contract — they won't change in patch/minor releases.

## Exit codes

| Code | Meaning |
| ---- | ------- |
| `0`  | Success |
| `74` | Endpoint unreachable or non-2xx HTTP (transient — safe to retry) |
| `78` | Configuration error (e.g. missing `OPENAI_API_KEY`) |
| `1`  | Unexpected error |

## Global options

| Flag | Description |
| ---- | ----------- |
| `--json` | Emit machine-readable JSON to stdout |
| `--quiet` | Minimal output ("ok" / "fail" + exit code) |
| `--timeout <SEC>` | HTTP timeout (default 30s) |
| `--api-key <KEY>` | Bearer token. Falls back to `OPENAI_API_KEY` env var |

## License

MIT © Factus Consulting ApS

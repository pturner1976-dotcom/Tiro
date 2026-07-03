# Architecture

## Overview

Tiro is a bounded local system. It consists of a single .NET 8 CLI process, a SQLite database, and optional integration with a local or remote embedding provider. A calling agent or shell wrapper invokes Tiro commands via its CLI surface. Tiro handles everything from schema initialization through lexical scoring, query planning, proxy indexing, and JSON packet assembly. It does not run continuously — it is invoked per command and exits.

```
caller (agent / shell wrapper / script)
   |
   +--> Tiro CLI (.NET 8)
              |
              +--> SQLite database ($TIRO_DB_PATH)
              |
              +--> embedding provider (optional)
              |         Ollama (local) or OpenAI embeddings
              |
              +--> query planner (optional)
                        bounded external API call, advisory only
```

## System Boundaries

### Tiro's responsibilities

Tiro owns:

- Schema initialization and idempotent migration via `init`
- All corpus ingestion: sources, documents, chunks
- All session memory ingestion: session creation, message appending, session YAML import
- All operational memory: decisions, todos, warnings, unknowns
- Fact lifecycle storage and status tracking
- Proxy index construction and evidence pointer management
- Lexical scoring, semantic scoring, and hybrid score merging
- Retrieval policy selection based on query intent analysis
- Query planning (optional, advisory, bounded external call)
- Structured JSON output for every command

### What Tiro does not own

Tiro does not own:

- The calling agent's runtime, model, or role configuration
- The agent framework's tool manifest or function dispatch
- The display layer, transcript rendering, or user interface
- Any decision about what to do with retrieved evidence
- Network access beyond the optional embedding and planner providers

The caller decides which Tiro commands to invoke, what to do with the output, and whether to trust or reject retrieved evidence.

## Authority Model

### Memory authority

Tiro's SQLite database is the authoritative store for all structured memory. Session artifacts, saved chat logs, and external files are not authoritative until explicitly ingested into Tiro and associated with a provenance record.

A saved chat session imported via `ingest-aichat-session` becomes session evidence in Tiro. It is evidence that a conversation occurred — it is not automatically promoted to operational records or facts. Promotion requires an explicit operator action.

### Tool authority

The calling agent or agent framework decides which Tiro commands to surface as tools and what parameter boundaries to apply. Tiro executes only what it is told via its CLI surface. Most Tiro commands are read-only. Write commands (`ingest-*`, `decision-add`, `fact-add`, `proxy-build`, etc.) must be surfaced explicitly and deliberately by the caller.

### Operator authority

The operator controls:

- `TIRO_DB_PATH` — which database is active
- When to run ingestion commands
- When to rebuild the proxy index
- Whether to trust or override retrieved evidence
- Deployment of Tiro as a tool to a calling agent

The agent framework and model are advisory inputs. The operator remains the authority over mutation, ingestion, and deployment decisions.

## Lane Model Summary

Tiro separates memory into four primary lanes rather than a single flat store. Each lane has distinct storage tables, distinct ingestion commands, and distinct retrieval behavior.

| Lane | Tables | Purpose |
|------|--------|---------|
| Corpus | `sources`, `documents`, `chunks` | Durable imported document evidence |
| Session | `sessions`, `messages` | Chronological conversation and session artifacts |
| Operational | `operational_records` | Explicit decisions, todos, warnings, unknowns |
| Fact lifecycle | `facts`, `fact_conflicts` | Lifecycle-aware claims with explicit status |

A fifth structural layer — the proxy/pointer layer — builds lightweight index records over corpus evidence to improve structured recall precision. It is not a primary memory lane; it is scaffolding over the corpus.

See [MEMORY_MODEL.md](MEMORY_MODEL.md) for the complete lane model including evidence discipline rules and fact lifecycle statuses.

## Where the CLI Fits

Every Tiro capability is exposed exclusively through the CLI. There is no library API, no HTTP server, and no background daemon. A caller integrates Tiro by invoking the binary, reading JSON from stdout, and using the exit code to detect errors.

This design makes Tiro wrappable by any agent framework that can execute shell commands and parse JSON. The published binary (`dotnet publish`) is the recommended runtime form. Using `dotnet run` is supported but adds process startup overhead on each invocation.

See [CLI_REFERENCE.md](CLI_REFERENCE.md) for the full command surface.

## Optional Components

### Embedding provider

When `TIRO_SEMANTIC_ENABLED=true`, Tiro calls an embedding API to generate and store vector embeddings for corpus chunks. Two providers are supported: `local` (Ollama, running on `localhost:11434` by default) and `openai`. Semantic search is merged with lexical scoring by configurable lane weights. Tiro falls back silently to lexical-only retrieval if the embedding provider is unavailable.

### Query planner

When a Gemini API key is present and the planner mode is `auto` or `on`, Tiro makes a single bounded external call to refine query terms before lexical scoring. The planner output is advisory. Tiro always falls back to the unrefined deterministic lexical query if the planner fails, times out, or returns invalid JSON. Planner presence does not raise reported retrieval confidence.

## Deployment Patterns

Tiro is designed for local deployment alongside the agent it serves. Common patterns:

- Single agent, single database: one `$TIRO_DB_PATH` shared by all sessions of one agent.
- Multi-corpus: multiple sources ingested into one database, filtered at query time by `--source-id` or `--document-id`.
- Isolated databases: separate `$TIRO_DB_PATH` values for different projects or agents, switched by the caller.
- Wrapped as an agent tool: the caller exposes selected Tiro commands as tools in its function manifest; Tiro handles retrieval and memory; the agent handles reasoning and response.

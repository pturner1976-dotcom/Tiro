# Getting Started

This guide takes you from a fresh clone to a working Tiro database with your first retrieval query in about five minutes.

## Requirements

- .NET 8 SDK
- SQLite (`libsqlite3`) — present by default on Linux and macOS; on Windows install the [SQLite runtime](https://sqlite.org/download.html)
- For semantic search: [Ollama](https://ollama.com) (local) or an OpenAI API key

## Build

```bash
dotnet build Tiro.sln
```

To produce a published binary that eliminates `dotnet run` overhead:

```bash
dotnet publish src/Tiro.Cli -c Release -o ./publish
```

The published binary is at `./publish/Tiro.Cli`. Commands below use this form. Substitute `dotnet run --project src/Tiro.Cli --` if you prefer to run from source.

## Set the Database Path

Tiro reads `TIRO_DB_PATH` from the environment to locate the database. Set it once per shell session to avoid passing `--db` on every call:

```bash
export TIRO_DB_PATH=$HOME/.local/share/tiro/tiro.sqlite3
```

You can use any path you prefer, including a project-local path:

```bash
export TIRO_DB_PATH=./data/tiro.sqlite3
```

## Initialize the Database

```bash
./publish/Tiro.Cli init
```

This creates the SQLite database at `$TIRO_DB_PATH` and initializes the schema idempotently. Running `init` on an existing database is safe.

## Ingest a Corpus

Corpus ingestion takes a JSONL file — one chunk object per line. Each object must include at least a `text` field. Additional fields such as `source`, `document`, and `metadata` are recorded as provenance.

```bash
./publish/Tiro.Cli ingest-chunks corpus.jsonl
```

Check what was ingested:

```bash
./publish/Tiro.Cli stats
./publish/Tiro.Cli inspect sources
```

## Run Your First Query

```bash
./publish/Tiro.Cli query deployment token rotation
```

Output is a structured JSON context packet. The packet includes primary evidence, supporting context, session evidence, operational memory records, facts, warnings, unknowns, and a retrieval policy summary. Pipe to `jq` for readable output:

```bash
./publish/Tiro.Cli query deployment token rotation | jq .
```

## Add Operational Memory

Operational records capture decisions, tasks, warnings, and open unknowns outside the corpus lane:

```bash
./publish/Tiro.Cli decision-add cli-setup "Chose TIRO_DB_PATH over per-command --db flags for this deployment"
./publish/Tiro.Cli warning-add ops "Corpus not yet indexed for semantic search; lexical only for now"
./publish/Tiro.Cli todo-add ops "Run semantic-index once Ollama is running"
```

These records appear in the `operational_memory` section of every query packet.

## Check Database Stats

```bash
./publish/Tiro.Cli stats
```

Returns a JSON summary with counts for sources, documents, chunks, sessions, messages, operational records, facts, and proxies.

## Enable Semantic Search with Ollama

By default Tiro uses lexical (BM25-style) scoring only. To add vector semantic search with a local Ollama model:

```bash
export TIRO_SEMANTIC_ENABLED=true
export TIRO_EMBEDDING_PROVIDER=local
export TIRO_EMBEDDING_MODEL=mxbai-embed-large
export TIRO_EMBEDDING_BASE_URL=http://localhost:11434/v1

# Pull the embedding model
ollama pull mxbai-embed-large

# Index all un-embedded corpus chunks
./publish/Tiro.Cli semantic-index
```

After indexing, `query` and `hybrid-search` will merge lexical and semantic scores automatically. To verify the embedding configuration:

```bash
./publish/Tiro.Cli semantic-status
```

## Next Steps

- [ARCHITECTURE.md](ARCHITECTURE.md) — how Tiro fits into an agent system
- [MEMORY_MODEL.md](MEMORY_MODEL.md) — the four lanes and evidence discipline rules
- [CLI_REFERENCE.md](CLI_REFERENCE.md) — full command reference with all flags
- [retrieval/RETRIEVAL_PIPELINE.md](retrieval/RETRIEVAL_PIPELINE.md) — how scoring and packet assembly work
- [api/TOOL_SURFACE.md](api/TOOL_SURFACE.md) — exposing Tiro as a tool to your agent framework

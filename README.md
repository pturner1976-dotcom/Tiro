# Tiro

A standalone local memory and retrieval engine for AI agents, built on .NET 8 and SQLite with **zero external dependencies**.

Tiro keeps corpus retrieval, session memory, structured operational memory, and lifecycle-aware facts distinct — then assembles them into compact, structured JSON context packets that an AI agent can use directly. It is designed to be the memory spine of a long-running agent: deterministic, evidence-first, and inspectable.

---

## Why Tiro

Most RAG tooling either couples tightly to a specific LLM framework or pulls in a long chain of external dependencies. Tiro does neither. The entire retrieval pipeline — lexical scoring, semantic search, query planning, fact lifecycle tracking, session memory — runs locally with nothing beyond the .NET standard library and SQLite via a direct P/Invoke to `libsqlite3`.

Key design decisions:

- **Hybrid retrieval** — combines deterministic lexical scoring with optional vector semantic search (Ollama or OpenAI embeddings), merged by configurable lane weights
- **Lifecycle-aware facts** — facts carry explicit status (`active`, `stale`, `superseded`, `conflicting`, `expired`) and are scored accordingly in retrieval packets
- **Intent-driven retrieval policy** — query terms are analysed at runtime to select a retrieval mode (`current-state`, `decision`, `archive`, `fact-lifecycle`, etc.) that weights evidence accordingly
- **Optional LLM query planning** — a bounded Gemini call can refine lexical terms before retrieval; it is purely advisory and Tiro always falls back to deterministic retrieval if the planner fails
- **Structured JSON output** — every command emits a machine-readable JSON object, making Tiro trivially wrappable as a tool for any LLM agent framework
- **No external NuGet dependencies** — the entire codebase runs on the .NET 8 standard library

---

## Getting Started

### Requirements

- .NET 8 SDK
- SQLite (`libsqlite3`) — present by default on Linux/macOS; on Windows install the [SQLite runtime](https://sqlite.org/download.html)
- For semantic search: [Ollama](https://ollama.com) (local) or an OpenAI API key

### Build

```bash
dotnet build Tiro.sln
```

### Quick start

```bash
# Initialise a new database
dotnet run --project src/Tiro.Cli -- init --db ./mydata/tiro.sqlite3

# Ingest a corpus from a JSONL file (one chunk object per line)
dotnet run --project src/Tiro.Cli -- ingest-chunks corpus.jsonl --db ./mydata/tiro.sqlite3

# Run a retrieval query
dotnet run --project src/Tiro.Cli -- query deployment token rotation --db ./mydata/tiro.sqlite3

# Check database stats
dotnet run --project src/Tiro.Cli -- stats --db ./mydata/tiro.sqlite3
```

Set `TIRO_DB_PATH` to avoid passing `--db` on every call:

```bash
export TIRO_DB_PATH=/path/to/tiro.sqlite3
dotnet run --project src/Tiro.Cli -- query deployment token rotation
```

The published binary eliminates the `dotnet run` overhead:

```bash
dotnet publish src/Tiro.Cli -c Release -o ./publish
./publish/Tiro.Cli query deployment token rotation
```

---

## CLI Reference

All commands emit structured JSON to stdout. Exit code is `0` on success, `1` on error.

### Database

| Command | Description |
|---------|-------------|
| `init` | Initialise a new SQLite database with the Tiro schema |
| `stats` | Summary counts for all lanes |
| `inspect [mode]` | Detailed state inspection (`sessions`, `operational`, `sources`, `documents`, `recent`, `stats`, `proxies`) |

### Corpus ingestion

| Command | Description |
|---------|-------------|
| `ingest-chunks <file.jsonl>` | Bulk-ingest corpus chunks from a JSONL file |
| `ingest-session-note` | Add a freeform note to a session |
| `ingest-operational-record` | Add a structured operational record |
| `ingest-aichat-session` | Ingest an aichat session YAML as session messages |
| `inspect-aichat-sessions` | List and inspect available aichat session files |

### Retrieval

| Command | Description |
|---------|-------------|
| `query <terms...>` | Main retrieval — returns a full context packet |
| `recall <terms...>` | Natural-language recall across all lanes |
| `search-debug <terms...>` | Retrieval with full scoring diagnostics |
| `phrase-search <phrase>` | Exact phrase search |
| `session-search <session_id> <terms...>` | Lexical search within a specific session |
| `semantic-query <terms...>` | Pure vector semantic search |
| `hybrid-search <terms...>` | Merged lexical + semantic search |

Retrieval flags (apply to `query`, `recall`, `search-debug`):

```
--limit <n>             Maximum results (default 20)
--context-window <0..2> Adjacent chunk context to include (default 0)
--session-id <id>       Include session evidence in the packet
--source-id <id>        Filter to a specific source
--document-id <id>      Filter to a specific document
--planner off|auto|on   Query planning mode (default: auto)
--debug-planner         Include planner diagnostics in output
--lanes <lanes>         Comma-separated lane list (corpus,session,facts,operational)
```

### Session memory

| Command | Description |
|---------|-------------|
| `session-create <session_id>` | Create a new session |
| `session-ingest <session_id> <direction> <source_identity> <text>` | Append a message to a session |
| `session-recent <session_id>` | Retrieve most recent messages |
| `session-query <session_id> <terms...>` | Lexical search within a session |
| `session-summary <session_id>` | Summarised view of a session |

### Operational memory

Records in this lane carry structured type (`decision`, `todo`, `unknown`, `warning`) and appear in query packets under `operational_memory`. Add `--session-id` to scope them to a session.

| Command | Description |
|---------|-------------|
| `decision-add <origin> <text>` | Record a decision |
| `todo-add <origin> <text>` | Record a pending task |
| `unknown-add <origin> <text>` | Record an open unknown |
| `warning-add <origin> <text>` | Record a warning |
| `decision-list` / `todo-list` / `unknown-list` / `warning-list` | List records by type |

### Fact lifecycle

Facts carry explicit status. Retrieval scores them accordingly — active facts score positively; stale, superseded, conflicting, and expired facts are penalised and surfaced with warnings.

| Command | Description |
|---------|-------------|
| `fact-add <text>` | Add a new active fact |
| `fact-list` | List all facts with status |
| `fact-update-status <fact_id> <status>` | Update fact status |
| `fact-supersede <old_fact_id> <new_text>` | Replace a fact and mark the old one superseded |
| `fact-conflict <fact_id_a> <fact_id_b>` | Mark two facts as conflicting |

### Proxy layer

The proxy layer builds lightweight index structures over corpus evidence for faster structured recall.

| Command | Description |
|---------|-------------|
| `proxy-build` | Build corpus proxy index |
| `proxy-inspect` | Inspect proxy index state |
| `proxy-recall <terms...>` | Recall via proxy layer |
| `proxy-hydrate <proxy_id>` | Hydrate a proxy pointer to its target evidence |
| `proof-carry-forward` | Validate carry-forward integrity |

### Semantic search

| Command | Description |
|---------|-------------|
| `semantic-status` | Show current embedding configuration |
| `semantic-index` | Index all un-embedded corpus chunks |

---

## Configuration

All configuration is via environment variables. No config files are required.

| Variable | Description | Default |
|----------|-------------|---------|
| `TIRO_DB_PATH` | Path to the SQLite database | `data/tiro.sqlite3` |
| `TIRO_SEMANTIC_ENABLED` | Enable semantic search | `false` |
| `TIRO_EMBEDDING_PROVIDER` | `local` (Ollama) or `openai` | `none` |
| `TIRO_EMBEDDING_MODEL` | Embedding model name | `text-embedding-3-small` |
| `TIRO_EMBEDDING_BASE_URL` | Base URL for embedding API | `https://api.openai.com/v1` |
| `OPENAI_API_KEY` | OpenAI API key (if provider is `openai`) | — |
| `GEMINI_API_KEY` | Gemini API key for query planning | — |
| `GEMINI_PLANNER_MODEL` | Gemini model for planning | `gemini-2.5-flash` |
| `AICHAT_SESSIONS_DIR` | Override aichat sessions directory | `~/.config/aichat/sessions` |
| `AICHAT_CONFIG_DIR` | Override aichat config directory | `~/.config/aichat` |

### Local embeddings (Ollama)

```bash
export TIRO_SEMANTIC_ENABLED=true
export TIRO_EMBEDDING_PROVIDER=local
export TIRO_EMBEDDING_MODEL=mxbai-embed-large
export TIRO_EMBEDDING_BASE_URL=http://localhost:11434/v1

# Pull the model, then index your corpus
ollama pull mxbai-embed-large
./Tiro.Cli semantic-index
```

---

## Retrieval Behaviour

### Lexical scoring

Query text is tokenised, lowercased, deduplicated, lightly suffix-normalised, and stripped of stopwords. Scoring uses token hit counts with small deterministic bonuses for term coverage, exact ordered phrase matches, and evidence concentration in shorter chunks.

Each result includes `rank`, `matched_terms`, per-term `score_details`, a `score_summary`, and a compact explanation string.

### Retrieval policy

Query intent is detected from normalised terms and selects a retrieval mode:

| Mode | Detected from | Emphasis |
|------|--------------|----------|
| `current-state` | current, now, latest, active, status | Recent session evidence, active facts |
| `decision` | decide, decided, chose, intent | Operational decisions |
| `unresolved` | todo, blocked, unknown, open | TODOs, warnings, unknowns |
| `archive` | source, document, provenance, reference | Corpus evidence |
| `fact-lifecycle` | conflict, stale, superseded, lifecycle | Lifecycle facts with status warnings |
| `general` | (no mode terms matched) | Balanced across all lanes |

Each evidence item in the packet carries five composable scores:

- `relevance_score` — lexical match strength
- `recency_score` — deterministic age bucket relative to newest candidate
- `importance_score` — lane/type weighting (decisions and warnings rank higher than archive chunks)
- `lifecycle_score` — active facts score positively; degraded status penalises
- `mode_score` — mode-specific emphasis

Final score is reported as `final=relevance+recency+importance+lifecycle+mode`.

### Query planning (optional)

When a Gemini API key is present and `--planner auto` or `--planner on` is set, Tiro makes a single bounded Gemini call requesting a small JSON payload with a refined `search_query`, `refined_terms`, and `packet_focus`. The output is advisory: Tiro runs the refined lexical query deterministically and falls back silently if the planner fails, times out, or returns invalid JSON. Planner presence never raises retrieval confidence.

---

## Output Format

Every `query` call returns a structured JSON packet:

```json
{
  "query": "deployment token rotation",
  "normalized_terms": ["deployment", "token", "rotation"],
  "confidence": "medium",
  "retrieval_policy": { "query_mode": "current-state", "mode_reason": "..." },
  "primary_evidence": [ ... ],
  "supporting_context": [ ... ],
  "session_evidence": [ ... ],
  "recent_session_context": [ ... ],
  "operational_memory": { "decisions": [...], "todos": [...], "unknowns": [...], "warnings": [...] },
  "facts": [ ... ],
  "unknowns": [ ... ],
  "warnings": [ ... ],
  "sources": [ ... ],
  "planner_status": "used|skipped|fallback|disabled"
}
```

Filters are never silently broadened. If a `--source-id` or `--document-id` filter matches nothing, the packet reports that explicitly rather than returning unfiltered results.

---

## Schema

The SQLite schema is initialised idempotently by `init` and contains:

- `schema_info` — schema version metadata
- `sources` — registered corpus sources
- `documents` — document registry
- `chunks` — corpus text chunks with provenance
- `sessions` — session registry
- `messages` — session messages with direction and source identity
- `operational_records` — decisions, todos, unknowns, warnings
- `facts` — lifecycle-aware facts with status tracking
- `embeddings` — optional vector embeddings per chunk

---

## Testing

```bash
dotnet test tests/Tiro.Cli.Tests
```

The test suite uses temporary in-process SQLite databases and covers retrieval scoring, fact lifecycle, session memory, operational records, planner fallback paths, semantic expansion, and aichat session ingestion.

---

## License

Tiro is licensed under the GNU Affero General Public License v3.0 only
(`AGPL-3.0-only`). See [LICENSE](LICENSE) for the full license text.

Copyright (C) 2026 Patrick Turner.

You may use, study, modify, and redistribute Tiro under the terms of the AGPL.
If you modify Tiro and make the modified version available for use over a network,
you must provide users interacting with that modified version an opportunity to
receive the corresponding source code, as required by AGPL section 13.

**Commercial licensing** — organizations that want to use, embed, modify,
distribute, host, or offer Tiro-based products or services without AGPL
source-sharing obligations may obtain a separate commercial license. See
[COMMERCIAL-LICENSE.md](COMMERCIAL-LICENSE.md) or contact pturner1976@gmail.com.

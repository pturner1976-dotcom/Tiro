# Tiro CLI Reference

The `tiro` CLI is the primary interface for corpus ingestion, memory retrieval, session
and operational record management, proxy construction, and maintenance tasks.

Structured service commands return JSON. Older maintenance commands return plain text
status lines.

---

## Global Options

```
tiro [--db <path>] [--limit <n>] [--source-id <id>] [--document-id <id>] \
     [--context-window 0..2] [--session-id <id>] [--planner on|off|auto] \
     [--debug-planner] <command> [<args>...]
```

| Option | Default | Description |
|---|---|---|
| `--db <path>` | `./data/tiro.sqlite3` | Path to the SQLite database file. In production use, set `TIRO_DB_PATH` in the environment; tool wrappers require an explicit path and will not guess. |
| `--limit <n>` | command-dependent | Maximum number of results to return. |
| `--source-id <id>` | unset | Restrict retrieval to a specific corpus source. |
| `--document-id <id>` | unset | Restrict retrieval to a specific document. |
| `--context-window 0..2` | 0 | Number of neighboring chunks to include around each matching chunk. |
| `--session-id <id>` | unset | Scope session retrieval or writes to a specific session. |
| `--planner on\|off\|auto` | `auto` | Enable or disable the query planner. `auto` uses the planner when it improves results. |
| `--debug-planner` | off | Emit planner internals in output for debugging. |

---

## Corpus and Inventory

### `ingest-chunks`

```
tiro [--db <path>] ingest-chunks <path>
```

Ingest a JSONL chunk corpus file into the database.

- Input: path to a Tiro chunk JSONL file (one chunk object per line).
- Output: plain text report — lines read, chunks inserted, duplicates skipped,
  sources and documents registered, total ingest time.
- Common mistake: pointing at a raw text or PDF file instead of a Tiro chunk JSONL.

---

### `stats` / `inspect stats`

```
tiro [--db <path>] stats
tiro [--db <path>] inspect stats
```

Return database counts and size summary.

- Output: structured JSON stats object with counts per lane and total database size.

---

### `inspect sources`

```
tiro [--db <path>] inspect sources [--limit <n>]
```

List known corpus sources.

---

### `inspect documents`

```
tiro [--db <path>] inspect documents [--source-id <id>] [--limit <n>]
```

List documents, optionally filtered by source.

- Common mistake: passing a lane name (e.g., `corpus`) instead of a numeric `source_id`.

---

## Query and Retrieval

### `query`

```
tiro [--db <path>] [--limit <n>] [--source-id <id>] [--document-id <id>] \
     [--context-window 0..2] [--session-id <id>] [--planner on|off|auto] \
     [--debug-planner] query <query-text>
```

Targeted evidence retrieval returning a `ContextPacket`. This is the primary read path.

**Output fields:**

| Field | Description |
|---|---|
| `query` | The original query string |
| `normalized_terms` | Tokenized and normalized query terms used for retrieval |
| `facts` | Lifecycle-aware facts relevant to the query |
| `warnings` | Warnings produced during retrieval |
| `unknowns` | Open unknowns relevant to the query |
| `sources` | Source metadata for returned evidence |
| `filters` | Active filters applied to the query |
| `primary_evidence` | Top-ranked corpus chunks |
| `supporting_context` | Additional supporting chunks |
| `session_evidence` | Relevant session messages |
| `operational_memory` | Relevant operational records (decisions, todos, etc.) |
| `retrieval_policy` | Summary of the retrieval policy applied |
| `planner` | Planner state (present when planner is active or `--debug-planner` is set) |

**Common mistakes:**
- Using a lane name as `--source-id` — the value must be a numeric source ID from `inspect sources`.
- Expecting session recall without supplying `--session-id`.
- Using `query` for concept-heavy or synonym-heavy prompts — `query` uses lexical scoring. Use `hybrid-search` or `semantic-query` when embeddings are configured.

---

### `search-debug`

```
tiro [--db <path>] [--limit <n>] [--source-id <id>] [--document-id <id>] \
     [--session-id <id>] [--planner on|off|auto] [--debug-planner] \
     search-debug <query-text>
```

Diagnostic view of query execution. Returns normalized terms, planner state, candidate
counts, top scores, and per-lane diagnostics. Use this to understand why a query returns
or does not return expected content.

---

### `recall`

```
tiro [--db <path>] [--limit <n>] [--planner on|off|auto] [--debug-planner] \
     recall <query-text> [--session-limit <n>] [--source-limit <n>] [--document-limit <n>]
```

Broad natural recall across corpus, sessions, operational memory, and facts. Does not
require knowing source IDs or session IDs in advance.

**Output fields:**

| Field | Description |
|---|---|
| `status` | Overall recall status |
| `query` | The original query string |
| `intent` | Planner-inferred intent (when planner is active) |
| `evidence` | Retrieved content across all lanes |
| `sources` | Source metadata |
| `documents` | Document metadata |
| `sessions` | Session metadata |
| `warnings` | Retrieval warnings |
| `unknowns` | Open unknowns |
| `proxy_candidates` | Proxy records matched during recall |
| `proxy_hydrated_evidence` | Authoritative evidence hydrated from matched proxies |

Falls back cleanly when proxies have not been built.

---

### `phrase-search`

```
tiro [--db <path>] [--session-id <id>] [--limit <n>] phrase-search <phrase> \
     [--lane session|operational|corpus|facts|all]
```

Exact substring search over stored content. Use this to verify that ingestion
succeeded and to diagnose scope issues.

---

### `session-search`

```
tiro [--db <path>] [--limit <n>] session-search <query-text> [--session-limit <n>]
```

Search the newest saved session evidence without needing to know a `session_id`.
Useful for "what did we discuss recently" queries.

---

### `session-summary`

```
tiro [--db <path>] [--limit <n>] session-summary <session_id>
```

Chronological readout of all messages in a session, in timestamp order.

---

## Proxy and Pointer Commands

### `proxy-build corpus`

```
tiro [--db <path>] proxy-build corpus [--document-id <id>] [--source-id <id>] [--rebuild]
```

Build corpus proxies and evidence pointers for the stored corpus. This is an operator
maintenance task, not a step performed during normal agent operation.

**Output fields:**

| Field | Description |
|---|---|
| `status` | `ok` or `error` |
| `db_path` | Database path used |
| `mode` | `incremental` or `rebuild` |
| `source_id` | Source filter applied, if any |
| `document_id` | Document filter applied, if any |
| `proxies_created` | Number of new proxies created |
| `proxies_superseded` | Number of existing proxies superseded |
| `pointers_created` | Number of evidence pointers created |
| `warnings` | Any warnings produced |
| `errors` | Any errors produced |

- Common mistake: running `proxy-build` before corpus chunks have been ingested.

---

### `proxy-inspect`

```
tiro [--db <path>] proxy-inspect [--lane <lane>] [--document-id <id>] \
     [--source-id <id>] [--status <status>] [--limit <n>]
```

Inspect proxy records without triggering full evidence hydration. Use this to audit
what proxies exist and their status.

---

### `proxy-recall`

```
tiro [--db <path>] [--limit <n>] proxy-recall <query-text>
```

Search proxy records by query, hydrate authoritative evidence via evidence pointers,
and return grounded results. Similar to `recall` but explicitly proxy-driven.

---

### `proxy-hydrate`

```
tiro [--db <path>] proxy-hydrate <pointer_id>
```

Hydrate one evidence pointer directly by ID. Use this to inspect what a specific
pointer resolves to.

---

## Session and Operational Ingest

### `ingest-session-note`

```
tiro [--db <path>] --session-id <id> ingest-session-note \
     --source-identity <identity> --text <text> \
     [--direction user|assistant|operator|system] \
     [--timestamp-utc <iso8601>]
```

Record a session message into the specified session lane.

- `--session-id` is required.
- `--source-identity` is required — it provides provenance for the record.
- `--direction` defaults to `operator` if not supplied.
- `--timestamp-utc` defaults to current UTC time if not supplied.

---

### `ingest-operational-record`

```
tiro [--db <path>] [--session-id <id>] ingest-operational-record \
     --record-type decision|todo|warning|unknown \
     --source-identity <identity> --text <text> \
     [--timestamp-utc <iso8601>]
```

Record an operational memory item: a decision, todo, warning, or unknown.

- `--record-type` is required.
- `--source-identity` is required.
- `--session-id` is optional; supply it to scope the record to a session.

---

### `ingest-aichat-session`

```
tiro [--db <path>] [--session-id <id>] ingest-aichat-session \
     --source-identity <identity> \
     [--file <path>|--latest] \
     [--max-chars <n>] \
     [--timestamp-utc <iso8601>]
```

Import a saved agent session YAML artifact as session evidence.

- Use `--file <path>` to specify an exact file, or `--latest` to find the newest
  available session file.
- `--max-chars` limits how many characters are imported from the session.
- Imported content is stored as session evidence only; it does not create operational
  records automatically.

---

### `inspect-aichat-sessions`

```
tiro inspect-aichat-sessions [--sessions-dir <path>]
```

Inspect candidate saved session YAML files in a directory and identify the newest one.
Useful before running `ingest-aichat-session --latest`.

---

## Fact Lifecycle Commands

### `fact-add`

```
tiro [--db <path>] [--session-id <id>] fact-add <text> <source_id> <origin_identity> \
     [--status active|stale|superseded|conflicting|unknown]
```

Add a new lifecycle-aware fact. Defaults to `status=active`.

---

### `fact-list`

```
tiro [--db <path>] [--limit <n>] fact-list \
     [--status active|stale|superseded|conflicting|unknown]
```

List facts, optionally filtered by lifecycle status.

---

### `fact-update-status`

```
tiro [--db <path>] fact-update-status <fact_id> <new_status>
```

Update the lifecycle status of an existing fact.

---

### `fact-supersede`

```
tiro [--db <path>] fact-supersede <superseding_fact_id> <superseded_fact_id>
```

Mark one fact as superseding another. Updates both facts' `supersedes_fact_id` and
`superseded_by_fact_id` fields and sets the superseded fact's status to `superseded`.

---

### `fact-conflict`

```
tiro [--db <path>] fact-conflict <fact_id_1> <fact_id_2>
```

Register a symmetric conflict between two facts. Both facts' statuses are updated
to `conflicting`.

---

## Proof Script Helper

### `proof-carry-forward`

```
tiro [--db <path>] [--session-id <id>] proof-carry-forward \
     [--phrase <text>] \
     [--record-type decision|todo|warning|unknown] \
     [--source-identity <identity>]
```

Smoke-test that ingested operational state survives a fresh retrieval pass. Use this
after ingesting records to confirm they are visible through the query surface.

---

## Legacy Maintenance Commands

The following commands remain in the codebase for local maintenance and test use.
They bypass the provenance conventions of the structured ingest commands and should
not be used in production operator flows.

| Command | Description |
|---|---|
| `session-create <session_id>` | Create a session record directly |
| `session-ingest <session_id> <direction> <source_identity> <text>` | Add a message to a session directly |
| `session-recent <session_id>` | Show recent messages in a session |
| `session-query <session_id> <query>` | Query within a specific session |
| `decision-add` | Add a decision record (legacy form) |
| `todo-add` | Add a todo record (legacy form) |
| `unknown-add` | Add an unknown record (legacy form) |
| `warning-add` | Add a warning record (legacy form) |
| `decision-list` | List decision records |
| `todo-list` | List todo records |
| `unknown-list` | List unknown records |
| `warning-list` | List warning records |

---

## Semantic Search

Semantic search requires embeddings to be indexed first. All four commands are gated on `TIRO_SEMANTIC_ENABLED=true` in the environment.

### `semantic-index`

```
tiro [--db <path>] semantic-index [--lanes corpus] [--limit <n>] [--rebuild] [--dry-run]
```

Embed un-indexed corpus chunks and store their vectors in the database. Must be run before `semantic-query` or `hybrid-search` will return results.

| Flag | Description |
|---|---|
| `--rebuild` | Re-embed chunks that already have vectors |
| `--dry-run` | Report candidates without calling the embedding API |
| `--limit <n>` | Cap the number of chunks indexed in a single run |

---

### `semantic-query`

```
tiro [--db <path>] [--limit <n>] semantic-query <query> \
     [--min-score <f>] [--lanes corpus,session]
```

Pure vector search. Embeds the query and retrieves corpus chunks by cosine similarity. Returns a JSON result set with scores and embedding metadata.

| Flag | Default | Description |
|---|---|---|
| `--min-score <f>` | `0.35` | Minimum cosine similarity threshold |
| `--lanes` | `corpus,session` | Lanes to search |

---

### `hybrid-search`

```
tiro [--db <path>] [--limit <n>] hybrid-search <query> \
     [--lanes corpus,session] \
     [--lexical-weight <f>] [--semantic-weight <f>] [--min-semantic-score <f>]
```

Merge lexical and semantic scores into a single ranked result set. Falls back to lexical-only if semantic search is unavailable, and notes the fallback in the response.

| Flag | Default | Description |
|---|---|---|
| `--lexical-weight <f>` | `0.55` | Weight applied to the lexical score component |
| `--semantic-weight <f>` | `0.45` | Weight applied to the semantic score component |
| `--min-semantic-score <f>` | `0.35` | Minimum cosine similarity for a semantic result to contribute |

---

### `semantic-status`

```
tiro [--db <path>] semantic-status
```

Report the current embedding configuration and indexed chunk counts. Does not call the embedding API.

---

## Environment Variables

| Variable | Description |
|---|---|
| `TIRO_DB_PATH` | Path to the SQLite database. Tool wrappers require this to be set and will not fall back to a default. |
| `TIRO_SEMANTIC_ENABLED` | Set to `true` to activate semantic search commands. |
| `TIRO_EMBEDDING_PROVIDER` | `local` (Ollama/OpenAI-compatible) or `openai` |
| `TIRO_EMBEDDING_MODEL` | Embedding model name (e.g. `nomic-embed-text`, `text-embedding-3-small`) |
| `TIRO_EMBEDDING_BASE_URL` | Base URL for the embedding API. Default: `https://api.openai.com/v1`. For Ollama: `http://localhost:11434/v1` |
| `GEMINI_API_KEY` | Enables the Gemini retrieval planner. Planner is disabled if absent. |
| `GEMINI_PLANNER_MODEL` | Gemini model to use for planning (default: `gemini-2.5-flash`) |

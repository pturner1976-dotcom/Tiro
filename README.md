# Tiro v3

Derived from the proven Tiro v1 codebase and transplanted into a standalone workspace for
controlled repackaging and parity validation. Until parity and integration gates
pass, Tiro v1 remains the known-good oracle.

## M013 memory standardization

Canonical operator surfaces are documented in `docs/public_surface.md`, the lane model is documented in `docs/memory_model.md`, and retrieval/hydration/packing boundaries are documented in `docs/retrieval_hydration_packing.md`.

Useful memory-loop checks:

```bash
scripts/tiro_stats.sh
scripts/tiro_direct_smoke.sh
scripts/tiro_wrapper_smoke.sh
scripts/tiro_memory_proof.sh
scripts/tiro_session_checkpoint_smoke.sh
scripts/tiro_latest_session_smoke.sh
```

The canonical local DB is `/home/SiliconMagician/Tiro_v1/data/tiro.sqlite3`. AIChat wrappers load `/home/SiliconMagician/.config/aichat/.env` when run directly and require `TIRO_DB_PATH` to point at an existing DB.

Saved AIChat sessions can be inspected with `inspect-aichat-sessions` and checkpointed with `ingest-aichat-session --latest`; explicit `--file` and `--session-id` override detection.

M015 expands the Gemini planner from single query refinement into bounded semantic retrieval advice. Planner output remains advisory: Tiro executes expanded lexical queries deterministically, deduplicates evidence, preserves explicit filters, and reports expansion diagnostics in `search-debug`.

Tiro is a standalone local memory/RAG spine. It keeps corpus retrieval, session/state memory, structured operational memory, and lifecycle-aware facts distinct while assembling compact context packets.

## Runtime Choice

Tiro uses C# on .NET 8. This matches the useful legacy salvage evidence while keeping Tiro standalone. No runtime namespaces, UI, TTS, AIChat integration, web retrieval, embeddings, or external reranking are included.

SQLite is accessed through the local `libsqlite3.so.0` library from the .NET executable, so no remote service is required.

## Build

```bash
dotnet build TiroV1.sln
```

## CLI

Default database path is `data/tiro.sqlite3` relative to the current working directory. Override it with `--db <path>`.

```bash
dotnet run --project src/Tiro.Cli -- init
dotnet run --project src/Tiro.Cli -- ingest-chunks tests/fixtures/m002_chunks.jsonl
dotnet run --project src/Tiro.Cli -- ingest-chunks tests/fixtures/m003_retrieval_chunks.jsonl
dotnet run --project src/Tiro.Cli -- --planner off --limit 2 query provenance retrieval
dotnet run --project src/Tiro.Cli -- --planner off --source-id source:m003-quality-notes-txt-fixture-m003-quality-notes-txt query provenance retrieval
dotnet run --project src/Tiro.Cli -- --planner off --document-id m003_quality --context-window 1 query packets confidence warnings sources
dotnet run --project src/Tiro.Cli -- session-create session-alpha
dotnet run --project src/Tiro.Cli -- session-ingest session-alpha user cli:user Remember deployment token rotation is current state
dotnet run --project src/Tiro.Cli -- --limit 4 session-recent session-alpha
dotnet run --project src/Tiro.Cli -- --limit 2 session-query session-alpha deployment token
dotnet run --project src/Tiro.Cli -- --planner off --session-id session-alpha query deployment provenance
dotnet run --project src/Tiro.Cli -- --session-id session-alpha decision-add cli:manual Use explicit provenance for deployment decisions
dotnet run --project src/Tiro.Cli -- --session-id session-alpha todo-add cli:manual Rotate deployment token before launch
dotnet run --project src/Tiro.Cli -- --session-id session-alpha unknown-add cli:manual Deployment owner is not confirmed
dotnet run --project src/Tiro.Cli -- --session-id session-alpha warning-add cli:manual Token rotation is blocked until owner confirms
dotnet run --project src/Tiro.Cli -- --session-id session-alpha decision-list
dotnet run --project src/Tiro.Cli -- --planner auto --limit 2 query provenance retrieval
dotnet run --project src/Tiro.Cli -- stats
```

Planner mode defaults to `auto`. Use `--planner off` for deterministic-only queries, `--planner auto` to use Gemini when configured and fall back otherwise, or `--planner on` to attempt planner use and still fall back on missing keys or failures. `--debug-planner` adds bounded planner diagnostics such as query lengths, refined-term counts, redacted Gemini endpoint URL, model, auth method, timeout, HTTP status, response body when returned, and pre-response inner exception details. It does not print prompt text or secret values.

Query supports explicit deterministic filters:

- `--source-id <id>`
- `--document-id <id>`
- `--context-window <0..2>`
- `--session-id <id>` to include session/state evidence in packets

Internally, CLI query uses `TiroQueryService.QueryAsync(TiroQueryRequest)` rather than owning retrieval semantics itself. Future wrappers should call that same service surface so there is one query pipeline for planner setup, filters, context windows, session evidence, operational memory, lifecycle facts, retrieval policy, and packet assembly.

Filters are never silently broadened. If a filter matches no chunks, the packet says so clearly. Adjacent context is bounded to two chunks on either side and is reported separately from ranked primary evidence.

Session commands:

- `session-create <session_id>`
- `session-ingest <session_id> <direction> <source_identity> <text>`
- `session-recent <session_id>`
- `session-query <session_id> <query>`

Operational memory commands:

- `decision-add <origin> <text>` / `decision-list`
- `todo-add <origin> <text>` / `todo-list`
- `unknown-add <origin> <text>` / `unknown-list`
- `warning-add <origin> <text>` / `warning-list`

Add `--session-id <id>` to scope operational records to a session.

`query` returns a structured JSON context packet containing `query`, `normalized_terms`, `confidence`, `facts`, `unknowns`, `warnings`, `sources`, `source_ids`, `filters`, `context_window`, `primary_evidence`, `supporting_context`, `session_id`, `session_evidence`, `recent_session_context`, `operational_memory`, `retrieval_policy`, `retrieval_results`, and planner status metadata.

## Retrieval Behavior

Retrieval still uses deterministic normalized lexical matching as the chassis. Query text is tokenized, lowercased, de-duplicated, lightly suffix-normalized, and stripped of simple stopwords. Scoring uses token hits rather than raw substring counts, with small deterministic bonuses for term coverage, exact ordered phrase matches, and concentrated evidence in shorter chunks.

Each primary evidence result includes provenance fields plus `rank`, `matched_terms`, per-term `score_details`, `score_summary`, and a compact explanation. Supporting context preserves exact source/document/chunk identity and records which primary chunk it supports.

M009 adds an explicit retrieval policy layer. Packets include `retrieval_policy.query_mode`, a mode reason, and per-evidence signals that keep these axes separate:

- `relevance_score`: lexical match strength from deterministic search.
- `recency_score`: deterministic age bucket relative to the newest candidate in the packet.
- `importance_score`: explicit lane/type weighting, such as decisions and warnings carrying more operational importance than generic archive chunks.
- `lifecycle_score`: active facts score positively; stale, superseded, conflicting, and expired fact evidence is penalized and warned about.
- `mode_score`: deterministic query-mode emphasis.

Query modes are detected from normalized query terms:

- `current-state`: terms such as current, now, latest, today, active, status, or state. This favors recent session/state evidence and active lifecycle facts.
- `decision`: decision/intent terms. This favors operational decisions.
- `unresolved`: TODO, blocked, warning, unknown, open, or unresolved terms. This favors operational TODOs, warnings, and unknowns.
- `archive`: archive/reference/source/document/provenance lookup terms. This favors corpus evidence.
- `fact-lifecycle`: conflict, stale, superseded, or lifecycle terms. This favors lifecycle facts while preserving lifecycle warnings.
- `general`: no mode-specific terms matched.

The final policy score is inspectable as `final=relevance+recency+importance+lifecycle+mode` in each signal explanation. The planner may read these deterministic signals, but it does not invent freshness, status, importance, or memory mutations.

Known limits:

- Retrieval is still lexical, not semantic.
- Confidence labels are evidence-based and conservative; planner presence never raises confidence.
- Freshness is deterministic metadata/rule based. Tiro does not mark records stale from conversation tone.
- Source IDs are deterministic local IDs, not content hashes.
- The current engine scans chunks in process, which is acceptable for milestone fixtures but not a large-corpus indexing strategy.

## Session / State Memory

M006 stores sessions independently from corpus documents and chunks. Messages belong to explicit sessions and preserve:

- `session_id`
- `message_id`
- `timestamp_utc`
- `direction`
- `source_identity`
- `text`

Session query is deterministic lexical retrieval over stored message text and message metadata. Recent session retrieval is bounded and chronological. Packets that use `--session-id` include `session_evidence` and `recent_session_context` separately from corpus `primary_evidence`, so current-state context does not blur into archive/document evidence.

Session/state memory is explicit only. Tiro does not infer durable state from vibe, mutate memory from planner output, or build a stale/superseded fact graph in M006.

## Operational Memory

M007 stores structured operational memory independently from corpus chunks and session transcript messages. Supported record types:

- `decision`
- `todo`
- `unknown`
- `warning`

Each record stores a record ID, creation timestamp, text, origin, status, optional session ID, and optional link fields for source/message IDs. Retrieval over operational memory is deterministic lexical matching. Query packets include matching records under `operational_memory`; operational decisions may also appear as `facts`, operational unknowns are surfaced in `unknowns`, and operational warnings are surfaced in `warnings`.

Operational memory is explicit only. Tiro does not infer decisions or TODOs from planner output or arbitrary transcripts in M007.

## Gemini Planner

The M004A planner is direct Gemini API integration inside Tiro. It is retrieval-specific and short-lived: it asks for one compact JSON object containing a refined lexical `search_query`, `refined_terms`, and `packet_focus`, with optional `warnings`. Tiro treats that output as advisory. Gemini never becomes the fact source, never mutates memory, and never produces final answer facts.

Planner requests use `responseMimeType: application/json` plus a narrow `responseJsonSchema` for those fields. The planner output budget is 384 tokens: large enough for the small JSON payload, small enough to discourage rambling.

Planner parsing is strict by default. It accepts:

- a single JSON object
- one JSON object wrapped in a single markdown code fence
- one JSON object after a single obvious preamble line

Truncated JSON, arbitrary prose, extra leading/trailing text, or missing required fields trigger deterministic fallback.

Secret loading uses `GEMINI_API_KEY` with this precedence:

- process environment
- `~/.env/Tiro_v1.env`

Planner model selection uses `GEMINI_PLANNER_MODEL` with the same precedence. This value is not secret. If unset, Tiro defaults to `gemini-2.5-flash`, which is the current Gemini API quickstart model shown by Google AI docs for `generateContent`.

The env file is optional. If the key or file is missing, or if the API call times out/fails/returns invalid JSON, Tiro falls back to deterministic retrieval and records a concise warning in the packet. Secret values are never logged; the CLI only reports whether `GEMINI_API_KEY` was found.

## Storage Summary

The M002 schema is initialized idempotently and contains:

- `schema_info`
- `sources`
- `documents`
- `chunks`
- `sessions`
- `messages`
- `operational_records`

Source registration happens during chunk ingestion. Chunks preserve `document_id`, `source_name`, `source_path`, `timeframe_or_era`, `chunk_id`, `chunk_index`, `chunk_count`, text, local `source_id`, and local ingestion timestamp.

## Scope Notes

Retrieval is deterministic lexical matching with optional bounded query planning and deterministic M009 weighting. It is intentionally not semantic retrieval, not autonomous agentic planning, not web search, not embeddings, not external reranking, and not planner-driven memory mutation.

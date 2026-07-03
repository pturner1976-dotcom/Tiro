# Tiro Public API Surface

This document defines which CLI commands and agent tool wrappers are stable public API,
which are internal or compatibility surfaces, and what callers may and may not rely on.

---

## Stable CLI Commands

The following commands form the canonical stable surface:

| Command | Role |
|---|---|
| `query` | Primary read path — returns a structured `ContextPacket` |
| `ingest-session-note` | Explicit write — add a session message |
| `ingest-operational-record` | Explicit write — add a decision, todo, warning, or unknown |
| `ingest-aichat-session` | Explicit write — import a saved agent session artifact |
| `stats` / `inspect stats` | Database summary and counts |
| `search-debug` | Query diagnostics (retrieval terms, scores, lane counts) |
| `proof-carry-forward` | Smoke proof that ingested operational state survives retrieval |

`query` is the canonical read path. The `ingest-*` commands are the canonical write paths.
Write commands require explicit caller intent and recorded provenance; they do not perform
silent or automatic capture of conversational turns.

---

## Stable Agent Tool Wrappers

When Tiro is wired into an agent runtime via tool wrappers, two tools form the stable
public surface:

| Tool | Role |
|---|---|
| `tiro_query` | Read-only targeted retrieval — returns a structured context packet |
| `tiro_ingest_state` | Controlled write — explicit session notes, operational records, saved session imports |

Agent callers should depend only on these two tools. Other tools (`tiro_recall`,
`tiro_inspect`, `tiro_session_summary`, `tiro_phrase_search`, `tiro_proxy_recall`)
are available and useful, but are considered extended surface rather than the minimal
stable contract.

---

## Compatibility and Internal Surfaces

The following commands remain available for tests and local maintenance, but callers
should not treat their exact output or behavior as stable public API:

- `session-ingest`
- `decision-add`, `todo-add`, `warning-add`, `unknown-add`
- `fact-add`

These commands bypass the wrapper conventions used by the stable ingest commands and
may have different output formats or validation behavior.

The following are not public API regardless of accessibility:

- SQLite schema internals (table names, column layout, row IDs)
- Direct `TiroStore` methods
- Retrieval policy internals
- Packet builder implementation details

---

## Experimental and Future Surfaces

The following are experimental or not yet implemented. Callers must not assume they
are present:

- `ingest shard`
- `tiro_ingest_docs`
- `tiro_ingest_http`
- Temporal proof-checking
- Semantic / vector embedding retrieval
- Causal trace

---

## Stability Promises

Stable commands and tools should continue to:

- Accept an explicit database path (`--db` flag or `TIRO_DB_PATH` environment variable)
- Preserve provenance fields on ingested records
- Separate memory lanes (corpus, session, operational, facts) without cross-contamination
- Return structured JSON where documented
- Avoid silently broadening filtered retrieval (a query scoped to a source or session
  must not quietly expand to the full corpus)

The `ContextPacket` shape returned by `query` and `tiro_query` should remain stable
unless a change is explicitly documented.

---

## What Callers May Rely On

- Explicit query filters: `--source-id`, `--document-id`, `--session-id`
- Session scope limiting retrieval to a specific session
- Operational record types: `decision`, `todo`, `warning`, `unknown`
- Lifecycle status fields on facts: `active`, `stale`, `superseded`, `conflicting`, `unknown`
- `warnings` and `unknowns` arrays in query output
- `retrieval_policy` summary in query output
- `ContextPacket` top-level keys: `query`, `normalized_terms`, `facts`, `warnings`,
  `unknowns`, `sources`, `filters`, `primary_evidence`, `supporting_context`,
  `session_evidence`, `operational_memory`, `retrieval_policy`, `planner`

---

## What Callers Must Not Rely On

- Private schema details (internal table names, column indexes, SQLite row IDs as
  stable cross-database identities)
- Planner output as ground truth (the planner is a query-refinement aid, not an
  authoritative reasoner)
- Implicit database creation from a fallback path — always supply an explicit path
- Casual conversational turns being saved automatically — writes are always explicit
- The internal `TiroStore` API — it is subject to change without notice

---

## Deprecation Policy

Commands listed as compatibility or internal surfaces may be removed or changed in
future versions without a separate deprecation notice. Stable commands will be
maintained across versions; breaking changes will be documented explicitly.

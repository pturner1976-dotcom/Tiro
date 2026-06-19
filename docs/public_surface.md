# Tiro Public Surface

## Canonical CLI surfaces

The stable operator CLI commands are:

- `query`
- `ingest-session-note`
- `ingest-operational-record`
- `ingest-aichat-session`
- `stats`
- `search-debug`
- `proof-carry-forward`

The canonical read path is `query`. The canonical write paths are the explicit state-ingest commands. Write commands require caller intent and provenance; they are not general chat capture.

## Canonical AIChat tools

The approved AIChat tools are:

- `tiro_query`
- `tiro_ingest_state`

`tiro_query` is read-only retrieval. `tiro_ingest_state` is a controlled write tool for explicit session notes, saved AIChat session artifacts, and operational records.

## Compatibility/internal surfaces

Older commands such as `session-ingest`, `decision-add`, `warning-add`, `fact-add`, and direct store methods remain compatibility or internal surfaces. They are useful for tests and local maintenance, but callers should not treat their exact output or behavior as the public API.

The SQLite schema, direct `TiroStore` methods, retrieval-policy internals, and packet-builder implementation are not public API.

## Experimental/future surfaces

The following are experimental or future work:

- `ingest shard`
- `tiro_ingest_docs`
- `tiro_ingest_http`
- temporal proof-checking
- semantic retrieval
- vector search
- causal trace

They must not be assumed present by callers.

## Stability promises

Canonical commands should continue to accept explicit DB paths, preserve provenance, separate memory lanes, return structured JSON where documented, and avoid silently broadening filtered retrieval.

Packet shape should remain stable unless a milestone explicitly documents the change.

## What callers may rely on

Callers may rely on explicit query filters, session scope, operational record types `decision`, `todo`, `warning`, and `unknown`, lifecycle status fields, warnings, unknowns, and retrieval-policy summaries.

AIChat callers may rely only on `tiro_query` and `tiro_ingest_state`.

## What callers must not rely on

Callers must not rely on private schema details, row ids as stable cross-database identities, planner output as truth, direct store internals, implicit DB fallback creation, or casual chat being saved automatically.

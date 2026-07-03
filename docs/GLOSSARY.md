# Glossary

Terms are listed alphabetically. Where a term has a precise technical meaning in Tiro's implementation, that meaning is given here. General usage notes follow where disambiguation is needed.

---

**Active fact**
A fact with status `active`. Indicates the claim is current and trusted. Active facts score positively in retrieval packets. See also: *fact lifecycle*.

**Agent framework**
The calling runtime that invokes Tiro commands and uses the retrieved JSON packets. Tiro is agnostic to which agent framework is used. Any runtime that can execute shell commands and parse JSON can integrate Tiro.

**Audit trail**
The property that superseded, stale, and conflicting records remain inspectable in the database rather than being deleted. Tiro's append-and-annotate design preserves the history of corrections.

**Caller**
Any process, script, agent, or shell wrapper that invokes Tiro commands via the CLI. The caller decides which commands to invoke, what parameters to pass, and how to use the JSON output.

**Canary**
A sensitive marker value embedded in the database or configuration to detect accidental disclosure or exfiltration. Canary values should never appear in retrieval output.

**Chunk**
A unit of corpus evidence. A chunk is a segment of text derived from a document, stored in the `chunks` table with provenance fields (`source_id`, `document_id`, `chunk_index`). Chunks are the primary unit of lexical and semantic scoring.

**Confidence**
A field in the query packet indicating how well the retrieval query matched available evidence. Values are typically `low`, `medium`, or `high`. Confidence reflects evidence coverage — it is not a claim about factual accuracy.

**Context window**
In Tiro's CLI flags, `--context-window <0..2>` controls how many adjacent chunks to include around each matched chunk. `0` returns the matched chunk only. `1` includes one neighbor on each side. `2` includes two.

**Corpus**
The corpus lane. Durable imported document evidence stored as sources, documents, and chunks. The corpus is the primary evidence store for long-term retrieval. See [MEMORY_MODEL.md](MEMORY_MODEL.md).

**Corpus chunk**
See *chunk*.

**Document**
A logical grouping of chunks within a source. Stored in the `documents` table. A source may contain multiple documents. A document may contain multiple chunks.

**document_id**
Stable identifier for a corpus document within a source. Assigned by Tiro at ingest time. Used for `--document-id` filter in retrieval commands.

**Embedding**
A numeric vector representation of a text chunk produced by an embedding model. Stored in the `embeddings` table. Used for semantic (vector) search. Embeddings are optional — Tiro operates lexically without them.

**Embedding provider**
The service that produces embeddings. Tiro supports `local` (Ollama) and `openai`. Configured via `TIRO_EMBEDDING_PROVIDER`, `TIRO_EMBEDDING_MODEL`, and `TIRO_EMBEDDING_BASE_URL`.

**Evidence**
Retrieved content from any Tiro lane — corpus chunks, session messages, operational records, or facts — included in a query packet. Evidence has provenance and a score. It is what the caller uses to reason about a query. Evidence is not automatically truth.

**Evidence pointer**
A stable record in the `evidence_pointers` table that identifies where authoritative evidence lives. Hydrating a pointer loads the target evidence. The hydrated target is the authoritative retrieval object. See also: *hydration*, *proxy*.

**Expired fact**
A fact with status `expired`. Indicates the claim has reached its natural end of life. Expired facts are penalized in retrieval and surfaced with warnings.

**Fact**
A stored claim in the `facts` table. Facts carry explicit lifecycle status and may reference supersession or conflict relationships. Facts are distinct from corpus chunks — they are standalone claims, not tied to a specific document. See also: *fact lifecycle*.

**Fact conflict**
An explicit record in `fact_conflicts` noting that two facts are in conflict. Tiro does not resolve conflicts automatically. Both facts remain in the database; both are surfaced with conflict metadata in retrieval packets.

**Fact lifecycle**
The status model for facts. A fact moves through states — `active`, `stale`, `superseded`, `conflicting`, `expired`, `unknown` — as the operator updates its status. Lifecycle state affects retrieval scoring. See [MEMORY_MODEL.md](MEMORY_MODEL.md).

**Hydration**
The act of loading the authoritative target referenced by an evidence pointer. A proxy recall result returns a pointer; hydrating that pointer via `proxy-hydrate` returns the full authoritative source evidence. See also: *evidence pointer*, *proxy*.

**Hostile evidence**
External or untrusted content that may contain malicious instructions, prompt injection, or misleading text. Hostile evidence must be handled as data only — it must never be executed, echoed verbatim into a model prompt as trusted content, or allowed to influence tool calls.

**Hybrid search**
A retrieval mode that merges lexical and semantic scores. Requires an embedding provider to be configured and corpus chunks to be indexed via `semantic-index`. Available as the `hybrid-search` command or automatically used by `query` when `TIRO_SEMANTIC_ENABLED=true`.

**Importance score**
One of the five composable scores in a retrieval packet item. Reflects the lane and type weight of the evidence — decisions and warnings rank higher than archive corpus chunks.

**Ingest**
The act of writing data into a Tiro lane. Ingestion commands include `ingest-chunks`, `ingest-session-note`, `ingest-operational-record`, and `ingest-aichat-session`. All ingestion is explicit and operator-initiated.

**Lane**
A major memory category in Tiro. The four primary lanes are corpus, session, operational, and fact lifecycle. Each lane has distinct storage tables, ingestion commands, and retrieval behavior. See [MEMORY_MODEL.md](MEMORY_MODEL.md).

**Lane weight**
A retrieval parameter that adjusts the relative contribution of each lane's evidence to the final query packet. Configurable via `--lanes` and the retrieval policy.

**Lexical scoring**
Tiro's default retrieval method. Query text is tokenized, lowercased, deduplicated, lightly suffix-normalized, and stripped of stopwords. Scoring uses token hit counts with bonuses for term coverage, exact phrase matches, and evidence concentration in shorter chunks. Lexical scoring is deterministic and requires no external service.

**Lifecycle score**
One of the five composable scores in a retrieval packet item. Active facts score positively; stale, superseded, conflicting, and expired facts are penalized.

**Message**
A single entry in the `messages` table. Part of the session lane. Each message has a direction (e.g., user or agent), a source identity, and a text body. Messages preserve chronology within a session.

**Mode score**
One of the five composable scores in a retrieval packet item. Reflects how well the evidence matches the detected retrieval mode (e.g., `current-state`, `decision`, `archive`).

**Operational memory**
The operational lane. Stores structured records of type `decision`, `todo`, `warning`, and `unknown`. Intended to capture operator-approved state in a labeled, attributable form. See [MEMORY_MODEL.md](MEMORY_MODEL.md).

**Operational record**
A single entry in the `operational_records` table. Carries a type, an origin string, and a text body. Operational records appear in the `operational_memory` section of query packets.

**Operator**
The human or automated process responsible for running Tiro, managing ingestion, and deciding what enters each memory lane. The operator controls `TIRO_DB_PATH`, ingestion commands, and deployment. The operator is the authority over mutation.

**Planner**
An optional bounded external API call (Gemini) that refines query terms before lexical scoring. The planner output is advisory only. Tiro always falls back to deterministic lexical retrieval if the planner fails. Planner presence does not raise reported confidence.

**Proxy**
A lightweight searchable record in `recall_proxies` used to improve recall precision and structure-aware discovery. Proxies are search aids built over corpus evidence — they are not authoritative truth. Proxies carry their own status: `active`, `stale`, `superseded`, `hidden`. See also: *evidence pointer*, *hydration*.

**Proxy build**
The `proxy-build` command. Constructs or rebuilds the proxy index over the corpus. Does not mutate original source chunks.

**Query packet**
The structured JSON object returned by the `query` command. Contains primary evidence, supporting context, session evidence, recent session context, operational memory, facts, unknowns, warnings, sources, and retrieval policy metadata.

**Query planning**
See *planner*.

**Recall**
Discovery-oriented retrieval via the `recall` command. Helps find likely evidence across all lanes without requiring internal IDs first. Useful when the caller does not know which source or document to target.

**Recency score**
One of the five composable scores in a retrieval packet item. A deterministic age bucket relative to the newest candidate in the result set. More recent evidence scores slightly higher when relevance scores are otherwise similar.

**Relevance score**
One of the five composable scores in a retrieval packet item. Reflects lexical match strength between query terms and the evidence text.

**Retrieval policy**
The per-query configuration that selects a retrieval mode, sets lane weights, and determines scoring emphasis. The mode is detected automatically from query terms. It can be influenced via `--lanes` and explicit planner guidance.

**Retrieval mode**
The active retrieval strategy selected by the retrieval policy. Modes include `current-state`, `decision`, `unresolved`, `archive`, `fact-lifecycle`, and `general`. Each mode applies different scoring emphasis across lanes.

**Schema**
The SQLite database schema initialized by `init`. Contains: `schema_info`, `sources`, `documents`, `chunks`, `sessions`, `messages`, `operational_records`, `facts`, `fact_conflicts`, `embeddings`, `recall_proxies`, `evidence_pointers`. See [api/DATABASE_SCHEMA.md](api/DATABASE_SCHEMA.md).

**Semantic search**
Vector-based retrieval using embeddings. Requires `TIRO_SEMANTIC_ENABLED=true` and an embedding provider. Finds evidence that is conceptually similar to the query even when lexical terms do not overlap. See also: *hybrid search*.

**Session**
A registered evidence stream in the `sessions` table. Sessions group related messages. Sessions can be created explicitly via `session-create` or implicitly via `ingest-aichat-session`.

**Session evidence**
The session lane. Chronological conversation and session artifacts stored as evidence, not truth. See [MEMORY_MODEL.md](MEMORY_MODEL.md).

**session_id**
Stable identifier for a Tiro session evidence stream. Assigned at session creation. Used to scope session-specific retrieval commands and to associate operational records with a session.

**Source**
A registered corpus source in the `sources` table. A source is the top-level provenance unit for corpus material. One source may contain multiple documents and many chunks.

**source_id**
Stable identifier for a corpus source. Assigned at ingest time. Used for `--source-id` filter in retrieval commands.

**Stale fact**
A fact with status `stale`. Indicates the claim may be outdated but has not yet been explicitly superseded. Stale facts are penalized in retrieval and surfaced with a warning.

**Stale proxy**
A proxy record with status `stale`. Penalized in proxy recall. Does not affect the status of the underlying corpus chunk.

**Superseded fact**
A fact with status `superseded`. Indicates the claim has been explicitly replaced by a newer fact via `fact-supersede`. The superseded fact remains in the database for audit. The `superseded_by_fact_id` field references the replacement.

**Tool surface**
The set of Tiro commands exposed as callable tools to an agent framework. The caller selects which commands to expose. Read-only commands (`query`, `recall`, `inspect`, `stats`, `proxy-recall`, `proxy-hydrate`) are safe to expose broadly. Write commands require explicit operator approval. See [api/TOOL_SURFACE.md](api/TOOL_SURFACE.md).

**Unknown fact**
A fact with status `unknown`. The fact's current truth-state has not been determined. Treated with caution in retrieval.

**Wrapper**
A shell script or thin program that bridges a caller's tool invocation to a Tiro CLI command. Wrappers apply parameter constraints, load environment variables, and enforce which Tiro commands are accessible to the agent. Most Tiro wrappers should be read-only.

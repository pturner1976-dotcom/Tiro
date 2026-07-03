# Hydration and Packing

Tiro's retrieval output goes through three sequential stages: retrieval, hydration, and packing. Each stage has a distinct responsibility. Understanding the boundary between them is important for callers that consume `ContextPacket` output.

---

## Stage 1: Retrieval

Retrieval finds candidate evidence records. It answers: *what records might be relevant to this query?*

Retrieval operates across Tiro's storage lanes — corpus chunks, session records, operational records, and lifecycle facts — using lexical scoring, retrieval policy, and optional planner expansion. The output of retrieval is a ranked list of candidate items with scores, not a complete evidence package.

---

## Stage 2: Hydration

Hydration attaches surrounding detail to each selected evidence item. It answers: *what does this evidence come from, and what context must travel with it?*

Hydrated fields include:

- **Source metadata** — document title, source identifier, ingestion timestamp, content type
- **Document identity** — which document or record the chunk belongs to
- **Chunk position** — where in the document this chunk appears (offset, sequence number, or chunk group)
- **Adjacent context** — neighboring chunks that provide surrounding context without being the primary match
- **Session metadata** — for session-lane evidence: session ID, turn index, timestamp
- **Operational record metadata** — for operational-lane evidence: record type, status, associated lane
- **Lifecycle status** — for fact-lane evidence: confirmation state, revision history, whether the fact is active, stale, or superseded

Hydration is what converts a bare chunk reference into a self-describing evidence item. Callers should not attempt to use raw retrieval output without hydration.

---

## Stage 3: Packing

Packing assembles hydrated evidence into a `ContextPacket` — the structured output that is safe to hand to a model or agent. It answers: *what evidence box can the caller use to answer a query?*

The packet keeps evidence lanes separate and includes:

- `primary_evidence` — top-ranked corpus and factual evidence directly matching the query
- `supporting_context` — adjacent or lower-ranked evidence that provides background
- `session_evidence` — evidence from the session lane, kept separate from corpus evidence
- `operational_memory` — evidence from the operational lane (task state, status records, and similar)
- `facts` — lifecycle-managed fact records with their confirmation and revision state
- `retrieval_results` — raw scored candidates before lane separation (available for inspection)
- `retrieval_policy` — the detected query mode and policy signals that influenced scoring
- `planner_metadata` — terms, lane suggestions, and notes from the advisory planner, if it ran
- `sources` — deduplicated list of all source documents contributing evidence
- `warnings` — structured warnings about evidence quality, missing lanes, or retrieval conditions
- `unknowns` — items the caller asked about that could not be resolved from available evidence
- `confidence` — an aggregate signal reflecting evidence coverage and query-match quality

---

## Evidence Lane Descriptions

### `primary_evidence`

Corpus chunks and fact records with the strongest lexical and policy match to the query. These are the items the caller should prioritize when constructing a response. Each item carries full hydration metadata including source identity, chunk position, and lifecycle status where applicable.

### `supporting_context`

Adjacent chunks, lower-scored corpus items, or related records that provide useful surrounding context. These support interpretation of primary evidence but are not themselves the best match. Callers should treat supporting context as background rather than authoritative answers.

### `session_evidence`

Records retrieved from the session lane. Session evidence is always kept separate from corpus evidence because its provenance — a conversation turn at a specific time — differs meaningfully from a corpus document. Session evidence is only included when a session scope is active (either via `--session-id` in `query`, or via session discovery in `recall`).

### `operational_memory`

Records from the operational lane: task status, decision logs, state transitions, and similar structured records that track what an agent or process has done. Operational records are not narrative content; they are structured state. Callers should use operational memory to understand current task state, not to answer factual questions.

---

## Warnings and Unknowns

Warnings and unknowns are first-class output fields. Packing adds them in the following conditions:

| Condition | Signal type |
|---|---|
| A requested lane returned no evidence | Warning |
| Evidence is partial (e.g., only one of several requested items found) | Warning |
| A lifecycle fact is stale or superseded | Warning |
| Evidence items are too similar to distinguish confidently | Warning (weak separation) |
| Planner was used and may have altered query terms | Warning (planner-assisted) |
| A query term or named item has no matching evidence | Unknown |
| A filter produced zero results | Warning |

Callers must inspect `warnings` and `unknowns` before treating a packet as complete. A packet with no `primary_evidence` and populated `unknowns` is a clean absence signal — Tiro found nothing, and is saying so explicitly.

---

## Caller Responsibilities

When using a `ContextPacket`, the caller is responsible for the following:

**Answer from evidence, not from memory.**
If the packet contains evidence on a topic, the caller's answer must be grounded in that evidence. Do not substitute general knowledge or prior session impressions for what the packet actually contains.

**Treat empty packets as genuine absences.**
If `primary_evidence` is empty and `unknowns` lists the queried item, the correct response is to report that the record does not support an answer — not to invent continuity or guess from context.

**Respect lane separation.**
Session evidence and operational memory are separate from corpus evidence for a reason. Do not conflate them. A session record that mentions a topic is not the same as a corpus document on that topic.

**Surface warnings to the user or upstream agent when relevant.**
Stale facts, weak separation, and planner-modified queries are conditions the caller needs to know about. Suppressing warnings produces answers that look more confident than the evidence justifies.

**Check lifecycle status on facts.**
Fact records carry explicit lifecycle state. A fact marked `superseded` should not be treated as current truth, even if it was the best lexical match.

---

## Why Packet Shape Is Stable

The `ContextPacket` structure is the proof-carrying boundary between Tiro and the caller. Its fields are versioned and stable so that callers can write reliable parsers, prompts, and grounding instructions against it. Changes to retrieval internals — scoring weights, planner behavior, lane ordering — do not change the packet schema. Callers should bind to the packet fields, not to internal retrieval behavior.

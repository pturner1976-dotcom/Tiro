# Memory Model

Tiro separates memory into distinct lanes rather than flattening everything into a single chat log. Each lane has different storage semantics, different ingestion commands, different retrieval behavior, and different evidence discipline rules. The separation is intentional: it makes retrieval more precise, it preserves provenance, and it makes correction possible without rewriting history.

## The Four Lanes

### Corpus lane

**Tables:** `sources`, `documents`, `chunks`

The corpus lane stores durable imported document evidence. It is appropriate for project documents, implementation reports, specifications, audits, reference material, and other content intentionally imported for long-term retrieval.

Corpus evidence is scored lexically and optionally semantically. Each chunk carries provenance back to its source and document. Source and document filters (`--source-id`, `--document-id`) allow callers to scope retrieval to specific imports.

**What belongs here:** Finalized or semi-stable documents imported with explicit intent. Material where provenance matters and where you want retrieval to surface the original text.

**What does not belong here:** Raw casual chat, live conversation transcripts, mutable operational state, unapproved claims, secrets, or anything too vague to retrieve responsibly. The corpus is not a dumping ground.

### Session lane

**Tables:** `sessions`, `messages`

The session lane stores chronological conversation and session artifacts. Sessions can be created explicitly and populated with messages via `session-ingest`, or populated by importing an agent session file via `ingest-aichat-session`.

Session evidence is useful for recall and audit — it preserves the chronology of what was observed during a conversation. It is not self-authorizing. A message in a session proves that text was observed in that session artifact at a point in time. It does not prove that the statement inside the message is correct, current, or safe to act on.

**What belongs here:** Explicit session notes, imported session YAML artifacts where the conversation record itself has value, short-term working state promoted into Tiro by explicit operator intent.

**What does not belong here:** Every message from every conversation. Session memory requires explicit operator intent or an approved saved-session import. Do not automate indiscriminate import of raw chat as though it were truth.

### Operational lane

**Tables:** `operational_records`

The operational lane stores structured operator-approved records of type `decision`, `todo`, `warning`, and `unknown`. These records capture state that matters for ongoing operation and that a flat transcript cannot preserve reliably.

Operational records appear in the `operational_memory` section of every query packet. Decisions and warnings are scored with higher importance weights than corpus archive chunks.

**What belongs here:** Explicit architectural decisions (with origin attribution), pending tasks, open unknowns that need tracking, warnings about known risks or conditions. Records should be short, labeled, and attributable to an origin string.

**What does not belong here:** General documents, unreviewed facts, secrets, credentials, or casual chatter. Do not use the operational lane as a second corpus. Do not let a model write operational records without explicit operator review.

### Fact lifecycle lane

**Tables:** `facts`, `fact_conflicts`

The fact lifecycle lane stores claims that need durable truth-state tracking. Unlike corpus chunks, facts are not tied to a specific document — they are standalone claims with explicit status that changes over time.

Fact lifecycle tracking is distinct from the other three lanes. It exists for situations where a claim's status matters as much as its content: whether something is currently true, known to be outdated, superseded by a newer claim, or in conflict with another claim.

**What belongs here:** Claims that need to be tracked over time with explicit status. Facts should be added deliberately, not automatically from retrieval results.

**What does not belong here:** Claims that should live as corpus chunks (where source provenance is the point), or operational records (where type and origin attribution are the point). Fact lifecycle is for truth-state tracking, not general storage.

## Fact Lifecycle Status Model

Every fact carries an explicit status. Retrieval scoring uses this status to weight facts appropriately.

| Status | Meaning | Retrieval effect |
|--------|---------|-----------------|
| `active` | The claim is current and trusted | Scored positively |
| `stale` | The claim may be outdated; not yet superseded | Penalized; surfaced with a warning |
| `superseded` | The claim has been explicitly replaced by a newer fact | Penalized; superseding fact referenced |
| `conflicting` | The claim is in explicit conflict with one or more other facts | Penalized; conflicts surfaced |
| `expired` | The claim has reached its natural end of life | Penalized; surfaced with a warning |
| `unknown` | Status has not been determined | Treated with caution |

Fact supersession is explicit. Use `fact-supersede <old_fact_id> <new_text>` to replace a fact and mark the old one as superseded. The old fact is not deleted — it remains inspectable for audit. The `supersedes_fact_id` and `superseded_by_fact_id` fields preserve the chain.

Fact conflicts are explicit. Use `fact-conflict <fact_id_a> <fact_id_b>` to record that two facts are in conflict. Tiro does not automatically resolve conflicts. The conflict record is surfaced in retrieval packets so the caller can decide how to handle it.

## Proxy/Pointer Layer

Above the four primary lanes, Tiro maintains a proxy/pointer layer:

**`recall_proxies`** — lightweight searchable index records built over corpus evidence. Proxies improve recall precision for structure-aware discovery. They are search aids, not authoritative truth. A stale or superseded proxy does not make the underlying corpus chunk stale.

**`evidence_pointers`** — stable pointer records that identify where authoritative evidence lives. Hydrating a pointer (via `proxy-hydrate`) loads the authoritative source evidence the pointer references. The hydrated target is the authoritative retrieval object.

Proxy records carry their own status model: `active`, `stale`, `superseded`, and `hidden`. Normal proxy recall excludes `hidden` records and penalizes `stale` and `superseded` ones. Rebuilding the proxy index via `proxy-build` supersedes or replaces scaffolding only — it does not mutate original corpus chunks.

## Evidence Discipline Rules

These rules apply to all operators and callers:

1. **Use explicit provenance.** Every corpus ingest should identify its source. Every operational record should carry an origin string. Session imports should record which session artifact they came from.

2. **Label operational records.** Use the appropriate type (`decision`, `todo`, `warning`, `unknown`). Do not use a `decision` record to store a warning, and vice versa.

3. **Preserve warnings and unknowns.** Do not discard or suppress warning and unknown records. They exist because the operator acknowledged uncertainty — that acknowledgement has audit value.

4. **Do not save casual chatter.** Casual conversation is not corpus evidence, is not an operational record, and is not a fact. Importing it indiscriminately pollutes retrieval and destroys auditability.

5. **Do not let the model decide what is true.** The model's output is advisory. Fact creation, fact supersession, and operational record creation require explicit operator intent. Do not wire these commands to automatic model output without a human review gate.

6. **Prefer narrow writes over broad imports.** If the operator wants one durable note, write one operational record. Do not import an entire session just to capture one takeaway.

7. **Raw chat is evidence, not truth.** A saved session proves that text was observed in a session artifact. It does not prove that the content is correct, current, or safe to act on. Session messages can contain mistakes, jokes, partial plans, or abandoned ideas.

## Auditability Guarantees

Tiro's design favors postmortem traceability over a clean single-truth table:

- Original corpus chunks, session messages, and operational records are stored separately and remain individually inspectable.
- Tiro does not rewrite source chunks during proxy generation or rebuild operations.
- Superseded and stale records remain in the database and can be inspected. They are not deleted.
- Fact supersession chains are explicit: a superseded fact references the fact that superseded it, and the new fact references what it superseded.
- Conflict records are explicit: `fact_conflicts` stores the pair, not a merged or resolved value.

This means the database is an append-and-annotate store, not a mutable truth table. Corrections create new records with explicit relationships to the old ones.

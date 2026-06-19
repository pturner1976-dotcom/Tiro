# Tiro Memory Model

## Corpus/archive memory

Corpus memory is durable imported document evidence stored as sources, documents, and chunks. It is appropriate for project documents, implementation reports, audits, fixtures, and other material intentionally imported for retrieval.

Do not store raw casual chat, mutable operational status, or unapproved claims as corpus chunks.

## Session/state memory

Session memory stores saved AIChat sessions, explicit session notes, and short-term working state promoted into Tiro. Raw chat is evidence that a conversation happened, not proof that the content is true.

Do not turn every message into memory. Session memory needs explicit operator intent or an approved saved-session ingest.

## Operational memory

Operational memory stores explicit decisions, TODOs, warnings, and unknowns. These records should be short, labeled, and attributable to an origin.

Do not use operational records for general documents, unreviewed facts, secrets, or casual chatter.

## Fact lifecycle memory

Fact lifecycle memory stores active, stale, superseded, conflicting, and unknown facts. It exists so facts can carry status and conflict information.

AIChat tools do not mutate lifecycle facts in the current public surface. Fact mutation remains an internal/operator-controlled activity.

## What belongs in each lane

Use corpus for imported durable documents. Use session memory for conversation artifacts and explicit state notes. Use operational memory for decisions, TODOs, warnings, and unknowns. Use fact lifecycle only when a claim needs durable truth-state tracking.

Discard material that is casual, unapproved, secret-bearing, unsupported, or too vague to retrieve responsibly.

## What must not be stored in each lane

Corpus must not become a dumping ground for live chat. Session memory must not be treated as truth. Operational memory must not contain secrets or unlabeled claims. Fact lifecycle must not be mutated by unapproved tooling.

## Raw chat as evidence, not truth

A saved chat proves that text was observed in a session artifact. It does not prove that the statement inside the chat is correct, current, or safe to act on.

## Operator discipline rules

Use explicit provenance. Label operational records. Preserve warnings and unknowns. Do not save casual chatter. Do not let the model decide what is true. Prefer a narrow memory write over a broad import when the operator only wants one durable note.

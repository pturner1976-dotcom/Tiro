# Tiro Roadmap

This roadmap reflects the state of the project as of mid-2026.

---

## Implemented Capabilities

The following capabilities are present in the current implementation:

- **Implemented: SQLite-backed corpus store** — deterministic storage and retrieval
  of ingested corpus chunks with full source and document inventory.
- **Implemented: hybrid lexical retrieval** — term-normalized lexical search across
  corpus, session evidence, operational records, and facts. No external dependencies.
- **Implemented: memory lane separation** — corpus, session, operational, and fact
  lanes are stored and queried independently without cross-contamination.
- **Implemented: session evidence tracking** — session messages are stored with
  direction, source identity, and timestamp. Sessions are queryable by ID or by
  recent-first ordering.
- **Implemented: operational record types** — explicit lifecycle tracking for
  decisions, todos, warnings, and unknowns, with optional session scoping.
- **Implemented: lifecycle-aware fact tracking** — facts carry status (`active`,
  `stale`, `superseded`, `conflicting`, `unknown`), provenance links, supersession
  chains, and conflict pairs.
- **Implemented: planner-assisted query refinement** — a lightweight query planner
  refines retrieval terms and selects lane strategy, with deterministic fallback when
  disabled.
- **Implemented: natural recall without IDs** — `recall` and `tiro_recall` find
  relevant content across all lanes without requiring the caller to supply source or
  session IDs.
- **Implemented: proxy-pointer corpus recall** — structure-aware proxy records and
  evidence pointers support efficient recall that hydrates authoritative corpus content
  on demand. Integrated into the normal `recall` path.
- **Implemented: explicit read/write tool separation** — read-only tools (`tiro_query`,
  `tiro_recall`, etc.) are separated from the write tool (`tiro_ingest_state`) at the
  agent interface boundary.

---

## Deferred Proxy Families

Proxy construction is currently implemented for corpus documents. The following proxy
families were scoped and deferred for future work:

- Session-window proxies
- Operational-record proxies
- Lifecycle/fact-chain proxies

---

## Planned Next Steps

### LLM Evidence Reranking

Bounded reranking over candidate pools using a language model. The reranker must not
invent evidence, override provenance, or bypass lifecycle policy. Retrieved evidence
must remain traceable to its source regardless of reranking order.

### Secret Redaction and Retrieval Hardening

Stronger secret and canary-aware retrieval guardrails on the Tiro side: detection and
redaction of credential-like patterns before evidence is returned to the caller.

### Additional Documentation Maintenance

A documentation maintenance pass once more retrieval and safety features are stable.

### Network/Log Source Integration

Integration with structured log and invariant sources as additional corpus lanes,
making network and operational telemetry queryable through the same retrieval surface.

---

## What Should Not Be Rushed

The following capabilities have known safety and discipline requirements. They should
not be added incrementally without corresponding audit and policy work:

- **Embeddings without audit discipline** — vector similarity search requires careful
  scope controls to avoid surfacing content that should be filtered.
- **Autonomous fact promotion** — facts should be promoted or superseded by explicit
  operator instruction, not by agent inference alone.
- **Broad shell exposure** — tool surfaces that expose shell execution or filesystem
  mutation must have explicit permission boundaries.
- **Browser automation from hostile web content** — retrieving external content must
  not trigger automated interaction with browser state or memory writes.
- **Reranking that overrides provenance or lifecycle policy** — a reranker that can
  suppress evidence because of its ranking score, rather than its lifecycle status,
  violates the provenance contract.

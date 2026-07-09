# Tiro Database Schema

Tiro uses a local SQLite database. This document describes the full schema: all tables,
columns, indexes, foreign key relationships, and known limitations.

---

## Initialization Behavior

- `PRAGMA foreign_keys = ON` is set on every connection open.
- `PRAGMA journal_mode = WAL` is set on every connection open.
- `InitializeSchema()` is called on demand by operational methods; the schema is
  created if it does not exist.
- An additive compatibility repair is applied to older databases:
  `EnsureColumn("messages", "source_identity", "TEXT NOT NULL DEFAULT '')` adds the
  column if it is missing, allowing older databases to work with current code without
  a full migration.
- Additional additive repairs now backfill `archived_utc TEXT NULL DEFAULT NULL`
  onto `operational_records` and `facts` when older databases are opened. Existing
  rows remain unarchived (`NULL`) after migration.

The schema is migration-light by design. It prefers create-if-missing plus additive
column repair over a full migration framework.

---

## Tables

### `schema_info`

Schema version and bookkeeping metadata.

| Column | Type | Notes |
|---|---|---|
| `key` | TEXT | Metadata key |
| `value` | TEXT | Metadata value |

---

### `sources`

Corpus source inventory. Each ingested corpus (a collection of documents) has one row.

| Column | Type | Notes |
|---|---|---|
| `source_id` | INTEGER | Primary key |
| `source_name` | TEXT | Human-readable name |
| `source_path` | TEXT | Original path or identifier of the ingested source |
| `timeframe_or_era` | TEXT | Optional era/timeframe label |
| `created_utc` | TEXT | ISO 8601 UTC timestamp |

---

### `documents`

Corpus document inventory within a source. Each ingested file or logical document
has one row.

| Column | Type | Notes |
|---|---|---|
| `document_id` | INTEGER | Primary key |
| `source_id` | INTEGER | Foreign key → `sources.source_id` |
| `title` | TEXT | Document title |
| `created_utc` | TEXT | ISO 8601 UTC timestamp |

---

### `chunks`

Authoritative corpus evidence chunks. Each chunk is a text segment from a document.

| Column | Type | Notes |
|---|---|---|
| `chunk_id` | INTEGER | Primary key |
| `document_id` | INTEGER | Foreign key → `documents.document_id` |
| `source_id` | INTEGER | Foreign key → `sources.source_id` (denormalized for query performance) |
| `chunk_index` | INTEGER | Position of this chunk within its document |
| `chunk_count` | INTEGER | Total chunks in the document at ingest time |
| `text` | TEXT | Chunk text content |
| `created_utc` | TEXT | ISO 8601 UTC timestamp |

---

### `sessions`

Session identity and creation timestamps.

| Column | Type | Notes |
|---|---|---|
| `session_id` | TEXT | Primary key (caller-supplied identifier) |
| `created_utc` | TEXT | ISO 8601 UTC timestamp |

---

### `messages`

Chronological session evidence. Each stored turn or note in a session has one row.

| Column | Type | Notes |
|---|---|---|
| `message_id` | INTEGER | Primary key |
| `session_id` | TEXT | Foreign key → `sessions.session_id` |
| `direction` | TEXT | `user`, `assistant`, `operator`, or `system` |
| `text` | TEXT | Message content |
| `timestamp_utc` | TEXT | ISO 8601 UTC timestamp |
| `source_identity` | TEXT | Identity label of the message origin; may be backfilled as `''` in older databases |

---

### `operational_records`

Explicit operator memory: decisions, todos, warnings, and unknowns.

| Column | Type | Notes |
|---|---|---|
| `record_id` | INTEGER | Primary key |
| `record_type` | TEXT | `decision`, `todo`, `warning`, or `unknown` |
| `text` | TEXT | Record content |
| `origin` | TEXT | Provenance label |
| `session_id` | TEXT | Optional; links to `sessions.session_id` when session-scoped |
| `created_utc` | TEXT | ISO 8601 UTC timestamp |
| `status` | TEXT | Operational lifecycle state such as `open` or `closed` |
| `archived_utc` | TEXT | Nullable ISO 8601 timestamp; non-null means the row is archived and excluded from retrieval by default |

---

### `facts`

Lifecycle-aware fact storage. Facts can be active, stale, superseded, conflicting,
or unknown, and may reference other records for provenance.

| Column | Type | Notes |
|---|---|---|
| `fact_id` | INTEGER | Primary key |
| `created_utc` | TEXT | ISO 8601 UTC timestamp |
| `text` | TEXT | Fact assertion text |
| `status` | TEXT | `active`, `stale`, `superseded`, `conflicting`, or `unknown` |
| `source_id` | INTEGER | Source the fact was derived from |
| `origin_identity` | TEXT | Identity label of the origin |
| `session_id` | TEXT | Optional; session the fact was derived from |
| `linked_source_ids` | TEXT | JSON array of related source IDs |
| `linked_message_ids` | TEXT | JSON array of related message IDs |
| `linked_record_ids` | TEXT | JSON array of related operational record IDs |
| `supersedes_fact_id` | INTEGER | ID of the fact this one supersedes, if any |
| `superseded_by_fact_id` | INTEGER | ID of the fact that supersedes this one, if any |
| `freshness_hint` | TEXT | Optional human-readable freshness label |
| `expires_utc` | TEXT | Optional ISO 8601 expiry timestamp |
| `archived_utc` | TEXT | Nullable ISO 8601 timestamp; archival is orthogonal to lifecycle status and never overwrites `status` |

---

### `fact_conflicts`

Explicit symmetric conflict pairs between facts. If two facts conflict, both orderings
are expected to be represented.

| Column | Type | Notes |
|---|---|---|
| `fact_id_1` | INTEGER | First fact in conflict pair |
| `fact_id_2` | INTEGER | Second fact in conflict pair |

---

### `recall_proxies`

Searchable, structure-aware proxy records used to support recall without requiring
full content scans. Proxies are built by an operator maintenance step and point to
authoritative evidence via `evidence_pointers`.

| Column | Type | Notes |
|---|---|---|
| `proxy_id` | INTEGER | Primary key |
| `lane` | TEXT | Memory lane: `corpus`, `session`, `operational`, `facts`, or `proxy` |
| `proxy_type` | TEXT | Categorization label within a lane |
| `title` | TEXT | Short descriptive title |
| `breadcrumb` | TEXT | Hierarchical path label (e.g., source > document > section) |
| `summary` | TEXT | Prose summary of the content this proxy covers |
| `keywords` | TEXT | Extracted keyword list |
| `entities` | TEXT | Extracted entity list |
| `source_id` | INTEGER | Optional; links to `sources.source_id` |
| `document_id` | INTEGER | Optional; links to `documents.document_id` |
| `session_id` | TEXT | Optional; links to `sessions.session_id` |
| `record_id` | INTEGER | Optional; links to `operational_records.record_id` |
| `fact_id` | INTEGER | Optional; links to `facts.fact_id` |
| `pointer_id` | INTEGER | Optional; links to `evidence_pointers.pointer_id` for hydration |
| `status` | TEXT | `active`, `superseded`, or similar lifecycle status |
| `created_utc` | TEXT | ISO 8601 UTC timestamp |
| `updated_utc` | TEXT | ISO 8601 UTC timestamp |
| `metadata_json` | TEXT | Arbitrary JSON metadata bag |

---

### `evidence_pointers`

Stable pointer records used to hydrate authoritative evidence. A pointer stores the
minimal information needed to retrieve exact content (chunk range, message range,
or record/fact ID) without duplicating the content itself.

| Column | Type | Notes |
|---|---|---|
| `pointer_id` | INTEGER | Primary key |
| `target_lane` | TEXT | Lane the target content belongs to |
| `target_kind` | TEXT | Kind of content: `chunk`, `message`, `record`, `fact`, etc. |
| `source_id` | INTEGER | Optional; links to `sources.source_id` |
| `document_id` | INTEGER | Optional; links to `documents.document_id` |
| `chunk_id_start` | INTEGER | Optional; first chunk in a range |
| `chunk_id_end` | INTEGER | Optional; last chunk in a range |
| `session_id` | TEXT | Optional; links to `sessions.session_id` |
| `message_id_start` | INTEGER | Optional; first message in a range |
| `message_id_end` | INTEGER | Optional; last message in a range |
| `record_id` | INTEGER | Optional; links to `operational_records.record_id` |
| `fact_id` | INTEGER | Optional; links to `facts.fact_id` |
| `text_hash` | TEXT | Hash of the content at pointer creation time |
| `created_utc` | TEXT | ISO 8601 UTC timestamp |
| `metadata_json` | TEXT | Arbitrary JSON metadata bag |

---

## Indexes

| Index | Table | Purpose |
|---|---|---|
| `idx_chunks_document_order` | `chunks` | Fast ordered chunk reads per document |
| `idx_chunks_source` | `chunks` | Filter chunks by source |
| `idx_messages_session_time` | `messages` | Chronological message reads per session |
| `idx_operational_records_type_session` | `operational_records` | Filter by type and session |
| `idx_facts_status` | `facts` | Filter facts by lifecycle status |
| `idx_facts_source` | `facts` | Filter facts by source |
| `idx_evidence_pointers_source_id` | `evidence_pointers` | Pointer lookup by source |
| `idx_evidence_pointers_document_id` | `evidence_pointers` | Pointer lookup by document |
| `idx_evidence_pointers_session_id` | `evidence_pointers` | Pointer lookup by session |
| `idx_recall_proxies_lane` | `recall_proxies` | Filter proxies by lane |
| `idx_recall_proxies_document_id` | `recall_proxies` | Proxy lookup by document |
| `idx_recall_proxies_source_id` | `recall_proxies` | Proxy lookup by source |
| `idx_recall_proxies_status` | `recall_proxies` | Filter proxies by status |
| `idx_recall_proxies_pointer_id` | `recall_proxies` | Join proxies to evidence pointers |

---

## Foreign Key Relationships

Enforced by SQLite foreign key constraints (enabled per connection):

- `documents.source_id` → `sources.source_id`
- `chunks.document_id` → `documents.document_id`
- `chunks.source_id` → `sources.source_id`
- `messages.session_id` → `sessions.session_id`
- `operational_records.session_id` → `sessions.session_id` (when session-scoped)
- `facts.session_id` → `sessions.session_id` (when present)

Application-level relationships (not foreign-key-enforced):

- `recall_proxies.pointer_id` → `evidence_pointers.pointer_id`
- `recall_proxies.document_id`, `source_id`, `session_id`, `record_id`, `fact_id`
  reference the underlying lane objects by application logic rather than formal FK
  constraints.

---

## Known Limitations

- There is no formal migration framework. Schema evolution uses additive column repair
  (`EnsureColumn`) rather than versioned migrations.
- Proxy and pointer foreign-key enforcement is primarily application-level. Not every
  cross-reference between `recall_proxies`, `evidence_pointers`, and the underlying
  lane tables is constrained at the SQLite level.
- The database is local SQLite using WAL mode. Read-only audit tooling must account
  for the WAL file; opening the database without the WAL can yield stale reads.
- Row IDs are local integers. They are not stable cross-database identities and must
  not be used to reference records across different database files.

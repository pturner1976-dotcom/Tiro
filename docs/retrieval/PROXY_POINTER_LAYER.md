# Proxy-Pointer Layer

The proxy-pointer layer adds a two-stage retrieval path on top of Tiro's base lexical recall. Proxies are lightweight searchable records optimized for fast matching. Pointers are stable references that identify the authoritative evidence target. Hydration is the act of loading the original evidence from the underlying lane using a pointer.

---

## Concepts

### Proxy

A proxy is a lightweight, denormalized record stored in the `recall_proxies` table. Its purpose is to be searched efficiently. A proxy carries:

- A human-readable title and breadcrumb path
- Extracted keywords and named entities
- A short summary of the source content
- Structured metadata (lane, source ID, document ID, status)

Proxy summaries are search aids, not truth. The text in a proxy record describes what the source contains; it is not authoritative evidence by itself.

### Pointer

An evidence pointer is a stable hydration record stored in the `evidence_pointers` table. Its purpose is to identify the authoritative evidence target — the specific lane record, chunk, or chunk range that holds the original content.

Each proxy links to one or more pointers. Pointers survive proxy rebuilds as long as the underlying content has not changed.

### Hydration

Hydration means following a pointer to its target and loading the original authoritative evidence from the underlying Tiro lane. The hydrated content — not the proxy summary — is what gets returned to the caller.

**Core rule:** use proxies to find; use hydrated pointer targets to answer.

---

## Proxy Index Build

```bash
tiro --db "$TIRO_DB_PATH" proxy-build corpus --document-id <id> [--rebuild]
```

Build behavior for corpus proxies:

- Initializes the proxy schema if it is not already present.
- Creates one `corpus_document` proxy per document — a document-level searchable record.
- Creates one `corpus_chunk_group` proxy per chunk — a finer-grained searchable record.
- The document proxy anchors to the first chunk as its hydration target.
- Chunk proxies use deterministic summaries and extracted keywords derived from chunk content.
- Running build again on the same document without `--rebuild` issues a warning and does not duplicate active proxies.
- Running with `--rebuild` supersedes all prior active proxies for the targeted document or source before generating new ones.

In the current implementation, proxy generation covers corpus documents and chunks. Session, operational, and lifecycle proxy families are not yet generated automatically and must be handled through direct recall.

---

## Proxy Inspection

```bash
tiro --db "$TIRO_DB_PATH" proxy-inspect --document-id <id>
```

Inspection filters the proxy table by lane, document ID, source ID, status, and result limit. It returns proxy metadata without hydrating any evidence, making it a low-cost diagnostic tool for verifying that proxies were built correctly.

Useful for:
- Confirming a document has active proxies before running recall
- Checking proxy status (active, stale, superseded, hidden)
- Reviewing which chunk groups were indexed

---

## Proxy Recall

```bash
tiro --db "$TIRO_DB_PATH" proxy-recall "<query terms>"
```

Proxy recall searches the proxy index using the same lexical normalization as base recall (tokenization, stopword removal, suffix normalization). The following proxy fields participate in scoring:

- Title
- Breadcrumb path
- Keywords
- Named entities
- Summary text
- Structured metadata

**Status filtering:**
- `hidden` proxies are excluded from all results.
- `stale` and `superseded` proxies are penalized in scoring but may appear if they are the only match.

After scoring, the top-ranked proxies are hydrated: their pointers are followed and the original chunk content is loaded from the corpus lane. The caller receives hydrated evidence, not raw proxy records.

---

## Hydration

```bash
tiro --db "$TIRO_DB_PATH" proxy-hydrate <pointer_id>
```

Direct hydration loads the authoritative evidence for a single pointer. In the current implementation, hydration is supported for corpus `chunk` and `chunk_range` pointer targets.

Hydration behavior:
- Returns structured `not_found` if the pointer ID does not exist.
- Issues a hash-mismatch warning when the stored content hash does not match the current chunk content, indicating the underlying document may have changed since the proxy was built.
- Non-corpus pointer targets are not yet implemented; they return a structured not-implemented response.

---

## Recall Integration

`TiroRecallService` incorporates proxy recall automatically when active corpus proxies are present:

1. Normal recall runs across all applicable lanes.
2. If active corpus proxies exist, proxy recall runs in parallel as an additional candidate source.
3. Proxy-derived evidence is merged into the combined result set.
4. Each proxy-derived item carries provenance metadata: `proxy_id`, `pointer_id`, `proxy_title`, and `proxy_breadcrumb`.
5. Results are deduplicated before the final packet is assembled.

If the proxy layer is empty — no active proxies for any document — normal recall runs without modification. The proxy layer is strictly additive; its absence does not degrade base recall.

---

## When to Use Proxy Recall vs Direct Recall

| Situation | Recommended approach |
|---|---|
| Corpus documents are large and chunked; want fuzzy topic search | Build proxies, use `recall` or `proxy-recall` |
| Caller has a known source ID or document ID | Use `query` directly; proxies add no benefit |
| Caller wants to explore what documents exist on a topic | Use `proxy-recall`; document-level proxies surface quickly |
| No proxies have been built yet | Use `recall`; base lexical search still works |
| Diagnosing why a document is not surfacing | Use `proxy-inspect` to check proxy status, then `proxy-hydrate` to test a specific pointer |

---

## Known Limitations

- **Corpus only (in the current implementation)** — proxy generation and hydration are implemented for corpus chunks and chunk ranges. Session, operational, and fact-lifecycle proxy families are not yet supported.
- **Manual build step required** — proxies are not built automatically on ingestion. Run `proxy-build` explicitly after ingesting new corpus documents, or after making significant changes to existing documents.
- **Rebuild required after content changes** — if a document's content changes, existing proxies may point to stale chunks. Run `proxy-build --rebuild` to regenerate. Hash-mismatch warnings during hydration indicate this condition.
- **No cross-document phrase matching** — proxy recall scores at the proxy record level. For exact phrase matching across documents, use `phrase-search` instead.

---

## Debugging Proxy Failures

- **`proxy-recall` returns no results** — verify that corpus ingestion completed and that `proxy-build` was run for the target document. Use `proxy-inspect` to confirm active proxies exist.
- **Proxies exist but hydration returns nothing** — use `proxy-hydrate <pointer_id>` directly to test a specific pointer. Check for hash-mismatch warnings indicating stale content.
- **Normal `recall` ignores proxies** — check whether any active corpus proxies exist in the database. If all proxies are stale or hidden, the proxy layer is skipped.
- **Result has proxy metadata but no evidence body** — hydration failed for that item. Treat it as weak evidence. The caller should not answer from proxy metadata alone.

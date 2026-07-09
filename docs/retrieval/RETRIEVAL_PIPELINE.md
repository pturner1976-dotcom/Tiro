# Retrieval Pipeline

Tiro's retrieval stack has two layers:

- **Lexical retrieval** — deterministic token matching and scoring. Always available, no external dependencies required.
- **Semantic retrieval** — vector embedding search via a local Ollama model or the OpenAI embeddings API. Optional; enabled through environment variables.

The `query` and `recall` commands use lexical retrieval plus an optional advisory planner. `semantic-query` runs pure vector search. `hybrid-search` merges both layers by configurable weight. All retrieval modes fall back gracefully: if semantic search is unavailable, lexical retrieval continues without interruption.

---

## Lexical Scoring

### Token Normalization

`LexicalSearch.Tokenize()` prepares query and document tokens using three steps:

1. **Alphanumeric extraction** — only alphanumeric characters are kept; punctuation and whitespace are discarded.
2. **Stopword stripping** — common function words (articles, prepositions, conjunctions, and similar) are removed.
3. **Suffix normalization** — common inflectional suffixes are trimmed to reduce surface variation (e.g., plurals, verb endings).

The same normalization is applied to both the query and the indexed fields, so comparisons are consistent.

### Current Stemming Rules

Normalization is intentionally small and deterministic rather than a full linguistic stemmer.

1. Lowercase the token and trim surrounding `'`, `.`, `-`, and `_`.
2. Collapse possessives ending in `'s` to the base token.
3. Preserve a protected exception list before suffix stripping:
   `analysis`, `billing`, `building`, `campus`, `ceiling`, `focus`, `meeting`, `plus`, `process`, `speed`, `status`, `virus`, `warning`
4. Collapse `ies` plurals to `y`.
5. Collapse `es` plurals for `ses` / `xes` / `zes` / `ches` / `shes`.
6. Strip `ing` for remaining tokens longer than 5 characters.
7. Strip `ed` for remaining tokens longer than 4 characters.
8. Strip a trailing `s` for remaining tokens longer than 3 characters, except words ending in `ss`, `us`, or `is`.

The exception list exists to avoid false-positive collisions such as `status -> statu`, `process -> proces`, and noun/verb collisions like `meeting -> meet`.

### Score Components

`LexicalSearch.Score()` computes a composite score from five components for each candidate evidence item:

| Component | What it measures |
|---|---|
| **Term hits** | How many normalized query tokens appear in the candidate's indexed fields |
| **Metadata hits** | Token matches against structured metadata fields (tags, labels, source identifiers) |
| **Coverage** | Fraction of distinct query tokens matched; rewards breadth of match over raw hit count |
| **Ordered phrase bonus** | Additional weight when query tokens appear in sequence within the same field |
| **Density (concentration) bonus** | Additional weight when matched tokens cluster tightly rather than scattering across a long field |

A candidate that matches all query terms in sequence within a short span scores significantly higher than one that matches the same terms in isolation across a long document.

---

## Retrieval Policy

`RetrievalPolicy` classifies each incoming query into a mode and applies mode-specific scoring signals on top of the base lexical score. The scoring constants live in `RetrievalWeights.Default`, which preserves the current default behavior but makes every component auditable from one place.

### Query Mode Detection

| Mode | When it applies |
|---|---|
| `current-state` | Caller is asking for the present known state of something |
| `decision` | Caller is asking about a choice, rationale, or outcome |
| `unresolved` | Caller is asking about open questions, pending items, or blockers |
| `archive` | Caller is retrieving historical or superseded records |
| `fact-lifecycle` | Caller is asking about fact status, confirmation, or revision history |
| `general` | No specific mode detected; broad lexical scoring applies |

The classifier evaluates all mode keyword sets against the normalized query terms, records every matching mode, and then resolves the winner deterministically:

1. Count matched terms per mode.
2. Pick the mode with the highest matched-term count.
3. On an exact tie, use fixed precedence:
   `fact-lifecycle` > `unresolved` > `decision` > `archive` > `current-state` > `general`

Diagnostics expose both the chosen mode and the full `competing_modes` list so tie situations are visible instead of being hidden by a first-match cascade.

### Per-Evidence Scoring Signals

After lexical scoring, retrieval policy applies up to five additional signals per evidence item:

1. **Relevance** — how well the item's content matches the inferred intent of the query mode
2. **Recency** — preference for recently written or updated evidence (weighted by mode; `archive` mode suppresses this)
3. **Importance** — metadata-declared importance or weight attached to the record
4. **Lifecycle** — penalizes stale, superseded, or retracted evidence; lifts confirmed or active evidence
5. **Mode alignment** — whether the evidence type is appropriate for the detected query mode (e.g., a lifecycle fact scores higher in `fact-lifecycle` mode than in `archive` mode)

Policy signals influence ordering and warnings. They do not widen or override explicit filters supplied by the caller.

---

## Planner Integration (Optional / Advisory)

`TiroQueryService` and recall flows support an optional retrieval planner in three modes:

- `off` — planner is disabled; pure deterministic lexical retrieval runs
- `on` — planner is always invoked before retrieval
- `auto` — planner is invoked when the query appears to benefit from expansion (heuristic)

When active, the planner can:

- Refine query terms (expand abbreviations, add synonyms)
- Suggest target lanes (corpus, session, operational, facts)
- Expand a vague query into multiple concrete sub-queries

### Current implementation: Gemini

The built-in planner client calls the Gemini API. Configure it with:

| Variable | Description | Default |
|----------|-------------|---------|
| `GEMINI_API_KEY` | Gemini API key | — (planner disabled if absent) |
| `GEMINI_PLANNER_MODEL` | Gemini model to use | `gemini-2.5-flash` |

The key is loaded from the process environment first, then from `~/.env/tiro.env` as a fallback.

### Custom planner implementations

The planner is abstracted behind `IRetrievalPlannerClient` in `GeminiPlannerClient.cs`:

```csharp
public interface IRetrievalPlannerClient
{
    Task<PlannerClientResult> PlanAsync(
        string query,
        IReadOnlyList<string> deterministicTerms,
        CancellationToken cancellationToken);
}
```

Any LLM or local model can be wired in by implementing this interface and supplying it to `TiroQueryService`. The planner contract requires returning a `PlannerClientResult` with refined terms and optional lane hints — the retrieval pipeline handles the rest deterministically.

**Fallback behavior is unconditional.** If the planner fails for any reason — network error, missing key, timeout, or malformed response — retrieval continues with the original query using deterministic lexical scoring. The planner is advisory; its absence never blocks retrieval.

---

## Query vs Recall

Tiro exposes two distinct retrieval entry points with different contracts.

### `query`

- Targeted evidence assembly with explicit filters.
- Caller supplies known lane names, source identifiers, or document IDs.
- Returns a structured `ContextPacket` with lanes kept separate.
- Warns when a `source_id` value looks like a lane name (a common misuse).
- Can include session evidence when `--session-id` is supplied.
- Best when the caller knows what they are looking for and wants structured output.

### `recall`

- Discovery-oriented; the caller does not need to know IDs in advance.
- Identifies intent classes automatically: document discovery, session discovery, topic search, negative controls.
- Searches across corpus, session, operational, and facts lanes.
- Best when the caller has a topic or question and wants Tiro to find the most relevant evidence.

Use `query` to retrieve a specific known item. Use `recall` when exploring or when IDs are unknown.

---

## Specialized Retrieval Surfaces

### `session-search`

Searches a bounded window of the most recent sessions. Issues a warning when the query window does not cover all available sessions, so the caller knows the result is not exhaustive.

### `phrase-search`

Performs exact substring matching rather than token-based scoring. Available across session, operational, corpus, facts, or all lanes simultaneously. When `lane=all`, a `session_id` argument constrains the session lane only and does not affect other lanes.

### Normal Recall Orchestration

`TiroRecallService` runs the full recall pipeline:

1. Expand query terms (planner if active, otherwise identity)
2. Search corpus, session, operational, facts, and phrase-match lanes
3. Optionally run proxy recall if active corpus proxies exist
4. Merge all candidate evidence
5. Deduplicate by source identity

If no active proxies exist, the service logs `No recall proxies available; fallback lexical recall used.` and continues without the proxy layer.

---

## Semantic Search

Semantic search embeds query text and corpus chunks into a shared vector space and retrieves by cosine similarity rather than token overlap. It is useful for concept-heavy or paraphrase-heavy queries where lexical overlap with stored text is low.

### Configuration

| Variable | Description | Default |
|----------|-------------|---------|
| `TIRO_SEMANTIC_ENABLED` | Must be `true` to activate semantic search | `false` |
| `TIRO_EMBEDDING_PROVIDER` | `local` (Ollama) or `openai` | `none` |
| `TIRO_EMBEDDING_MODEL` | Model name to use for embeddings | `text-embedding-3-small` |
| `TIRO_EMBEDDING_BASE_URL` | Base URL for the embedding API | `https://api.openai.com/v1` |

For local embeddings via Ollama, set `TIRO_EMBEDDING_PROVIDER=local` and `TIRO_EMBEDDING_BASE_URL=http://localhost:11434/v1`. No API key is required. The OpenAI-compatible embedding endpoint (`/embeddings`) is used regardless of provider.

### `semantic-index`

Embeds all un-indexed corpus chunks and stores the vectors in the database. Must be run before semantic search will return results.

```
tiro semantic-index [--lanes corpus] [--limit <n>] [--rebuild] [--dry-run]
```

`--rebuild` re-embeds chunks that already have vectors. `--dry-run` reports candidates without calling the embedding API.

### `semantic-query`

Pure vector search — embeds the query and retrieves the closest chunks by cosine similarity.

```
tiro semantic-query <query> [--limit <n>] [--min-score <f>] [--lanes corpus,session]
```

`--min-score` filters results below a similarity threshold (default `0.35`). Returns a structured JSON response with scored results and embedding metadata.

### `hybrid-search`

Merges lexical and semantic scores into a single ranked result set.

```
tiro hybrid-search <query> [--limit <n>] [--lanes corpus,session] \
  [--lexical-weight <f>] [--semantic-weight <f>] [--min-semantic-score <f>]
```

| Flag | Default | Description |
|------|---------|-------------|
| `--lexical-weight` | `0.55` | Weight applied to the lexical score component |
| `--semantic-weight` | `0.45` | Weight applied to the semantic score component |
| `--min-semantic-score` | `0.35` | Minimum cosine similarity for a semantic result to contribute |

If semantic search is unavailable (not enabled or no key), `hybrid-search` falls back to lexical-only scoring and notes the fallback in the response.

### `semantic-status`

Reports the current embedding configuration without making any API calls.

```
tiro semantic-status
```

---

## Fallback Behavior

| Condition | Fallback |
|---|---|
| Planner fails | Deterministic lexical retrieval with the original query |
| No active proxies | Non-proxy recall; full pipeline still runs |
| Weak or no matches | `warnings` and `unknowns` fields populated; no invented evidence |
| Stale/superseded facts match | Returned with lifecycle penalties and a warning |

Tiro never invents evidence to fill a gap. Absent evidence produces structured absence signals, not fabricated content.

---

## Common Failure Modes

- **Phrase exists, query misses it** — if a phrase is stored verbatim but shares few tokens with the query, lexical overlap may be too low. Use `phrase-search` for exact substring lookup.
- **`query` used where `recall` is appropriate** — `query` requires the caller to know source identifiers. If IDs are unknown, use `recall` instead.
- **Stale facts surface** — if a stale or superseded fact is the only lexical match, it will appear with a lifecycle penalty and warning. The caller must inspect the lifecycle status field.
- **Empty session results** — session evidence is only included in `query` when `--session-id` is supplied. Use `session-search` or `recall` for session discovery without a known ID.
- **Synonym-heavy queries miss** — `query` and `recall` use lexical scoring, which requires token overlap with stored text. For concept-heavy or synonym-heavy queries, use `semantic-query` or `hybrid-search` if embeddings are configured, or use planner expansion to broaden lexical terms.

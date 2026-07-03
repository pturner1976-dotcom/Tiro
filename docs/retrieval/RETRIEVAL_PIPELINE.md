# Retrieval Pipeline

Tiro uses a deterministic lexical retrieval stack. There are no embeddings and no vector indexes. All retrieval is based on token matching, scoring heuristics, and an optional advisory planner that can refine terms but whose failures always fall back to deterministic behavior.

---

## Lexical Scoring

### Token Normalization

`LexicalSearch.Tokenize()` prepares query and document tokens using three steps:

1. **Alphanumeric extraction** — only alphanumeric characters are kept; punctuation and whitespace are discarded.
2. **Stopword stripping** — common function words (articles, prepositions, conjunctions, and similar) are removed.
3. **Suffix normalization** — common inflectional suffixes are trimmed to reduce surface variation (e.g., plurals, verb endings).

The same normalization is applied to both the query and the indexed fields, so comparisons are consistent.

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

`RetrievalPolicy` classifies each incoming query into a mode and applies mode-specific scoring signals on top of the base lexical score.

### Query Mode Detection

| Mode | When it applies |
|---|---|
| `current-state` | Caller is asking for the present known state of something |
| `decision` | Caller is asking about a choice, rationale, or outcome |
| `unresolved` | Caller is asking about open questions, pending items, or blockers |
| `archive` | Caller is retrieving historical or superseded records |
| `fact-lifecycle` | Caller is asking about fact status, confirmation, or revision history |
| `general` | No specific mode detected; broad lexical scoring applies |

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
- **Synonym-heavy queries miss** — no embeddings means concept-heavy or synonym-heavy queries require good lexical overlap with stored text. Rephrase using terms that appear in the stored records, or use planner expansion.

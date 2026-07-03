# Debugging Guide

Troubleshooting matrix for common Tiro problems. Each entry describes the symptom, likely cause, diagnostic command, and fix.

---

## Retrieval Returns Nothing

**Symptom:** `query` or `recall` returns zero results for content you expect to be present.

**Likely causes:**
- `TIRO_DB_PATH` is unset, empty, or points to a different file than the one that was ingested.
- Corpus was never ingested into this database.
- The query terms do not match the stored text after tokenization.

**Diagnose:**

```bash
# Confirm the variable is set and the file exists
echo "$TIRO_DB_PATH"
ls -lh "$TIRO_DB_PATH"

# Check table counts
dotnet run --project src/Tiro.Cli/Tiro.Cli.csproj -- --db "$TIRO_DB_PATH" inspect stats

# Confirm the source was ingested
dotnet run --project src/Tiro.Cli/Tiro.Cli.csproj -- --db "$TIRO_DB_PATH" inspect sources --limit 20

# Test with an exact phrase known to be in the corpus
dotnet run --project src/Tiro.Cli/Tiro.Cli.csproj -- --db "$TIRO_DB_PATH" phrase-search \
  "exact phrase from your corpus" --lane corpus
```

**Fix:** If `inspect stats` shows zero chunks and `inspect sources` is empty, re-run ingestion against the correct database path. If `phrase-search` finds the text but `query` does not, see "Low Confidence Results" below.

---

## Low Confidence Results / Wrong Ranking

**Symptom:** `query` returns results but they are not the expected chunks, or confidence scores are lower than expected.

**Likely causes:**
- Lexical tokenization did not rank the expected chunk strongly enough for the query phrasing.
- Proxies have not been built, so the proxy-layer ranking path is unavailable.

**Diagnose:**

```bash
# Confirm text exists in the DB
dotnet run --project src/Tiro.Cli/Tiro.Cli.csproj -- --db "$TIRO_DB_PATH" phrase-search \
  "exact phrase" --lane corpus

# Inspect score components and normalization
dotnet run --project src/Tiro.Cli/Tiro.Cli.csproj -- --db "$TIRO_DB_PATH" --planner off \
  search-debug "your query"

# Try open recall and proxy recall as alternatives
dotnet run --project src/Tiro.Cli/Tiro.Cli.csproj -- --db "$TIRO_DB_PATH" recall "topic"
dotnet run --project src/Tiro.Cli/Tiro.Cli.csproj -- --db "$TIRO_DB_PATH" proxy-recall "topic"
```

**Fix:** If the text exists but scores poorly, build proxies for the relevant document and retry `proxy-recall`. Adjust query phrasing to use terms closer to the stored text. Use `recall` rather than `query` for open-ended discovery.

---

## Session Evidence Missing

**Symptom:** A session was ingested but querying by `session_id` returns no evidence.

**Likely causes:**
- The `session_id` used at query time does not match the derived ID used during ingestion.
- Ingestion was run against a different `TIRO_DB_PATH`.
- The session file was empty or contained no parseable messages.

**Diagnose:**

```bash
# List all known sessions
dotnet run --project src/Tiro.Cli/Tiro.Cli.csproj -- --db "$TIRO_DB_PATH" inspect sessions

# Inspect stats to confirm session row counts
dotnet run --project src/Tiro.Cli/Tiro.Cli.csproj -- --db "$TIRO_DB_PATH" inspect stats
```

**Fix:** Use the exact `session_id` value returned by `inspect sessions`. Do not construct session IDs manually from lane names or file names — they are derived identifiers. If no sessions are listed, re-run ingestion and confirm it exits without error.

---

## Facts Not Appearing

**Symptom:** A fact that should be stored is not returned by retrieval, or `inspect stats` shows zero facts when some are expected.

**Likely causes:**
- Facts are written only by explicit fact-ingestion paths. Session import does not create facts.
- The fact was written to a different database.

**Diagnose:**

```bash
dotnet run --project src/Tiro.Cli/Tiro.Cli.csproj -- --db "$TIRO_DB_PATH" inspect stats
dotnet run --project src/Tiro.Cli/Tiro.Cli.csproj -- --db "$TIRO_DB_PATH" --planner off \
  search-debug "fact keyword"
```

**Fix:** Confirm the fact write path was invoked explicitly against the correct database. Session import (`ingest-aichat-session`) does not populate the facts lane — this is by design.

---

## Planner Errors or Unexpected Planner Behavior

**Symptom:** Retrieval results change unpredictably, or the planner selects wrong lanes/sources.

**Likely causes:**
- Planner inference produced an incorrect intent classification.
- A planner failure did not fall back cleanly to direct retrieval.

**Diagnose:**

```bash
# Bypass the planner to establish a deterministic baseline
dotnet run --project src/Tiro.Cli/Tiro.Cli.csproj -- --db "$TIRO_DB_PATH" --planner off query \
  "your query"

# Expose bounded planner diagnostics (does not print secrets)
dotnet run --project src/Tiro.Cli/Tiro.Cli.csproj -- --db "$TIRO_DB_PATH" --debug-planner query \
  "your query"
```

**Fix:** If `--planner off` returns correct results but the planner does not, the planner's intent classification is wrong for this query phrasing. Rephrase the query or use `--planner off` explicitly. Planner failure should fall back to direct retrieval rather than blocking — if retrieval is completely blocked, that is a separate bug.

---

## Semantic Search Not Working / Embedding Issues

**Symptom:** Semantic or embedding-based retrieval returns nothing or consistently poor results, while lexical search (`phrase-search`) works.

**Likely causes:**
- Embeddings were not generated at ingest time.
- The embedding backend is unavailable or misconfigured.
- Chunks are present but have no embedding vectors stored.

**Diagnose:**

```bash
# Confirm lexical path works independently
dotnet run --project src/Tiro.Cli/Tiro.Cli.csproj -- --db "$TIRO_DB_PATH" phrase-search \
  "known phrase" --lane corpus

# Use search-debug to see which lanes and paths are being used
dotnet run --project src/Tiro.Cli/Tiro.Cli.csproj -- --db "$TIRO_DB_PATH" --planner off \
  search-debug "your query"
```

**Fix:** Re-ingest with an embedding configuration that is active, or confirm the embedding model endpoint is reachable. If only lexical retrieval is available, `phrase-search`, `query`, and `recall` will still function — semantic ranking will be absent.

---

## Ingestion Failures

**Symptom:** `ingest-chunks` or `ingest-aichat-session` exits with an error, or subsequent `inspect stats` shows no change.

**Likely causes:**
- The input file path is wrong or the file does not exist.
- The JSONL is malformed — a line is not valid JSON or is missing required fields.
- `TIRO_DB_PATH` is unset or the path is not writable.

**Diagnose:**

```bash
# Verify the file exists and is non-empty
ls -lh /path/to/corpus-chunks.jsonl

# Check the first few lines for JSON validity
head -3 /path/to/corpus-chunks.jsonl | jq .

# Confirm DB path and permissions
ls -lh "$TIRO_DB_PATH"

# Re-run ingest directly and capture stderr
dotnet run --project src/Tiro.Cli/Tiro.Cli.csproj -- --db "$TIRO_DB_PATH" ingest-chunks \
  /path/to/corpus-chunks.jsonl 2>&1
```

**Fix:** Correct the file path, fix any malformed JSON lines, and ensure `TIRO_DB_PATH` is set and writable. If the file has mixed line endings or a BOM, strip them before ingestion.

---

## Wrong `source_id`, `document_id`, or `session_id`

**Symptom:** Filtered queries against a specific source, document, or session return nothing even though the data exists.

**Likely cause:** The ID passed to the command was guessed from a lane name or display label rather than read from the database.

**Diagnose:**

```bash
dotnet run --project src/Tiro.Cli/Tiro.Cli.csproj -- --db "$TIRO_DB_PATH" inspect sources --limit 20
dotnet run --project src/Tiro.Cli/Tiro.Cli.csproj -- --db "$TIRO_DB_PATH" inspect documents --limit 20
dotnet run --project src/Tiro.Cli/Tiro.Cli.csproj -- --db "$TIRO_DB_PATH" inspect sessions
```

**Fix:** Use the exact identifier values returned by `inspect sources`, `inspect documents`, or `inspect sessions`. Do not substitute lane names like `corpus` for `source_id` values — they are not interchangeable. `TiroQueryService` will warn if a `source_id` value looks like a lane label.

---

## Proxy Hydration Fails

**Symptom:** `proxy-recall` returns proxy pointers but hydration fails or returns empty text.

**Likely causes:**
- The pointer target kind is not implemented for hydration in the current implementation.
- Text hash mismatch: the underlying chunk was modified after the proxy was built.
- The proxy is stale or marked superseded.

**Diagnose:**

```bash
dotnet run --project src/Tiro.Cli/Tiro.Cli.csproj -- --db "$TIRO_DB_PATH" proxy-hydrate <pointer_id>
dotnet run --project src/Tiro.Cli/Tiro.Cli.csproj -- --db "$TIRO_DB_PATH" proxy-inspect \
  --document-id <document_id>
```

**Fix:** If hash mismatch is reported, rebuild proxies for the affected document. If the pointer target kind is unimplemented, use `recall` or `phrase-search` as a fallback for the affected document.

---

## Wrapper Exit Code or JSON Error

**Symptom:** A shell wrapper returns a JSON error response or exits nonzero. Stderr is empty or redacted.

**Likely causes:**
- Read-only wrappers return structured JSON failure with exit `0` for usage or validation errors.
- Write-path wrappers (`tiro_ingest_state`) may exit nonzero for runtime failures.
- A deeper CLI error was redacted from stderr by the wrapper.

**Diagnose:**

Run the equivalent CLI command directly (without the wrapper) to see unredacted output:

```bash
dotnet run --project src/Tiro.Cli/Tiro.Cli.csproj -- --db "$TIRO_DB_PATH" <command> 2>&1
```

**Fix:** Resolve the underlying CLI error, then retry through the wrapper. If the wrapper itself is the source of the error, inspect the wrapper script's argument handling.

---

## Caller Reports IDs Not in the Database

**Symptom:** An AI agent caller cites specific `source_id`, `document_id`, or chunk IDs that do not appear in `inspect` output.

**Likely cause:** The caller hallucinated the identifiers rather than reading them from tool output.

**Fix:** Treat any ID not returned by an `inspect` command as invalid. Re-run the relevant query tool and use only the identifiers returned in the actual tool response. Do not allow the caller to pass self-generated IDs to filtered queries.

---

## Raw SQLite Inspection

When CLI commands are insufficient for diagnosis, direct SQLite inspection is available. Use read-only techniques:

```bash
sqlite3 "$TIRO_DB_PATH" ".tables"
sqlite3 "$TIRO_DB_PATH" "SELECT COUNT(*) FROM Chunks;"
```

Caution: the database uses WAL mode. Reads during active writes may see an inconsistent snapshot. Never run ad hoc `DELETE`, `UPDATE`, or `DROP` against a live database.

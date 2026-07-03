# Testing

Covers build commands, the automated test suite, manual smoke tests, memory retention verification, and the regression checklist.

---

## Build Commands

Run from the repository root.

### Full solution build

```bash
dotnet build TiroV1.sln --disable-build-servers
```

### Run unit tests (with build)

```bash
dotnet test TiroV1.sln --verbosity normal
```

### Run unit tests (skip rebuild)

```bash
dotnet run --project tests/Tiro.Cli.Tests --no-build
dotnet test TiroV1.sln --no-build --verbosity normal
```

Run the full build/test sequence after any schema change, retrieval logic change, or planner modification.

---

## Smoke Scripts

The `scripts/` directory contains a set of shell-based smoke tests. Each script is self-contained and targets a specific boundary or behavior. Run them with `$TIRO_DB_PATH` set to a test database — do not run destructive or write-path smokes against your production database.

### Direct CLI smoke

```bash
scripts/tiro_direct_smoke.sh
```

Writes a unique operational decision through the CLI, retrieves it, and verifies that a unique negative-control token returns no evidence. Proves that the CLI ingest/query path works without wrappers.

### Wrapper smoke

```bash
scripts/tiro_wrapper_smoke.sh
```

Writes through the ingest wrapper and reads through the query wrapper. Proves the wrapper boundary functions correctly and that wrappers invoke the CLI as expected.

### Memory proof (carry-forward)

```bash
scripts/tiro_memory_proof.sh
```

Queries a unique token before ingestion (expects zero results), writes it as an operational decision, queries again in the same process, then queries again in a fresh CLI process. All three post-ingest queries must return evidence. Proves persistence across process boundaries.

### Session checkpoint smoke

```bash
scripts/tiro_session_checkpoint_smoke.sh
```

Imports a saved session YAML, queries it by session ID, and verifies that facts and corpus chunks were not mutated during import. Proves session ingestion respects lane isolation.

### Latest-session smoke

```bash
scripts/tiro_latest_session_smoke.sh
```

Confirms newest-session discovery behavior and that derived session IDs are deterministic for a given input session file.

### Stats helper

```bash
scripts/tiro_stats.sh
```

Prints the active database path, file size, modification time, table row counts, and recent samples from session messages, operational records, chunks, and facts. Useful as a quick health check before running other tests.

---

## Validate the Function Manifest

Run this after any change to wrapper scripts or tool definitions, before starting an agent session:

```bash
cd <your-agent-functions-directory>
argc --argc-run argcfile.sh build
argc --argc-run argcfile.sh check
jq -r '.[].name' functions.json
```

Confirms the manifest is well-formed and lists the tool names that will be visible to the agent runtime.

---

## Manual Corpus Smoke Test

Use any corpus JSONL file to confirm the ingest/proxy/recall pipeline end to end.

```bash
# Ingest corpus chunks
dotnet run --project src/Tiro.Cli/Tiro.Cli.csproj -- --db "$TIRO_DB_PATH" ingest-chunks \
  /path/to/your-corpus-chunks.jsonl

# Build proxies for a document
dotnet run --project src/Tiro.Cli/Tiro.Cli.csproj -- --db "$TIRO_DB_PATH" proxy-build corpus \
  --document-id <document_id>

# Inspect proxy state
dotnet run --project src/Tiro.Cli/Tiro.Cli.csproj -- --db "$TIRO_DB_PATH" proxy-inspect \
  --document-id <document_id>

# Recall via proxy layer
dotnet run --project src/Tiro.Cli/Tiro.Cli.csproj -- --db "$TIRO_DB_PATH" proxy-recall \
  "topic from the ingested corpus"

# Open recall
dotnet run --project src/Tiro.Cli/Tiro.Cli.csproj -- --db "$TIRO_DB_PATH" recall \
  "topic from the ingested corpus"
```

---

## Verifying Memory Retention Across Turns

To confirm that ingested content persists and is retrievable across independent processes:

1. Establish a baseline: run `phrase-search` or `search-debug` on a unique token that has not been ingested. Confirm zero results.
2. Ingest the token as an operational decision or corpus chunk.
3. Query in the same process. Confirm evidence is returned.
4. Exit and reopen the CLI in a fresh process with the same `$TIRO_DB_PATH`.
5. Query again. Confirm the same evidence is returned.

If step 4 returns nothing while step 3 succeeded, the database path is likely inconsistent between processes (check that `$TIRO_DB_PATH` resolves to the same file in both shells).

### Saved session retention

Use `tests/fixtures/session_test.yaml` or a session file of your own:

```bash
dotnet run --project src/Tiro.Cli/Tiro.Cli.csproj -- \
  --db "$TIRO_DB_PATH" \
  --session-id <derived-session-id> \
  ingest-aichat-session \
  --source-identity aichat:<role-name> \
  --file tests/fixtures/session_test.yaml
```

After import, query with the same `session_id` and confirm:
- Session evidence is present.
- Fact and corpus chunk counts are unchanged from before import.

---

## Regression Checklist

Run the applicable items after any change in the categories listed.

| Change area | Required checks |
|---|---|
| Schema or migration | Full build + `dotnet test`; `inspect stats` on a fresh DB |
| Retrieval or scoring logic | Full build + `dotnet test`; direct CLI smoke; memory proof |
| Planner logic | Full build + `dotnet test`; targeted query with `--planner off` baseline |
| Proxy layer | Full build + `dotnet test`; manual corpus smoke (build → inspect → recall) |
| Wrapper scripts | Manifest build/check; wrapper smoke |
| Role or manifest edits | Manifest build/check; restart agent session; stale-session smoke |
| Session ingestion | Session checkpoint smoke; confirm lane isolation |

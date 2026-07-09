# Operations Runbook

Practical procedures for operators running Tiro: initializing a database, ingesting corpus material, querying memory, managing sessions, and maintaining the function manifest.

---

## Set the Database Path

All Tiro CLI commands require `TIRO_DB_PATH` to point to an existing SQLite database file. Callers that wrap the CLI will also require this variable; they will not guess a path or create a production database automatically.

```bash
export TIRO_DB_PATH="./data/tiro.sqlite3"
```

Use an absolute path or a project-relative path that unambiguously identifies your database file. Verify it exists before running any other command:

```bash
ls -lh "$TIRO_DB_PATH"
```

---

## Initialize a Database

If no database exists yet, the CLI will create one on first use. Run a stats inspection to confirm the schema was initialized:

```bash
dotnet run --project src/Tiro.Cli/Tiro.Cli.csproj -- --db "$TIRO_DB_PATH" inspect stats
```

Expected output shows zero-count tables for chunks, facts, sessions, and operational records — confirming an empty but healthy schema.

---

## Ingest a Corpus JSONL

Corpus material is supplied as newline-delimited JSON (`.jsonl`) where each line is a chunk record. Pass the file to `ingest-chunks`:

```bash
dotnet run --project src/Tiro.Cli/Tiro.Cli.csproj -- --db "$TIRO_DB_PATH" ingest-chunks \
  /path/to/your-corpus-chunks.jsonl
```

Run `inspect stats` after ingestion to confirm chunk and source counts increased:

```bash
dotnet run --project src/Tiro.Cli/Tiro.Cli.csproj -- --db "$TIRO_DB_PATH" inspect stats
```

---

## Inspect Database State

### Summary stats

```bash
dotnet run --project src/Tiro.Cli/Tiro.Cli.csproj -- --db "$TIRO_DB_PATH" inspect stats
```

Prints table row counts, file size, and modification time.

### Sources

```bash
dotnet run --project src/Tiro.Cli/Tiro.Cli.csproj -- --db "$TIRO_DB_PATH" inspect sources --limit 20
```

Lists ingested sources with their machine identifiers (`source_id`). Use these IDs in filtered queries — do not guess them from lane labels like `corpus`.

### Documents

```bash
# List all documents
dotnet run --project src/Tiro.Cli/Tiro.Cli.csproj -- --db "$TIRO_DB_PATH" inspect documents --limit 20

# Filter by source
dotnet run --project src/Tiro.Cli/Tiro.Cli.csproj -- --db "$TIRO_DB_PATH" inspect documents --source-id "<source_id>"
```

### Sessions

```bash
dotnet run --project src/Tiro.Cli/Tiro.Cli.csproj -- --db "$TIRO_DB_PATH" inspect sessions
```

Lists ingested session records with their derived session identifiers.

---

## Query, Retrieve, and Recall

Tiro provides several retrieval modes. Choose based on whether you need targeted lookup, open-ended discovery, or exact phrase verification.

### Targeted query (planner disabled)

Use when you have a specific question and want deterministic retrieval without planner inference:

```bash
dotnet run --project src/Tiro.Cli/Tiro.Cli.csproj -- --db "$TIRO_DB_PATH" --planner off query \
  "your search question"
```

### Natural recall

Use for open-ended discovery across all lanes:

```bash
dotnet run --project src/Tiro.Cli/Tiro.Cli.csproj -- --db "$TIRO_DB_PATH" recall \
  "topic or concept to recall"
```

### Exact phrase search

Use to confirm whether specific text exists in the database. Useful for diagnosing ingestion problems:

```bash
dotnet run --project src/Tiro.Cli/Tiro.Cli.csproj -- --db "$TIRO_DB_PATH" phrase-search \
  "exact phrase to find" --lane corpus
```

### Search debug

Use when retrieval results are unexpected. Reports normalized terms, lane filters, candidate counts, score components, and warnings:

```bash
dotnet run --project src/Tiro.Cli/Tiro.Cli.csproj -- --db "$TIRO_DB_PATH" --planner off \
  search-debug "your search question"
```

Add `--include-archived` when auditing archived lifecycle or operational evidence; archived rows remain excluded by default.

---

## Build and Inspect Proxies

Proxies are lightweight summary pointers built over ingested documents. They improve recall performance for long documents. Proxy building is an operator maintenance action — it is not performed automatically during ingestion.

### Build proxies for a document

```bash
dotnet run --project src/Tiro.Cli/Tiro.Cli.csproj -- --db "$TIRO_DB_PATH" proxy-build corpus \
  --document-id <document_id>
```

### Inspect proxy state

```bash
dotnet run --project src/Tiro.Cli/Tiro.Cli.csproj -- --db "$TIRO_DB_PATH" proxy-inspect \
  --document-id <document_id>
```

### Proxy-layer recall

```bash
dotnet run --project src/Tiro.Cli/Tiro.Cli.csproj -- --db "$TIRO_DB_PATH" proxy-recall \
  "topic to recall via proxies"
```

If `recall` warns `No recall proxies available; fallback lexical recall used`, run `proxy-build` for the relevant documents and retry.

---

## Compaction

Use archival to reduce the default retrieval surface area for long-running stores without deleting any evidence.

### Dry run first

```bash
dotnet run --project src/Tiro.Cli/Tiro.Cli.csproj -- --db "$TIRO_DB_PATH" archive \
  --older-than-days 90 \
  --dry-run
```

Recommended starting point: `90` days. This is only guidance, not a built-in default; the command requires an explicit age threshold every time.

### Apply archival

```bash
dotnet run --project src/Tiro.Cli/Tiro.Cli.csproj -- --db "$TIRO_DB_PATH" archive \
  --older-than-days 90
```

Default archival is conservative:

- facts must already be `stale` or `superseded`
- operational records must already be `closed`

Use `--status active` or another explicit status only when you intentionally want to override those defaults.

### Reverse archival

```bash
dotnet run --project src/Tiro.Cli/Tiro.Cli.csproj -- --db "$TIRO_DB_PATH" unarchive \
  --evidence-key fact|123
```

Or clear all recent archive marks:

```bash
dotnet run --project src/Tiro.Cli/Tiro.Cli.csproj -- --db "$TIRO_DB_PATH" unarchive \
  --all-since 2026-07-08T00:00:00Z
```

Archive and unarchive only flip `archived_utc`; they do not delete rows or rewrite lifecycle status.

---

## Ingest a Saved Session

Import a saved agent session file into Tiro's session lane:

```bash
dotnet run --project src/Tiro.Cli/Tiro.Cli.csproj -- \
  --db "$TIRO_DB_PATH" \
  --session-id <derived-session-id> \
  ingest-aichat-session \
  --source-identity aichat:<role-name> \
  --file /path/to/session.yaml
```

After ingestion, query with the same `session_id` to verify session evidence is present. Confirm that facts and corpus chunks were not modified by inspecting their counts before and after.

---

## Build and Validate the Function Manifest

When wrappers or tool definitions change, rebuild and validate the function manifest before starting an agent session:

```bash
cd <your-agent-functions-directory>
argc --argc-run argcfile.sh build
argc --argc-run argcfile.sh check
jq -r '.[].name' functions.json
```

This confirms the manifest is syntactically valid and lists the tool names available to the agent runtime.

---

## Handle Stale Sessions

A stale agent session can cause the runtime to miss newly added tools or retain behavior from an older configuration. When this is suspected:

1. Compare the active role definition, the function manifest (`functions.json`), and the agent runtime configuration.
2. Rebuild and re-validate the function manifest if any wrappers changed.
3. Restart the agent session to pick up the updated tool list.
4. If a tool is present in `functions.json` but absent from observed runtime behavior, the most likely cause is a stale session caching an old tool list — not a code defect.

---

## Common Pathing Reference

| Variable / Path | Purpose |
|---|---|
| `$TIRO_DB_PATH` | Points to the active SQLite database file |
| `./data/tiro.sqlite3` | Conventional relative path for a project-local database |
| `src/Tiro.Cli/Tiro.Cli.csproj` | CLI project file, relative to repo root |
| `scripts/` | Smoke and utility scripts, relative to repo root |
| `tests/fixtures/` | Test fixture files, relative to repo root |

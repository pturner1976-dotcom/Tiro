#!/usr/bin/env bash
set -euo pipefail

repo="${TIRO_REPO_DIR:-/home/SiliconMagician/Tiro_v1}"
project="$repo/src/Tiro.Cli/Tiro.Cli.csproj"
[[ -f "$project" ]] || { echo "FAIL: Tiro CLI project not found." >&2; exit 1; }

tmpdir="$(mktemp -d)"
trap 'rm -rf "$tmpdir"' EXIT
db="$tmpdir/tiro.sqlite3"
session_file="$tmpdir/m014_checkpoint_session.yaml"
session_id="m014_checkpoint_session"
phrase="m014 checkpoint smoke phrase $$"

cat > "$session_file" <<YAML
model: gemini:gemini-2.5-flash
role_name: session_fixture
save_session: true
messages:
- role: user
  content: |-
    The checkpoint phrase is $phrase.
- role: assistant
  content: |-
    Session checkpoint acknowledged.
YAML

before_stats="$(dotnet run --project "$project" -- --db "$db" stats)"
before_facts="$(jq '.FactCount' <<<"$before_stats")"
before_chunks="$(jq '.ChunkCount' <<<"$before_stats")"

ingest_out="$(dotnet run --project "$project" -- --db "$db" --session-id "$session_id" ingest-aichat-session --source-identity session_fixture_runtime --file "$session_file")"
query_out="$(dotnet run --project "$project" -- --db "$db" --planner off --session-id "$session_id" query "$phrase")"
after_stats="$(dotnet run --project "$project" -- --db "$db" stats)"

session_count="$(jq '(.session_evidence // []) | length' <<<"$query_out")"
after_facts="$(jq '.FactCount' <<<"$after_stats")"
after_chunks="$(jq '.ChunkCount' <<<"$after_stats")"
selected_file="$(jq -r '.selected_file' <<<"$ingest_out")"

echo "db_path: $db"
echo "selected_file: $selected_file"
echo "session_id: $session_id"
echo "session_evidence_count: $session_count"
echo "facts_before_after: $before_facts/$after_facts"
echo "chunks_before_after: $before_chunks/$after_chunks"

if [[ "$selected_file" == "$session_file" && "$session_count" -gt 0 && "$before_facts" == "$after_facts" && "$before_chunks" == "$after_chunks" ]]; then
  echo "PASS tiro_session_checkpoint_smoke"
else
  echo "FAIL tiro_session_checkpoint_smoke"
  exit 1
fi

#!/usr/bin/env bash
set -euo pipefail

repo="${TIRO_REPO_DIR:-/home/SiliconMagician/Tiro_v1}"
project="$repo/src/Tiro.Cli/Tiro.Cli.csproj"
[[ -f "$project" ]] || { echo "FAIL: Tiro CLI project not found." >&2; exit 1; }

tmpdir="$(mktemp -d)"
trap 'rm -rf "$tmpdir"' EXIT
db="$tmpdir/tiro.sqlite3"
older="$tmpdir/older_session.yaml"
newer="$tmpdir/newest session.yaml"

cat > "$older" <<'YAML'
model: gemini:gemini-2.5-flash
messages:
- role: user
  content: older fixture
YAML

cat > "$newer" <<'YAML'
model: gemini:gemini-2.5-flash
messages:
- role: user
  content: m014 latest smoke selected phrase
YAML

touch -d '2026-05-10T00:00:00Z' "$older"
touch -d '2026-05-10T00:01:00Z' "$newer"

inspect_out="$(dotnet run --project "$project" -- inspect-aichat-sessions --sessions-dir "$tmpdir")"
ingest_out="$(dotnet run --project "$project" -- --db "$db" ingest-aichat-session --source-identity session_fixture_runtime --latest --sessions-dir "$tmpdir")"

newest_file="$(jq -r '.newest_file' <<<"$inspect_out")"
selected_file="$(jq -r '.selected_file' <<<"$ingest_out")"
session_id="$(jq -r '.session_id' <<<"$ingest_out")"
messages_written="$(jq '.messages_written' <<<"$ingest_out")"

echo "sessions_dir: $tmpdir"
echo "newest_file: $newest_file"
echo "selected_file: $selected_file"
echo "derived_session_id: $session_id"
echo "messages_written: $messages_written"

if [[ "$newest_file" == "$newer" && "$selected_file" == "$newer" && "$session_id" == "newest_session" && "$messages_written" -gt 0 ]]; then
  echo "PASS tiro_latest_session_smoke"
else
  echo "FAIL tiro_latest_session_smoke"
  exit 1
fi

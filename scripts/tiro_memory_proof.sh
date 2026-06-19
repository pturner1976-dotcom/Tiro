#!/usr/bin/env bash
set -euo pipefail

load_aichat_env_defaults() {
  local env_file="${AICHAT_ENV_FILE:-${AICHAT_CONFIG_DIR:-/home/SiliconMagician/.config/aichat}/.env}"
  [[ -f "$env_file" ]] || return 0
  local line key value
  while IFS= read -r line || [[ -n "$line" ]]; do
    [[ "$line" =~ ^[[:space:]]*$ || "$line" =~ ^[[:space:]]*# ]] && continue
    key="${line%%=*}"
    value="${line#*=}"
    key="${key#"${key%%[![:space:]]*}"}"
    key="${key%"${key##*[![:space:]]}"}"
    case "$key" in
      TIRO_DB_PATH) [[ -n "${TIRO_DB_PATH:-}" ]] || export TIRO_DB_PATH="$value" ;;
      TIRO_REPO_DIR) [[ -n "${TIRO_REPO_DIR:-}" ]] || export TIRO_REPO_DIR="$value" ;;
    esac
  done < "$env_file"
}

count_evidence() {
  jq '((.primary_evidence // []) | length) + ((.session_evidence // []) | length) + ((.operational_memory // []) | length) + ((.facts // [] | map(select(.evidence_type == "fact-lifecycle")) | length))' "$1"
}

load_aichat_env_defaults
repo="${TIRO_REPO_DIR:-/home/SiliconMagician/Tiro_v1}"
project="$repo/src/Tiro.Cli/Tiro.Cli.csproj"
db_path="${TIRO_DB_PATH:-}"
[[ -f "$project" ]] || { echo "FAIL: Tiro CLI project not found." >&2; exit 1; }
[[ -n "$db_path" ]] || { echo "FAIL: TIRO_DB_PATH is not set." >&2; exit 1; }
[[ -f "$db_path" ]] || { echo "FAIL: TIRO_DB_PATH does not point to an existing database." >&2; exit 1; }

stamp="$(date -u +%Y%m%dT%H%M%SZ)"
token="m013proofcarryforward${stamp}$$"
phrase="proof carry forward alpha $token"
before="$(mktemp)"
after="$(mktemp)"
fresh="$(mktemp)"
trap 'rm -f "$before" "$after" "$fresh"' EXIT

dotnet run --project "$project" -- --db "$db_path" --planner off --limit 5 query "$token" > "$before"
dotnet run --project "$project" -- --db "$db_path" ingest-operational-record \
  --record-type decision \
  --source-identity script:tiro_memory_proof \
  --text "$phrase" >/tmp/tiro_memory_proof_ingest.json
dotnet run --project "$project" -- --db "$db_path" --planner off --limit 5 query "$token" > "$after"
dotnet run --project "$project" -- --db "$db_path" --planner off --limit 5 query "$token" > "$fresh"

before_count="$(count_evidence "$before")"
after_count="$(count_evidence "$after")"
fresh_count="$(count_evidence "$fresh")"

echo "active_db_path: $db_path"
echo "phrase: $phrase"
echo "before_count: $before_count"
echo "after_count: $after_count"
echo "fresh_process_count: $fresh_count"

if [[ "$before_count" -eq 0 && "$after_count" -gt 0 && "$fresh_count" -gt 0 ]]; then
  echo "PASS tiro_memory_proof"
else
  echo "FAIL tiro_memory_proof"
  exit 1
fi

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

load_aichat_env_defaults
repo="${TIRO_REPO_DIR:-/home/SiliconMagician/Tiro_v1}"
project="$repo/src/Tiro.Cli/Tiro.Cli.csproj"
db_path="${TIRO_DB_PATH:-}"
[[ -f "$project" ]] || { echo "FAIL: Tiro CLI project not found." >&2; exit 1; }
[[ -n "$db_path" ]] || { echo "FAIL: TIRO_DB_PATH is not set." >&2; exit 1; }
[[ -f "$db_path" ]] || { echo "FAIL: TIRO_DB_PATH does not point to an existing database." >&2; exit 1; }

stamp="$(date -u +%Y%m%dT%H%M%SZ)"
token="m013directsmoke${stamp}$$"
phrase="M013 direct smoke operational decision $token"
negative="m013directnegative${stamp}$$"

echo "active_db_path: $db_path"
dotnet run --project "$project" -- --db "$db_path" ingest-operational-record \
  --record-type decision \
  --source-identity script:tiro_direct_smoke \
  --text "$phrase" >/tmp/tiro_direct_smoke_ingest.json

dotnet run --project "$project" -- --db "$db_path" --planner off --limit 5 query "$token" >/tmp/tiro_direct_smoke_query.json
dotnet run --project "$project" -- --db "$db_path" --planner off --limit 5 query "$negative" >/tmp/tiro_direct_smoke_negative.json

positive_count="$(jq '(.operational_memory // []) | length' /tmp/tiro_direct_smoke_query.json)"
negative_count="$(jq '((.primary_evidence // []) | length) + ((.session_evidence // []) | length) + ((.operational_memory // []) | length) + ((.facts // [] | map(select(.evidence_type == "fact-lifecycle")) | length))' /tmp/tiro_direct_smoke_negative.json)"

echo "phrase: $phrase"
echo "positive_operational_count: $positive_count"
echo "negative_evidence_count: $negative_count"

if [[ "$positive_count" -gt 0 && "$negative_count" -eq 0 ]]; then
  echo "PASS tiro_direct_smoke"
else
  echo "FAIL tiro_direct_smoke"
  exit 1
fi

#!/usr/bin/env bash
set -euo pipefail

query_tool="${TIRO_QUERY_TOOL:-/home/SiliconMagician/.config/aichat/functions/tools/tiro_query.sh}"
ingest_tool="${TIRO_INGEST_STATE_TOOL:-/home/SiliconMagician/.config/aichat/functions/tools/tiro_ingest_state.sh}"
[[ -x "$query_tool" ]] || { echo "FAIL: tiro_query wrapper is not executable." >&2; exit 1; }
[[ -x "$ingest_tool" ]] || { echo "FAIL: tiro_ingest_state wrapper is not executable." >&2; exit 1; }

stamp="$(date -u +%Y%m%dT%H%M%SZ)"
token="m013wrappersmoke${stamp}$$"
phrase="M013 wrapper smoke operational decision $token"
negative="m013wrappernegative${stamp}$$"

ingest_out="$(mktemp)"
query_out="$(mktemp)"
negative_out="$(mktemp)"
trap 'rm -f "$ingest_out" "$query_out" "$negative_out"' EXIT

LLM_OUTPUT="$ingest_out" "$ingest_tool" \
  --mode operational_record \
  --record-type decision \
  --source-identity script:tiro_wrapper_smoke \
  --text "$phrase"

LLM_OUTPUT="$query_out" "$query_tool" --planner-mode off --limit 5 --query "$token"
LLM_OUTPUT="$negative_out" "$query_tool" --planner-mode off --limit 5 --query "$negative"

positive_count="$(jq '(.operational_memory // []) | length' "$query_out")"
negative_count="$(jq '((.primary_evidence // []) | length) + ((.session_evidence // []) | length) + ((.operational_memory // []) | length) + ((.facts // [] | map(select(.evidence_type == "fact-lifecycle")) | length))' "$negative_out")"

echo "phrase: $phrase"
echo "positive_operational_count: $positive_count"
echo "negative_evidence_count: $negative_count"

if [[ "$positive_count" -gt 0 && "$negative_count" -eq 0 ]]; then
  echo "PASS tiro_wrapper_smoke"
else
  echo "FAIL tiro_wrapper_smoke"
  exit 1
fi

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

redact() {
  sed -E \
    -e 's/AIza[0-9A-Za-z_-]+/<redacted>/g' \
    -e 's/(GEMINI_API_KEY=)[^[:space:]]+/\1<redacted>/g' \
    -e 's/(api_key: )[^\r\n]+/\1<redacted>/g' \
    -e 's/(key=)[^&[:space:]]+/\1<redacted>/g'
}

load_aichat_env_defaults
db_path="${TIRO_DB_PATH:-}"
[[ -n "$db_path" ]] || { echo "FAIL: TIRO_DB_PATH is not set." >&2; exit 1; }
[[ -f "$db_path" ]] || { echo "FAIL: TIRO_DB_PATH does not point to an existing database." >&2; exit 1; }

python3 - "$db_path" <<'PY' | redact
import os
import sqlite3
import sys
from datetime import datetime, timezone

db = sys.argv[1]
tables = [
    ("sources", "sources"),
    ("documents", "documents"),
    ("chunks", "chunks"),
    ("sessions", "sessions"),
    ("session_messages", "messages"),
    ("operational_records", "operational_records"),
    ("facts", "facts"),
    ("fact_conflicts", "fact_conflicts"),
]

def compact(value, limit=180):
    value = "" if value is None else str(value).replace("\n", " ").replace("\r", " ")
    while "  " in value:
        value = value.replace("  ", " ")
    return value if len(value) <= limit else value[:limit] + "..."

stat = os.stat(db)
mtime = datetime.fromtimestamp(stat.st_mtime, timezone.utc).isoformat()
print(f"active_db_path: {db}")
print(f"db_file_size_bytes: {stat.st_size}")
print(f"db_mtime_utc: {mtime}")
print()
with sqlite3.connect(f"file:{db}?immutable=1", uri=True) as conn:
    cur = conn.cursor()
    print("table_counts:")
    for label, table in tables:
        try:
            count = cur.execute(f"select count(*) from {table}").fetchone()[0]
        except sqlite3.Error as exc:
            count = f"error:{exc}"
        print(f"  {label}: {count}")

    samples = [
        ("sessions", "select session_id, created_utc from sessions order by created_utc desc, session_id desc limit 3"),
        ("session_messages", "select message_id, session_id, timestamp_utc, direction, source_identity, text from messages order by timestamp_utc desc, message_id desc limit 3"),
        ("operational_records", "select record_id, record_type, created_utc, origin, session_id, status, text from operational_records order by created_utc desc, record_id desc limit 3"),
        ("chunks", "select chunk_id, document_id, source_id, ingested_utc, text from chunks order by ingested_utc desc, chunk_id desc limit 3"),
        ("facts", "select fact_id, created_utc, status, source_id, origin_identity, session_id, text from facts order by created_utc desc, fact_id desc limit 3"),
    ]
    for label, sql in samples:
        print()
        print(f"recent_{label}:")
        try:
            rows = cur.execute(sql).fetchall()
        except sqlite3.Error as exc:
            print(f"  error: {exc}")
            continue
        if not rows:
            print("  (none)")
            continue
        for row in rows:
            print("  - " + " | ".join(compact(value) for value in row))
PY

#!/usr/bin/env bash
set -euo pipefail

repo_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
project="$repo_dir/src/Tiro.Cli"
snapshot_path="$repo_dir/tests/fixtures/scoring_snapshot.jsonl"
mode="${1:-write}"

tmpdir="$(mktemp -d)"
db_path="$tmpdir/scoring_eval.sqlite3"
generated_path="$tmpdir/generated_snapshot.jsonl"
trap 'rm -rf "$tmpdir"' EXIT

dotnet run --no-build --project "$project" -- --db "$db_path" init >/dev/null
dotnet run --no-build --project "$project" -- --db "$db_path" ingest-chunks "$repo_dir/fixtures/full-functional/corpus_chunks.jsonl" >/dev/null
dotnet run --no-build --project "$project" -- --db "$db_path" ingest-chunks "$repo_dir/fixtures/parity/corpus_chunks.jsonl" >/dev/null
dotnet run --no-build --project "$project" -- --db "$db_path" ingest-chunks "$repo_dir/docs/stack_smashing_tiro_chunks.jsonl" >/dev/null
dotnet run --no-build --project "$project" -- --db "$db_path" ingest-chunks "$repo_dir/tests/fixtures/m002_chunks.jsonl" >/dev/null
dotnet run --no-build --project "$project" -- --db "$db_path" ingest-chunks "$repo_dir/tests/fixtures/m003_retrieval_chunks.jsonl" >/dev/null
dotnet run --no-build --project "$project" -- --db "$db_path" ingest-chunks "$repo_dir/tests/fixtures/m009_policy_chunks.jsonl" >/dev/null

dotnet run --no-build --project "$project" -- --db "$db_path" session-create eval-session >/dev/null
dotnet run --no-build --project "$project" -- --db "$db_path" session-ingest eval-session user eval:session "Current deployment token owner is Eli and the current state remains token rotation." >/dev/null
dotnet run --no-build --project "$project" -- --db "$db_path" --session-id eval-session ingest-operational-record --record-type decision --source-identity eval:operational --text "Decision: deployment token owner is Morgan." >/dev/null
dotnet run --no-build --project "$project" -- --db "$db_path" --session-id eval-session ingest-operational-record --record-type todo --source-identity eval:operational --text "TODO: confirm deployment token owner." >/dev/null
dotnet run --no-build --project "$project" -- --db "$db_path" --session-id eval-session ingest-operational-record --record-type warning --source-identity eval:operational --text "Warning: deployment token owner is blocked until review completes." >/dev/null
dotnet run --no-build --project "$project" -- --db "$db_path" --session-id eval-session ingest-operational-record --record-type unknown --source-identity eval:operational --text "Unknown: deployment token owner is not confirmed." >/dev/null
dotnet run --no-build --project "$project" -- --db "$db_path" fact-add "Conflict lifecycle launch window is obsolete." source:eval eval:fact --status stale >/dev/null
dotnet run --no-build --project "$project" -- --db "$db_path" fact-add "Conflict lifecycle launch window is replaced." source:eval eval:fact --status superseded >/dev/null
dotnet run --no-build --project "$project" -- --db "$db_path" fact-add "Current deployment token owner remains supported." source:eval eval:fact --status active >/dev/null

python3 - "$repo_dir" "$db_path" "$generated_path" <<'PY'
import json
import subprocess
import sys

repo_dir = sys.argv[1]
db_path = sys.argv[2]
generated_path = sys.argv[3]
queries = [
    {"query": "provenance retrieval"},
    {"query": "packets confidence"},
    {"query": "source provenance retrieval"},
    {"query": "archive deployment token owner"},
    {"query": "current deployment token owner", "session_id": "eval-session"},
    {"query": "deployment token decision", "session_id": "eval-session"},
    {"query": "todo deployment token owner", "session_id": "eval-session"},
    {"query": "warning deployment token owner", "session_id": "eval-session"},
    {"query": "unknown deployment token owner", "session_id": "eval-session"},
    {"query": "conflict lifecycle launch window"},
    {"query": "stack smashing article"},
    {"query": "nop sled shellcode"},
    {"query": "deterministic lexical retrieval"},
    {"query": "document provenance reference"},
    {"query": "current state token rotation", "session_id": "eval-session"},
]

def run_query(spec):
    cmd = [
        "dotnet", "run", "--no-build", "--project", "src/Tiro.Cli", "--",
        "--db", db_path, "--planner", "off", "--limit", "5",
    ]
    if spec.get("session_id"):
        cmd += ["--session-id", spec["session_id"]]
    cmd += ["query", spec["query"]]
    completed = subprocess.run(cmd, cwd=repo_dir, capture_output=True, text=True, check=True)
    packet = json.loads(completed.stdout)
    top = [
        {"evidence_key": item["evidence_key"], "final_score": item["final_score"]}
        for item in packet.get("retrieval_policy", {}).get("signals", [])[:5]
    ]
    return {
        "query": spec["query"],
        "session_id": spec.get("session_id"),
        "query_mode": packet.get("retrieval_policy", {}).get("query_mode"),
        "top": top,
    }

rows = [run_query(spec) for spec in queries]
with open(generated_path, "w", encoding="utf-8") as handle:
    for row in rows:
        handle.write(json.dumps(row, separators=(",", ":")) + "\n")
PY

if [[ "$mode" == "--check" ]]; then
  python3 - "$snapshot_path" "$generated_path" <<'PY'
import json
import sys

snapshot_path, generated_path = sys.argv[1:3]

def load(path):
    rows = {}
    with open(path, "r", encoding="utf-8") as handle:
        for line in handle:
            if not line.strip():
                continue
            row = json.loads(line)
            rows[(row["query"], row.get("session_id"))] = row
    return rows

old = load(snapshot_path)
new = load(generated_path)
diffs = []
for key in sorted(set(old) | set(new)):
    if old.get(key) != new.get(key):
        diffs.append((key, old.get(key), new.get(key)))

if diffs:
    for key, previous, current in diffs:
        query, session_id = key
        print(f"query: {query}")
        if session_id:
            print(f"session_id: {session_id}")
        print(f"old top-5: {previous['top'] if previous else 'missing'}")
        print(f"new top-5: {current['top'] if current else 'missing'}")
        print()
    raise SystemExit(1)

print("PASS tiro_scoring_eval --check")
PY
else
  cp "$generated_path" "$snapshot_path"
  echo "Wrote $snapshot_path"
fi

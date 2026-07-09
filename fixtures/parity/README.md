# Tiro Parity Fixtures

These fixtures are synthetic and deterministic. They are committed specifically
for `v3 parity` parity testing and do not derive from the live Tiro v1
database.

Files:

- `corpus_chunks.jsonl`
  Purpose: minimal corpus evidence for `query`, `recall`, `inspect`, exact
  phrase search, proxy build, proxy recall, and no-result checks.
  Purpose: saved-session ingest path for session summary, session evidence, and
  exact phrase behavior in a non-corpus lane.

Fixture record intent:

- `parity_archive_alpha`
  Purpose: positive corpus retrieval with explicit provenance and one exact
  phrase target.
- `parity_archive_beta`
  Purpose: negative-control and honest no-result behavior.
- `parity_session_alpha`
  Purpose: saved-session ingest coverage for session recall and summary.
- operational decision / todo / warning / unknown
  Created by the fixture builder to exercise operational retrieval lanes.
- lifecycle facts in `active`, `stale`, `superseded`, `conflicting`, `unknown`
  Created by the fixture builder to exercise fact-lifecycle retrieval behavior.
- corpus proxies and pointers
  Created by the fixture builder via `proxy-build corpus` to exercise pointer
  creation and proxy-backed recall without inheriting any old data.

Rules:

- Do not replace these fixtures with copied v1 runtime data.
- Keep the dataset small enough to inspect by eye.
- Add new fixture records only when they support a named parity case.

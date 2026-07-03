# Tiro Documentation

Tiro is a standalone local memory and retrieval engine for AI agents. It is built on .NET 8 and SQLite with zero external dependencies. Tiro keeps corpus retrieval, session memory, structured operational memory, and lifecycle-aware facts in distinct lanes, then assembles them into compact, structured JSON context packets that a calling agent can use directly.

## Architecture Overview

Tiro is a single .NET 8 CLI process backed by a local SQLite database. A caller — an AI agent, a shell wrapper, or a script — invokes Tiro commands via its CLI surface. Every command emits structured JSON to stdout. Tiro owns all schema initialization, ingestion, query planning, lexical and semantic scoring, fact lifecycle tracking, and retrieval packet assembly. It does not own the agent runtime, the model, or the display layer. Those remain the caller's responsibility.

## Documentation Index

### Core

| File | Description |
|------|-------------|
| [README.md](README.md) | This navigation page |
| [GETTING_STARTED.md](GETTING_STARTED.md) | Five-minute setup: build, init, ingest, query |
| [ARCHITECTURE.md](ARCHITECTURE.md) | System boundaries, authority model, lane structure |
| [MEMORY_MODEL.md](MEMORY_MODEL.md) | The four memory lanes, evidence discipline, fact lifecycle |
| [GLOSSARY.md](GLOSSARY.md) | Term definitions for contributors and integrators |
| [ROADMAP.md](ROADMAP.md) | Planned features and direction |
| [CLI_REFERENCE.md](CLI_REFERENCE.md) | Complete command reference with flags and output format |

### Retrieval

| File | Description |
|------|-------------|
| [retrieval/RETRIEVAL_PIPELINE.md](retrieval/RETRIEVAL_PIPELINE.md) | How queries are scored, planned, and assembled into packets |
| [retrieval/PROXY_POINTER_LAYER.md](retrieval/PROXY_POINTER_LAYER.md) | The proxy index and evidence pointer hydration layer |
| [retrieval/HYDRATION_PACKING.md](retrieval/HYDRATION_PACKING.md) | How evidence pointers are resolved and packed into context |

### API

| File | Description |
|------|-------------|
| [api/PUBLIC_SURFACE.md](api/PUBLIC_SURFACE.md) | The stable public API: commands, flags, JSON output contracts |
| [api/TOOL_SURFACE.md](api/TOOL_SURFACE.md) | How to expose Tiro as a tool to an LLM agent framework |
| [api/DATABASE_SCHEMA.md](api/DATABASE_SCHEMA.md) | SQLite schema reference: all tables, columns, and constraints |

### Operations

| File | Description |
|------|-------------|
| [operations/OPERATIONS_RUNBOOK.md](operations/OPERATIONS_RUNBOOK.md) | Day-to-day operator procedures: ingest, rebuild, inspect, backup |
| [operations/TESTING.md](operations/TESTING.md) | Running the test suite, smoke tests, in-process SQLite test pattern |
| [operations/DEBUGGING.md](operations/DEBUGGING.md) | Diagnosing retrieval gaps, planner failures, embedding errors |
| [operations/SECURITY.md](operations/SECURITY.md) | Security model, hostile evidence handling, canary discipline |

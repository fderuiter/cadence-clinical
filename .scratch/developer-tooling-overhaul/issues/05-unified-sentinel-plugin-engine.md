# 05: Unified Sentinel Plugin Engine & Parallel Validation

**What to build:**
A pluggable `SentinelCheck` concurrent engine consolidating architecture drift, duplication, ADR, import, and schema verifications under `cadence check --parallel`.

**Blocked by:** Ticket 02

**Status:** ready-for-agent

- [x] `SentinelCheck` ABC and `SentinelResult` model in `packages/sentinels/base.py`
- [x] Refactored sentinel plugins for drift, duplication, ADRs, imports, and schemas
- [x] Concurrent async engine in `packages/cli/commands/check.py` executing all checks in parallel
- [x] Unified TerminalDocument rendering and JSON telemetry output
- [x] Unit tests for sentinel engine and individual check plugins

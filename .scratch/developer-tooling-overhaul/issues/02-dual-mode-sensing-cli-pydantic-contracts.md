# 02: Dual-Mode Sensing CLI & Shared Pydantic Contracts

**What to build:**
A unified contract seam in `packages/tooling_core` and auto-sensing `TerminalDocument` rendering that outputs styled Rich tables in interactive TTYs and deterministic JSON/NDJSON envelopes with remediation hints when piped or invoked by AI agents.

**Blocked by:** None (can start immediately)

**Status:** ready-for-agent

- [x] Pydantic input/output schemas in `packages/tooling_core/contracts.py`
- [x] Decoupled handler business logic in `packages/tooling_core/handlers.py`
- [x] Enhanced `TerminalDocument` in `packages/cli/formatting.py` with auto-sensing `isatty()` and remediation encoders
- [x] CLI command wrappers in `packages/cli/commands/` invoking core handlers
- [x] Contract tests verifying JSON envelope compliance on non-TTY calls

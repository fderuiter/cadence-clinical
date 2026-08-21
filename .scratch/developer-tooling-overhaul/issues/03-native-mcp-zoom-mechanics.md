# 03: Native MCP Server Progressive Disclosure & Zoom Mechanics

**What to build:**
Native stdio MCP server tools emitting compact summary envelopes with `zoom_token` references and interactive inspection tools for on-demand exploration of deep error traces and logs without context blowup.

**Blocked by:** Ticket 02

**Status:** ready-for-agent

- [x] Zoom token registry for stateful or cryptographic trace unpacking
- [x] Compact default summary responses for `doctor_diagnose`, `run_sentinels`, `run_fast_tests`
- [x] Dedicated `inspect_zoom_target` MCP tool for granular trace/log exploration
- [x] MCP JSON-RPC unit and regression tests in `packages/cli/tests/test_mcp_server.py`

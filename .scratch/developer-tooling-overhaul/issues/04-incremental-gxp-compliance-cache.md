# 04: Incremental GxP Compliance Synchronization Cache

**What to build:**
Sub-2s GxP compliance synchronization (`cadence gxp sync` / `sync_gxp.py`) caching test outcomes in `.cadence/gxp_cache.json` and merging JUnit XML deltas to regenerate `Requirements_Traceability_Matrix.md`.

**Blocked by:** Ticket 01

**Status:** ready-for-agent

- [x] Cache engine in `.cadence/gxp_cache.json` hashing test nodes and source dependencies
- [x] Delta runner in `scripts/sync_gxp.py` invoking only dirty/affected test cases on local branches
- [x] JUnit XML delta merger preserving historical passed results
- [x] Traceability matrix generation executing in <2 seconds for single-line changes
- [x] Full test execution override via `--full` flag and CI detection

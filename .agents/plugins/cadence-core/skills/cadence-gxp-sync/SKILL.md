---
name: cadence-gxp-sync
description: "Synchronize 21 CFR Part 11 GxP compliance documentation, Requirements Traceability Matrix (RTM), and IQ/OQ/PQ execution reports. Use when adding/modifying tests, updating @req docstrings, or resolving CI compliance drift."
license: MIT
metadata:
  author: Cadence Team
  version: "1.0"
---

# Cadence GxP Compliance Synchronization

Automates 21 CFR Part 11 and FDA GxP compliance document regeneration and traceability matrix alignment.

## When to Run

Run this workflow whenever:
- Test functions are added, renamed, or deleted.
- `@req:PRD-SYS-xxx` tags in test docstrings are added or modified.
- Test pass/fail outcomes change.
- CI fails with: `GxP compliance documentation is out of sync with the current system state!`

## Primary Execution Command

```bash
uv run python scripts/sync_gxp.py
```
*(Or via Cadence CLI: `uv run cadence gxp sync`)*

## What the Synchronization Script Does

1. Runs the test suite and generates `report.xml`:
   `uv run pytest -n auto --junitxml=report.xml`
2. Generates the updated Traceability Matrix and Execution Report:
   `uv run python scripts/generate_rtm.py`
3. Stages the generated documentation files:
   - `docs/SDLC/Requirements_Traceability_Matrix.md`
   - `docs/SDLC/IQ_OQ_PQ_Execution_Report.md`

## Verification & Commit

1. Review the git diff on the staged SDLC documents:
   `git diff --staged docs/SDLC/`
2. Commit the synced compliance artifacts:
   `git commit -m "docs(rtm): sync GxP compliance docs with current test state"`

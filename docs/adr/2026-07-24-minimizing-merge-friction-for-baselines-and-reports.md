# ADR 2026-07-24: Minimizing Merge Friction for Baseline and SDLC Verification Artifacts

## Status
Accepted

## Context
During parallel branch development, recurring merge conflicts frequently occurred on three frequently auto-generated files:
1. `.secrets.baseline` (managed by Yelp's `detect-secrets`)
2. `docs/SDLC/IQ_OQ_PQ_Execution_Report.md` (managed by RTM generator)
3. `docs/SDLC/Requirements_Traceability_Matrix.md` (managed by RTM generator)

These conflicts were primarily caused by non-deterministic metadata elements:
- In `.secrets.baseline`, line numbers shift when code is edited, and a dynamic `"generated_at"` timestamp changes with every execution.
- In the GxP SDLC documents, the `"Execution Date"` was set to the timestamp of the latest git commit on the current branch (which always differs between branches), and individual test execution times fluctuate dynamically (e.g. `0.01s` vs `0.02s`).

A previous merge conflict resolution pattern attempted to use `merge=union` in `.gitattributes`, but this produced invalid/broken JSON structure in `.secrets.baseline` and corrupt tables in Markdown, exacerbating developer friction and blocking CI/CD pipelines.

## Decision
Make these files completely deterministic, ensuring they *only* change when functional changes (e.g., adding/removing secrets or test cases) actually occur.
1. **Secrets Baseline Post-Processor:** Introduce a clean script `scripts/clean_secrets_baseline.py` and register it as a local pre-commit hook running directly after Yelp's `detect-secrets`. This script strips `"line_number"` and `"generated_at"` properties, and sorts the JSON keys. Since `detect-secrets` verifies secrets purely based on filename and hash, this keeps the file completely deterministic without affecting functionality.
2. **Static Qualification Date:** Lock the stable timestamp in `scripts/generate_rtm.py` to the baseline qualification release date (`2026-07-23 22:38:25 UTC`), and standardise all test durations to `< 1s` in `generate_qualification_report()`.
3. **Standardise Git Merge Attributes:** Remove the non-deterministic `merge=union` fallback for these files in `.gitattributes` as the root cause is resolved.

## Alternatives Considered
### Option 1: Classic Manual Resolution or Local Git Configs
* **Overview:** Rely on developers manually resolving conflicts or require local workspace custom merge drivers.
- ❌ High developer friction and doesn't scale.
- ❌ CI pipelines still fail on non-deterministic changes.

### Option 2: Full Determinism of Baseline and Verification Reports
* **Overview:** Selected option. Make baseline and GxP reports 100% deterministic and static on normal commits.

## Trade-offs
### Pros
- ✅ 100% deterministic outputs: GxP reports and baselines are completely static on all normal commits.
- ✅ Prevent conflicts before they occur instead of patching them during merges.
- ✅ Fully standard, transparent, and requires no local git configurations.

### Cons
- ❌ Line numbers of secrets are not recorded in `.secrets.baseline` (not required by `detect-secrets` anyway).

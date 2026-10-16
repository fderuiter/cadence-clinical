# ADR-2532: Feature Branches Never Commit the GxP Snapshot

- **Status:** Accepted
- **Date:** 2026-10-16
- **Authors:** @fderuiter, Claude Code
- **Deciders:** @fderuiter (one integration owner regenerates the GxP documents, 2026-10-15; backlog pass, 2026-10-16)
- **Requirements:** PRD-SYS-053 (development guardrails: the quality gates and generated GxP reports must report the
  truth about a change), 21 CFR 11.10(a), issue #5151
- **Amends:** [ADR-2287](2026-07-24-minimizing-merge-friction-for-baselines-and-reports.md),
  [ADR-2531](2026-10-15-every-mapped-test-is-mandatory-evidence.md)

---

## 1. Context & Problem Statement

`docs/SDLC/Requirements_Traceability_Matrix.md` and `docs/SDLC/IQ_OQ_PQ_Execution_Report.md` are generated from the
requirement documents, the requirement tags on tests and the test results. Every branch that added or moved a tag had
to regenerate and commit both, because the `rtm` gate and the agent Stop hook failed while the committed RTM's test
mapping differed from the tags. Since ADR-2531 each regeneration also writes a new evidence block (revision and time),
so two branches always conflicted on both documents, even when their code was independent.

ADR-2287 relied on `*.md merge=custom-metadata-driver` in `.gitattributes`. GitHub ignores custom merge drivers when it
decides whether a pull request can merge, no driver of that name was configured, and the CLI's
`GitMergeDriverService` only printed a message.

## 2. Decision Drivers & Constraints

- The evidence is the requirement documents, the tags and the test results, not each branch's rendering of them.
- A snapshot must still be deterministic, integrity-stamped, taken from real PostgreSQL-backed results, and refuse
  unknown requirement IDs, failing tagged tests and skipped database tests.
- One integration owner regenerates the documents for the integrated candidate (user decision, 2026-10-15).
- No `merge=union`: it corrupts Markdown tables.

## 3. Options Considered

### Option 1: Source-first validation on branches, a snapshot only from the integrated candidate (Selected)

- **Overview:** `gxp sync` runs the tests and validates tags and results on a branch, writing a preview to
  `TestResults/gxp/preview/` and nothing under `docs/SDLC`. `gxp snapshot` is the only command that writes and stages
  the documents. A `gxp-snapshot` gate refuses a branch that changes them unless `CADENCE_GXP_SNAPSHOT=1`.
- **Pros:**
  - ✅ Parallel branches stop conflicting on the documents.
  - ✅ The snapshot's rules are unchanged; they apply once, to the candidate that is actually merged.
- **Cons:**
  - ❌ Between snapshots the committed RTM lags the tags on `main`. The `rtm` gate reports that only in snapshot mode.

### Option 2: Keep regenerating on every branch and merge with a custom driver

- **Cons:**
  - ❌ GitHub cannot run it, so pull requests still show conflicts; and the driver was never implemented.

### Option 3: Generate the documents only in CI and never commit them

- **Cons:**
  - ❌ GitHub Actions does not run until 2027, and a reviewable, committed snapshot is wanted for qualification.

## 4. Decision Outcome

**Chosen option: Option 1.**

| Command or gate                                        | Branch (default)                                                                                       | Snapshot mode (`CADENCE_GXP_SNAPSHOT=1`, or `gxp snapshot`)                                       |
| ------------------------------------------------------ | ------------------------------------------------------------------------------------------------------ | ------------------------------------------------------------------------------------------------- |
| `gxp sync`                                             | Runs the tests; writes the preview; fails on unknown IDs, failing tagged tests, skipped database tests | not used                                                                                          |
| `gxp snapshot`                                         | not used                                                                                               | Runs the tests; writes `docs/SDLC` and stages it; same failures, plus the mapping must be current |
| `gxp validate`, `check rtm`, Stop hook                 | Unknown IDs and broken integrity stamps fail                                                           | Also fails when the snapshot's mapping is out of date                                             |
| `check gxp-snapshot` (pre-commit, `cadence check`, CI) | Fails when the documents or `docs/SDLC/runs/` change                                                   | Admits them; in CI, compares the regenerated documents with the snapshot (`gxp diff`)             |

The custom merge driver lines are removed from `.gitattributes`.

## 5. Consequences & Trade-offs

- Feature pull requests no longer carry the two documents. A branch that already changed them restores them with
  `git checkout origin/main -- docs/SDLC/Requirements_Traceability_Matrix.md docs/SDLC/IQ_OQ_PQ_Execution_Report.md docs/SDLC/runs`.
- The integration owner runs `CADENCE_GXP_SNAPSHOT=1 pnpm cadence gxp snapshot` against PostgreSQL on the integrated
  candidate and commits with the same variable set (`--commit` does both).
- A working-copy integrity stamp is still not an electronic signature.

## 6. Implementation & Verification

- `packages/compliance/src/GxpSnapshotPolicy.cs`, `RtmGeneratorService.GenerateRtm(requireCurrentSnapshot)`,
  `packages/tooling_core/src/Services/SyncGxPService.cs`, `packages/cli/src/Services/GxpSnapshotGuardService.cs`,
  `CiPlan.cs` (compliance job), `HookCommands.Stop`, `.github/workflows/ci.yml`, `.gitattributes`.
- `tests/Cadence.UnitTests/Packages/GxpSnapshotPolicyTests.cs`: two git branches that add differently tagged tests pass
  branch validation and the guard, carry neither document, and merge one after the other without a conflict; a staged
  document is refused outside snapshot mode; branch validation still fails on unknown IDs and broken stamps.

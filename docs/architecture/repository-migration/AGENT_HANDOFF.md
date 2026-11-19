# Multi-repository agent execution guide

> [!NOTE]
> Historical reference for the former repository migration program. See the [project plan](../../PROJECT_PLAN.md) for current delivery and [ADR-2603](../../adr/2026-11-19-keep-cadence-in-one-repository.md) for the source layout decision.

**This is a planning/orchestration contract, not permission to reorganize or delete application code automatically.**

## Begin every task

1. Read the [repository registry](REPOSITORY_REGISTRY.md), [capability ownership](CAPABILITY_OWNERSHIP_MATRIX.md), [migration plan](MIGRATION_PLAN.md), current [AGENTS.md](../../../AGENTS.md) and relevant kernel ADRs.
2. Inspect current `main` in both source and destination; do not assume a past SHA still matches.
3. Determine whether the work is **documentation bootstrap**, **boundary preparation**, **source extraction**, **integration**, or **retirement**. Do not conflate them.
4. Assign exactly one source owner and destination bounded context to a change. For ambiguous classes (execution, interop, knowledge, fileshare), inventory at file/symbol level and get an architecture decision.
5. State assumptions, impacted data, packages, external schemas, permissions and validation/test commands before implementation.

## Contract for all pull requests

- One coherent vertical slice with exact source revision and target path.
- No duplicate kernel concept, alternate permission model, unsupported database write, or unversioned cross-product dependency.
- All code/config/DB migrations and relevant architecture tests updated.
- Real PostgreSQL/fault tests for clinical write guarantees; record pass/fail/skip/not-run accurately.
- Synthetic fixtures; no PHI/secrets or unreviewed licensed terminology.
- Update authoritative docs/ADR and exact version manifest as needed.
- Document rollback and source-retirement timing; do not mass-close or delete unrelated issues.
- Never claim software validation or compliance solely from a successful build.

## Initial coordinated tasks

1. File-by-file source inventory and dependency graph (no moves).
2. Review kernel cross-boundary tests and packages; complete [five-gates roadmap](https://github.com/fderuiter/cadence-clinical/issues/5310) evidence where applicable.
3. Draft explicit multi-repo ADR reconciling accepted ADR-2610 and ADR-2600.
4. Demonstrate kernel package consumer and independent Blueprint synthetic-study vertical slice.
5. Only then schedule Capture extraction; remaining domains follow evidenced priorities.

## Synchronization point

Actual kernel extraction (any move of kernel or module implementation code) may begin only when all four conditions hold:

1. The source and dependency inventory is complete (task 1), with symbol-level dispositions for every ambiguous row.
2. An approved architecture decision and ownership matrix exist (task 2), reconciling ADR-2600 and ADR-2610.
3. Existing clinical transaction, authorization, audit and integrity guarantees have an executable verification baseline that runs against PostgreSQL with zero skips.
4. The migration has explicit acceptance criteria and a rollback strategy.

Until this section is marked met in the migration plan, tasks 2 and 3 may draft proposals but must not finalize decisions or move code. All tasks share one inventory and one set of decision records; do not duplicate research or build parallel implementations. Preserve the [five-gates roadmap](https://github.com/fderuiter/cadence-clinical/issues/5310) and the current runtime architecture.

## Reporting

Each change report: source SHA, destination SHA, moved/untouched files, behavior preserved, commands executed, tests passed/failed/skipped, ADR decisions, known risks, next dependency, and links to the relevant issue/PR. No unverified completion.

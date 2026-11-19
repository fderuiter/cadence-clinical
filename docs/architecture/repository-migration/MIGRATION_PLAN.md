# Cadence: 10-repository controlled migration program

> [!NOTE]
> Historical reference for the former repository migration program. See the [project plan](../../PROJECT_PLAN.md) for current delivery and [ADR-2603](../../adr/2026-11-19-keep-cadence-in-one-repository.md) for the source layout decision.

**Status: kickoff plan only — no source implementation migrated.** All ten repositories are private; new destinations contain documentation charters. The current runtime and historical issues remain in [cadence-clinical](https://github.com/fderuiter/cadence-clinical).

## Scope and guardrails
- This plan changes **source code organization** first, not the running system's transaction/deployment model. Multi-repo does not require microservices.
- Existing [ADR-2610](../../adr/2026-10-15-scaffold-services-become-modules-merge-or-are-deleted.md) chose a single composition host; existing [ADR-2600](../../adr/2026-10-19-modules-write-through-the-kernel-command-path.md) chose one transaction across kernel and module rows. Keep both active unless a subsequent ADR formally changes them.
- Preserve the [five-gates integrity epic](https://github.com/fderuiter/cadence-clinical/issues/5310), including honest test qualification, authorization, eCRF reference workflow, crash/replay behavior and architecture checks.
- Original source is the only active implementation during bootstrap. No copying code and claiming two authoritative writers; avoid simultaneous incompatible DB migrations.
- Avoid forcing CI/CD deployment changes; preserve the project's current pre-deployment/local-validation decisions until explicitly revisited.
- Use synthetic fixtures; no PHI, secrets, clinical production claims or public licensing assumptions.

## Phase 0 — Inventory and decision gate
- Record the exact original main SHA and dependency/build baselines; inventory source, migrations, API endpoints, test suites, state and licenses.
- Expand [capability ownership](CAPABILITY_OWNERSHIP_MATRIX.md) to **file-level** assignments. Identify code that must be split rather than moved wholesale.
- Define desired package identifiers, cross-repo versioning and publishing channels.
- Draft/approve a migration ADR explicitly reconciling ADR-2610 (host) and ADR-2600 (clinical transaction).
- Review source ISC license, third-party dictionary/standards redistribution, and brand clearance before public release.

**Exit:** Every owned capability has one destination and proof; no hidden policy or DB dependencies.

## Phase 1 — Establish boundaries in the current repo
- Create forbidden-dependency architecture tests and classify allowed public kernel SDK contracts.
- Add contract snapshots, reproducible PostgreSQL test environments, baseline eCRF journey and release metadata.
- Keep the current host integrated while boundary refactoring occurs.

**Exit:** No product module requires another product's internal DbContext, aggregate or implementation assembly.

## Phase 2 — Extract and package Cadence Core
- Migrate kernel implementation with verified provenance and licenses.
- Publish versioned abstraction/module SDK packages and agreed internal artifacts.
- Verify tenant isolation, actor/authority, temporal corrections, audit/sealing, outbox/inbox, idempotency and failure injection on PostgreSQL.
- Recompose the original host from pinned artifacts; do not change database ownership or transactional semantics implicitly.

**Exit:** A clean checkout builds and the mandatory baseline tests pass with packaged kernel.

## Phase 3 — Pilot Blueprint repository
- Extract a narrow USDM/SoA/study-spec vertical slice, including API, relevant UI, tests and migration history.
- Verify a synthetic medical-device study can produce an approved, testable EDC build specification.
- Verify external integration contract and local developer override process.

**Exit:** Independently versioned Blueprint builds and integrates without requiring changes in unrelated product repos.

## Phase 4 — Extract Capture
- Split execution code by explicit boundaries: EDC/CDMS into Capture; RTSM/supply/device allocation into Allocate; kernel records remain Core.
- Preserve full enter/correct/query/review/sign/lock workflow with strict Vue types and PostgreSQL evidence.
- Run synthetic end-to-end compatibility from Blueprint to Capture.

**Exit:** All clinical reference-slice invariants and integration tests still pass after repackaging.

## Phase 5 — Remaining domain extraction
Recommended tentative order: Orbit → Engage → Vigil → Allocate → Lens → Assure. Reorder based on dependencies and test readiness, not product-marketing deadlines. Do not create scaffolds just to fill directories.

**Exit per repo:** Independently buildable and testable, domain and contract tests passing, exact source retirement accounted for, system integration demonstrated.

## Phase 6 — Composition and release qualification
- Establish a version-pinned assembly manifest for compatible NuGet/npm packages, API/event schemas and database migrations.
- Provide one local workspace setup with optional source overrides, a consistent CLI, and system-level synthetic tests.
- Record versioned executable evidence and reproducible configuration. Retain rollback to known-good source/artifact versions.

**Exit:** Reproduce the integrated system from a clean checkout and a pinned manifest without floating branch heads.

## Phase 7 — Commercial and regulated readiness (separate decisions)
- Confirm standalone SKU entitlements and third-party imports/exports.
- Validate per intended clinical use, establish operations, support, security and retention controls.
- Clear brand/trademarks, product licensing, external terminology rights and contractual record-access requirements.

## Shared acceptance rubric for every extraction
1. Ownership: one source for each canonical fact.
2. Correctness: required tests run and pass against identified revision/configuration; skips count as unqualified.
3. Integrity: actor, scope, consent, blinding, audit, idempotency, lineage, temporal history, concurrency and recovery preserved where applicable.
4. Compatibility: public contracts versioned; integrated host, UI and external import/export work.
5. Provenance: source git history/copyright/license obligation documented.
6. Reversibility: no premature delete; safe rollback described and rehearsed.
7. Transparency: docs and feature flags accurately describe what exists.

## High-severity risks
- **Split-brain kernel:** multiple `Study`, `Participant`, `Observation` or audit owners.
- **False atomicity:** a distributed service writes clinical data but relies on async audit.
- **Runtime distributed monolith:** domain repos still require same-day coordinated releases.
- **Regulated history loss:** schema refactor drops approvals, corrections or provenance.
- **License/brand debt:** source ISC copied without attribution or licensed standards redistributed.
- **Validation theater:** skipped DB tests or mocks presented as qualified clinical evidence.

## Program governance
One central initiative in this repository; domain-owned implementation issues in destination repos, linked to central epics. No mass-closure or issue transfers without reconciled links. Review roadmap impact with each extraction.

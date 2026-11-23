# Cadence: repository registry and ownership source of truth

> [!NOTE] Historical reference for the former repository migration program. See
> the [project plan](../../PROJECT_PLAN.md) for current delivery and
> [ADR-2603](../../adr/2026-11-19-keep-cadence-in-one-repository.md) for the
> source layout decision.

**Status:** planning and bootstrap committed 2026-10-28. **No clinical
implementation has yet been extracted** into the nine new product/foundation
repositories. This registry is the designated inventory of target source
ownership, not an assertion that runtime services have been moved.

## Repositories (10 total)

- `cadence-clinical` (**Workspace / Complete**) — Platform composition, gateway,
  shared client shell, CLI, release integration. Initial state: existing source;
  integration authority.
- `cadence-kernel` (**Core**, not a paid SKU) — Canonical clinical identity,
  temporal record, permissions, audit, command SDK. Initial state: documentation
  initialized; implementation remains in source.
- `cadence-study-design` (**Blueprint**) — USDM/protocol authoring, study build,
  study standards authoring. Initial state: documentation initialized.
- `cadence-clinical-data` (**Capture**) — EDC, eCRF, eSource, CDMS, clinical
  coding and queries. Initial state: documentation initialized.
- `cadence-trial-operations` (**Orbit**) — CTMS, startup, eTMF/eISF, monitoring,
  site finance, operational workflows. Initial state: documentation initialized.
- `cadence-participants` (**Engage**) — Recruitment, eConsent, eCOA/ePRO,
  participant portal/DCT. Initial state: documentation initialized.
- `cadence-interventions` (**Allocate**) — RTSM, approved randomization, supply,
  device accountability. Initial state: documentation initialized.
- `cadence-safety` (**Vigil**) — Safety case, SAE reconciliation, PV, E2B(R3),
  signal detection. Initial state: documentation initialized.
- `cadence-evidence` (**Lens**) — Labs, imaging, DHT, statistical analysis,
  standardized datasets and RWE. Initial state: documentation initialized.
- `cadence-quality-regulatory` (**Assure**) — QMS, CAPA, inspection, RIM,
  submissions and transparency. Initial state: documentation initialized.

## Principles

- Repository != bounded-context runtime service != purchasable application. The
  first governs source ownership; a product may license several application
  modules from one repository.
- Product domains own workflows, not duplicate kernel canonical `Study`, `Site`,
  `Participant`, `Observation`, `Source`, `Artifact`, `Grant`, policy, audit or
  transaction authority.
- `cadence-clinical` remains the **only active implementation and composition
  authority** until each extraction clears its gates. Do not edit the original
  code as though it has already moved.
- `cadence-kernel` must publish intentionally narrow versioned
  `Cadence.Kernel.Abstractions` and `Cadence.Kernel.Modules` contracts; do not
  force products to depend on the kernel implementation/persistence project.
- The kernel's approved PostgreSQL transaction model (ADR-2600) and the single
  composition host direction (ADR-2610) are **not automatically superseded** by
  having multiple Git repositories.
- All new repository branding is provisional. The source repo uses ISC and each
  new repo's distribution/license status must be explicitly reviewed; do not
  assume missing LICENSE equals ISC.
- No production clinical records or validations are implied by repo
  initialization. Use synthetic data until separate readiness qualification.

## What lives where

See [capability ownership](CAPABILITY_OWNERSHIP_MATRIX.md) for current-source
disposition and exact domain conflicts. Each destination repository contains a
separate `ARCHITECTURE.md`, `AGENTS.md`, and `docs/MIGRATION.md` defining its
intended boundary and proof.

## Cross-repo version strategy

- Target: pinned, immutable NuGet libraries/modules for the .NET host and
  versioned npm Vue feature libraries for the workspace; use published
  OpenAPI/schema/event contracts for external services.
- Until packages are actually published, these are design goals, not existing
  artifact feeds.
- No floating dependency on another repo's `main`; no cross-repo private
  `DbContext` or domain internals.
- Composition manifest in `cadence-clinical` will pin verified versions.
  Consumer/producer contract tests and a system-level synthetic journey gate
  upgrades.

## Governance

Use [migration plan](MIGRATION_PLAN.md), [agent handoff](AGENT_HANDOFF.md), and
the existing
[five-gates epic](https://github.com/fderuiter/cadence-clinical/issues/5310).
Future architecture ADRs must explicitly reconcile ADR-2610 and ADR-2600; do not
silently reclassify them as superseded.

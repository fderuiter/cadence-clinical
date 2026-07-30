# ADR-108: Consolidated eClinical Services and Compliance Enhancements

* **Status:** Accepted
* **Date:** 2026-07-29
* **Authors:** @fderuiter
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Fifteen distinct feature branches (PRs #782 through #796) introduced enhancements across Keycloak RBAC role mapping, multi-tenant global library isolation, legacy SPA runtime retirement, eTMF site-scoped document models, site-id schema inheritance, TSDV immutable enrollment sequences, batch sign-off token hardening, lab ranges CENTRAL invariants, eTMF ingestion service refactoring, bulk study archiving, Schedule of Activities soft retirement, SDTM SUPP-- record propagation, Vue Auditor Portal integration, eConsent comprehension checks, and notifications action-items queues. Consolidating these enhancements into `dev` requires formalizing their architectural integration under 21 CFR Part 11 and GxP standards (PRD-SYS-001).

## 2. Decision Drivers & Constraints

* Unified security & tenant isolation across Global Library and study instances.
* Deprecation of legacy browser-side header signing in favor of gateway-exclusive Bearer token authentication.
* Strict multi-site isolation for eTMF documents and clinical audit trails.
* Full requirements traceability and qualification report verification under GxP protocol.

## 3. Options Considered

1. Option A (Selected): Consolidate PRs #782 through #796 in a single topologically sequenced integration branch `feature/consolidate-prs-782-796`, resolving inter-PR conflicts, enforcing linting and GxP RTM generation.
2. Option B: Merge PRs individually without unified validation, risking inter-PR merge regressions and broken client boundaries.

## 4. Decision Outcome

Chosen option: Option A. Sequential topological integration ensures security foundations and legacy deprecations are established prior to feature service and UI integration.

## 5. Consequences & Trade-offs

* Positive: Single audit point for 15 PRs with 100% test pass rate, updated RTM traceability, and clean frontend/backend compilation.
* Negative: Multi-stage conflict resolution required for shared service modules.

## 6. Implementation & Verification

* Target files modified across `apps/`, `packages/`, `docs/`, and `tests/`.
* Verified using `pnpm run lint`, `pnpm run build`, `uv run ruff check .`, `uv run pytest`, and `uv run python scripts/sync_gxp.py`.

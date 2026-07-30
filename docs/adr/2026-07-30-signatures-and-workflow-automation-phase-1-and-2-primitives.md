# ADR-112: Signatures and Workflow Automation Phase 1 and 2 Primitives

* **Status:** Accepted
* **Date:** 2026-07-30
* **Authors:** @fderuiter
* **Deciders:** Lead Architect

---

## 1. Context & Problem Statement

21 CFR Part 11 and GxP regulatory compliance require robust signature gating, RBAC permissions, and signature manifestation tracking across study versioning, monitoring visits, regulatory forms, and training logs (PRD-SYS-001).

## 2. Decision Drivers & Constraints

* Require generalized 21 CFR Part 11 SigningReason enum values.
* Centralize signature-gated path matching in `packages/security/gating.py`.
* Register fine-grained RBAC permissions across SysAdmin, Sponsor Designer, Sponsor DM, and Site staff roles.
* Extend core relational models (`StudyVersion`, `MonitoringVisit`, `Ticket`) with signature manifestation fields and introduce `RegulatoryForm` and `TrainingLog` models.

## 3. Options Considered

1. Centralized signature gating and RBAC permission registration (Selected).
2. Ad-hoc per-endpoint signature validation logic in individual FastAPI routers.

## 4. Decision Outcome

Chosen option 1 because centralizing signature gating in `packages/security/gating.py` and security middleware guarantees uniform enforcement across all eClinical service boundaries.

## 5. Consequences & Trade-offs

* Positive: Standardized 21 CFR Part 11 electronic signature enforcement across gateway and downstream services.
* Negative: Requires maintainers to register new signature-gated endpoints in `gating.py` or `regulated_actions.py`.

## 6. Implementation & Verification

* Modified `packages/security/gating.py`, `packages/security/middleware.py`, `packages/security/rbac.py`, `apps/ctms/models.py`, `apps/org/models.py`, and `apps/tickets/models.py`.
* Verified with automated unit and integration test suites.

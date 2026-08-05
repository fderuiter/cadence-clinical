# ADR-131: Informed Consent Multi-Language Translation Management

- **Status:** Accepted
- **Date:** 2026-07-31
- **Authors:** @jules
- **Deciders:** @jules

---

## 1. Context & Problem Statement

To provide accessible and localized Informed Consent Forms (ICF) to clinical trial subjects globally, the eConsent service must support high-assurance translation authoring, review workflows, and low-latency retrieval. The system must adhere to FDA 21 CFR Part 11 auditing constraints while supporting multi-language templates and clauses.

This decision addresses the following requirements:

- **PRD-SYS-001**: Comprehensive audit trails, GxP validation, and traceability.
- **Trace-10**: Efficient and secure approved content retrieval for translated consent forms.

## 2. Decision Drivers & Constraints

- **Strict GxP / Part 11 Compliance:** Every change, transition, and update to any consent translation must be version-controlled, auditable, and trace-logged.
- **Low Latency & High Availability:** Subject-facing portal retrieves approved translations frequently, requiring a high-performance, thread-safe, read-through caching layer.
- **Stale-On-Error Robustness:** The caching layer must fall back gracefully to return expired/stale translations if the relational database is unreachable.
- **Input Validation:** Only supported ISO 639-1 language codes are accepted.

## 3. Options Considered

1. **Integrated Multi-Language Versioning (Selected):** Represent translation as an audited, version-tracked entity `ConsentTranslation` referencing either a source clause or a template version. Add review workflow states (`DRAFT`, `IN_REVIEW`, `APPROVED`) and protect approved retrievals with a read-through cache with thread safety and stale-on-error fallback.
2. **Dynamic In-Memory Localized Translation:** Perform live translation at runtime using an external translation provider. Rejected due to high latency, lack of deterministic human review, and compliance/reproducibility issues under GxP standards.

## 4. Decision Outcome

We choose **Option 1 (Integrated Multi-Language Versioning)**.

- **Database Model:** A `ConsentTranslation` model in `apps/econsent/models.py` tracking version indices, languages, translated content (title and text), and Part 11 metadata (`created_at`, `created_by`, `reason_for_change`).
- **Review Workflow:** Status transition endpoints `DRAFT -> IN_REVIEW -> APPROVED` enforce valid paths and invalidate caches upon approval.
- **Caching Layer:** `ApprovedTranslationCache` under `apps/econsent/cache.py` provides thread-safe in-memory caching with TTL and eviction, with read-through retrieval returning stale data on database failure.
- **Localization Validation:** A centralized `packages/core-models/localization/` library defines supported ISO 639-1 language codes and `validate_language_code` validation functions.

## 5. Consequences & Trade-offs

- **Positive:**
  - **Auditability:** Complete Part 11 trace history for translations identical to original clauses/templates.
  - **Performance:** Read-through caching ensures high speed for patient retrieval.
  - **Reliability:** Stale-on-error capability prevents subject signup blockages on intermittent database failures.
- **Negative:**
  - Requires explicit status workflow steps and cache invalidation hand-offs upon transition.

## 6. Implementation & Verification

- **Implementation Files:**
  - `apps/econsent/models.py`
  - `apps/econsent/main.py`
  - `apps/econsent/cache.py`
  - `packages/core-models/localization/models.py`
- **Verification Tests:**
  - `tests/test_econsent_translations.py` contains thorough validation, CRUD, status transitions, cache hits, expiration, and stale-on-error unit tests.

# ADR-139: Expose CRA Monitoring SDV Transport Models and Shared Sign-Off Logic

- **Status:** Accepted
- **Date:** 2026-07-31
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Clinical Research Associate (CRA) monitoring requires unified Source Data Verification (SDV) sign-off capabilities across study, subject, visit, page, and field scopes. This step exposes enhanced transport models in `packages/core-models/execution/sdv_transport_models.py` and modularized verification helpers in `apps/execution/sdv_helper.py` to ensure GxP-compliant audit logging and bulk SDV operations.

## 2. Decision Drivers & Constraints

- Ensure strict alignment with GxP 21 CFR Part 11 audit trails (`verified_by`, `verified_at`, `signing_reason`).
- Maintain backward compatibility with existing SDV request/response DTOs while supporting multi-scope verification.
- System requirement compliance: PRD-SYS-001.

## 3. Options Considered

1. **Modular SDV Transport & Helper Abstraction (Selected)**: Create dedicated `sdv_helper.py` and extend `sdv_transport_models.py` with optional legacy fields.
2. Direct inline endpoint logic: Embed sign-off database operations inside route handlers without reusable helpers.

## 4. Decision Outcome

Chosen option 1 because reusable helper methods in `apps/execution/sdv_helper.py` enable consistent SDV verification across single and bulk routes while maintaining strict database isolation.

## 5. Consequences & Trade-offs

- **Positive**: Shared sign-off logic prevents duplication between individual and bulk SDV endpoints.
- **Positive**: Preserves backward compatibility via optional legacy fields in `BulkSdvSignOffResponse`.
- **Negative**: Requires maintaining helper functions alongside ORM model mappings.

## 6. Implementation & Verification

- Modified `packages/core-models/execution/sdv_transport_models.py` and `apps/execution/sdv_helper.py`.
- Verified with existing and expanded SDV test suites in `tests/`.

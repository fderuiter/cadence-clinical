# ADR-105: eConsent Authoring UI and Patient-Facing Portal Integration

* **Status:** Accepted
* **Date:** 2026-08-24
* **Authors:** @fderuiter
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement
With the eConsent microservice scaffolded and ready, we need to build high-fidelity visual authoring screens for sponsors/designers to compose and publish consent templates, and build participant-facing consent review and signature workflows inside the participant companion portal. To avoid duplicate rendering and parsing code, common presenting structures and gating mechanics must be unified.

This decision implements requirements under Trace-12 and ADR-062.

## 2. Decision Drivers & Constraints
* **Driver 1 (Standardized Presentation):** Both apps/web and apps/subject-portal require a unified, structured presentation of consent clauses and metadata.
* **Driver 2 (Compliance and Gating):** Strict 21 CFR Part 11 mandates that electronic signatures must remain disabled until patient comprehension is validated.
* **Driver 3 (Credential Hygiene):** To comply with Part 11, passwords and PINs must never be retained in client state/DOM after any submission or failure.

## 3. Options Considered
### Option 1: Separate Application Implementations
Implement all rendering, normalizers, and gating checks independently in both `apps/web` and `apps/subject-portal`.
- Pros:
  * ✅ No cross-package coupling.
- Cons:
  * ❌ Leads to duplicate translation/ordering logic.
  * ❌ Risk of inconsistent compliance checks and security behaviors between sponsor preview and actual patient portal.

### Option 2: Shared Presentation Normalizer in packages/ui (Selected)
Centralize template clause normalizers, answers shaping, and gating decisions in a new module `packages/ui/econsent.js`, shared by both applications.
- Pros:
  * ✅ Single source of truth for ordering and rendering structures.
  * ✅ Guarantees that the visual content seen by the sponsor during drafting matches exactly what is displayed to the participant.
  * ✅ Standardizes the gating logic for electronic signature activation.
- Cons:
  * ❌ Introduces a light import dependency in `apps/subject-portal` on `packages/ui`.

## 4. Decision Outcome
* **Chosen Option:** Option 2
* **Justification:** Option 2 provides absolute consistency and compliance security. By sharing the normalization and gating code, we ensure zero discrepancy between the designed consent template and the final signed participant form.

## 5. Consequences & Trade-offs
* **Positive Impact:** Structured presentation is unified; easy to verify and maintain gating requirements.
* **Negative Impact / Technical Debt:** Requires clean package workspace imports in the patient portal.
* **Mitigation Strategy:** Provide robust client-side unit test suites in `packages/ui/tests/econsent_utils.test.js` to prevent regressions.

## 6. Implementation & Verification
* **Affected Repositories / Services:** `apps/web/`, `apps/subject-portal/`, `packages/ui/`
* **Verification Plan:** Verified using Vitest suites across `packages/ui`, `apps/web`, and `apps/subject-portal/`.

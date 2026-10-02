# ADR-2803: Synchronous 6-Facet Prerequisite Evaluator with HMAC Token Verification Engine

- **Status:** Accepted
- **Date:** 2026-10-02
- **Authors:** @stitch-agent
- **Deciders:** @engineering-leads, @compliance-qa

---

## 1. Context & Problem Statement

Site activation previously relied on a flat boolean `SITE_ACTIVATION` milestone check in `SiteComplianceCache`. Under SRS-PRD-SSU-001 and 21 CFR Part 11, site greenlight certification requires evaluating 6 mandatory regulatory prerequisites (`facet_regulatory_irb`, `facet_cta_budget`, `facet_eisf_baseline`, `facet_doa_log`, `facet_staff_training`, `facet_ip_release`), issuing signed HMAC SHA-256 `GreenlightToken` payloads upon activation or executive GxP override, and enforcing token verification in `SubjectApplicationService` prior to subject screening and enrollment.

## 2. Decision Drivers & Constraints

- **Regulatory Compliance:** 21 CFR Part 11 and GxP compliance require cryptographic verification of greenlight status prior to subject screening/enrollment.
- **Auditability:** Complete audit logs (`SITE_GREENLIGHT_ISSUED`, `BLOCKED_ACTIVATION`, `BLOCKED_ENROLLMENT`) must be written for all evaluation events.
- **Dual Step-Up Authentication:** Executive GxP overrides require dual 21 CFR Part 11 step-up re-authentication tokens (`X-Sig-Token`) from QA Director and Lead Medical Monitor.
- **Performance:** Synchronous 6-facet evaluation and HMAC signing must complete in < 50ms.

## 3. Options Considered

### Option 1: Async Out-of-Band Greenlight Verification
- **Overview:** Evaluate facets asynchronously in a background worker and poll for greenlight tokens.
- **Pros:** Keeps API request payloads smaller.
- **Cons:** ❌ Introduces race conditions and latency during site activation and subject enrollment.

### Option 2: Synchronous In-Process 6-Facet Evaluator with HMAC Token Engine (Selected)
- **Overview:** Perform synchronous, in-process 6-facet evaluation in `SiteComplianceApplicationService`, generate HMAC SHA-256 signed `GreenlightToken` tokens, and enforce verification in `SubjectApplicationService`.
- **Pros:**
  - ✅ Guarantees zero latency drift and sub-millisecond evaluation (< 50ms requirement).
  - ✅ Cryptographically verifies site, study, and expiration bounds in `SubjectApplicationService`.
  - ✅ Interoperable with Python reference implementation.
- **Cons:**
  - ❌ Requires step-up signature token verification logic during executive overrides.

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Guarantees strict regulatory compliance and synchronous gating for site activation and subject screening/enrollment without out-of-band state inconsistency.

## 5. Consequences & Trade-offs

- **Positive Impact:** Strong GxP cryptographic assurance for site readiness before subjects can be enrolled.
- **Negative Impact / Technical Debt:** Requires callers to provide valid GxP step-up re-authentication tokens when initiating executive overrides.
- **Mitigation Strategy:** Automated unit and architecture tests verify token signing, verification, and role validation.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `Cadence.Security`, `Cadence.Apps.Execution`.
- **Verification Plan:** Unit tests in `GreenlightTokenTests.cs`, `SiteComplianceApplicationServiceTests.cs`, and `SubjectApplicationServiceStateTests.cs`.

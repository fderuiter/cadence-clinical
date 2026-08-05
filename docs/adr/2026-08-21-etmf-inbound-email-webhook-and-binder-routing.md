# ADR-110: eTMF Inbound-Email Webhook and Binder Routing

- **Status:** Accepted
- **Date:** 2026-08-21
- **Authors:** @jules
- **Deciders:** @fderuiter, @gxp-lead
- **Requirement Reference:** PRD-SYS-001

---

## 1. Context & Problem Statement

Users need to forward clinical trial correspondence and associated documents into specific binders within the electronic Trial Master File (eTMF) securely without bypassing taxonomy, versioning, immutability, or 21 CFR Part 11 audit controls. We need to introduce an inbound-email webhook that validates provider requests, resolves the target study/binder location, and routes message content and attachments into the shared eTMF ingestion service.

This decision implements requirements under PRD-SYS-001.

## 2. Decision Drivers & Constraints

- **Driver 1:** Secure provider authentication via HMAC-SHA256 signature verification over request timestamp and token.
- **Driver 2:** Strict 21 CFR Part 11 and GxP compliance (e.g., preserving audit trails with action `EMAIL_INGEST` and maintaining immutability).
- **Driver 3:** Robust validation of routing targets via a deterministic structured recipient email address.
- **Driver 4:** Isolation of sensitive study details in rejection error responses to prevent information leakage.

## 3. Options Considered

### Option 1: Inline Direct Ingestion Logic in Gateway

- **Overview:** Implement email parsing, HMAC checks, and ingestion logic directly within the API Gateway service.
- **Pros:**
  - ✅ Centralizes authentication.
- **Cons:**
  - ❌ Bloats the gateway with service-specific business logic (eTMF taxonomy, file attachments, and direct db transactions).

### Option 2: Downstream Webhook Endpoint with Path Exemption in Gateway and Middleware (Selected)

- **Overview:** Expose a secure path `/api/v1/etmf/inbound-email` on the eTMF service, exempted from Gateway authentication middleware, relying on dedicated HMAC-SHA256 signature verification local to the service.
- **Pros:**
  - ✅ Tight encapsulation of business and ingestion logic within the eTMF domain.
  - ✅ Clean service-to-service routing and delegation to the shared ingestion service.
  - ✅ Easy integration with external SMTP parsed multipart webhook providers (e.g., Mailgun, Sendgrid).
- **Cons:**
  - ❌ Requires specific gateway proxy and middleware auth exemptions.

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Implementing a dedicated webhook endpoint in the eTMF service allows utilizing our clean, transactional ingestion service directly (`ingest_tmf_document`) under GxP audit constraints. HMAC verification, timestamp freshness checks, and replay prevention ensure the exempted endpoint remains completely secure.

## 5. Consequences & Trade-offs

- **Positive Impact:** Secure, unauthenticated path for SMTP webhook providers. Automated extraction of study ID and binder metadata from recipient email address. Idempotency guarantees and full GxP-compliant `EMAIL_INGEST` auditing.
- **Negative Impact / Technical Debt:** Added responsibility of maintaining an active HMAC shared secret (`INBOUND_EMAIL_HMAC_SECRET`) on both the external SMTP provider and eTMF microservice.
- **Mitigation Strategy:** Detailed secret-rotation guidelines added to the operational guide, integrated with automated monitoring.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `apps/etmf`, `apps/gateway`, `packages/security`
- **Verification Plan:** Verify signature verification, routing, replay protection, size-limit checks, multi-attachment behavior, and idempotency using automated integration tests in `tests/test_etmf_inbound_email.py`.

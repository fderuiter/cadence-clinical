# ADR-067: Signed NCI Thesaurus CT Web Client

* **Status:** Accepted
* **Date:** 2026-08-09
* **Authors:** @jules
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement
The Cadence Clinical platform requires an authenticated web API client under `apps/web/src/api/` to securely interact with the API Gateway endpoints for controlled terminology (CT) validation, text-based terminology search, and study-level terminology reports. The client must reuse the cryptographic signing library in `packages/ui/signing.js` to construct canonical gateway signatures and verify transmission boundaries.

Additionally, this change aligns standard GxP formatting and style standardizations across database entities, including `apps/execution/database/models.py`.

## 2. Decision Drivers & Constraints
* **Compliance (FDA 21 CFR Part 11):** All API operations must utilize signed gateway headers to guarantee auditable identity propagation down to microservices.
* **Separation of Concerns:** Client errors, invalid terminology states (`VALID`, `INVALID`, `DEGRADED`), and transport failures must be cleanly isolated and handled.
* **No Hardcoded URLs:** The Gateway URL must be dynamically configurable for different deployment environments.

## 3. Options Considered
### Option 1: Direct Service Calls without Signature Verification
* **Overview:** Front-end queries the designer service directly over HTTP without gateway signatures.
* **Pros:**
  * ✅ Simpler front-end setup.
* **Cons:**
  * ❌ Violates 21 CFR Part 11 identity tracking and GxP compliance.
  * ❌ Bypasses API Gateway security and rate-limiting blocks.

### Option 2: Gateway-Targeted signed client using packages/ui/signing.js (Selected)
* **Overview:** Implement `terminologyClient` inside `apps/web/src/api/` to target the gateway base URL, generate HMAC signatures dynamically using the shared cryptographic helper, and map terminology status fields distinctly from physical transmission errors via `TerminologyNetworkError`.
* **Pros:**
  * ✅ High security: Uses GxP-compliant canonical JSON signatures for identity propagation.
  * ✅ Configurable: Dynamically configures the API gateway base URL.
  * ✅ High resilience: Distinguishes logical terminology states (`VALID`, `INVALID`, `DEGRADED`) from connection drops or bad gateway responses (502/503).
* **Cons:**
  * ❌ Requires maintaining front-end cryptographic signature generation.

## 4. Decision Outcome
* **Chosen Option:** Option 2
* **Justification:** Reusing the canonical signing toolkit and targeting the central API Gateway is the only way to satisfy regulatory GxP tracing and 21 CFR Part 11 security constraints.

## 5. Consequences & Trade-offs
* **Positive Impact:** Allows the front-end to safely search, validate, and compile CT reports with full identity tracking.
* **Negative Impact / Technical Debt:** Requires keeping the front-end signing utility and Vite configuration aligned.

## 6. Implementation & Verification
* **Affected Repositories / Services:** `apps/web` (terminology API client and unit tests), `apps/execution/database` (models formatting alignment).
* **Verification Plan:** Verified using Vite/Vitest to run `apps/web/tests/terminology_client.test.js` and checking that ADR and linter/formatter validations pass cleanly.

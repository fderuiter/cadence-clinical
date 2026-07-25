# ADR-057: Rolling Versioning and Transition Support for Canonical JSON Signatures

* **Status:** Accepted
* **Date:** 2026-07-24
* **Authors:** @jules
* **Deciders:** @engineering-lead, @gxp-auditor

---

## 1. Context & Problem Statement
To eliminate parameter injection and signature collision vulnerabilities in our legacy colon-concatenated signature format, we are migrating to secure Canonical JSON Signing (Version 2). However, a "big-bang" deployment that upgrades the Gateway and all downstream microservices simultaneously is extremely risky and can cause service disruption or downtime. To ensure a zero-downtime transition, we must support both the legacy (Version 1) format and the secure canonical (Version 2) format concurrently during the migration window.

## 2. Decision Drivers & Constraints
* **Zero Downtime:** Downstream services must be able to verify requests continuously without interruption during the roll-out of the gateway and microservices.
* **Compatibility:** Existing web clients and microservices utilizing Version 1 signatures must remain functional during transition.
* **Security:** Rolling upgrade mechanisms must prevent downgrade attacks once services are upgraded.

## 3. Options Considered

### Option 1: Simultaneous Big-Bang Deployment
* **Overview:** Upgrade the entire infrastructure at once, instantly deprecating and removing Version 1 signature verification.
* **Pros:**
  * ✅ Simplifies code by avoiding legacy fallback logic.
* **Cons:**
  * ❌ Extremely high deployment risk and potential for service disruption.

### Option 2: Rolling Versioning with Dynamic Inspection (Selected)
* **Overview:** The gateway and downstream middleware inspect the `X-Signature-Version` header. The system supports both legacy Version 1 (colon-concatenated) and Version 2 (canonical JSON) signature verification paths during the transition.
* **Pros:**
  * ✅ Phased, risk-free deployment of microservices.
  * ✅ No disruption to active trial operations.
* **Cons:**
  * ❌ Code base temporarily contains two validation paths.

## 4. Decision Outcome
* **Chosen Option:** Option 2
* **Justification:** Option 2 aligns with industry best practices for secure API migrations and satisfies GxP zero-downtime deployment safety guidelines.

## 5. Consequences & Trade-offs
* **Positive Impact:** Enables smooth sequential deployment of microservices and gateway without any API downtime.
* **Negative Impact:** Temporary maintenance of Version 1 verification paths in middleware and SDKs.

## 6. Implementation & Verification
* **Affected Repositories / Services:** `apps/gateway`, `packages/security`, `packages/ui`
* **Verification Plan:** Validated via unit/integration tests in `tests/test_security_middleware.py` and `packages/ui/tests/signing.test.js`.

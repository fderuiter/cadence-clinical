# ADR-095: CI Schema Introspection and Gateway Aggregation Resilience

* **Status:** Accepted
* **Date:** 2026-07-27
* **Authors:** @jules
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

The API gateway dynamically aggregates OpenAPI specifications from downstream microservices to populate the unified developer documentation portal. Previously, this aggregation lacked build-time validation, making the portal vulnerable to runtime crashes (500 errors) caused by malformed schemas, name collisions, or circular reference loops in downstream services.

To prevent documentation downtime and catch schema-related bugs before they reach production, this PR introduces active CI-stage schema introspection, isolates downstream aggregation failures, and safeguards against infinite recursion loops.

---

## 2. Decision Drivers & Constraints

* **Zero Documentation Downtime:** Downstream microservice aggregation failures should not cascade and take the entire portal offline.
* **Infinite Recursion Protection:** Nested circular schema reference loops must be cleanly detected and broken to prevent server memory exhaustion.
* **Hermetic and Fast CI Builds:** Build-time validation must run completely offline without active database or service network dependencies, completing in under 15 seconds.

---

## 3. Options Considered

### Option 1: Live Aggregation Validation in Production
* **Overview:** Rely strictly on runtime checks and manual troubleshooting after deployment when downstream schemas crash.
* **Pros:**
  * ✅ No build-time scripts or static analysis requirements.
* **Cons:**
  * ❌ High risk of production downtime/500 errors for the developer portal.
  * ❌ Schema issues are only detected after code has been deployed to production.

### Option 2: Active CI Schema Introspection with Graceful Gateway Isolation
* **Overview:** Build a hermetic offline static schema validation script for the CI runner, protect the runtime Gateway with failure isolation, and implement a cycle-breaking reference-rewriting engine.
* **Pros:**
  * ✅ Prevents circular references from crashing the Gateway with cycle detection.
  * ✅ Gracefully isolates broken downstream schemas, serving healthy ones instead of a 500 error.
  * ✅ Fast, offline-capable verification pipeline in CI.
* **Cons:**
  * ❌ Requires maintaining mock microservice schemas for validation.

---

## 4. Decision Outcome

* **Chosen Option:** Option 2
* **Justification:** Implementing build-time validation and runtime safety bounds perfectly aligns with our compliance and high-availability standards, completely eliminating documentation runtime crashes while maintaining an extremely fast verification pipeline.

---

## 5. Consequences & Trade-offs

* **Positive Impact:**
  * Clean, robust developer documentation portal that handles failures gracefully.
  * Rapid feedback loops in CI when developers introduce circular references or schema name collisions.
* **Negative Impact / Technical Debt:**
  * Adds extra test maintenance overhead for the static schema validation test scenarios.
* **Mitigation Strategy:** Keep tests cleanly decoupled and mock downstream structures using minimal valid schema patterns.

---

## 6. Implementation & Verification

* **Affected Repositories / Services:**
  * `apps/gateway/main.py`: Isolated downstream aggregation and added visited-object ID cycle tracking.
  * `scripts/validate_schemas.py`: Hermetic static schema compiler and namespace collision validator.
* **Verification Plan:**
  * Run `scripts/validate_schemas.py` during the CI build process.
  * Verify nested, recursive, cyclic, and isolated failure scenarios in `tests/test_schema_validation.py`.

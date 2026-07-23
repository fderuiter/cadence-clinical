# ADR-042: Decoupled API-First In-Memory Diffing

* **Status:** Accepted
* **Date:** 2026-07-22
* **Authors:** @google-labs-jules
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement
Currently, the `/api/v1/studies/{study_id}/differences` endpoint requires a direct connection to the underlying graph database (Neo4j/Cypher) to compute differences between two versions of a clinical study. As we migrate to an API-first microservices architecture, direct database connections from downstream domain services are deprecated. This resulted in the endpoint throwing a persistent `503 Service Unavailable` error, blocking clinical study designers from comparing different study design versions.

## 2. Decision Drivers & Constraints
* **Driver 1:** Decoupling downstream domain services from direct database dependencies.
* **Driver 2:** High availability and eliminating `503 Service Unavailable` errors.
* **Driver 3:** Performance (execution under 2 seconds for standard payload sizes up to 10MB).

## 3. Options Considered
### Option 1: Direct Database Diffing (Status Quo)
* **Overview:** Retaining the direct Neo4j connection for this single endpoint to compute differences within the database.
* **Pros:**
  * ✅ No new architecture or algorithm needed.
* **Cons:**
  * ❌ Violates the API-first separation of concerns.
  * ❌ Creates tight coupling to the database schema and drivers.

### Option 2: Dedicated Diffing Microservice
* **Overview:** Spinning up an entirely new microservice solely for diffing JSON payloads.
* **Pros:**
  * ✅ Strong isolation of diffing logic.
* **Cons:**
  * ❌ High overhead of deploying, maintaining, and monitoring a new service for a lightweight capability.

### Option 3: Decoupled API-First In-Memory Diffing - Selected
* **Overview:** Asynchronously fetch complete study payloads from an external registry (`STUDY_REGISTRY_URL`) and perform a dynamic, high-performance in-memory comparison by flattening nested payloads.
* **Pros:**
  * ✅ Decouples the designer service from the database completely.
  * ✅ Extremely fast for standard payload sizes.
* **Cons:**
  * ❌ Increased memory footprint when comparing very large payloads.
  * ❌ Dependency on external registry uptime and network latency.

## 4. Decision Outcome
* **Chosen Option:** Option 3 (Decoupled API-First In-Memory Diffing)
* **Justification:** This option perfectly satisfies the architectural shift away from direct database coupling without incurring the massive operational overhead of deploying a dedicated microservice. It efficiently meets our performance requirement (<2 seconds) while running purely in-memory.

## 5. Consequences & Trade-offs
* **Positive Impact:** Decouples the designer service from the database layer, ensures high availability, and executes extremely fast for standard payload sizes.
* **Negative Impact / Technical Debt:** Increased memory footprint on the service when processing very large payloads (>10MB). Introduces a network dependency on the external registry.
* **Mitigation Strategy:** Implemented graceful timeouts and `502/504` error handling to manage registry availability issues.

## 6. Implementation & Verification
* **Affected Repositories / Services:** `apps/designer/main.py`
* **Verification Plan:** Validated via automated test coverage in `tests/test_designer_differences.py`, specifically checking missing versions, offline registry handling, timeouts, and successful diff generation.

# ADR-0091: Zero-Trust Database Isolation and Agent Facade Microservice

* **Status:** Accepted
* **Date:** 2026-07-27
* **Authors:** @jules
* **Deciders:** @engineering-lead, @security-architect

---

## 1. Context & Problem Statement
AI developer agents currently interact directly with proprietary clinical algorithm modules and raw database systems due to missing API facade layers. In addition, clients can bypass site-scoped authorization checks using spoofed delegation headers. This poses a significant threat to intellectual property protection, GxP compliance, and trial data security.

## 2. Decision Drivers & Constraints
* **Driver 1:** Restrict database access and isolate database host port exposures from deployment configurations.
* **Driver 2:** Secure site-scoped delegation checks by stripping spoofed delegation and target site headers at the gateway.
* **Driver 3:** Establish a clean, isolated facade microservice contract to serve as the exclusive interface for automated agent workflows without copying or re-implementing clinical algorithms.
* **Constraint 1:** Preserving all existing OIDC verification, signature validation, and short-lived replay prevention mechanism rules at the API gateway.

## 3. Options Considered
### Option 1: Direct Port Forwarding with Client-side Enforcement
* **Overview:** Keep database host port configurations open for testing but rely on developer agents and downstream code to check delegation headers.
* **Pros:**
  * ✅ Requires fewer infrastructure changes.
* **Cons:**
  * ❌ Highly vulnerable to delegation header spoofing.
  * ❌ Exposes database port boundaries to the host.

### Option 2: Gateway Header Stripping and Isolated Facade Service
* **Overview:** Remove database port exposures from docker-compose deployments, strip all incoming client delegation headers at the gateway, and introduce an isolated Agent Facade service that delegates clinical operations downstream via secure gateway signature propagation.
* **Pros:**
  * ✅ Zero direct database port exposure to host interfaces.
  * ✅ Prevents delegation claim spoofing.
  * ✅ Clean API schema isolation for automated agent interactions.
* **Cons:**
  * ❌ Minimal network overhead for proxying downstream requests.

## 4. Decision Outcome
* **Chosen Option:** Option 2
* **Justification:** Option 2 completely closes the delegation security gaps by removing raw database connection availability from agents and stripping client-provided headers at the gateway, ensuring a robust zero-trust boundary.

## 5. Consequences & Trade-offs
* **Positive Impact:** Database ports are fully isolated; client-supplied delegation headers are stripped; automated agents interact with clean schemas.
* **Negative Impact / Technical Debt:** Additional routing rules in the gateway.
* **Mitigation Strategy:** Managed via automated gateway integration tests.

## 6. Implementation & Verification
* **Affected Repositories / Services:** `apps/gateway`, `docker`, and a new `apps/agent_facade` microservice.
* **Verification Plan:** Validated via automated unit and integration tests (`tests/test_gateway.py` and `tests/test_agent_facade.py`).

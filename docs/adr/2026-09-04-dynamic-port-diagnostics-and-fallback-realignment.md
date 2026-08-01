# ADR-146: Dynamic Port Diagnostics and Fallback Realignment

* **Status:** Accepted
* **Date:** 2026-09-04
* **Authors:** @jules
* **Deciders:** @engineering-lead, @quality-officer

---

## 1. Context & Problem Statement
Configuration drift between our active Docker orchestrator setup, local validation scripts, and onboarding documentation triggers false-positive checks and setup friction. Concurrently, misaligned fallback defaults in our API gateway and security packages route traffic incorrectly when developers run services natively. We need to dynamically derive port definitions from our orchestrator configuration, align codebase routing fallbacks to match orchestration host ports, and resolve existing collisions between CTMS and Quality service ports.

This change is aligned with and traces back to **PRD-SYS-001**.

## 2. Decision Drivers & Constraints
* **Driver 1:** Eliminate false-positive port validation alerts.
* **Driver 2:** 100% of local service checks accurately reflect real availability.
* **Driver 3:** Ensure developers can seamlessly debug services natively alongside Docker layers.

## 3. Options Considered
### Option 1: Static Configuration Mapping
* **Overview:** Manually update all static port checklists, gateway fallbacks, and shared library defaults.
* **Pros:**
  * Simple, no code execution needed for parsing.
* **Cons:**
  * ❌ Does not resolve future drift or configuration changes dynamically.

### Option 2: Dynamic YAML Parsing and Fallback Realignment - Selected
* **Overview:** Dynamically parse `docker/docker-compose.yml` to extract host-to-container port mappings, override local checks with those values, and align default gateway routing/shared client fallbacks.
* **Pros:**
  * ✅ Eliminates drift permanently and dynamically.
  * ✅ Zero manual updates required for port verification when orchestration configurations change.
* **Cons:**
  * ❌ Slightly more complex parsing implementation.

## 4. Decision Outcome
* **Chosen Option:** Option 2
* **Justification:** Option 2 solves the configuration drift permanently by directly reading the source of truth (`docker-compose.yml`) for host-to-container port mappings.

## 5. Consequences & Trade-offs
* **Positive Impact:** Port verification correctly validates exact host ports dynamically. Gateway routing and fallback parameters route natively executed microservices correctly.
* **Negative Impact / Technical Debt:** None identified.
* **Mitigation Strategy:** Continue to monitor CI check runs.

## 6. Implementation & Verification
* **Affected Repositories / Services:** API Gateway (`apps/gateway`), Security Shared Package (`packages/security`), and local verification tools.
* **Verification Plan:** Verify using unit tests and local CLI check commands.

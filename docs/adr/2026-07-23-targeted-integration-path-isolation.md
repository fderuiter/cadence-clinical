# ADR 2026-07-23: Targeted Integration with Path Isolation

## Status
Accepted (Signed off by Lead Clinical Architect)

## Context
Modern stateless security layers (OAuth2/OIDC token-based) need to coexist with legacy stateful session structures (JVM memory/servlet session filters) without risk of broken security boundaries or routing regressions. The central API gateway serves as the entry point, and there is a critical need for clear isolation rules to prevent fragile legacy session wrappers from interfering with modern routes.

## Decision
1. **Routing Path Isolation Boundaries:**
   - Define exact URI patterns to isolate routing:
     - **Modern Stateless Services (e.g., Designer, Execution):** Served under `/api/v2/*` and `/modern/*`. These routes completely bypass legacy JVM/Java session filters and are strictly stateless.
     - **Legacy Stateful Application Paths:** Served under `/api/v1/*` and `/legacy/*`. These utilize stateful session memory and legacy Java servlet session beans.

2. **Identity Header Propagation Flow:**
   - Define the exact propagation headers from the Central API Gateway:
     - `X-User-Id`: Extracted from OIDC token's `sub` claim.
     - `X-User-Roles`: Extracted from OIDC token's `realm_access` claim.
     - `X-Gateway-Timestamp`: Gateway-generated timestamp to prevent replay attacks.
     - `X-Gateway-Signature`: Cryptographic signature (`HMAC-SHA256`) of the headers/timestamp generated with `GATEWAY_SECRET` to prevent header spoofing.

3. **No Mixed State Isolation Policy:**
   - Forbid mixing of legacy Java session memory (stateful session lifecycles) with modern stateless routes. Keep them strictly isolated at the API gateway router layer.

## Alternatives Considered
- **Bridging Token Claims Directly into Legacy Session Memory:** Rejected because it introduces tight coupling, complex cache synchronization, high risk of session-exhaustion attacks, and violates modern stateless microservice principles.
- **Dual OIDC Validation at Downstream and Legacy Services:** Rejected because duplicating token validation/JWKS fetching adds latency and breaks legacy compatibility constraints.

## Trade-offs
- **Positive Consequences (Benefits):**
  - **Security Boundary Reinforcement:** Complete physical path-level isolation ensures no session hijacking or privilege escalation across legacy/modern boundaries.
  - **Maintainability:** Modern services remain lightweight and independent of legacy JVM servlet containers.
  - **Backward Compatibility:** Legacy session lifecycles remain completely untouched and stable.
- **Negative Consequences (Risks/Costs):**
  - **Routing Redundancy:** Downstream integration clients must manage two distinct sets of auth expectations depending on the path being accessed.
  - **Routing Rules Complexity:** API Gateway configuration must be carefully managed to prevent path routing leaks.

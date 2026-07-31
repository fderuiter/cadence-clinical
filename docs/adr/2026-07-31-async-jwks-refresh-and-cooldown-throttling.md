# ADR-121: Async JWKS Refresh and Cooldown Throttling

* **Status:** Accepted
* **Date:** 2026-07-31
* **Authors:** @google-labs-jules
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Previously, the API Gateway statically cached JWKS keys from Keycloak at application launch. This caused complete authentication outages whenever Keycloak rotated its signing keys, making cached keys obsolete, or whenever Keycloak was temporarily offline during gateway startup. 
These scenarios required manual administrator intervention and gateway restarts to recover. 

Transitioning to an asynchronous, on-demand JWKS refresh model allows the gateway to self-heal seamlessly during key rotation events or recovery from offline startup conditions. This addresses requirement PRD-UNI-001.

## 2. Decision Drivers & Constraints

* Keycloak key rotation must not cause gateway auth downtime.
* Temporary Keycloak offline states during startup must not cause permanent failure of the gateway.
* High concurrent traffic of unrecognized key IDs must not overwhelm Keycloak with duplicate fetches (no cache stampede).
* Malicious actors must be prevented from overloading Keycloak with invalid key IDs via a strict cooldown throttling mechanism.
* GxP compliance and system stability constraint (PRD-UNI-001).

## 3. Options Considered

1. **Option A (Selected)**: Implement async token verification with an asynchronous lock (`jwks_fetch_lock`) for fetch operations, combined with a 5-minute cooldown throttle (`COOLDOWN_DURATION = 300.0`) and strict 5.0-second HTTP timeouts.
2. **Option B**: Block the gateway event loop with a synchronous network fetch on demand when an unknown key ID is encountered.

## 4. Decision Outcome

Chosen option: Option A because it ensures the API gateway does not block, provides excellent concurrency safety, avoids cache stampedes via lock serialization, and preserves identity provider rate limiting using the cooldown throttle pattern, fully satisfying PRD-UNI-001.

## 5. Consequences & Trade-offs

* Positive: Zero gateway auth downtime during key rotation, self-healing recovery after offline startups, and robust concurrency safety.
* Negative: A minor increase in code complexity inside the API gateway token verification path.

## 6. Implementation & Verification

* Target files/packages modified: `apps/gateway/main.py`.
* Verification tests added under `tests/test_gateway.py` covering seamless key rotation, cooldown throttling, offline startup resiliency, concurrent fetch prevention, and fetch timeouts.

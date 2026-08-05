# ADR-111: eISF Gateway Integration, Docker-Compose Wiring, and eTMF Sync Topology

- **Status:** Accepted
- **Date:** 2026-08-26
- **Authors:** @fderuiter
- **Deciders:** @engineering_leads, @qa_lead

---

## 1. Context & Problem Statement

The electronic Investigator Site File (eISF) service (`apps/eisf`) has been successfully scaffolded and tested, but was not yet fully integrated into the platform runtime, gateway proxy routing, or Docker-Compose network configuration. To ensure the microservice is reachable through standard client flows and dynamically aggregated into the unified OpenAPI schema, it must be integrated with the Central API Gateway (`apps/gateway`) on host port `8010`.

Additionally, the architectural boundaries and synchronization mechanism between eISF (the site-level archive) and eTMF (the sponsor-level master archive) need clear topological definition, establishing trust boundaries and security controls.

_Scope Note:_ This ADR specifically covers gateway/compose topology, network ports, and the service-to-service synchronization topology. [ADR-065](2026-08-09-eisf-maintenance-and-formatting.md) remains the authoritative decision record for eISF-local browse, view, downloads, RBAC, and binder completeness workflows.

This decision implements requirements under **Trace-16**.

## 2. Decision Drivers & Constraints

- **Single Port Rule:** The eISF service must reside on a unique, non-conflicting port (`8010`) to prevent collisions (such as the 8005/8007 CTMS/Quality collision pattern). Since the Organization Directory service (`apps/org`) previously bound to port `8010` on the host, we shifted the `org` host and container mappings to `8012`, avoiding overlapping configurations, and we also mapped eConsent to port `8011` on both host and container.
- **Site Isolation Integrity:** The gateway-routed eISF endpoints must inherit and validate Keycloak JWT claims, propagating gateway-signed site claims (`X-Site-Id`, `X-Gateway-Signature`) downstream unchanged so that `enforce_site_isolation` centrally rejects cross-site violations.
- **Sync Reliability & Decoupling:** Site-level documents must flow to the eTMF securely without introducing circular dependency loops or exposing raw Protected Health Information (PHI).
- **Standards & Precedents:** Retaining service-to-service signed handoffs aligns with established sync paradigms used in eCOA/interop and the eTMF inbound-email webhooks.

## 3. Options Considered

### Option 1: Gateway-Routed Sync Proxying

- **Overview:** Route all synchronization payloads through the public API Gateway endpoints, executing additional JWT validations on the synchronization loop.
- **Pros:**
  - ✅ Reuses existing gateway routes.
- **Cons:**
  - ❌ Adds routing hops and latency.
  - ❌ Exposes synchronization-specific interfaces to the public ingress space unnecessarily.

### Option 2: Direct Signed Service-to-Service Sync Topology

- **Overview:** Use the gateway-signed service token convention to propagate synced documents directly from eISF to eTMF using downstream URLs, completely bypassing the gateway for internal worker flows.
- **Pros:**
  - ✅ Eliminates unnecessary gateway proxy overhead and public endpoint exposure.
  - ✅ Simplifies transactional integrity and exception logging in the synchronization code path.
  - ✅ Robust precedent: Aligns with interop/eCOA synchronization and the eTMF inbound-email webhook topology.
- **Cons:**
  - ❌ Requires configuring microservice URLs (`ETMF_URL`) directly in the downstream environments, which is already standard.

## 4. Decision Outcome

- **Chosen Option:** Option 2 (Direct Signed Service-to-Service Sync Topology)
- **Justification:** Choosing Option 2 preserves network simplicity, keeps sync channels internal, and ensures that sync processes are highly decoupled from public ingress routing.

### Integrated Port Topology & Host Resolution

- **eISF Service Port:** Bound to container port `8010` and exposed on the host as `"8010:8010"` to ensure direct local accessibility.
- **Org Service Port:** Shifted on the host and container to `"8012:8012"` to completely eliminate port conflicts on local developer boxes, aligning with the clean port assignments across the orchestration ecosystem.
- **Gateway SERVICES Entry:** `"eisf": os.getenv("EISF_URL", "http://localhost:8010")`.

## 5. Consequences & Trade-offs

- **Positive Impact:** Preserves the end-to-end site-isolation security model. Since the gateway strips client-supplied scope headers and generates canonical signatures solely from validated OIDC tokens, downstream services can fully trust the injected site boundaries.
- **Open Constraint #343 (eTMF Deduplication):** The receiving-side contract for synchronized document deduplication and site-aware expected document list checking in eTMF remains open and pending. The eISF-local side is fully implemented, verified, and ready.
- **Open Constraint #693 (Redacted Derivative Sync):** Bidirectional sync propagation is constrained solely to redacted-derivative documents. Raw unredacted source documents (retaining PHI) are strictly barred from crossing boundaries onto the eTMF sync pathway.

## 6. Implementation & Verification

- **Affected Repositories / Services:**
  - `apps/gateway` (SERVICES, proxy routing, OpenAPI spec aggregation)
  - `apps/eisf` (main API and direct `propagate_to_etmf` sync handoff)
  - `docker/docker-compose.yml` (eisf service block, org port mapping shift, depends_on)
  - `packages/security` (shared gateway signing)
- **Verification Plan:**
  - Unit and contract verification executed under `tests/test_eisf_sync.py` and `tests/test_eisf_api.py`.
  - Integration path, header propagation, and site claim extraction verified under `tests/test_gateway.py` (specifically `test_eisf_gateway_site_isolation_propagation` and `test_gateway_proxy_eisf_headers_propagation`).

# ADR-060: eCOA Portal and Interop Deployment Architecture

* **Status:** Accepted
* **Date:** 2026-08-08
* **Authors:** @jules
* **Deciders:** @fderuiter, @architect-lead

---

## 1. Context & Problem Statement
With the introduction of the electronic Clinical Outcome Assessment (eCOA) and patient-reported outcomes (ePRO) capabilities, the platform needs a robust deployment configuration and system-level boundaries. Specifically:
1. The **eCOA/ePRO patient interface** needs to be highly resilient, supporting offline operation for participants in remote areas or with intermittent network connectivity.
2. The **interoperability backend (`apps/interop/`)** requires integration into containerized deployments with unified database setup and reliable communication pathways.
3. Strict **regulatory security boundaries** must be established to ensure that trial participants (under the `Subject` role) are authorized only to access designated ePRO submission and sync routes, preventing access to sponsor/investigator resources or raw clinical databases.
4. **Lightweight patient notifications and compliance reminders** need to be calculated based on the subject's eCOA assignment schedule, without introducing heavyweight polling overhead.

This decision implements requirements under Trace-8.

## 2. Decision Drivers & Constraints
* **Driver 1 (Regulatory Security & Role Boundaries):** Under FDA 21 CFR Part 11 and GCP guidelines, patient identities must be strictly isolated. Users with the `Subject` role should have zero visibility into other subjects' records or staff-scoped clinical operations.
* **Driver 2 (Offline Resilience & PWA Support):** Patients must be able to log diary entries offline (e.g., in flight or in remote clinics). These entries must be queued locally and reconciled reliably upon reconnection without silent data discarding.
* **Driver 3 (Deployment Simplicity):** Containerized environments must orchestrate the Gateway, the Interop service, and the Subject Portal seamlessly using standard service discovery.
* **Driver 4 (Notification Scalability):** Patient compliance depends on timely reminders. The platform must compute overdue or upcoming assignments and queue corresponding email, SMS, or in-app alerts.

## 3. Options Considered

### Option 1: Monolithic Deployment with Shared Web Portal
Integrate patient portal UI features into the existing sponsor/investigator web client (`apps/web`), and handle interop queries inside the primary execution database.
* **Pros:**
  * ✅ Avoids adding new services to `docker-compose.yml` and routing rules to the Gateway.
* **Cons:**
  * ❌ Severe risk of authorization bypass; exposing investigator-scoped frontend bundles to patients is a critical GxP vulnerability.
  * ❌ Sub-optimal bundle sizes for offline PWA rendering, degrading mobile page-load performance.

### Option 2: Decoupled Portal and Interop Gateway Service (Selected)
Deploy the eCOA Subject Portal (`apps/subject-portal`) as an independent PWA and run the Interop Gateway (`apps/interop/`) as a dedicated, containerized microservice behind the API Gateway.
* **Pros:**
  * ✅ Complete physical and logical segregation of patient-facing UI from investigator-scoped UI.
  * ✅ Granular, gateway-enforced role boundary that drops non-ePRO routes for `Subject` JWTs.
  * ✅ Optimized mobile performance and offline-first PWA caching (IndexedDB local queues and service workers).
  * ✅ Self-contained, lightweight notification compute loop using async background tasks.
* **Cons:**
  * ❌ Requires managing two additional services in the deployment configuration (`docker-compose.yml`).

---

## 4. Decision Outcome
**Chosen Option:** Option 2
We chose Option 2 to enforce strict security boundaries, maintain compliance with Part 11 and GCP, and provide an offline-resilient user experience tailored to mobile device performance.

### Rationale
* **Interop Deployment Extension:** The `apps/interop` service runs as a dedicated Python FastAPI backend on port `8004`, persisting ePRO submissions, instruments, and notifications in a site-isolated SQLite file-based database (`interop.db`). It links with the Postgres service for health check sequencing.
* **Separate Portal Deployment:** The `apps/subject-portal` is deployed as a standalone Node-based client on port `5174`, utilizing Vite for hot-reloading and PWA service-worker routing.
* **Subject Authorization:** The API Gateway (`apps/gateway/main.py`) acts as the gatekeeper. Users carrying the Keycloak role `Subject` are blocked with an HTTP 403 Forbidden on any path other than `/api/v1/interop/epro/submit` and `/api/v1/interop/epro/sync`. Downstream, the `verify_subject_identity` helper verifies that the payload subject ID matches the OIDC token sub claim.
* **Lightweight Notifications:** Instead of relying on external enterprise scheduler complexity, a lightweight computed reminder endpoint (`POST /api/v1/interop/reminders/compute`) calculates upcoming or overdue entries by reconciling `SubjectAssignment` intervals against `EPROSubmission` logs, dispatching notification records (EMAIL, SMS, WEBHOOK, IN_APP) via background workers.

---

## 5. Consequences & Trade-offs
* **Positive Impact:**
  * Zero exposure of clinical administrative routes to trial participants.
  * Patients can successfully complete clinical assessments offline and sync later.
  * Simplified containerized setup with single-command startup.
* **Negative Impact:**
  * Requires explicit maintenance of independent network routing configurations.
* **Mitigation Strategy:**
  * Standardize environment variables (`INTEROP_URL`) and automate routing assertions inside integration tests.

---

## 6. Implementation & Verification
* **Affected Repositories / Services:**
  - `docker/docker-compose.yml` (registered services, ports, database URLs)
  - `apps/gateway/main.py` (Centralized routing and `Subject` role verification)
  - `apps/interop/main.py` (Conflict resolution, bulk sync, compliance compute, and reminders)
* **Verification Plan:**
  - Verify that `docker compose up -d` starts `cadence-interop` and `cadence-subject-portal` containers cleanly.
  - Run `uv run pytest` to execute backend integration tests covering Subject role restrictions and sync reconciliations.
  - Run `pnpm check` to verify full workspace formatting and linting.

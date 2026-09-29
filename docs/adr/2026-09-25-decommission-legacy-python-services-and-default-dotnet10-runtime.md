# ADR-2443: Decommission Legacy Python Compose Services and Set .NET 10 as Default Runtime

Also known as: ADR-2240 (former number)

- **Status:** Accepted
- **Date:** 2026-09-25
- **Requirements:** PRD-SYS-053
- **Authors:** @fderuiter
- **Deciders:** @clinical-platform/architecture

---

## 1. Context & Problem Statement

Container orchestration previously relied on legacy Python 3.14 images and uv package installers for microservices in `docker-compose.yml`. Developers had to build heavy Python images during local container execution even though .NET 10 microservices existed. To complete the migration to .NET 10 and optimize local container startup, all legacy Python container definitions and Dockerfiles must be decommissioned.

## 2. Decision Drivers & Constraints

- **Driver 1:** Remove all legacy Python container image dependencies and speed up local container startup.
- **Driver 2:** Ensure all microservices run as .NET 10 container services by default without explicit profile flags.
- **Driver 3:** Maintain cross-service communication ports and environment variable configurations.

## 3. Options Considered

### Option 1: Retain Dual-Stack Python and .NET Compose Definitions

- **Overview:** Keep Python container definitions under `--profile python` alongside .NET containers.
- **Pros:**
  - ✅ Keeps legacy fallback available in compose.
- **Cons:**
  - ❌ Leaves unused Python Dockerfiles and compose overhead in the repository.

### Option 2: Remove Legacy Python Compose Services and Set .NET 10 as Default (Selected)

- **Overview:** Remove all legacy Python microservice definitions and Dockerfile references, promote .NET 10 microservice definitions to default service keys (`gateway`, `designer`, `execution`, etc.), and remove `profiles:` flags.
- **Pros:**
  - ✅ Reduces build times and local resource usage.
  - ✅ Eliminates Python base image downloads during `docker compose up`.
  - ✅ Ensures single-stack .NET 10 runtime consistency.
- **Cons:**
  - ❌ Removes local Python container execution option (now fully transitioned to .NET 10).

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Option 2 completes the clean-break transition to .NET 10, simplifies local container orchestration, and optimizes developer inner-loop velocity.

## 5. Consequences & Trade-offs

- **Positive Impact:** Local container startup time decreases due to single-stack image caching; zero Python base container image downloads occur during `docker compose up`.
- **Negative Impact / Technical Debt:** Python backend microservice local container execution is fully retired.
- **Mitigation Strategy:** All backend business logic is fully transcribed and verified in .NET 10 ASP.NET Core minimal APIs.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `docker/docker-compose.yml`, `docker/Dockerfile*`, `apps/*/Dockerfile`
- **Verification Plan:** Verified via `dotnet cadence check`, `validate_architecture_drift.py`, and `docker-compose.yml` structural validation.

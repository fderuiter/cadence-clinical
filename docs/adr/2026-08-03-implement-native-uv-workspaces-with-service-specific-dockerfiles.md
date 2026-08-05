# ADR-255: Implement native uv workspaces with service-specific Dockerfiles

- **Status:** Accepted
- **Date:** 2026-08-03
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

We need to transition the monorepo architecture of our Python services to use native `uv` workspaces. Previously, services and shared packages were managed with individual configurations or virtual environments, which made building service-specific Docker containers complex and led to inconsistent environment isolation in development and CI/CD. The transition to `uv` workspaces will align our dependency management with native workspace structures while allowing optimized, service-specific Dockerfiles.

## 2. Decision Drivers & Constraints

- Ensure fast, deterministic, and isolated builds for each microservice.
- Reduce Docker image sizes by leveraging multi-stage builds and `uv`'s workspace-aware caching mechanisms.
- Guarantee strict GxP boundary verification (PRD-SYS-001) by ensuring correct dependency mapping.

## 3. Options Considered

1. **Option A (Selected): Native `uv` workspaces with multi-stage, service-specific Dockerfiles.**
2. **Option B: Monolithic Docker image containing all services and packages.**

## 4. Decision Outcome

Chosen option: **Option A** because it allows each service (e.g. apps/designer, apps/execution) to have its own lightweight, production-ready container built precisely with its dependencies, while keeping the entire codebase under a unified workspace concept in the monorepo. This optimizes both development velocity and GxP isolation boundaries.

## 5. Consequences & Trade-offs

- Positive: Extremely fast development environment setup with `uv sync --all-extras`.
- Positive: Tiny, optimized service Docker containers.
- Negative: Slightly more verbose configuration across `pyproject.toml` files in workspaces.

## 6. Implementation & Verification

- Target files/packages modified: `pyproject.toml`, individual app/package `pyproject.toml` files, Dockerfiles, and security configurations.
- Verification: Validated with full workspace tests and ADR/RTM schema validation runs.

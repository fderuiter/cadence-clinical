# ADR-2159: Centralize Duplicate API Clients into Shared Package

- **Status:** Accepted
- **Date:** 2026-08-05
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Previously, developers had to manually write and maintain duplicate service-to-service API client wrappers across several backend microservices (`execution`, `interop`, `tickets`, and `etmf`). This led to:

- Code duplication and configuration drift.
- Divergent signature implementations.
- Inconsistent return/parsing structures.

We need to centralize these API clients into a shared monorepo package (`packages/security`) to enforce uniform HMAC context propagation and identity credential rules at the gateway level. Furthermore, enabling high-performance asynchronous connection pooling ensures we consistently respect our internal 100ms SLA without relying on heavy runtime code-generation tools. This decision traces back to requirement **PRD-SYS-001** (Standard Audit Logging & Regulated Security Integration).

## 2. Decision Drivers & Constraints

- Eliminate redundant client wrapper implementations across microservices.
- Respect the internal high-performance 100ms service-to-service SLA under GxP.
- Support consistent HMAC context validation and identity credential passing at the gateway layer (**PRD-SYS-001**).

## 3. Options Considered

1. **Option A (Selected): Centralized Shared Clients (`packages/security`)**: Move all shared service-to-service client classes (`NotificationClient`, `DesignerCriteriaClient`) under the centralized package `packages/security` inheriting from `GatewayBaseClient`. Use standard connection-pooling helpers with `httpx.AsyncClient`.
2. **Option B (Alternative): Keep Independent Microservice Clients**: Keep microservice client copies but write manual synchronization checks/linters to ensure they don't drift. This is highly error-prone and adds significant developer overhead.

## 4. Decision Outcome

Chosen option: **Option A** because centralizing duplicate API clients into `packages/security` avoids redundancy, guarantees unified security headers/HMAC signing via `GatewayBaseClient`, and simplifies client consumption across `apps/execution`, `apps/interop`, `apps/tickets`, and `apps/etmf` while enforcing the 100ms performance SLA via optional pooled `AsyncClient` references.

## 5. Consequences & Trade-offs

- **Positive:** Complete elimination of duplicate client wrappers, uniform HMAC propagation, guaranteed alignment with GatewayAuthMiddleware, and simplified maintenance.
- **Negative:** Slight coupling of microservices to the shared `packages/security` module, which is fully acceptable within our monorepo boundaries.

## 6. Implementation & Verification

- **Centralized API clients** in `packages/security/__init__.py`, `packages/security/designer_client.py`, and `packages/security/notifications_client.py`.
- **Refactored callers** in `apps/execution/designer_client.py`, `apps/execution/notifications_client.py`, `apps/interop/designer_client.py`, `apps/tickets/notifications_client.py`, and `apps/etmf/notifications_client.py` to use the shared implementation.
- **Verified** via existing and updated unit/integration test suites to ensure zero regression.

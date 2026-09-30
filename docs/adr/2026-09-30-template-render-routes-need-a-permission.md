# ADR-2511: Template Render Routes Need a Permission

- **Status:** Accepted
- **Date:** 2026-09-30
- **Authors:** @fderuiter, Claude Code
- **Deciders:** @fderuiter
- **Requirements:** PRD-CRF-009

---

## 1. Context & Problem Statement

Three services render documents from caller-supplied data:

- Safety renders an ICSR (E2B XML) from `POST /api/v1/safety/icsr/render`.
- Notifications renders a notification body from `POST /api/v1/notifications/render`.
- CTMS renders a monitoring letter from `POST /api/v1/ctms/letters/render`.

Each route admitted any signed-in caller (#4960). A participant or auditor could produce a document that looks like
an official safety report, sponsor notification or monitoring letter. The permission matrix had no permission for
safety reports or notifications, so [ADR-2510](2026-09-30-endpoint-permissions-from-the-permission-matrix.md) could
not be applied to the first two routes as it stood.

## 2. Decision Drivers & Constraints

- **Least privilege:** only the roles that produce each document may render it.
- **One source of grants:** `PermissionMatrix`, through `RequirePermission` (ADR-2510).
- **No new roles:** grant the new permissions to existing roles.

## 3. Options Considered

### Option 1: Reuse an existing permission for all three routes

- **Cons:**
  - ❌ No existing permission matches safety reporting or notifications, so any choice grants the wrong roles.

### Option 2: Add `safety_report` and `notification` permissions, reuse `ctms_monitoring_visit:create` (Selected)

- **Overview:**
  - `safety_report:create` (and `read`) goes to sysadmin and the sponsor medical monitor, who own safety reporting.
  - `notification:send` goes to sysadmin, the sponsor data manager, the sponsor medical monitor, the CRA and the
    monitor, the roles that send study communications.
  - The monitoring-letter route uses `ctms_monitoring_visit:create`, which sysadmin, the sponsor data manager and the
    CRA already hold.
- **Pros:**
  - ✅ Each route states the permission it needs, and other callers get `403`.
- **Cons:**
  - ❌ Two new permission names to keep in step with the Trust engine's grants (#4781).

## 4. Decision Outcome

- **Chosen Option:** Option 2.
- **Justification:** It closes the gap without new roles, and each grant follows who produces the document.

## 5. Consequences & Trade-offs

- **Positive Impact:**
  - Site staff, participants and auditors can no longer render safety reports, notifications or monitoring letters.
- **Negative Impact / Technical Debt:**
  - Callers outside the listed roles that rendered these documents before now get `403`.

## 6. Implementation & Verification

- **Affected code:**
  - `packages/security/src/Rbac/PermissionMatrix.cs`
  - `apps/safety/src/Endpoints/IcsrTemplateEndpoints.cs`
  - `apps/notifications/src/Endpoints/NotificationTemplateEndpoints.cs`
  - `apps/ctms/src/Endpoints/LetterTemplateEndpoints.cs`
- **Verification:** `tests/Cadence.UnitTests/Security/RenderRouteAuthorityTests.cs` hosts each service and checks that
  holders of the permission get `200` and other signed callers get `403`.

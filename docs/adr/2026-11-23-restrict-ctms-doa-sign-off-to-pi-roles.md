# ADR-2648: Restrict CTMS Delegation of Authority Sign-Off Permissions to Principal Investigator Roles

- **Status:** Accepted
- **Date:** 2026-11-23
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-SYS-192

---

## 1. Context & Problem Statement

Under ICH E6(R3) Good Clinical Practice, the Principal Investigator (PI) retains personal accountability for endorsing site Delegation of Authority (DOA) logs; sub-investigators are delegated clinical staff and must not sign off on site delegation assignments. While `PermissionMatrix.cs` already restricted the Knowledge module's `delegation_log:sign` permission to `piBase` (`principal_investigator`, `authorized_er_physician`, and `lead_investigator`), `PermissionMatrix.Ctms.cs` granted `ctms_doa:sign_off` and `ctms_doa:sign` directly to `RoleNormalization.ROLE_INVESTIGATOR` inside `AddCtmsGrants()` before `piBase` was cloned.

Because `RoleNormalization.NormalizeRole` maps `sub_investigator`, `sub investigator`, and `sub-investigator` to `ROLE_INVESTIGATOR`, any sub-investigator inherited `ctms_doa:sign_off` and passed `.RequirePermission("ctms_doa:sign_off")` on `POST /api/v1/ctms/doa/sign-off`, allowing a sub-investigator to endorse DOA records and emit `DOA_PI_SIGN_OFF` audit entries.

## 2. Decision Drivers & Constraints

- **ICH E6(R3) Separation of Duties (`PRD-SYS-192`):** Only `sysadmin` and Principal Investigator roles (`principal_investigator`, `authorized_er_physician`, `lead_investigator`) may hold `ctms_doa:sign_off` and `ctms_doa:sign`. Sub-investigators and base `investigator` callers must receive HTTP `403 Forbidden` on `POST /api/v1/ctms/doa/sign-off`.
- **Preserve Operational DOA Access for Site Staff:** Non-signing `ctms_doa` permissions (`read`, `assign`, `create`, `revoke`, `update`, `export`) must remain intact for `ROLE_INVESTIGATOR` and existing operational roles.
- **Respect `PermissionMatrix` Initialization Order & Contract Sync:** `PermissionMatrix.cs` overwrites the PI role buckets with `ClonePermissions(piBase)` after `AddModuleGrants()`, and `apps/web/scripts/generate-canvas-contracts.js` parses `PermissionMatrix*.cs` to generate `apps/web/src/canvas/generated/permissions.ts`.

## 3. Options Considered

### Option 1: Grant `ctms_doa` Sign-Off via `piBase` Union in `PermissionMatrix.cs` and `ROLE_SYSADMIN` in `PermissionMatrix.Ctms.cs` (Selected)

- **Overview:** Narrow the `ctms_doa` `"sign_off"` and `"sign"` grant in `PermissionMatrix.Ctms.cs` to `RoleNormalization.ROLE_SYSADMIN` only, union `"sign_off"` and `"sign"` into `piBase["ctms_doa"]` in `PermissionMatrix.cs` alongside `piBase["delegation_log"]`, and teach `generate-canvas-contracts.js` to apply `piBase["..."].UnionWith([...])` mutations when generating `apps/web/src/canvas/generated/permissions.ts`.
- **Pros:**
  - Cleanly aligns CTMS DOA sign-off with the existing `delegation_log:sign` PI boundary (`#5311`) without introducing new role buckets or stripping non-signing `ctms_doa` actions from `ROLE_INVESTIGATOR` or `piBase`.
  - Keeps backend C# RBAC enforcement and generated frontend TypeScript `ROLE_PERMISSIONS` / `PERMISSION_ROLES` contracts in exact lockstep.
- **Cons:**
  - Requires extending `parsePermissionMatrix()` in `apps/web/scripts/generate-canvas-contracts.js` to recognize `.UnionWith([...])` on `piBase` entries.

### Option 2: Split `sub_investigator` into a Separate Canonical Role Bucket

- **Overview:** Change `RoleNormalization.NormalizeRole` so `sub_investigator` no longer normalizes to `ROLE_INVESTIGATOR`.
- **Pros:**
  - Distinguishes sub-investigators from generic `investigator` claims globally.
- **Cons:**
  - Broadly alters role normalization across every clinical module (`execution`, `safety`, `econsent`, `imaging`, `quality`) where sub-investigators intentionally share clinical investigator permissions, and still leaves raw `investigator` holding PI-only sign-off authority.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Granting `ctms_doa:sign_off` and `ctms_doa:sign` exclusively to `ROLE_SYSADMIN` in `PermissionMatrix.Ctms.cs` and unioning them onto `piBase["ctms_doa"]` in `PermissionMatrix.cs` enforces PI-only DOA endorsement across both backend routes and generated web authority helpers while preserving existing `ROLE_INVESTIGATOR` operational grants.

## 5. Consequences & Trade-offs

- **Positive:** `sub_investigator` and base `investigator` callers are rejected fail-closed with HTTP `403 Forbidden` by `.RequirePermission("ctms_doa:sign_off")` on `POST /api/v1/ctms/doa/sign-off`, and `canSignOffDoa` in `apps/web` disables DOA sign-off controls for non-PI investigators.
- **Negative / Mitigations:** Existing unit test fixtures that invoked DOA sign-off as `ROLE_INVESTIGATOR` are updated to `ROLE_PRINCIPAL_INVESTIGATOR` to match the tightened RBAC boundary.

## 6. Implementation & Verification

- **Affected Components:**
  - `packages/security/src/Rbac/PermissionMatrix.Ctms.cs`
  - `packages/security/src/Rbac/PermissionMatrix.cs`
  - `apps/web/scripts/generate-canvas-contracts.js`
  - `apps/web/src/canvas/generated/permissions.ts`
  - `apps/web/src/canvas/authority/ctms.ts`
- **Verification:**
  - `tests/Cadence.UnitTests/Ctms/DoaEndpointsTests.cs` verifies `PermissionMatrix.HasPermission` across PI, sub-investigator, investigator, and sysadmin roles, and exercises `WebApplicationFactory<Cadence.Apps.Ctms.Program>` confirming `sub_investigator` and `investigator` receive HTTP `403 Forbidden` with `SignedOff == false` while `principal_investigator` receives HTTP `200 OK` with `SignedOff == true`.
  - `apps/web/tests/canvas/contracts.spec.ts` verifies generated TypeScript permission parity and `canSignOffDoa` behavior.

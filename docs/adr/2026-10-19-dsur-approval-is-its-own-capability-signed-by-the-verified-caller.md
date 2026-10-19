# ADR-2518: DSUR Approval Is Its Own Capability, Signed by the Verified Caller

- **Status:** Accepted
- **Date:** 2026-10-19
- **Authors:** @fderuiter, Claude Code
- **Deciders:** @fderuiter
- **Requirements:** PRD-PVG-008 (physician e-signatures on safety packages), PRD-PVG-004 (locked periodic reports),
  PRD-SYS-001 (audit fields and entries)
- **Issues:** #5462
- **Amends:** [ADR-2591](2026-09-30-synchronous-in-process-dsur-compilation-and-cryptographic-signing-engine.md)
  (the approval path only)

---

## 1. Context & Problem Statement

[ADR-2591](2026-09-30-synchronous-in-process-dsur-compilation-and-cryptographic-signing-engine.md) made DSUR approval
apply a `GxPSigner` manifest and lock the record. Inspection (#5462) found that the approval was not an electronic
signature in the Part 11 sense:

- The signer came from `DsurApproveRequest.SignerUserId` in the body, else the caller, else the literal
  `"safety-physician"`; compile fell back to `"safety-officer"`.
- A missing body became an empty request, so approval needed no reason for change.
- Compile, approve, update, get and list all required one permission, `safety_report:create`, and the compiler could
  approve their own report.
- No re-authentication: there was no step-up signature token, and a `/periodic-reports/{id}/sign` alias did the same.
- The signed manifest was stored in `EsignatureManifestId varchar(255)`, which it never fits, so approval failed on
  PostgreSQL.

## 2. Decision Drivers & Constraints

- The authority to sign comes from the verified caller, never the request body (ADR-2515 §2, ADR-2516).
- Signing needs fresh re-authentication through the shared step-up token (`X-Sig-Token`, contract v3).
- Approval is a different authority from authoring (separation of duties, PRD-PVG-008).
- Pre-deployment: no compatibility for old clients or routes.

## 3. Options Considered

### Option 1: The shared step-up filter, a separate permission and a compiler check (Selected)

- **Overview:** the approve routes use `RequireElectronicSignature` from `Cadence.Security`, the same filter knowledge
  and eConsent use, bound to action `/api/v1/safety/dsur/{id}/approve` and semantic action
  `safety.periodic_report.approve`. They require `safety_report:approve` (`PermissionMatrix.Safety.cs`, sponsor medical
  monitor only). The handler signs as the verified `sub`, requires a reason for change, and refuses the report's
  compiler with 403.
- **Pros:**
  - ✅ Reuses the platform signing gate and single-use token store; no safety-specific token code.
  - ✅ The signer, approver and audit actor are the same verified identity.
- **Cons:**
  - ❌ The manifest is still `GxPSigner`'s, not execution's eCRF `SignatureManifest`, which lives in the execution module.

### Option 2: Move DSUR approval onto execution's form signature service

- **Rejected:** that service and its manifest are execution's own persistence; referencing them from safety breaks the
  service boundary (pattern 8), and the shared seam for other modules is the step-up filter.

## 4. Decision Outcome

- **Chosen Option:** Option 1.
- The body is `{ signingReason?, reasonForChange }`; `SignerUserId` is gone. Compile, update and approve answer 401
  without a verified caller; reads need `safety_report:read`. The `/periodic-reports/{id}/sign` alias is removed; the
  `/periodic-reports/{id}/approve` alias stays, bound to the same canonical token action.
- Compile, update and approve record `DSUR_COMPILED`, `DSUR_UPDATED` and `DSUR_APPROVED` through `AuditActions`; the
  safety host adds `GxPAuditInterceptor` (actor: the verified caller) through a new `configureOptions` hook on
  `AddCadencePostgresDbContext`.
- `periodic_safety_reports.EsignatureManifestId` becomes `text` (migration `WidenDsurSignatureManifest`).

## 5. Consequences & Trade-offs

- **Positive Impact:** a DSUR approval is attributable to one re-authenticated, authorised person who did not compile it,
  with a reason for change on the record and in the audit chain.
- **Negative Impact / Technical Debt:** safety keeps the process-local `ReplayCache`, so a token is single use within
  one safety process only (ADR-2515 §4.2), until safety folds into the single host (#5315).
- **Mitigation Strategy:** host tests on the real safety `Program` and a PostgreSQL integration test.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `apps/safety`, `packages/security` (`PermissionMatrix.Safety.cs`),
  `packages/database` (`AddCadencePostgresDbContext` options hook).
- **Verification Plan:** `tests/Cadence.UnitTests/Safety/` (`DsurCompilerEngineTests`, `DsurApprovalAuthorityTests`) and
  `tests/Cadence.IntegrationTests/Safety/DsurApprovalPostgresTests.cs`.

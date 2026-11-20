# ADR-2816: Caller-Bound Participant Self-Service Wallet and Receipt OCR Capabilities

- **Status:** Accepted
- **Date:** 2026-11-19
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Clinical trial participants using the Participant Companion Portal (`apps/subject-portal`) need live visibility into their study wallet balances, self-service receipt OCR reimbursement claims, on-demand balance payouts, and automatic protocol stipend credits upon synchronized ePRO diary completion (PRD-PAT-002, PRD-PAY-006, PRD-SYS-182). Previously, the `subject` role held no Payments permissions (`PermissionMatrix.Payments.cs`), forcing participant wallet views behind demo-only sandboxes and relying on synthetic fallback OCR values when real receipt parsing was absent. Granting broad site-staff wallet permissions (`payments_wallet:read`, `payments_wallet:disburse`, `payments_expense:submit`) to `subject` would violate least-privilege isolation by exposing multi-participant directory and coordinator endpoints.

## 2. Decision Drivers & Constraints

- **Least-Privilege Participant Isolation:** A `subject` caller must never enumerate other participants' wallets, read raw routing or card numbers, verify IRS Form W-9 certifications, or mutate another participant's ledger.
- **21 CFR Part 11 Data Integrity & Zero Fabrication:** Receipt OCR must extract merchant, date, category, and gross amount strictly from actual image/document bytes (`ReceiptOcrEngine`) and return `ocrReadable = false` with `confidenceScore = 0` on blank, blurry, or corrupted images rather than fabricating fallback values.
- **Server-Governed Compensation Rules:** Synchronized ePRO diary stipends must derive amounts exclusively from server-side `ParticipantStipendRule` records and reject client-supplied `amount` fields (`400 Bad Request`).
- **Clinical Kernel Boundary Compliance (K-XS-1):** Public DTOs in non-kernel assemblies must not introduce new raw `string StudyId` or `string SiteId` members.

## 3. Options Considered

### Option 1: Dedicated `payments_self_*` RBAC Capabilities Scoped to `/api/v1/payments/participant/me/*` (Selected)

- **Overview:** Introduce five narrow capabilities (`payments_self_wallet:read`, `payments_self_wallet:disburse`, `payments_self_expense:ocr_scan`, `payments_self_expense:submit`, `payments_self_stipend:credit`) granted to `subject` in `PermissionMatrix.Payments.cs`, paired with signed-caller `/api/v1/payments/participant/me/*` endpoints that resolve wallets exclusively via `caller.UserId` and mask all bank/card identifiers before serialization.
- **Pros:**
  - ✅ Enforces strict `403 Forbidden` isolation against cross-participant `subjectId`/`walletId` tampering and all coordinator/finance endpoints.
  - ✅ Preserves full 21 CFR Part 11 audit trails (`EXPENSE_CLAIM_AUTO_APPROVED`, `DIGITAL_DISBURSEMENT_DISPATCHED`, `PARTICIPANT_STIPEND_CREDITED`) and IRS `$600` Form W-9 tax gates.
  - ✅ Centralizes deterministic raster/vector receipt OCR in `ReceiptOcrEngine` for both participant self-service and site-staff workflows.
- **Cons:**
  - ❌ Adds dedicated self-service endpoint contracts alongside existing coordinator endpoints.

### Option 2: Grant Existing `payments_wallet:*` and `payments_expense:*` Permissions to `subject`

- **Overview:** Reuse existing staff wallet directory and disbursement endpoints with conditional role checks inside each handler.
- **Pros:**
  - ✅ Fewer route registrations in `PaymentEndpoints.cs`.
- **Cons:**
  - ❌ Broadens RBAC blast radius across multi-participant wallet listing and coordinator review routes, violating least-privilege defense-in-depth.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Dedicated `payments_self_*` capabilities enforced at both the RBAC permission gate and `ResolveOwnWalletAsync` (`caller.UserId` binding) guarantee that participants can manage only their own study wallet while keeping coordinator review, card issuance, W-9 verification, and site invoice operations completely inaccessible to `subject` tokens.

## 5. Consequences & Trade-offs

- **Positive Impact:** Participants gain real-time wallet balances, authentic receipt OCR with policy-backed `<= $100.00` auto-approval and direct ACH dispatch, on-demand balance payouts, and idempotent server-rule diary stipends outside demo mode.
- **Negative Impact / Technical Debt:** Legacy `ParticipantWallet` rows remain keyed by study/site protocol codes until full migration to Kernel `StudyId`/`SiteId` value types; self-service DTOs expose these as `StudyCode`/`SiteCode` mapped to JSON `studyId`/`siteId`.
- **Mitigation Strategy:** Verified via `KernelBoundaryTests`, `PaymentsCallerAuthorizationTests`, `ParticipantSelfServiceAndOcrTests`, and OpenAPI drift sentinels.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/security` (`PermissionMatrix.Payments.cs`), `apps/payments` (`ReceiptOcrEngine.cs`, `PaymentEndpoints.cs`), `apps/subject-portal`, and `apps/web` (`ParticipantWalletView.vue`).
- **Verification Plan:** Automated xUnit tests in `tests/Cadence.UnitTests/Payments/ParticipantSelfServiceAndOcrTests.cs`, `KernelBoundaryTests` in `tests/Cadence.ArchitectureTests/`, and Vitest suites in `apps/subject-portal` and `apps/web`.

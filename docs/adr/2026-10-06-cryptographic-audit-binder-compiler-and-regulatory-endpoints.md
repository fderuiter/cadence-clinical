# ADR-2810: Cryptographic Audit Binder Compiler and Regulatory Endpoints

- **Status:** Accepted
- **Date:** 2026-10-06
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Regulatory compliance under 21 CFR Part 11 and ICH GCP E6(R2/R3) requires regulatory submissions to provide immutable, verifiable audit trails and structured trial master file (TMF) documents. In trial management systems, assembling inspection-ready regulatory binders and verifying cryptographic audit chain integrity often requires manual collection and offline verification, introducing audit risk and potential compliance gaps.

This decision addresses the architecture and design of a 1-click cryptographic audit binder compiler and regulatory endpoints in `Cadence.Apps.Etmf` (PRD-SUB-001, PRD-SYS-053).

## 2. Decision Drivers & Constraints

- **Driver 1:** 21 CFR Part 11 electronic record and signature requirements (PRD-SUB-001).
- **Driver 2:** Cryptographic audit trail chain integrity with Merkle tree verification (PRD-SYS-053).
- **Driver 3:** OpenAPI schema synchronization and documentation parity.
- **Driver 4:** Performance and zero-drift maintainability in C# .NET 10 environment.

## 3. Options Considered

### Option 1: Native In-Memory Cryptographic Binder Compiler & Endpoint Group (Selected)

- **Overview:** Implement `AuditBinderCompiler` in `Cadence.Apps.Etmf.Domain` and register REST endpoints under `/api/v1/etmf` in `EtmfBinderEndpoints`.
- **Pros:**
  - ✅ Fully satisfies 21 CFR Part 11 and cryptographic audit chain requirements (PRD-SUB-001, PRD-SYS-053).
  - ✅ Compiles structured ZIP/PDF regulatory binders with SHA-256 Merkle root verification and electronic signature certificates.
  - ✅ Clean architectural separation between endpoint layer and compiler domain service.
- **Cons:**
  - ❌ Requires maintaining OpenAPI schemas and API documentation in lockstep.

### Option 2: Asynchronous Offline CLI Worker Tooling

- **Overview:** Compile regulatory binders outside the service boundary via external worker scripts.
- **Pros:**
  - ✅ Keeps web API service lightweight.
- **Cons:**
  - ❌ Fails real-time regulatory inspection readiness checks and 1-click binder generation expectations.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Option 1 provides seamless 1-click regulatory binder compilation and real-time cryptographic audit log verification within `Cadence.Apps.Etmf`, maintaining compliance with PRD-SUB-001 and PRD-SYS-053 while providing full OpenAPI contract parity.

## 5. Consequences & Trade-offs

- **Positive Impact:** End-to-end cryptographic audit trail verification, automated inspection readiness scoring, and standardized TMF binder generation.
- **Negative Impact / Technical Debt:** Requires ongoing synchronization of OpenAPI specs and documentation upon endpoint model changes.
- **Mitigation Strategy:** Automated Quality Sentinel Gates (Gate 15 OpenAPI staging and Gate 16 AST contract checks) in `pnpm check`.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `apps/etmf` (`Cadence.Apps.Etmf`)
- **Verification Plan:** Unit tests in `tests/Cadence.UnitTests/Etmf/AuditBinderCompilerTests.cs` and Quality Sentinel Gate 6 check.

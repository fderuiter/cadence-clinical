# ADR-2541: Consolidate GxPAuditInterceptor on Cadence.Database and Remove Obsolete Security Interceptor

- **Status:** Accepted
- **Date:** 2026-09-30
- **Authors:** @cadence-platform
- **Deciders:** Architecture Review Board

---

## 1. Context & Problem Statement

The Cadence platform requires strict 21 CFR Part 11 regulatory compliance and audit trail integrity (@req:PRD-SYS-053). Previously, two duplicate implementations of `GxPAuditInterceptor` existed in the codebase: `Cadence.Database.GxPAuditInterceptor` and `Cadence.Security.Audit.GxPAuditInterceptor`.

The security package interceptor delegated logging out-of-band to an `AuditDigestEngine`, lacked EF Core transactional integration, required HTTP context principals, and was never attached to any `DbContext`. In contrast, `Cadence.Database.GxPAuditInterceptor` is the authoritative single audit writer mandated by ADR-2446 and is actively registered in application composition roots.

## 2. Decision Drivers & Constraints

- **21 CFR Part 11 Compliance:** All audit records must commit atomically inside primary EF Core transactions.
- **Architectural Clarity:** Eliminate split audit trails and developer confusion caused by duplicate interceptor classes.
- **Fail-Closed Security:** Prevent unattached security interceptors from creating false expectations of GxP audit coverage.

## 3. Options Considered

### Option 1: Retain Both Interceptors
- **Overview:** Keep `Cadence.Security.Audit.GxPAuditInterceptor` alongside `Cadence.Database.GxPAuditInterceptor`.
- **Pros:**
  - ✅ No files deleted.
- **Cons:**
  - ❌ Duplicate class names cause ambiguity and accidental misuse.
  - ❌ Security interceptor lacks transactional guarantees.

### Option 2: Consolidate on Cadence.Database.GxPAuditInterceptor (Selected)
- **Overview:** Permanently delete `Cadence.Security.Audit.GxPAuditInterceptor` and remove its DI registration from `CadenceSecurityServiceCollectionExtensions.cs`.
- **Pros:**
  - ✅ Single authoritative `GxPAuditInterceptor` in `Cadence.Database`.
  - ✅ All database operations write Merkle-chained `AuditLogEntry` rows within the EF Core transaction.
  - ✅ Clean DI configuration in `Cadence.Security`.
- **Cons:**
  - ❌ Requires updating references and adding this ADR.

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Eliminates duplicate implementations, ensures fail-closed 21 CFR Part 11 compliance, and aligns with ADR-2446.

## 5. Consequences & Trade-offs

- **Positive Impact:** Single source of truth for EF Core audit trail interceptors across all platform microservices.
- **Negative Impact / Technical Debt:** None.
- **Mitigation Strategy:** Automated CLI quality sentinels verify single interceptor registration and architectural purity.

## 6. Implementation & Verification

- **Affected Packages:** `packages/security`, `packages/database`
- **Verification Plan:** Verified via `dotnet build Cadence.sln`, `dotnet test`, and `cadence check` CLI quality sentinels.

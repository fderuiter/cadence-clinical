# ADR-2248: Encapsulate EF Core Generic Repositories and AWS S3 Storage Port in .NET 10

- **Status:** Accepted
- **Date:** 2026-09-29
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

As part of the platform-wide migration to native .NET 10 (C# 14), legacy Python packages for database persistence (`packages/database`) and object storage (`packages/storage`) required replacement with high-performance, strongly-typed C# implementations. Database operations required unified global soft delete query filtering (`e => !e.IsDeleted`), state-based soft delete interception (`EntityState.Deleted` -> `EntityState.Modified`), generic repository CRUD contracts, and Cypher graph query safety validation to prevent unbounded traversals. Similarly, object storage required a production-grade AWS S3 port supporting SHA-256 integrity validation, AES-256 server-side encryption, presigned URLs, and multipart upload lifecycles.
@req:PRD-SYS-001 @req:PRD-SYS-053 @req:PRD-DOC-001 @req:PRD-DOC-002

## 2. Decision Drivers & Constraints

- **Driver 1 (21 CFR Part 11 / PRD-SYS-053):** Immutable audit trails and automatic soft delete handling across all relational persistence operations.
- **Driver 2 (Data Integrity / PRD-DOC-001):** Cryptographic SHA-256 payload integrity verification on S3 storage operations (`PutObjectAsync` / `GetObjectAsync`).
- **Driver 3 (Performance & Security):** Enforce fast presigned S3 URL generation (< 5ms) and AES-256 encryption at rest.
- **Constraint:** Zero cross-boundary leaks; full decommissioning of legacy Python persistence and storage packages.

## 3. Options Considered

### Option 1: Native .NET 10 `Cadence.Database` and `Cadence.Storage` (Selected)

- **Overview:** Build `CadenceDbContextBase` and `EfRepository<T>` in `Cadence.Database` for EF Core 10 persistence with soft delete filtering. Implement `S3StoragePort` in `Cadence.Storage` utilizing `AWSSDK.S3` with SHA-256 integrity validation and multipart lifecycle support. Fully delete legacy Python files under `packages/database` and `packages/storage`.
- **Pros:**
  - ✅ Native .NET 10 compile-time safety and sub-millisecond execution.
  - ✅ Automatic soft delete state interception and global query filters.
  - ✅ Strict SHA-256 integrity checks and AES-256 server-side encryption.
  - ✅ Full elimination of Python database/storage dependencies.
- **Cons:**
  - ❌ Requires unit test suites verifying EF Core and AWS S3 mock/integration flows.

### Option 2: Python Interop Sidecars

- **Overview:** Wrap legacy Python database/storage scripts via gRPC/HTTP sidecars.
- **Pros:**
  - ✅ Reuses existing Python code.
- **Cons:**
  - ❌ High latency and process overhead.
  - ❌ Violates the platform's .NET 10 native architecture standard.

## 4. Decision Outcome

- **Chosen Option:** Option 1: Native .NET 10 `Cadence.Database` and `Cadence.Storage`.
- **Justification:** Delivers native C# performance, strict type safety, and automatic GxP compliance for persistence and object storage.

## 5. Consequences & Trade-offs

- **Positive Impact:** Native .NET 10 persistence and storage capabilities with zero Python runtime overhead.
- **Negative Impact / Technical Debt:** None.
- **Mitigation Strategy:** Enforced via `Cadence.UnitTests` covering EF Core CRUD, soft delete, Cypher safety, and S3 integrity checks.

## 6. Implementation & Verification

- **Code:** Implemented in `packages/database/src` and `packages/storage/src`.
- **Tests:** Verified via `tests/Packages/DatabaseAndStorageUnitTests.cs` and `dotnet cadence check`.

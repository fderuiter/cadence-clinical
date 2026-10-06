# ADR-2810: Asynchronous CSV Batch Ingestion Pipeline for Clinical Subject Rosters

- **Status:** Accepted
- **Date:** 2026-10-06
- **Authors:** @jules
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Research coordinators and data managers require bulk subject roster ingestion for multi-center clinical trials. Synchronous row-by-row creation causes database lock contention and HTTP timeouts when processing up to 1,000 subjects across multiple sites (PRD-SUB-001).

## 2. Decision Drivers & Constraints

- **Compliance & Auditability:** 21 CFR Part 11 audit logging via `IAuditLogWriter` and demographic encryption at rest via `IDemographicsEncryptionService`.
- **Sequential Index Assignment:** Guaranteed contiguous, zero-gap enrollment index assignment per study via `ISubjectRepository.EnrollNextIndexAsync`.
- **Pre-Enrollment Validation:** Site activation status check via `ISiteComplianceRepository`, subject code uniqueness check, and demographic date schema validation.
- **Performance:** Bulk pre-enrollment validation of 100 subject records in under 5 seconds (PRD-SUB-001).

## 3. Options Considered

### Option 1: Asynchronous Staging Queue with Two-Phase Commit (Selected)

- **Overview:** Staging uploaded CSV rows in `subject_batch_imports` and `subject_batch_rows`, background validation via `System.Threading.Channels`, REST status inspection endpoints, and transactional batch commit.
- **Pros:**
  - ✅ Satisfies requirement PRD-SUB-001 with fast upload response and background validation.
  - ✅ Prevents HTTP timeouts and row-level lock contention during bulk validation.
  - ✅ Enables inspection and row-level error reporting prior to final transactional commit.
- **Cons:**
  - ❌ Requires additional staging entities and background queue worker infrastructure.

### Option 2: Synchronous Ingestion in HTTP Request

- **Overview:** Process all rows and enroll subjects within the initial POST request loop.
- **Pros:**
  - ✅ Simple request/response cycle without staging tables.
- **Cons:**
  - ❌ High risk of HTTP request timeouts on 1,000-subject CSV roster uploads.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Chosen because it satisfies PRD-SUB-001 while ensuring system maintainability, auditability, and clear operational boundaries.

## 5. Consequences & Trade-offs

- **Positive Impact:** Fast upload response, zero HTTP timeout risk, detailed row-level error reporting, and transactional commit with sequential index assignment.
- **Negative Impact / Technical Debt:** Maintenance overhead of additional structured abstractions (`subject_batch_imports`, `subject_batch_rows`).
- **Mitigation Strategy:** Automated test coverage in `SubjectBatchIngestionTests` and CI lint validation.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `apps/execution`
- **Verification Plan:** Automated unit tests in `tests/Cadence.UnitTests/Execution/Subjects/SubjectBatchIngestionTests.cs` and GxP compliance sync verification.

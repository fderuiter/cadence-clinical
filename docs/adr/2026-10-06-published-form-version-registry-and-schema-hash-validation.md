# ADR-2809: Published Form Version Registry and Schema Hash Validation

- **Status:** Accepted
- **Date:** 2026-10-06
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

In clinical trial data management under 21 CFR Part 11 and GxP standards, electronic Case Report Forms (eCRFs) submitted by site coordinators or participants must strictly conform to the approved protocol version form design and cryptographic schema hash.
Without schema snapshotting and validation on form submission:

1. Form submissions in Execution service could accept outdated or mismatched form structure data.
2. Clinical Kernel approvals of USDM protocol versions would lack form snapshot persistence and published schema tracking across services.

To address this, requirement PRD-SYS-053 requires:

- Clinical Kernel: Upon protocol version approval, compile USDM forms via `UsdmCompiler`, persist `FormVersionSnapshot` records in kernel storage, and publish `FormVersionPublished` domain events containing form definitions, version identifiers, and canonical schema hashes.
- Execution Service: Consume `FormVersionPublished` domain events to register form versions in `PublishedFormVersionRegistry`, and validate eCRF form submission requests against published schemas and form hashes, rejecting mismatches or unregistered forms with `PROTOCOL_VERSION_SCHEMA_MISMATCH`.

## 2. Decision Drivers & Constraints

- **PRD-SYS-053 Compliance**: Strict form version snapshotting during protocol approval and submission validation in the execution engine.
- **21 CFR Part 11 Auditability & GxP Integrity**: Immutability of published form version records and strict rejection of form submission payloads with invalid form hashes or unregistered protocol form versions.
- **K-TIME-3 Time Abstraction**: Absolute prohibition of `DateTimeOffset.UtcNow` or `DateTime.UtcNow` in favor of `IClock` dependency injection.
- **Schema & Outbox Isolation**: EF Core persistence in Execution service under the `edc` schema (`edc.published_form_version_registries`) with composite indexing on `(study_id, form_id, protocol_version_id)`.

## 3. Options Considered

### Option 1: Asynchronous Event-Driven Snapshot & Execution Registry (Selected)

- **Overview**:
  - Clinical Kernel compiles USDM forms during `ApproveProtocolVersionHandler`, persists `FormVersionSnapshot` entity records, and raises `FormVersionPublished` domain events.
  - Execution engine consumes `FormVersionPublished` via MassTransit (`FormVersionPublishedConsumer`), populating or updating `PublishedFormVersionRegistry` records.
  - On eCRF form submission, `FormSubmissionApplicationService` verifies the request against `PublishedFormVersionRegistry` records for the specified `(study_id, form_id, protocol_version_id)`.
- **Pros:**
  - ✅ Satisfies requirement PRD-SYS-053 and 21 CFR Part 11 audit requirements.
  - ✅ Clean separation of concerns between Clinical Kernel design authority and Execution runtime.
  - ✅ High throughput submission validation backed by indexed relational lookup in Execution persistence.
  - ✅ Full compliance with `IClock` time abstraction.
- **Cons:**
  - ❌ Requires additional table (`edc.published_form_version_registries`) and event consumer infrastructure.

### Option 2: Direct Cross-Service Query to Kernel on Every Form Submission

- **Overview**: Execution engine queries Clinical Kernel API on every form submission to fetch form definitions and hashes.
- **Pros:**
  - ✅ No duplicate form metadata storage in Execution service.
- **Cons:**
  - ❌ High latency and tight coupling on high-frequency form submission endpoints.
  - ❌ Violates microservice boundary autonomy and resiliency standards.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Option 1 provides event-driven decoupling, low latency submission validation, and robust GxP auditability as required by PRD-SYS-053.

## 5. Consequences & Trade-offs

- **Positive Impact:** Guarantees eCRF form submission integrity across protocol versions, prevents structural schema mismatches, and maintains audit trails.
- **Negative Impact / Technical Debt:** Requires maintenance of `PublishedFormVersionRegistry` EF Core entity and database migration in Execution service.
- **Mitigation Strategy:** Automated unit tests and EF Core migration snapshots verified via `pnpm cadence check`.

## 6. Implementation & Verification

- **Affected Repositories / Services:**
  - `packages/kernel/src/Cadence.Kernel.Domain/Entities/FormVersionSnapshot.cs`
  - `packages/kernel/src/Cadence.Kernel.Abstractions/Events/ProtocolEvents.cs`
  - `packages/kernel/src/Cadence.Kernel.Application/Protocol/ProtocolCommandHandlers.cs`
  - `apps/execution/src/Domain/Forms/PublishedFormVersionRegistry.cs`
  - `apps/execution/src/Domain/Ports/IPublishedFormVersionRegistryRepository.cs`
  - `apps/execution/src/Infrastructure/Persistence/Repositories/PublishedFormVersionRegistryRepository.cs`
  - `apps/execution/src/Infrastructure/Consumers/FormVersionPublishedConsumer.cs`
  - `apps/execution/src/Application/Forms/FormSubmissionApplicationService.cs`
  - `apps/execution/src/Infrastructure/Persistence/Migrations/20261006120000_AddPublishedFormVersionRegistry.cs`
- **Verification Plan:**
  - Unit tests in `FormSubmissionProtocolVersionTests.cs` and `FormVersionPublishedConsumerTests.cs`.
  - Quality sentinel verification via `pnpm cadence check`.

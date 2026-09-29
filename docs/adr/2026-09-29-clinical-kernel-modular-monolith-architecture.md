# ADR-2244: Clinical Kernel Modular Monolith Architecture

- **Status:** Accepted
- **Date:** 2026-09-29
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Following the .NET 10 migration (ADR-2243), the platform operated as 18 decoupled microservices. While this achieved runtime unification, it inherited conceptual fragmentation from the original topology: foundational clinical concepts—such as `Study`, `Site`, `Subject`/`Participant`, `Personnel`, and `AuditLog`—were duplicated across multiple service schemas (`apps/execution`, `apps/ctms`, `apps/org`, `apps/econsent`, `apps/tickets`). Maintaining consistency across service boundaries required complex distributed sagas, outbox synchronizations, and reconciliation workers, introducing failure windows under 21 CFR Part 11 (PRD-SYS-053) and ICH GCP E6(R2).

In reality, a clinical trial has a single body of truth: a protocol specifies what *should* happen, sites and participants make things *actually* happen, and systems record that they happened. EDC, CTMS, eTMF, RTSM, Safety, and eCOA are **projections and workflows over one shared body of clinical truth**, not independent owners of it.

## 2. Decision Drivers & Constraints

- **Driver 1 (21 CFR Part 11 / PRD-SYS-053):** Transactional consistency and cryptographically verifiable audit trails across state mutations, eliminating cross-service sync drift.
- **Driver 2 (Bitemporality):** Ability to reconstruct protocol applicability, subject enrollment status, and clinical observations as of valid time ($t$) and recorded time ($\text{known\_at}$).
- **Driver 3 (Zero Forbidden Redefinitions):** Forbid modules from declaring their own `Study`, `Site`, `Participant`, `Observation`, `Person`, or `AuditLog` tables.
- **Constraint:** Retain high developer velocity, sub-second test execution, and Native AOT readiness without introducing distributed query overhead.

## 3. Options Considered

### Option 1: Clinical Kernel Modular Monolith (Selected)

- **Overview:** Establish a core library and single relational schema (`Cadence.Kernel.*`) encapsulating Identity (Tenant, Organization, Person), Protocol (Study, ProtocolVersion, Applicability), Participant (Enrollment, Cohort), Observation (Assertions, Sources), Authority (Actor, Capability, Delegation), and Audit (Attestation, Monotonic Merkle Ledger). Product modules (`Edc`, `Ctms`, `Etmf`, `Rtsm`, `Safety`, `Ecoa`) run as in-process workflow engines and projection modules referencing strongly-typed UUIDv7 IDs (`StudyId`, `SiteId`, `ParticipantId`).
- **Pros:**
  - ✅ Atomic ACID transactions across clinical mutations and audit entries within PostgreSQL.
  - ✅ Eradicates duplicate tables, distributed transactions, and cross-service data sync drift.
  - ✅ Fast in-memory queries with compile-time type safety via C# 14 records and EF Core 10.
  - ✅ Enforced via static architecture sentinels (ArchUnitNET K-XS-1 / K-XS-2).
- **Cons:**
  - ❌ Requires refactoring existing service schemas to delegate core entity ownership to the Kernel.

### Option 2: Retain 18 Decoupled Microservices with Distributed Sagas

- **Overview:** Maintain 18 independent database schemas and rely on MassTransit outbox events, HTTP REST calls, and background synchronization jobs to propagate entity states.
- **Pros:**
  - ✅ Maintains existing database schema isolation per microservice.
- **Cons:**
  - ❌ High operational complexity and eventual consistency lag across critical clinical data.
  - ❌ Susceptible to synchronization failure windows under stringent GxP inspection.
  - ❌ Significant cognitive overhead for developers maintaining 18 separate database boundaries locally.

## 4. Decision Outcome

- **Chosen Option:** Option 1: Clinical Kernel Modular Monolith.
- **Justification:** Unifying the core clinical truth in `Cadence.Kernel` guarantees absolute regulatory integrity, eliminates distributed sync failures under 21 CFR Part 11, and vastly simplifies developer workflows and local orchestration.

## 5. Consequences & Trade-offs

- **Positive Impact:**
  - Single source of clinical truth across all workflows.
  - Sub-second unit tests and simplified integration test harnesses.
  - Full bitemporal auditability and historical reconstruction.
- **Negative Impact / Technical Debt:** Requires systematic migration of existing entities in `apps/execution`, `apps/ctms`, and `apps/org` into `Cadence.Kernel`.
- **Mitigation Strategy:** Phased rollout starting with Kernel domain abstractions and the Digital Data Flow (DDF) core spine, protected by ArchUnitNET architectural sentinels.

## 6. Implementation & Verification

- **Affected Assemblies:** `Cadence.Kernel.*`, `Cadence.Apps.Execution`, `Cadence.Apps.Ctms`, `Cadence.Apps.Org`, `Cadence.Apps.Designer`, `Cadence.Contracts`.
- **Verification Plan:**
  - Automated ArchUnitNET rules verifying K-XS-1 and K-XS-2 boundary purity.
  - Acceptance tests validating historical observation corrections and cross-site protocol version applicability.
  - Local orchestration managed via .NET Aspire AppHost and Cadence CLI (`dotnet cadence check`).

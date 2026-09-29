# Cadence Clinical Kernel

The core clinical domain modular monolith (`Cadence.Kernel.*`) providing the unified, shared clinical truth for the Cadence Clinical Research Software Platform.

## Architecture

As formalized in [ADR-2244](file:///Volumes/SanDisk%20External%20SSD/Code/cadence-clinical/docs/adr/2026-09-29-clinical-kernel-modular-monolith-architecture.md) and specified in [`CLINICAL_KERNEL_SPECIFICATION.md`](file:///Volumes/SanDisk%20External%20SSD/Code/cadence-clinical/docs/kernel/CLINICAL_KERNEL_SPECIFICATION.md), the Clinical Kernel consolidates:
- **Identity & Ownership**: Tenant, Organization, Person, Study, Site, StudySite
- **Clinical Subjects**: Participant, Enrollment, Cohort
- **Clinical Truth**: Observations, Assertions, Sources, Provenance
- **Authority**: Actors, Capabilities, Delegations, Attestations (21 CFR Part 11 electronic signatures)
- **Temporal Integrity**: PostgreSQL native bitemporal intervals (`valid_period`, `recorded_period`) via `tstzrange` and `gist` (ADR-2246)
- **Audit Ledger**: Monotonic cryptographic Merkle chain

## Assemblies
- `Cadence.Kernel.Abstractions`: Strongly-typed UUIDv7 value structs (`TenantId`, `OrganizationId`, `PersonId`, `StudyId`, `SiteId`, `StudySiteId`, `ParticipantId`, `ObservationId`, `AttestationId`), core interfaces, and Native AOT serialization contexts.
- `Cadence.Kernel.Domain`: Canonical domain entities, bitemporal models, and EF Core 10 mappings.

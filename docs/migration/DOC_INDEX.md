# Python-to-.NET Port Archive Index

Historical Phase 0 audits and bounded-context migration slice records. Part of the sharded [Documentation Index](../DOCUMENTATION_INDEX.md); rows in each table are sorted alphabetically by file path.

## Python-to-.NET port (completed)

| Document | Status | What it is |
| --- | --- | --- |
| [Bounded context migration matrix](BOUNDED_CONTEXT_MIGRATION_MATRIX.md) | Historical | Port sequencing |
| [Dependency and parity catalog](DEPENDENCY_AND_PARITY_CATALOG.md) | Historical | Python-to-.NET library crosswalk |
| [Modernization hub](index.md) | Historical | Phase 0 baseline and links to the port documents |
| [Phase 0 readiness audit](Migration_Phase0_Readiness_Audit.md) | Historical | Gap analysis before the port |
| [Phase 0 working papers](phase0/) | Historical | Validation gate, topology, contexts, plan |
| [00. Phase 0 Validation Gate — Audit Report](phase0/00_validation_gate_report.md) | Historical | Document ID: PHASE0-NET10-00-VALIDATION |
| [00a. Gate 5 Remediation — Clean dotnet build Cadence.sln Restored](phase0/00a_build_remediation.md) | Historical | Document ID: PHASE0-NET10-00A-BUILD-REMEDIATION |
| [01. System Topology & Entry Point Audit](phase0/01_system_topology.md) | Historical | Historical record (Phase 0 discovery, September 2026). It describes the system as it was then. Neo4j, listed here as ... |
| [02. Bounded Context Decomposition & Coupling Analysis](phase0/02_bounded_contexts.md) | Historical | Historical record (Phase 0 discovery, September 2026). It describes the system as it was then. Neo4j, listed here as ... |
| [03. Migration Sequencing Plan & Strangler Fig Design](phase0/03_migration_plan.md) | Historical | Historical record (Phase 0 discovery, September 2026). It describes the system as it was then. Neo4j, listed here as ... |
| [05. Orphan Audit Gate — Zero-Orphan Verification](phase0/05_orphan_audit_gate.md) | Historical | Document ID: PHASE0-NET10-05 |
| [Phase 0 decomposition and scaffold](PHASE0_DOMAIN_DECOMPOSITION_AND_SCAFFOLD.md) | Historical | Solution scaffold decisions |
| [Solution scaffold guide](SCAFFOLD_GUIDE.md) | Historical | How the solution was scaffolded |
| [Behavioral & Invariant Specification: Clinical Observations, Form Submissions & Dynamic Projections](slices/execution-observations/01_spec.md) | Historical | Slice: `execution-observations` |
| [Technical Design: EXECUTION-OBSERVATIONS (.NET 10 / C# 14)](slices/execution-observations/02_technical_design.md) | Historical | Slice: `execution-observations` · Bounded Context: `Cadence.Apps.Execution` · Target Assembly: `apps/execution/src/C...` |
| [Tracer-Bullet Development Tickets: EXECUTION-OBSERVATIONS](slices/execution-observations/03_tickets.md) | Historical | Slice: `execution-observations` · Bounded Context: `Cadence.Apps.Execution` · Standard: 6-Step Service Transcription ... |
| [Implementation Sign-Off: execution-observations](slices/execution-observations/04_implementation_signoff.md) | Historical | Slice: `execution-observations` · Bounded Context: `Cadence.Apps.Execution` · Standard: 6-Step Service Transcription ... |
| [Phase 1 Validation Gate Report: execution-subjects](slices/execution-subjects/00_phase1_validation_report.md) | Historical | Remediation Addendum (2026-09-04, same-day follow-up): The five FAIL verdicts recorded below were addressed in a reme... |
| [Phase 2 Validation Gate Report: EXECUTION-SUBJECTS](slices/execution-subjects/00_phase2_validation_report.md) | Historical | Scope note on file naming. The validation prompt names `01_functional_spec.md` as the reference spec; this slice's ac... |
| [Phase 3 Validation Gate Report: EXECUTION-SUBJECTS](slices/execution-subjects/00_phase3_validation_report.md) | Historical | Superseded Notice (2026-09-05): The verdict below (§5, "PHASE 4 BLOCKED") audited the pre-remediation 8-ticket `03_t...` |
| [Behavioral & Invariant Specification: Subject Lifecycle & Enrollment](slices/execution-subjects/01_spec.md) | Historical | Slice: `execution-subjects` |
| [Target .NET 10 Architecture: Subject Lifecycle & Enrollment](slices/execution-subjects/02_architecture.md) | Historical | Slice: `execution-subjects` |
| [Technical Design: EXECUTION-SUBJECTS (.NET 10 / C# 14)](slices/execution-subjects/02_technical_design.md) | Historical | Supersedes the pre-remediation draft at `02_architecture.md` (and the backlog in `03_tickets.md`), both of which were... |
| [Migration Backlog: EXECUTION-SUBJECTS](slices/execution-subjects/03_tickets.md) | Historical | Slice Code: `SUBJ` · Bounded Context: `Cadence.Apps.Execution` · Target Project: `apps/execution/src/Cadence.Apps.Ex...` |
| [Implementation Sign-Off: execution-subjects](slices/execution-subjects/04_implementation_signoff.md) | Historical | - Status: VERIFIED & COMPLETED |
| [Functional Specification: Participant Compensation](slices/participant-compensation/Participant_Compensation_Functional_Spec.md) | Historical | Slice: `participant-compensation` |
| [System topology audit](SYSTEM_TOPOLOGY_AUDIT.md) | Historical | Entry points and persistence of the Python system |

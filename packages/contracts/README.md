# Cadence.Contracts

Zero-dependency MassTransit domain event contracts for asynchronous cross-service messaging via the EF Core transactional outbox ([ADR-2524](../../docs/adr/2026-10-19-messaging-stays-on-the-apache-licensed-masstransit-8-line.md)). Synchronous clinical mutations across modules go through the in-process kernel command path ([ADR-2244](../../docs/adr/2026-09-29-clinical-kernel-modular-monolith-architecture.md), [ADR-2600](../../docs/adr/2026-10-19-modules-write-through-the-kernel-command-path.md), [ADR-2610](../../docs/adr/2026-10-15-scaffold-services-become-modules-merge-or-are-deleted.md)), not cross-service HTTP interfaces.

## Project

- Project: `packages/contracts/src/Cadence.Contracts.csproj` (assembly `Cadence.Contracts`, namespace `Cadence.Contracts`).
- Domain event records (`@req:PRD-SYS-001`):
  - `ClinicalVisitCompleted` — published by `ctms` when a monitoring visit report is completed or signed off; consumed by `payments`.
  - `EConsentCompleted` — consumed by `etmf` when an informed consent form is completed or signed.
  - `EconsentReconsentRequired` — published by `econsent` when an ICF version or protocol amendment requires re-consent; consumed by `notifications`.
  - `EtmfDocumentFilingRequested` — consumed by `etmf` when cross-service eTMF document filing is requested.
  - `Grade3ToxicityDetected` — published by `execution` (`LabToxicityGovernanceService`) when a Grade 3/4 laboratory toxicity event is detected or confirmed.
- Every public member has an XML comment: the project sets `CadenceRequireXmlDocs`, so a missing one fails the build (`CS1591`).

## Key dependencies

None (zero `<ProjectReference>` and `<PackageReference>` items; uses .NET base class library types only).

## Referenced by

The six active MassTransit publishers and consumers (`ctms`, `econsent`, `etmf`, `execution`, `notifications`, `payments`) and `tests/Cadence.ArchitectureTests` (for `K-XS-1` boundary scanning).

## Tests

- Event producer and consumer unit tests in `tests/Cadence.UnitTests/Cadence.UnitTests.csproj` (`MonitoringVisitPublishEventTests`, `EconsentEndpointsTests`, `EtmfConsumersTests`, `LabToxicityGradingPipelineTests`, `EconsentReconsentConsumerTests`, `ClinicalVisitCompletedConsumerTests`).
- Boundary and XML documentation policy tests in `tests/Cadence.ArchitectureTests/Cadence.ArchitectureTests.csproj` (`KernelBoundaryTests`, `WarningSuppressionPolicyTests`).

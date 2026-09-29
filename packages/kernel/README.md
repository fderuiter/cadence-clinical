# Clinical kernel

First assemblies of the clinical kernel described in [ADR-2244](../../docs/adr/2026-09-29-clinical-kernel-modular-monolith-architecture.md) and the [clinical kernel specification](../../docs/kernel/CLINICAL_KERNEL_SPECIFICATION.md). They hold the shared identifiers, time source and core entities that the application modules will build on (roadmap phase 0, spec §15).

## Assemblies

- `src/Cadence.Kernel.Abstractions/Cadence.Kernel.Abstractions.csproj`:
  - `Cadence.Kernel`: `IEntityId` and strongly typed identifiers `TenantId`, `OrganizationId`, `PersonId`, `StudyId`, `SiteId`, `StudySiteId`, `ProtocolVersionId`, `ParticipantId`, `ObservationId`, `AttestationId`.
  - `Cadence.Kernel.Serialization`: JSON and type converters for the identifiers and `KernelJsonSerializerContext`.
  - `Cadence.Kernel.Time`: `IClock` and `SystemClock`.
  - `Cadence.Kernel.Templating`: `ITemplateRenderer`, `FluidTemplateRenderer` and the `AddFluidTemplating()` registration used by the CTMS, notifications and safety services.
- `src/Cadence.Kernel.Domain/Cadence.Kernel.Domain.csproj`:
  - `Cadence.Kernel.Domain.Entities`: `Tenant`, `Organization`, `Person`, `Study`, `Site`, `StudySite`, `ProtocolVersion`, `Participant`, `Observation`, `Attestation`.
  - `Cadence.Kernel.Domain.Bitemporal`: `IBitemporalEntity`, `TemporalPeriod`, `BitemporalQueryExtensions`.
  - `Cadence.Kernel.Domain.ValueConversion` and `KernelModelConfigurationBuilderExtensions`: EF Core value converters for the identifiers.

Participant enrolment, assertions, sources, authority and the audit chain from the specification are not implemented here yet.

## Build rules

`Directory.Build.props` in this folder adds `Microsoft.CodeAnalysis.BannedApiAnalyzers` with the repository `BannedSymbols.txt`, so kernel code must take time from `IClock` instead of the wall clock; violations fail the build.

## Key dependencies

- Abstractions: `Fluid.Core`, `Microsoft.Extensions.DependencyInjection.Abstractions`.
- Domain: `Cadence.Kernel.Abstractions`, `Cadence.Core.Domain`, `Microsoft.EntityFrameworkCore`.

## Referenced by

- Abstractions: every .NET service, `packages/ddf`, `packages/testing`, the unit and architecture test projects.
- Domain: `apps/designer`, `apps/org`, `packages/ddf`, the unit and architecture test projects.

## Tests

```bash
dotnet test tests/Cadence.UnitTests.csproj --filter "FullyQualifiedName~Cadence.UnitTests.Kernel"
dotnet test tests/Cadence.ArchitectureTests.csproj --filter "FullyQualifiedName~Cadence.ArchitectureTests.Architecture"
```

The second command runs the kernel boundary rules in `tests/Architecture/`.

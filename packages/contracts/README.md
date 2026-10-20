# Cadence.Contracts

Cross-service DTOs and typed HTTP client interfaces for service-to-service calls.

## Project

- Project: `packages/contracts/src/Cadence.Contracts.csproj` (assembly `Cadence.Contracts`, namespace `Cadence.Contracts`).
- `IExecutionApiClient` with `SubjectSummaryDto` and `ObservationSummaryDto`.
- `IExecutionAmendmentApiClient` with `PublishAmendmentRequestDto`, `PublishAmendmentResponseDto`, `AmendmentSnapshotDto`, `AmendmentActivityDto`.
- `IDesignerEligibilityApiClient` with `DesignerEligibilityCriterionDto`, `DesignerExpressionNodeDto`, `FieldReferenceDto`.
- Only interfaces and records exist; there are no client implementations or DI registrations yet.
- Every public member has an XML comment: the project sets `CadenceRequireXmlDocs`, so a missing one fails the build (CS1591).

## Key dependencies

- Project reference: `Cadence.Core.Domain`.
- Packages: `Polly`, `Microsoft.Extensions.Http.Polly`.

## Referenced by

The scaffold services (`ctms`, `econsent`, `eisf`, `etmf`, `fileshare`, `imaging`, `interop`, `knowledge`, `notifications`, `org`, `payments`, `quality`, `safety`, `tickets`). None of them uses these types in code yet; `execution`, `designer` and `gateway` do not reference the package.

## Tests

No tests exist for this package.

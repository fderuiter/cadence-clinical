# Clock Migration Backlog (K-TIME-3, #4784)

| | |
| --- | --- |
| **Status** | No wall-clock reads remain in `apps/` or `packages/`. Every app and every package that had one opts into the ban; `core-models`, `telemetry`, `ddf`, `templating`, `storage`, `sentinels` and `contracts` have none and are not opted in yet |
| **Date** | 2026-09-29 |
| **Spec** | `docs/kernel/CLINICAL_KERNEL_SPECIFICATION.md` §4.1 K-TIME-3, §10 |
| **Issue** | #4784 |

## What is done

- `Cadence.Kernel.Time.IClock` (`DateTimeOffset UtcNow`) and `SystemClock` live in
  `packages/kernel/src/Cadence.Kernel.Abstractions/Time/`.
- `Cadence.Testing.Time.FakeClock` (settable, advanceable, frozen by default) lives in `packages/testing/src/Time/`.
- `apps/execution` uses the kernel clock. Its own `Application/Abstractions/IClock.cs` and
  `Infrastructure/SystemClock.cs` are deleted. The semantics are the same: both return `DateTimeOffset` in UTC.
- `Microsoft.CodeAnalysis.BannedApiAnalyzers` is pinned in `Directory.Packages.props`. The repo-root
  `BannedSymbols.txt` bans `DateTime.Now`, `DateTime.UtcNow`, `DateTimeOffset.Now` and `DateTimeOffset.UtcNow`.
- `Microsoft.CodeAnalysis.BannedApiAnalyzers` and `BannedSymbols.txt` are included unconditionally in root `Directory.Build.props`, enforcing K-TIME-3 globally across all C# projects in the repository.
  Because `TreatWarningsAsErrors` is on, a violation fails the build with RS0030. Tests use `FakeClock`, or `SystemClock.Instance`
  where the code under test checks the real clock ([TESTING.md, Time in tests](../TESTING.md#time-in-tests)).
- In the app entities every timestamp is a `DateTimeOffset` (execution's `ObservationDate` aside, see below). The
  ones a writer must supply (`CreatedAt`, `UpdatedAt`, `Timestamp`, `ReceivedAt`, interop's `DeviceTimestamp` and
  `ServerTimestamp`, eConsent's `SignedAt` and `WithdrawalDate`) are `required` with no default, so code that creates a row must pass a time it took from `IClock` (or, for `DeviceTimestamp`, the device's own time).
  The columns were already `timestamp with time zone`, so the change needs no migration
  (`dotnet ef migrations has-pending-model-changes` reports none).
- `packages/database`'s `GxPAuditInterceptor` takes a required `IClock` and no longer falls back to the wall clock,
  so every audit field and audit entry timestamp comes from the injected clock (#4784). Execution passes its
  registered `IClock`. The value is still normalized to UTC `DateTime` before hashing, so the Merkle block hash
  format is unchanged and no migration is needed.

## Last packages (done)

The last three packages with wall-clock reads were the audit, signing and
replay-protection packages the security thread owns (#4784):

- `packages/database`: `GxPAuditInterceptor` takes a required `IClock`.
- `packages/compliance`: `GxPSigner.SignGxpMarkdown`, `Pkcs7Signer.GenerateSigningCredentials` and
  `RtmGeneratorService.GenerateRtm` take an optional `IClock` (default `SystemClock.Instance`).
  `ComplianceAuditLogRecord.Timestamp` no longer defaults to the current time: a record without one stays without
  one.
- `packages/security`: `ReplayCache` and `AuditDigestEngine` take an optional `IClock` (default
  `SystemClock.Instance`; `AddCadenceSecurity` passes the registered clock when there is one).
  `AuditLogRecord.Timestamp` is `required`.

### Shared packages (done)

These projects opt into the ban. They needed no migration.

| Project | Change |
| --- | --- |
| `packages/deid` | `DicomDeidentifier.Deidentify` takes `deidentifiedAt` and `RedactionManifestBuilder.BuildRedactionManifest` takes `recordedAt`. The certificate and manifest `Timestamp` default to empty, so the builder is the only place that sets them. |
| `packages/hexagonal` | `DomainEvent.OccurredAt` is `required`. `BaseEntity`/`AggregateRoot` take `createdAt` as a required constructor argument. |
| `packages/tooling_core` (all three projects) | `CreateAdrService.CreateAdr` takes the date |
| `packages/cli` | `scaffold adr` dates the file from `SystemClock.Instance`, its composition root |
| `packages/testing` | `TestSecurityContext` signs headers and step-up tokens at `SystemClock.Instance.UtcNow` unless a time is given, because the gateway checks the real clock |

The counts below are from `main` at the base of this change and exclude EF `Migrations/`.

### `packages/engine_core` (done)

The project opts into the ban. None of these changes needed a migration or changes the audit hash format.

| File | Violations | Change |
| --- | ---: | --- |
| `AggregateRootBase.cs` | ~~1~~ 0 | `CreatedAt` has no default. Every save path already stamps it from its clock: the kernel command transaction, and `GxPAuditInterceptor` for execution. It stays `DateTime`, so `IGxPAuditable` and the columns do not change. No code in quality, ctms, etmf or fileshare writes their aggregates yet. The first writer there needs a save path that stamps `CreatedAt`, such as `GxPAuditInterceptor`. |
| `Audit/AuditLogEntry.cs` | ~~1~~ 0 | `Timestamp` is `required` and stays `DateTime`, so the hashed `{timestamp:O}` text does not change. The only writer, `GxPAuditInterceptor`, already set it. |
| `Rules/RuleAst.cs` | ~~1~~ 0 | `RuleDefinition.CreatedAt` is a `required DateTimeOffset` |
| `Rules/RuleEvaluator.cs` | ~~1~~ 0 | `asOf` is a required constructor argument |
| `Toxicity/LabToxicityGradingEngine.cs` | ~~8~~ 0 | `EvaluateObservation` and `EvaluateHysLaw` take `evaluatedAt` |
| `Supply/DTPLogisticsEngine.cs` | ~~1~~ 0 | `EvaluateDtpDispatch` takes `authorizedAt` |

## How to count again

```bash
grep -rEn 'DateTime(Offset)?\.(Utc)?Now\b' --include=*.cs --exclude-dir=Migrations apps packages
```

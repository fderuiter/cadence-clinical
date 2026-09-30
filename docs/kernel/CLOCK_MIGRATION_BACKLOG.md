# Clock Migration Backlog (K-TIME-3, #4784)

| | |
| --- | --- |
| **Status** | Open (`apps/` and `packages/engine_core` done; `packages/database` interceptor and other shared packages left) |
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
- The analyzer is enabled for every project under `packages/kernel/` through `packages/kernel/Directory.Build.props`.
  Because `TreatWarningsAsErrors` is on, a violation there fails the build with RS0030.
- The analyzer also covers every test project under `tests/` through `tests/Directory.Build.props` (#4913). Tests
  use `FakeClock`, or `SystemClock.Instance` where the code under test checks the real clock
  ([TESTING.md, Time in tests](../TESTING.md#time-in-tests)).
- The analyzer covers every project under `apps/`, the whole project and not only `Domain/`. The repo-root
  `Directory.Build.props` sets `CadenceBanWallClock` for any project under `apps/` and then adds the analyzer and
  `BannedSymbols.txt`. Any other project opts in with `<CadenceBanWallClock>true</CadenceBanWallClock>` in its
  `.csproj`. The apps moved over two at a time (#4942, #4946, #4949, #4953, #4970, #4975, #4984, and payments with
  the apps-wide default last).
- In the app entities every timestamp is a `DateTimeOffset` (execution's `ObservationDate` aside, see below). The
  ones a writer must supply (`CreatedAt`, `UpdatedAt`, `Timestamp`, `ReceivedAt`, interop's `DeviceTimestamp` and
  `ServerTimestamp`, eConsent's `SignedAt` and `WithdrawalDate`) are `required` with no default, so code that creates a row must pass a time it took from `IClock` (or, for `DeviceTimestamp`, the device's own time).
  The columns were already `timestamp with time zone`, so the change needs no migration
  (`dotnet ef migrations has-pending-model-changes` reports none).
- `packages/database`'s `GxPAuditInterceptor` takes a required `IClock` and no longer falls back to the wall clock,
  so every audit field and audit entry timestamp comes from the injected clock (#4784). Execution passes its
  registered `IClock`. The value is still normalized to UTC `DateTime` before hashing, so the Merkle block hash
  format is unchanged and no migration is needed. `packages/database` opts in with `CadenceBanWallClock`.

## What is left

The analyzer is **not** yet enabled for `packages/database` or the other shared packages under `packages/`
outside `packages/kernel/` and `packages/engine_core/`. Once a project is clean, it sets
`<CadenceBanWallClock>true</CadenceBanWallClock>` in its `.csproj`.

- `packages/database`: `GxPAuditInterceptor` falls back to `DateTime.UtcNow` when no clock is passed. The security
  thread owns this change (audit code): take a required `IClock`, then opt the project in.
- `packages/compliance`, `packages/deid`, `packages/hexagonal`, `packages/security`, `packages/tooling_core`,
  `packages/cli` and `packages/testing` still read the wall clock (find them with the grep below over `packages/`).

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

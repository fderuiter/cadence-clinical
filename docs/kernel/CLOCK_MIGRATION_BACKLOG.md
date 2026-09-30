# Clock Migration Backlog (K-TIME-3, #4784)

| | |
| --- | --- |
| **Status** | Open |
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
- Applications opt in project by project with `<CadenceBanWallClock>true</CadenceBanWallClock>` in the `.csproj`. The
  repo-root `Directory.Build.props` then adds the same analyzer and `BannedSymbols.txt` to the whole project, not
  only `Domain/`. Opted in so far: every app whose row in the table below says Done.
- In those apps every timestamp is a `DateTimeOffset`, and the ones a writer must supply (`CreatedAt`, `UpdatedAt`,
  `Timestamp`, `ReceivedAt`, interop's `DeviceTimestamp` and `ServerTimestamp`) are `required` with no default, so
  code that creates a row must pass a time it took from `IClock` (or, for `DeviceTimestamp`, the device's own time).
  The columns were already `timestamp with time zone`, so the change needs no migration
  (`dotnet ef migrations has-pending-model-changes` reports none).

## What is left

The analyzer is **not** yet enabled for `packages/engine_core` or the apps not listed above. Enabling it there
today would break the build. Each follow-up below removes one group of violations. Once a group is clean, the
same `PackageReference` and `AdditionalFiles` items from `packages/kernel/Directory.Build.props` are added to
that project (for an app, `<CadenceBanWallClock>true</CadenceBanWallClock>` in its `.csproj`).

The counts below are from `main` at the base of this change and exclude EF `Migrations/`.

### `apps/*/src` (110 property initializers in 14 apps, plus 2 fallbacks in execution)

| App | Violations | File | Kind |
| --- | ---: | --- | --- |
| payments | 22 | `Domain/PaymentEntities.cs` | `= DateTime.UtcNow` property initializers |
| interop | ~~19~~ 0 | `Domain/InteropEntities.cs` | Done: `DeviceTimestamp` and `ServerTimestamp` no longer both default to server now (spec §10) |
| econsent | 16 | `Domain/EconsentEntities.cs` | initializers |
| safety | 15 | `Domain/SafetyEntities.cs` | initializers |
| imaging | ~~10~~ 0 | `Domain/ImagingEntities.cs` | Done |
| knowledge | ~~6~~ 0 | `Domain/KnowledgeEntities.cs` | Done |
| etmf | ~~4~~ 0 | `Domain/EtmfEntities.cs` | Done |
| org | ~~4~~ 0 | `Domain/OrgEntities.cs` | Done |
| tickets | ~~4~~ 0 | `Domain/TicketEntities.cs` | Done |
| eisf | ~~3~~ 0 | `Domain/EisfEntities.cs` | Done |
| fileshare | ~~3~~ 0 | `Domain/FileshareEntities.cs` | Done |
| notifications | ~~2~~ 0 | `Domain/NotificationEntities.cs` | Done |
| ctms | ~~1~~ 0 | `Domain/CtmsEntities.cs` | Done |
| quality | ~~1~~ 0 | `Domain/QualityEntities.cs` | Done |
| execution | ~~2~~ 0 | `Domain/Queries/ClinicalQuery.cs:141` | Done. `ClinicalQuery.Close` now requires the time, and SDV verification, query resolution and escalation are `DateTimeOffset`. Before this, the service passed `IClock.UtcNow.DateTime` (`DateTimeKind.Unspecified`), which Npgsql refuses for `timestamptz`, so SDV and query close failed on PostgreSQL. An observation time without an offset is now stored as UTC. `ObservationDate` stays `DateTime` (valid time from the caller) |

Per app, the follow-up is the same: remove the initializer, make the property a `required DateTimeOffset`
(`timestamptz`), set the value from `IClock` wherever the app creates the row, opt the project in, and add an EF
migration if `has-pending-model-changes` reports one.

### `packages/engine_core` (deferred: changing these cascades)

| File | Violations | Why it is deferred |
| --- | ---: | --- |
| `AggregateRootBase.cs:10` | 1 | `CreatedAt` is `DateTime` and implements `IGxPAuditable.CreatedAt` (`DateTime`). Moving to `DateTimeOffset` changes the interface, `GxPAuditInterceptor`, and the aggregates in execution, ctms, etmf, fileshare, quality, `packages/database`, `packages/hexagonal` and `packages/kernel`, with EF column-type migrations. |
| `Audit/AuditLogEntry.cs:17` | 1 | `Timestamp` is `DateTime` and feeds `ComputeBlockHash` as `{timestamp:O}`. Moving to `DateTimeOffset` changes the hashed text (`Z` becomes `+00:00`) and breaks Merkle-chain verification of rows already written. There are 18 `new AuditLogEntry` call sites in 17 files (15 of them under `apps/`). This needs its own ADR and a hash-version field. |
| `Rules/RuleAst.cs:45` | 1 | `CreatedAt` initializer (JSON-serialised rule metadata) |
| `Rules/RuleEvaluator.cs:34` | 1 | `asOf ?? DateTime.UtcNow` fallback |
| `Toxicity/LabToxicityGradingEngine.cs` | 8 | `EvalTimestamp`/`DraftTimestamp` strings |
| `Supply/DTPLogisticsEngine.cs:107` | 1 | `AuthorizedTimestamp` string |

## How to count again

```bash
grep -rEn 'DateTime(Offset)?\.(Utc)?Now\b' --include=*.cs apps/*/src/Domain packages/engine_core/src
```

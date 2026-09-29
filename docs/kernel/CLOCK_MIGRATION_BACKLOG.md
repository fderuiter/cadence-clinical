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

## What is left

The analyzer is **not** yet enabled for `packages/engine_core` or `apps/*/src/Domain`. Enabling it there
today would break the build. Each follow-up below removes one group of violations. Once a group is clean, the
same `PackageReference` and `AdditionalFiles` items from `packages/kernel/Directory.Build.props` are added to
that project (for an app, scoped to `Domain/**` with a `Directory.Build.props` or an `.editorconfig`
`dotnet_diagnostic.RS0030.severity` override outside `Domain/`).

The counts below are from `main` at the base of this change and exclude EF `Migrations/`.

### `apps/*/src` (110 property initializers in 14 apps, plus 2 fallbacks in execution)

| App | Violations | File | Kind |
| --- | ---: | --- | --- |
| payments | 22 | `Domain/PaymentEntities.cs` | `= DateTime.UtcNow` property initializers |
| interop | 19 | `Domain/InteropEntities.cs` | initializers. `DeviceTimestamp` and `ServerTimestamp` both default to server now (breaks offline capture, spec §10) |
| econsent | 16 | `Domain/EconsentEntities.cs` | initializers |
| safety | 15 | `Domain/SafetyEntities.cs` | initializers |
| imaging | 10 | `Domain/ImagingEntities.cs` | initializers |
| knowledge | 6 | `Domain/KnowledgeEntities.cs` | initializers |
| etmf | 4 | `Domain/EtmfEntities.cs` | initializers |
| org | 4 | `Domain/OrgEntities.cs` | initializers |
| tickets | 4 | `Domain/TicketEntities.cs` | initializers |
| eisf | 3 | `Domain/EisfEntities.cs` | initializers |
| fileshare | 3 | `Domain/FileshareEntities.cs` | initializers |
| notifications | 2 | `Domain/NotificationEntities.cs` | initializers |
| ctms | 1 | `Domain/CtmsEntities.cs` | initializer |
| quality | 1 | `Domain/QualityEntities.cs` | initializer |
| execution | 2 | `Domain/Observations/ClinicalObservation.cs:144`, `Domain/Queries/ClinicalQuery.cs:141` | `?? DateTime.UtcNow` fallbacks. Pass `IClock` from the application service instead |

Per app, the follow-up is the same: remove the initializer, set the value from `IClock` in the command
handler, change the property to `DateTimeOffset` (`timestamptz`), and add an EF migration.

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

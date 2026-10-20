# Execution module

Execution's eCRF forms as a module of the kernel composition host ([ADR-2601](../../../docs/adr/2026-10-19-one-composition-host-for-the-kernel-and-its-modules.md), [ADR-2622](../../../docs/adr/2026-10-19-ecrf-forms-are-collection-instances-saved-in-one-command.md), #5460). The kernel holds a form's clinical truth: the `CollectionInstance`, its values as observations, the assertions, signatures and locks. This module adds what only Execution needs, the form's layout, and serves the eCRF screen's three routes. The rest of Execution (subjects, consent, queries, CEC, toxicity) still runs as the standalone [Execution service](../README.md) until it moves here too.

**Status:** forms open, read and save end to end on PostgreSQL through the kernel host. Edit checks (#5368), queries (#5461), SDV (#5465), signatures (#5463) and locks (#5466) over the HTTP surface come next.

## Project

- Project: `apps/execution/module/Cadence.Modules.Execution.csproj`. Of the kernel it references only `Cadence.Kernel.Abstractions` and the module SDK `Cadence.Kernel.Modules` (K-XS-2), plus `Cadence.Core.Domain` and `Cadence.Security`; the kernel host references it.
- `ExecutionModule.AddCadenceExecutionModule(connectionString)` registers the module with the kernel's authorization (`AddCadenceModule`), its command store in the `execution` schema, the `OpenFormInstance` handler and validator, and the event consumer. `MapExecutionModule()` maps the routes, each for a signed-in caller whose access the kernel then decides per object.
- `Domain/FormInstance`: one per kernel form instance, with the same id. It snapshots the CollectionSpec's item order from the governing protocol version when the form opens, so the screen keeps its layout.
- `Application/OpenFormOnCollectionOpened`: consumes the kernel's `CollectionInstanceOpened` event after commit and sends `OpenFormInstance` (`execution.form.open`) as `svc:execution-forms`, keyed by the delivery, so a redelivered event changes nothing twice. A kernel form the module cannot find, or that names another enrollment, is `M.Execution.Form.CollectionInstanceUnknown`.
- `Infrastructure/ExecutionModuleDbContext`: table `execution.form_instances` with Row-Level Security on `tenant_id` (`ModuleTenantIsolationSql`). `AddModuleMigrations` registers its migrations, which the host's `KernelMigrationHostedService` applies after the kernel's, and `ExecutionModuleDatabase.GrantAppRoleSql(role)` grants read, insert and update to the application role. Reads outside commands use `AddModuleTenantContext` (a no-tracking context scoped to the caller's tenant).

## HTTP surface

Every route needs the gateway-signed caller, runs in the caller's kernel tenant (`KernelTenantFilter`) and answers RFC 9457 problem details with the kernel's stable error codes. Writes are kernel commands: they need an `Idempotency-Key` header, take the reason for change only from the signed identity, and are authorized against the form's enrollment. The wire is snake_case and values are typed (ADR-2623).

| Route (`/api/v1/execution/forms…`) | Does                                                                                                                                                                                                                                                       |
| ---------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `POST` (no suffix)                 | Opens a form (`OpenCollectionInstance`, `collection.open`) for an enrollment, CollectionSpec, optional visit and collection time, under the protocol version in force. 201.                                                                                |
| `GET /{formId}`                    | The form: items in layout order with their values and revisions, `complete` and `missing_required` (every required item needs a value; a reasoned missing value counts), locks, assertions and signatures. 403 when the caller may not see its enrollment. |
| `PUT /{formId}/values`             | Saves a page (`RecordCollectedValues`, `collection.record`): each value names the revision it was edited from. 409 `K.Collection.RevisionStale` refuses the whole page; 422 `K.Collection.ValuesInvalid` names each bad item; 423 when locked.             |

The gateway forwards `/api/v1/execution/forms/**` to the kernel host (`KERNEL_SERVICE_URL`); the two-segment prefix wins over Execution's `/api/v1/execution/**` by routing precedence.

## Authorization

The coordinator needs `collection.open` and `collection.record` at the participant's study site. The consumer's service account `svc:execution-forms` needs a tenant-wide grant of `execution.form.open` in every tenant that uses forms: in Development the kernel host's demo data issues it (`DEMO_EXECUTION_FORMS`); in production the tenant's administrator issues it when the tenant starts using eCRF forms.

## Tests

- `tests/Cadence.IntegrationTests/Execution/ExecutionFormModulePostgresTests.cs` (PostgreSQL, application role): opening a kernel form opens the module's form with the specification's item order, as `svc:execution-forms` with the delivery's idempotency key; a redelivered event opens one form; unknown, mismatched and existing forms are refused.
- `tests/Cadence.IntegrationTests/Execution/ExecutionFormHostPostgresTests.cs`: the real kernel host with a signed coordinator opens a form, saves pages, derives completeness and refuses a stale page whole; a caller without a grant can neither read nor save.

# ADR-2600: Modules Write Through the Kernel Command Path

- **Status:** Accepted
- **Date:** 2026-10-19
- **Authors:** @fderuiter, Claude Code
- **Deciders:** @fderuiter (gate 2 of epic #5310)
- **Issues:** #5366, #5440, #5314 (part of #5310, gate 2)
- **Amends:** spec §2.3 K-XS-2 and §2.4 (adds `Cadence.Kernel.Modules`); ADR-2523 (contexts on the command path)
- **Requirements:** PRD-SYS-001 (audit fields and audit entries, 21 CFR Part 11 § 11.10(e)), PRD-SYS-053

---

## 1. Context & Problem Statement

The kernel command pipeline (ADR-2449) commits a command's state, provenance, lineage, events, its audit chain link,
outbox rows and its idempotency record in one PostgreSQL transaction. Only the kernel can use it:

- `ICommandHandler`, `ICommandContext` and `IKernelUnitOfWork` live in `Cadence.Kernel.Application`, which K-XS-2 forbids
  modules to reference, so a module cannot write a handler.
- `KernelCommandTransaction` writes only aggregates mapped by `KernelDbContext`. A module's records live in the module's
  own schema and `DbContext` (fold-in plan §1), so even a module handler could not commit them with the kernel's audit.
- Each scaffold service therefore built its own write path: its own audit table or interceptor, its own validation, its
  own error shapes and, in Execution, its own MassTransit outbox.

The gate 2 design (artifact `1f45ecf7-2a47-4a7c-a000-9b36be29e2eb`) proposed one path for every
clinical write, kernel or module. The project owner accepted its three recommendations on 2026-10-19.

## 2. Decision Drivers & Constraints

- **K-TX-1, K-TX-2, K-TX-3, K-TX-5:** one aggregate per command; state, audit, events and outbox in one transaction;
  idempotency key and reason on every mutation; no write without an Actor.
- **One history per record** (ADR-2620 §4.2): the kernel audit chain is authoritative for clinical mutations.
- **K-XS-2:** modules never reach kernel persistence or the kernel's inner layers.
- **Fold-in plan rev 2:** modules own their records in their own schema; no new frameworks; events never enforce
  commit-time invariants.
- Pre-deployment: no dual-write, shadow audit or back-compatibility layer.

## 3. Options Considered

### Option A: The module context enlists on the command's connection and transaction (Selected)

- **Pros:** ✅ The module change, the kernel audit link, events, outbox and idempotency commit or fail together.
  ✅ Modules keep ownership of their records and schema. ✅ No second history and no reconciliation job.
- **Cons:** ❌ Every module shares one PostgreSQL database; moving a module to its own database later needs a new boundary
  decision.

### Option B: Module records become kernel aggregates in `KernelDbContext`

- **Cons:** ❌ Breaks K-SCOPE-1 and the fold-in ownership rules; the kernel grows with every product.

### Option C: The module commits alone and sends an event the kernel audits

- **Cons:** ❌ A crash between the two leaves an unaudited clinical change. ❌ Two histories and a reconciliation job.

### Option D: A distributed (two-phase) transaction across the contexts

- **Cons:** ❌ Prepared-transaction operations for no gain over Option A inside one database.

## 4. Decision Outcome

- **Chosen Option:** Option A.

### 4.1 The module SDK (`Cadence.Kernel.Modules`)

A new kernel assembly holds the contracts every handler, kernel or module, implements: `ICommandHandler<TCommand,
TResult>`, `ICommandContext` and `IKernelUnitOfWork`, plus `AddCommandHandler<TCommand, TResult, THandler>()`. Kernel
handlers use the same contracts, so there is one handler model. K-XS-2 now reads: applications reference
`Cadence.Kernel.Abstractions` and `Cadence.Kernel.Modules` only, never Domain, Application, Persistence or Api;
`KernelBoundaryTests` and `KernelLayeringTests` enforce both halves. IdentityVault staging stays kernel-only
(`IIdentityVaultStaging` in Application).

### 4.2 One transaction across the kernel and a module

`KernelCommandTransaction` routes each aggregate type to the store that maps it: `KernelDbContext`, or a module context
the module registers for the command path. A module context is created on the command's open, tenant-scoped connection
and enlisted in the kernel's transaction (`Database.UseTransaction`), so two contexts are one PostgreSQL transaction.
The commit stamps the GxP fields on the one changed aggregate, writes the module rows with the version predicate,
then lineage, events, the audit chain link, outbox rows and the idempotency record, and commits. A failure at any stage
rolls back both schemas; the failure-injection hook gains a module-write stage that integration tests use.

Contexts on the command path register **no** retrying execution strategy, as `KernelDbContext` already does: a retry
cannot replay a command's transaction, so the client retries the whole command with the same idempotency key
(K-TX-3). This is the one exception to ADR-2523, which keeps applying to module contexts used outside commands.

### 4.3 Audit ownership

A module aggregate written through a command is audited on the tenant's kernel chain, with a namespaced aggregate type
(for example `execution.FormRecord`), as ADR-2620 does for observations. A module context refuses `SaveChanges`
outside a command for any `IGxPAuditable` type, the rule `KernelDbContext` already enforces. The host's audit
interceptor (#5312) covers only non-clinical module data.

### 4.4 Validation

The pipeline runs FluentValidation `IValidator<TCommand>` for every command after resolving the actor and before
authorization, and fails with `K.Command.Invalid` (422) and field errors. A command with nothing to validate beyond the
pipeline-wide checks (tenant, idempotency key, reason) carries `[NoValidator("why")]`; a catalog test fails when a
command has neither. Domain invariants stay in the handler. .NET 10 endpoint validation is not used: commands are also
sent by event consumers and jobs, which never pass an endpoint.

Validators register with `AddCommandValidator<TCommand, TValidator>()` and check shape only, so the stage opens no
database work; field errors travel on `KernelError.Errors`. The catalog check is `CommandValidationCatalog` in
`Cadence.Testing`, which the kernel's and each module's tests call. The kernel's existing commands check their inputs
in their handlers and return registered domain errors that clients already rely on; they carry
`[NoValidator(NoValidatorAttribute.HandlerChecksInputs)]` until #5448 moves their shape checks into validators.

### 4.5 Errors

Modules register their errors in the same closed `KernelError` registry with an `M.<Module>.` code prefix. Each command
declares the codes its handler can return; endpoints that send commands declare the pipeline-wide codes plus those in
OpenAPI, and tests fail when a handler returns an undeclared code.

Implemented (#5366) as follows. A command lists its errors with `[RaisesKernelErrors(typeof(Registry), nameof(…))]`, so
a renamed or removed field fails the build. The pipeline-wide set is `KernelErrors.CommandPipeline`. In the module SDK,
`.ProducesKernelCommand<TCommand>()` declares both sets on the endpoint: a problem response per status, the codes in
each response's description, and the `x-kernel-errors` extension on the operation. Mapping a command that declares
nothing throws. `KernelErrorEndpoints.Problem` renders an error as problem details with its `code` member.
`KernelErrorCatalog` collects every registered error of the kernel's and the modules' assemblies and reports malformed
codes and clashes. A unit test reads the published `kernel_openapi.json` and fails when a kernel endpoint that takes a
body declares no kernel errors or names an unregistered one; the two in-memory protocol computations are exempt
because they call no kernel service. The test sender `AddDeclaredKernelErrorsCheck()` (`Cadence.Testing`) throws when a
command that declares its errors returns another. The kernel's other commands declare theirs as they gain endpoints or
validators (#5448).

### 4.6 After commit

The host runs the outbox relay. A consumer reacts to an event by sending a command as a named service account with
idempotency key `<consumer>:<event id>`, so the existing idempotency table is the consumer inbox: a redelivered event
replays the stored outcome instead of acting twice. Multi-aggregate work is a saga of such commands (K-TX-1).

## 5. Consequences & Trade-offs

- ✅ A new module inherits actor resolution, authorization, validation, idempotency, audit, lineage and the outbox
  instead of rebuilding them.
- ✅ One audit history per record, in one tamper-evident chain per tenant.
- ❌ The per-tenant chain head serialises commits within a tenant; partition it per study if a tenant's commit rate
  approaches the ceiling (a spec change raised on #4719 first).
- ❌ All modules share one database (Option A).

## 6. Implementation & Verification

1. `Cadence.Kernel.Modules` with the shared contracts; kernel handlers moved onto it (#5440).
2. Module context enlistment, the module-write failure stage and a test module proving atomicity and the version check
   against PostgreSQL as the application role (#5366).
3. The validation stage, `[NoValidator]` and the catalog test; declared error codes in OpenAPI (#5366).
4. Background jobs and in-process event delivery in the host (#5313, #5314).

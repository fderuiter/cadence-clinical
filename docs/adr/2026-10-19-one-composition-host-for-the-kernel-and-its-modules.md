# ADR-2601: One Composition Host for the Kernel and Its Modules

- **Status:** Accepted
- **Date:** 2026-10-19
- **Authors:** @fderuiter, Claude Code
- **Deciders:** @fderuiter (gate 2 of epic #5310)
- **Issues:** #5315, #5313 (part of #5310, gate 2)
- **Amends:** ADR-2244 (how the modular monolith is hosted), ADR-2540 (the kernel API host)
- **Requirements:** PRD-SYS-053

---

## 1. Context & Problem Statement

ADR-2244 chose a modular monolith, and ADR-2610 decided that all 17 scaffold services become `Cadence.Modules.*` in one
host. What runs today is 19 processes: the AppHost starts each service separately, and only `Cadence.Kernel.Host`
(`apps/kernel-api`) registers kernel persistence and the command pipeline. The kernel's background jobs
(`AddKernelBackgroundJobs`: outbox relay, audit chain sealing, lineage check) are never registered, so committed events
are never delivered (#5313). A module and the kernel cannot share a process or a transaction, so ADR-2600 has nowhere to
run.

## 2. Decision Drivers & Constraints

- ADR-2600: a module command commits on the kernel's connection and transaction, which needs one process.
- Fold-in plan §1: each module exposes one `AddCadence<Name>Module` and one `Map<Name>Module` and has no `Program.cs`.
- The relay keeps each tenant's event order exactly only when one relay runs; sealing must not run twice concurrently.
- Keep the stack (ASP.NET Core, Aspire, Keycloak gateway); no new frameworks.

## 3. Options Considered

### Option 1: The kernel host becomes the composition host (Selected)

- **Pros:** ✅ It already registers kernel persistence, the pipeline, authorization, visibility and the tenant resolver.
  ✅ One place where modules plug in.
- **Cons:** ❌ The gateway, AppHost and docs route to it under a new name.

### Option 2: A new host project beside the kernel host

- **Cons:** ❌ Two hosts with kernel persistence again, and duplicated registration.

### Option 3: Keep one process per module and call the kernel over HTTP

- **Cons:** ❌ No shared transaction, so ADR-2600 is impossible; contradicts ADR-2244 and ADR-2610.

## 4. Decision Outcome

- **Chosen Option:** Option 1.

1. `apps/kernel-api` becomes the composition host: it registers the kernel, then each module through
   `AddCadence<Name>Module(builder)`, and maps each module's routes through `Map<Name>Module(app)`. Execution is the first
   module moved in (gate 3).
2. The host calls `AddKernelBackgroundJobs()` and registers the in-process event publisher (ADR-2600 §4.6). The outbox
   relay and audit sealing each hold a PostgreSQL advisory lock as a leader lock while they run a pass, so with several
   replicas exactly one runs each; the others skip that pass.
3. A module declares itself with `AddCadenceModule(name, commandAssembly)`; the kernel's command catalogue then
   includes the capabilities its commands declare, so module commands are authorized against kernel grants exactly
   like kernel commands, and fail closed without a declaration (#5449).
4. The gateway routes kernel and module routes to the host. A single module manifest replaces the six hand-kept copies
   of the service topology (gateway, AppHost, compose, `ARCHITECTURE.md`, `LOCAL_DEV_ENVIRONMENT.md`,
   `ENVIRONMENT_VARIABLES.md`) as modules move in.

## 5. Consequences & Trade-offs

- ✅ Every module and the kernel share a process, a connection pool and a transaction.
- ✅ Committed events are delivered; audit chains are sealed in the running system, not only in tests.
- ❌ A defect in one module can take down the host; module isolation becomes a code-review and test concern, and heavy
  work runs in isolated workers (fold-in plan §1).

## 6. Implementation & Verification

1. Background jobs, leader locks and in-process event delivery in the host, with an integration test that a committed
   event reaches a consumer once in effect (#5313, #5314).
2. Module registration seams in the host and the manifest (#5315); Execution moves in under gate 3.

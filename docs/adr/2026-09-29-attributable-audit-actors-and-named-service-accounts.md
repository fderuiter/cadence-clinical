# ADR-2257: Attributable Audit Actors and Named Service Accounts

- **Status:** Accepted
- **Date:** 2026-09-29
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Some writes were attributed to nobody, and some were not audited at all (#4767):

- `HttpCurrentAuditContextAccessor` returned `"system"` whenever a request had no `sub` claim, so any write from outside a request was stamped `"system"`. That included the eConsent consumer's GxP interceptor entries.
- `Cadence.Security.Audit.GxPAuditInterceptor` defaulted its user to `"system"`.
- 21 entity properties in payments, imaging and interop defaulted `CreatedBy` or `ReaderId` to `"system"`.
- Deleting subject demographics and applying the eTMF compliance webhook wrote no semantic audit entry.

21 CFR 11.10(e) and ALCOA+ require every change to be attributable to the person or process that made it.

## 2. Decision Drivers & Constraints

- **Attribution (PRD-SYS-053):** every audit entry must name a real actor.
- **Fail closed:** a write with no resolvable actor must fail, not fall back to a placeholder.
- **Background work is legitimate:** consumers and jobs have no caller, but they act on the system's behalf and must say so explicitly.

## 3. Options Considered

1. **Keep a default actor and label it better** (for example `"unknown"`). Rejected: still records changes against nobody.
2. **Fail on a missing actor and let background work declare a named service account.** Chosen.
3. **Give every consumer its own DI container or bus.** Rejected: heavy, and MassTransit cannot override one shared registration per consumer type.

## 4. Decision Outcome

1. **No anonymous fallback.** `ICurrentAuditContextAccessor.UserId` returns the authenticated caller's `sub`, or throws `AuditActorUnresolvedException`. The security package's interceptor throws for the same reason. Authenticated HTTP callers never hit this, because the gateway scheme rejects unauthenticated requests with 401 first; anything else surfaces as a 500.
2. **Named service accounts.** Background work declares a `ServiceAccount` whose id must start with `svc:`, through `IAuditActorScope.RunAs` at the start of its scope. The eConsent consumer runs as `svc:econsent-consumer`, replacing the legacy `system-pubsub`. One scoped `AmbientAuditContextAccessor` implements both interfaces, so the EF interceptor sees the declared account.
3. **Actor resolved only when needed.** `Cadence.Database.GxPAuditInterceptor` resolves the actor only when the save contains an audited change, so saves with nothing to audit (outbox bookkeeping) need no actor.
4. **Every execution write is audited.** Subject demographics deletion writes `DELETE_DEMOGRAPHICS`, and the eTMF webhook writes `ETMF_COMPLIANCE_UPDATE`, each in the same save as the change.
5. **No default actors in entities.** `CreatedBy` and `ReaderId` in payments, imaging and interop are `required`, so the compiler forces every construction to name one. `AuditAttributionSentinelTests` fails the build on any new actor-named member or local that defaults to a `"system…"` literal.

## 5. Consequences & Trade-offs

- Positive: every audit entry names a person or a named service account, and a missing actor is a visible failure.
- Negative: new background work must declare a service account before writing, or its saves fail. Tests that seed data outside a request use a `svc:test-seeder` scope.
- The legacy golden-master expectation for SCN-009-A changes from `system-pubsub` to `svc:econsent-consumer`.
- Long term, kernel rule K-PROV-1 (every write has a provenance activity with an actor) supersedes this mechanism.

## 6. Implementation & Verification

- `apps/execution`: `AmbientAuditContextAccessor`, `ServiceAccount`/`ServiceAccounts`, `IAuditActorScope`; the consumer calls `RunAs`; the two new semantic audit entries.
- `packages/database` and `packages/security`: interceptors with no `"system"` default.
- Tests: `CurrentAuditContextAccessorTests`, `ExecutionAuditAttributionTests` (every mutating route over HTTP is audited against the signed caller), `GxPAuditTrailGenerationTests` (a save with no actor fails), `AuditAttributionSentinelTests`, and the golden-master SCN-009-A assertion. PRD-SYS-053.

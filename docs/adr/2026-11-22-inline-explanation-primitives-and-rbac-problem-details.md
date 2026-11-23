# ADR-2660: Inline Explanation Primitives and RBAC Problem Details

- **Status:** Accepted
- **Date:** 2026-11-22
- **Requirements:** PRD-SYS-001, PRD-CRF-015

## 1. Context

ADR-2482 defined the Clinical Kernel `Explanation` structure (`subject`, `claim`, `producer`, `steps`, `conditions`, `next_actions`), yet blocked clinical actions still lacked populated `NextAction.Target` links, endpoint-boundary `INodeVisibility` masking (`K-EXP-2`), standalone HTTP 403 `ProblemDetails.explanation` payloads, and shared inline UI primitives.

## 2. Decision

1. Populate `NextAction.Target` and `NextAction.Capability` across `AuthorizationDecision.Explain`, `CaptureConditions`, `LocksInForce` (HTTP 423), and subscription refusals, and replace raw actor and `ProtocolVersionId` UUIDs in `AuthorityEvaluator` with sequential protocol version numbers and `NodeRef` evidence.
2. Apply bulk `INodeVisibility` masking (`masked:<kind>:<Reason>`) via `IKernelExplanationMasker` across `can-i`, `commands`, and kernel HTTP error responses while keeping persistent `AuditEntries` unredacted (`K-EXP-2`).
3. Register `CadenceAuthorizationMiddlewareResultHandler` and `RbacExplanationBuilder` in `Cadence.Security` (and scope guards in `ClinicalScopeGuard`, `CtmsScopeGuard`, `Etmf`, and `Execution`) so all protected standalone routes emit HTTP 403 `ProblemDetails` with `explanation` (`K-XS-1`, `K-XS-2`).
4. Export `EntityLink`, `ClinicalExplanation`, and `ClinicalGatedAction` from `packages/ui`, wired to a scope-preserving peek drawer and 403/422/423 toast error explanations in `apps/web`.

## 3. Consequences

- Blocked controls remain visible in place with plain-English clinical explanations and clickable `NodeRef` targets without leaking restricted or blinded identifiers.
- Standalone services and kernel endpoints share one uniform `ProblemDetails.explanation` wire contract.

## 4. Alternatives Considered

- Migrating standalone RBAC tables into kernel bitemporal grant tables was deferred as out of scope to preserve `K-XS-1`/`K-XS-2` service database boundaries.
- Hiding gated controls behind `v-if` or hover-only tooltips was rejected for tablet-first clinical usability.

## 5. Implementation Notes

- Wire masking lives in `Cadence.Kernel.Abstractions/Explanations/ExplanationMasking.cs`.
- Standalone RBAC explanation synthesis lives in `packages/security/src/Rbac/RbacExplanationBuilder.cs`.
- Shared Vue components live in `packages/ui/src/components/clinical/`.

## 6. Verification

- Verified by `Cadence.ArchitectureTests`, `Cadence.UnitTests`, `Cadence.IntegrationTests`, and `packages/ui` + `apps/web` Vitest suites.

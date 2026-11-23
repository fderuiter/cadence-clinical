# ADR-2657: Inline Explanation Primitives and RBAC Problem Details

- **Status:** Accepted
- **Date:** 2026-11-22
- **Requirements:** PRD-SYS-001, PRD-CRF-015

## 1. Context & Problem Statement

ADR-2482 defined the Clinical Kernel `Explanation` structure (`subject`, `claim`, `producer`, `steps`, `conditions`, `next_actions`), yet blocked clinical actions still lacked populated `NextAction.Target` links, endpoint-boundary `INodeVisibility` masking (`K-EXP-2`), standalone HTTP 403 `ProblemDetails.explanation` payloads, and shared inline UI primitives.

## 2. Decision Drivers & Constraints

- Regulated clinical users on tablets and desktop workspaces must see why an action is gated in plain clinical English without mystery disabled buttons or raw permission tokens.
- Referenced `NodeRef` evidence and `NextAction.Target` links must never leak blinded or out-of-scope identifiers (`K-EXP-2`), while persistent `AuditEntries` remain unredacted.
- Standalone services must preserve their independent database and authorization boundaries (`K-XS-1`, `K-XS-2`) while matching the kernel `ProblemDetails.explanation` wire shape.

## 3. Options Considered

1. **Populate `NextAction.Target`, enforce wire-boundary `INodeVisibility` masking, synthesize standalone RBAC 403 `Explanation`s, and ship shared `packages/ui` primitives (`EntityLink`, `ClinicalExplanation`, `ClinicalGatedAction`)** — Chosen.
2. **Migrate standalone RBAC tables into kernel bitemporal grant tables** — Rejected as out of scope to preserve `K-XS-1` and `K-XS-2` service database boundaries.
3. **Hide gated controls behind `v-if` or hover-only `title` tooltips** — Rejected for tablet-first clinical usability and WCAG 2.2 AA accessibility.

## 4. Decision Outcome

1. Populate `NextAction.Target` and `NextAction.Capability` across `AuthorizationDecision.Explain`, `CaptureConditions`, `LocksInForce` (HTTP 423), and subscription refusals, and replace raw actor and `ProtocolVersionId` UUIDs in `AuthorityEvaluator` with sequential protocol version numbers and `NodeRef` evidence.
2. Apply bulk `INodeVisibility` masking (`masked:<kind>:<Reason>`) via `IKernelExplanationMasker` across `can-i`, `commands`, and kernel HTTP error responses while keeping persistent `AuditEntries` unredacted (`K-EXP-2`).
3. Register `CadenceAuthorizationMiddlewareResultHandler` and `RbacExplanationBuilder` in `Cadence.Security` (and scope guards in `ClinicalScopeGuard`, `CtmsScopeGuard`, `Etmf`, and `Execution`) so all protected standalone routes emit HTTP 403 `ProblemDetails` with `explanation` (`K-XS-1`, `K-XS-2`).
4. Export `EntityLink`, `ClinicalExplanation`, and `ClinicalGatedAction` from `packages/ui`, wired to a scope-preserving peek drawer and 403/422/423 toast error explanations in `apps/web`.

## 5. Consequences & Trade-offs

- Blocked controls remain visible in place with plain-English clinical explanations and clickable `NodeRef` targets without leaking restricted or blinded identifiers.
- Standalone services and kernel endpoints share one uniform `ProblemDetails.explanation` wire contract without coupling standalone persistence to kernel grant tables.

## 6. Implementation & Verification

- Wire masking lives in `packages/kernel/src/Cadence.Kernel.Abstractions/Explanations/ExplanationMasking.cs`.
- Standalone RBAC explanation synthesis lives in `packages/security/src/Rbac/RbacExplanationBuilder.cs`.
- Shared Vue components live in `packages/ui/src/components/clinical/`.
- Verified by `Cadence.ArchitectureTests`, `Cadence.UnitTests`, `Cadence.IntegrationTests`, and `packages/ui` + `apps/web` Vitest suites.

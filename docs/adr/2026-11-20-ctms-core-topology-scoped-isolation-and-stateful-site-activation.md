# ADR-2642: CTMS Core Topology, Scoped Isolation, and Stateful Site Activation

- **Status:** Accepted
- **Date:** 2026-11-20
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** @req:PRD-SYS-192, @req:PRD-SYS-046

---

## 1. Context & Problem Statement

The CTMS service (`apps/ctms`) already serves operational endpoints for Startup Greenlight evaluation and certification, Site Milestones, Monitoring Visits, and Delegation of Authority (DoA) logs, while Execution (`apps/execution`) serves RTSM kit accountability, autologous chain-of-custody, randomization schemes, and site activation. However, `ctms` remained marked `Core: false` in `ServiceTopology`, omitted from OpenAPI TypeScript code generation, lacked fail-closed caller study/site scope guards on several endpoints, and hid its real API workspaces in non-demo web builds. In addition, `SiteComplianceCache` recorded eTMF document compliance without persisting whether a site had actually been activated, causing `SiteActivationPanel` to forget activation state across page reloads.

## 2. Decision Drivers & Constraints

- **Core clinical availability:** Non-demo deployments require Startup Greenlight, Milestones, Monitoring Visits, Delegation Logs, and Investigational Supply workspaces backed by live `.NET` endpoints.
- **Fail-closed study and site isolation (ADR-2516):** Every CTMS endpoint must evaluate `CallerScope`, `StudyAccessEvaluator`, and `SiteAccessEvaluator` via a shared `EndpointScopeGuard` and return HTTP `403` (`FORBIDDEN_SCOPE`) outside assigned scope.
- **Separation of duties:** `site_compliance:activate` and `ctms_greenlight:certify` remain restricted to `ROLE_SYSADMIN` and `ROLE_SPONSOR_DM`, while `ctms_doa:sign_off` remains restricted to `ROLE_SYSADMIN` and `ROLE_INVESTIGATOR`.
- **Stateful Part 11 site activation:** Site activation must persist `IsActivated`, `ActivatedAt`, and `ActivatedBy` on `SiteComplianceCache` while preserving `ETMF_COMPLIANCE_UPDATE` and `BLOCKED_ACTIVATION` audit invariants.

## 3. Options Considered

### Option 1: Keep CTMS Optional and Derive Activation Only in Client State

- **Overview:** Leave `ctms` as an optional profile service and track site activation transiently in Vue component refs.
- **Pros:**
  - ✅ Requires no database migration or topology update.
- **Cons:**
  - ❌ Reloading `/ctms` or Clinical Canvas forgets site activation and invites duplicate activation attempts.
  - ❌ Core `dotnet run --project apps/apphost/src` omits `ctms`, leaving `/api/v1/ctms/*` unreachable in default stacks.

### Option 2: Promote CTMS to Core, Share EndpointScopeGuard, and Persist Activation State (Selected)

- **Overview:** Promote `ctms` to `Core: true` in `ServiceTopology` and AppHost, generate `src/api/generated/ctms.ts` wrapped by `ctmsApi.ts` (ADR-2610), enforce study/site scope via `Cadence.Security.Rbac.EndpointScopeGuard`, and persist `IsActivated`, `ActivatedAt`, and `ActivatedBy` on `SiteComplianceCache`.
- **Pros:**
  - ✅ Default AppHost stacks boot `ctms` alongside `execution`, and OpenAPI contract drift fails at build time.
  - ✅ Activation is idempotent and auditable across reloads while read-only roles see status without mutation controls.
- **Cons:**
  - ❌ Adds three columns and an EF Core migration to `execution.site_compliance_cache`.

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Promoting `ctms` to core topology, centralizing `EndpointScopeGuard` in `Cadence.Security`, and persisting activation state on `SiteComplianceCache` delivers end-to-end GxP traceability, separation of duties, and zero-mock clinical operations outside demo builds while keeping unbacked grant financials, CRA allocation, recruitment mutations, and adaptive dose escalation gated behind `#4829`.

## 5. Consequences & Trade-offs

- **Positive Impact:** Clinical Canvas (`Start` and `Conduct`) and `/ctms` / `/ssu` expose real Startup Readiness, Site Milestones, Monitoring Visits, Delegation Log, and Investigational Supply workflows with UX-9 scope guidance on HTTP `403` (`FORBIDDEN_SCOPE`).
- **Negative Impact / Technical Debt:** Grant financials, CRA allocation, recruitment target edits, and oncology dose escalation remain deferred to `#4829`.
- **Mitigation Strategy:** Explicit non-demo scope banners link users to `/plans` (`ADR-2547`) while engineering tracking remains under `#4829`, preventing unbacked client-only mutations.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/topology`, `packages/security`, `apps/apphost`, `apps/ctms`, `apps/execution`, `apps/web`, `docs/openapi`, `docs/schema`.
- **Verification Plan:** Verified via xUnit scope/RBAC/idempotency suites in `Cadence.UnitTests`, `Cadence.ArchitectureTests`, `cadence check` sentinels, and Vitest suites in `apps/web`.

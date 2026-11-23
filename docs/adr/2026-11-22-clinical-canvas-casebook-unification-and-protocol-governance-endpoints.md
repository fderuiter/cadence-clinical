# ADR-2606: Clinical Canvas Casebook Unification and Kernel Protocol Governance Endpoints

- **Status:** Accepted
- **Date:** 2026-11-22
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-SYS-001, PRD-SYS-004, PRD-SYS-192

---

## 1. Context & Problem Statement

Clinical data capture and protocol go-live workflows previously spanned split surfaces or lacked HTTP transport bindings over existing kernel domain commands:

1. **Split Conduct Casebook Navigation:** Outside static demo mode (`VITE_DEMO_MODE !== "true"`), `/ecrf` routes, onboarding launchers, and standalone handoff links still navigated staff away from the unified Clinical Canvas Conduct workspace (`#casebook-matrix`), fragmenting participant casebook context.
2. **Missing Kernel Protocol Governance HTTP Surface:** While `Cadence.Kernel` already defined domain commands for protocol version review (`SubmitProtocolVersionForReview`, `ReturnProtocolVersionToDraft`), 21 CFR Part 11 attested approval (`Attest` + `ApproveProtocolVersion`), immutable evidence artifacts (`RegisterArtifact`), bitemporal effectivity (`DeclareEffectivity`, `ChangeEffectivityStatus`), and study-site linking/activation (`LinkStudySite`, `ActivateStudySite`), `Cadence.Kernel.Api` did not expose HTTP endpoints or read models for sponsors and site leads to execute end-to-end protocol go-live from the Clinical Canvas, nor were these routes mapped in `ProductFamilyGuardMiddleware` and `ExpiredSubscriptionWriteGuardMiddleware` (`ADR-2605`).
3. **Siteless Participant Assignment Gap:** Subjects created prior to site placement in Execution could not be assigned to a clinical site with an immutable audit record and synchronous kernel screening synchronization (`SynchronizeScreeningEnrollmentAsync`).

## 2. Decision Drivers & Constraints

- **Unified Clinical Canvas Conduct (`PRD-SYS-001`):** All non-demo clinical form entry and casebook navigation must resolve into `/studies/:studyCode` (`stage=conduct`) while preserving standalone `/ecrf` persona walkthroughs when `VITE_DEMO_MODE === "true"`.
- **21 CFR Part 11 Step-Up Signatures & Commercial Posture Parity (`PRD-SYS-004`, `PRD-SYS-192`, `ADR-2452`, `ADR-2456`, `ADR-2605`):** `POST /api/v1/kernel/protocol-versions/{versionRef}/approve` must verify `ITenantEntitlementReader` (`IsReadOnly` -> `403 K.Subscription.Expired` and `ProductFamily.Blueprint` -> `403 K.Commercial.ProductFamilyNotEntitled`) and `protocol.approve` authority before consuming single-use `X-Sig-Token` step-up credentials or dispatching `[BaselineKernelCommand]` `Attest` bound to the frozen protocol `ContentHash`.
- **Product-Family Middleware Parity (`ADR-2605`):** `ProductFamilyGuardMiddleware.ResolveKernelFamilies` must mirror `KernelCommandPipeline.ResolveRequiredCommandFamilies` across study artifact routes (`AllFamilies`), effectivity and study-site routes (`[Blueprint, Capture, Orbit]`), and protocol lifecycle routes (`[Blueprint]`).
- **Kernel Boundary Hygiene (`K-XS-1`):** Execution DTOs for audited subject site assignment (`PATCH /api/v1/execution/subjects/{subjectId}/site`) must preserve wire compatibility (`site_id`) without introducing prohibited public `SiteId` C# property names outside the kernel.

## 3. Options Considered

### Option 1: Keep Standalone `/ecrf` Navigation and Manage Protocol Go-Live Only via Seed Scripts

- **Overview:** Leave `/ecrf` active in production builds and rely on seeded database state for approved protocol versions, effectivities, and activated study sites.
- **Pros:**
  - Avoids adding HTTP endpoints, OpenAPI schemas, or Canvas governance controls.
- **Cons:**
  - Self-service trial workspaces (`ADR-2547`) and sponsors importing protocols (`ADR-2476`) cannot approve protocol versions, register regulatory/ethics evidence artifacts, declare site effectivity, or open governed collection instances (`409 K.Collection.NoGoverningVersion`) through the UI and API.

### Option 2: Unify Non-Demo Conduct on Clinical Canvas and Expose Kernel Governance & Audited Site Assignment Endpoints (Selected)

- **Overview:** Redirect non-demo `/ecrf` navigation into Clinical Canvas (`ecrfRedirect.ts`) while gating demo walkthrough affordances behind `isDemoBuild()`; expose `KernelProtocolGovernanceEndpoints` and `IStudyGovernanceReader` (`KernelStudyGovernanceReader`) over `KernelCommandPipeline` with full `ADR-2605` subscription-expiry, product-family, `Idempotency-Key`, `X-Change-Reason`, and `X-Sig-Token` enforcement; align `ProductFamilyGuardMiddleware` and onboarding/demo role capabilities (`participant.register`, `artifact.register`, `artifact.add_version`); and add audited `ClinicalSubject.AssignSite` (`PATCH /api/v1/execution/subjects/{subjectId}/site`) with automatic kernel screening synchronization.
- **Pros:**
  - Delivers one governed Conduct casebook experience in production builds without breaking static demo persona walkthroughs.
  - Exposes complete protocol review, Part 11 signed approval, hash-addressed evidence registration, effectivity transitions, and study-site activation through `KernelCommandPipeline` with zero orphan attestations on expired or unentitled tenants.
  - Synchronizes siteless subject assignment into kernel screening enrollments within existing service boundaries (`K-XS-1`, `K-XS-2`).
- **Cons:**
  - Adds new kernel governance HTTP endpoints and generated OpenAPI client methods that must stay synchronized with `ProductFamilyGuardMiddleware` and `ExpiredSubscriptionStandaloneServicesTests`.

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Routing non-demo Conduct flows into Clinical Canvas and binding `KernelProtocolGovernanceEndpoints` directly to `KernelCommandPipeline`, `SigTokenVerifier`, `ITenantEntitlementReader`, and `ProductFamilyGuardMiddleware` satisfies Part 11 attestation integrity, `ADR-2605` commercial enforcement, and bitemporal effectivity governance without duplicating domain logic.

## 5. Consequences & Trade-offs

- **Positive Impact:** Sponsors and trial workspaces can take imported draft protocols through review, Part 11 electronic signature approval, approval artifact registration, site effectivity go-live, kernel study-site activation, and siteless participant site assignment directly in Clinical Canvas; expired or unentitled workspaces fail closed (`403`) before step-up token consumption or attestation persistence.
- **Negative Impact / Technical Debt:** Protocol approval requires two sequential kernel commands (`Attest` followed by `ApproveProtocolVersion`) sharing pre-validated authority and entitlement checks inside `ApproveProtocolVersionAsync`.
- **Mitigation Strategy:** Pre-flight `ITenantEntitlementReader`, `IKernelAuthorization`, `ProtocolVersionStatus.InReview`, and `SigTokenVerifier` checks execute before either command runs, covered by `KernelProtocolGovernanceEndpointsTests` and `ExpiredSubscriptionStandaloneServicesTests`.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/kernel` (`Abstractions`, `Persistence`, `Api`), `packages/security` (`ProductFamilyGuardMiddleware`), `apps/kernel-api` (`Program.cs`, `TrialOnboardingService.cs`, `DevelopmentDemoData.cs`), `apps/execution` (`ClinicalSubject`, `SubjectApplicationService`, `SubjectEndpoints`), `packages/demo-fixtures`, `apps/web` (`router`, `canvas`, `ProtocolVersionsSection.vue`, `SiteActivationPanel.vue`, `ParticipantSiteAssignment.vue`), `docs/openapi/kernel_openapi.json`, `docs/api/kernel.md`.
- **Verification Plan:** Verified via `KernelProtocolGovernanceEndpointsTests`, `ExpiredSubscriptionStandaloneServicesTests`, `CommercialAndOnboardingUnitTests`, `SubjectApplicationServiceAssignSiteTests`, `SubjectEndpointsRoutingTests`, `Cadence.ArchitectureTests`, `apps/web` Vitest suites, and Cadence CLI quality sentinels (`adrs`, `openapi`, `api-clients`, `demo-manifest`, `baseline-ratchet`, `public-bundle`).

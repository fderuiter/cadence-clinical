# Cadence Clinical project plan

Cadence is in active development with no deployment, live clinical records, or
production users. The delivery goal is a fast, trustworthy path from approved
protocol to database lock. Start with one synthetic medical-device study and
carry it through study build, site execution, discrepancy resolution, and
evidence handoff.

Use [Goals and priorities](GOALS_AND_PRIORITIES.md) to select work. The
[kernel specification](kernel/CLINICAL_KERNEL_SPECIFICATION.md) governs clinical
semantics, the [architecture](../ARCHITECTURE.md) describes the runtime, and the
[five-gates epic](https://github.com/fderuiter/cadence-clinical/issues/5310)
tracks acceptance evidence. GitHub issues own item status.

## Architecture and scope

`fderuiter/cadence-clinical` is the implementation, integration, and release
source. Domain modules converge on one .NET composition host and one PostgreSQL
database through the existing fold-in work. Keep each module responsible for its
own workflows and data, with the kernel owning canonical clinical facts,
authorization, audit, attestations, and locks. Clinical mutations use the shared
command path and transaction. Modules exchange public contracts and durable
events. [ADR-2603](adr/2026-11-19-keep-cadence-in-one-repository.md) ·
[ADR-2600](adr/2026-10-19-modules-write-through-the-kernel-command-path.md) ·
[ADR-2610](adr/2026-10-15-scaffold-services-become-modules-merge-or-are-deleted.md)

The current delivery scope covers protocol and study design, site and
participant readiness, capture, oversight, and clinical evidence handoff.
Billing and site finance, recruitment, and downstream biostatistical analysis
are outside this plan. The
[commercial product catalog](strategy/PRODUCT_CATALOG.md) records longer-term
packaging hypotheses and the
[kernel roadmap](kernel/CLINICAL_KERNEL_SPECIFICATION.md#15-roadmap) records
broader platform capabilities.

## Module ownership

The paths below are implementation starting points. Some contain multiple
domains or standalone services being folded into the kernel host. Use the
accepted ownership decisions when a file spans domains.
[ADR-2602 ownership decisions](adr/2026-10-28-multi-repository-ownership-and-kernel-package-boundaries.md#61-open-items-after-acceptance)
· [Current service inventory](../ARCHITECTURE.md#3-running-services-today)

- **Core** (`packages/kernel/`): Canonical protocol, study, site, participant,
  clinical observations, artifacts, scoped grants, consent status, temporal
  history, audit and attestations.
- **Workspace** (`apps/kernel-api/`, `apps/gateway/`, `apps/web/`,
  `apps/notifications/`, `apps/intelligence/`, `packages/ui/`): Host, gateway,
  staff experience, shared UI, delivery, and governed assistant proposals.
- **Blueprint** (`apps/designer/`, `apps/knowledge/`, `packages/ddf/`): Protocol
  authoring, study build, reusable collection specs, amendment impact, and
  standards governance workflows.
- **Capture** (`apps/execution/module/`, parts of `apps/execution/` and
  `apps/interop/`): eCRF workflows, correction, query handling, source
  verification, and eSource intake.
- **Orbit** (`apps/ctms/`, `apps/org/`, `apps/etmf/`, `apps/eisf/`): Site
  readiness, monitoring, training and delegation administration, and essential
  documents.
- **Engage** (`apps/econsent/`, `apps/subject-portal/`, parts of
  `apps/execution/` and `apps/interop/`): Participant enrollment and consent
  workflows, portal, and participant-reported outcomes.
- **Allocate** (allocation, supply, device, and custody code in
  `apps/execution/`): Randomization, scope-controlled unblinding, supply, device
  accountability, and custody workflows.
- **Vigil** (`apps/safety/`, clinical event committee code in
  `apps/execution/`): Safety cases, serious-event reconciliation, and clinical
  event committee workflows.
- **Lens** (`apps/imaging/`, lab and telemetry code in `apps/execution/` and
  `apps/interop/`): Lab, specimen, imaging, and scientific device evidence,
  including reference ranges and reconciliation.
- **Assure** (`apps/quality/` and quality-related kernel contracts): Quality
  review, deviations, corrective and preventive action, lock-readiness views,
  and inspection evidence.

Core owns authoritative grants and qualifications. Orbit administers the
associated records. Engage owns the participant workflow and Core owns identity
and consent status. Allocate owns investigational device assignment and custody,
Lens owns scientific device data and lab reference ranges, and Vigil owns
clinical event committee workflows. Capture owns the eCRF query workflow and
refers to canonical observations and revisions. Assure surfaces lock blockers,
while Core enforces a lock at the write boundary.

## Delivery sequence

1. **Establish an executed baseline:** Reproduce mandatory PostgreSQL scenarios
   locally, repair skip-to-qualification behavior, and record the tested
   revision, configuration, commands, and results. _Evidence to advance:_
   Required groups execute and pass. The qualification gate rejects a
   deliberately skipped or failed mandatory test.
   [#5695](https://github.com/fderuiter/cadence-clinical/issues/5695) ·
   [#5696](https://github.com/fderuiter/cadence-clinical/issues/5696)
2. **Prove the Capture reference visit:** Enter, save, correct, query, verify,
   review, sign, and lock on the real staff UI, host, and PostgreSQL path.
   Exercise audit, access, revision, concurrency, and recovery behavior.
   _Evidence to advance:_ A repeatable browser journey and restricted-role
   database tests pass. A stale write, prohibited write, or ambiguous query/lock
   race has an explicit outcome.
   [#5365](https://github.com/fderuiter/cadence-clinical/issues/5365) ·
   [#5586](https://github.com/fderuiter/cadence-clinical/issues/5586)
3. **Connect Blueprint to Capture:** Compile a structured protocol into an
   approved build, apply it at the appropriate site and time, and exercise a
   site-specific amendment. _Evidence to advance:_ At least one protocol
   requirement traces through a versioned form or rule to a recorded test
   result. Prior records retain their governing revision.
   [#5631](https://github.com/fderuiter/cadence-clinical/issues/5631) ·
   [#5640](https://github.com/fderuiter/cadence-clinical/issues/5640)
4. **Extend the same study across domains:** Prove site and consent readiness,
   intervention accountability, evidence intake, safety reconciliation, and
   sponsor quality review. Fold each worked slice into the host under its owning
   module. _Evidence to advance:_ Each selected handoff has one real-path
   success, a blocked action with a stated reason, and a recovery or remediation
   path. [#5310](https://github.com/fderuiter/cadence-clinical/issues/5310) ·
   [#5378](https://github.com/fderuiter/cadence-clinical/issues/5378)
5. **Verify lock and evidence handoff:** Review blockers and complete a
   controlled lock, then generate clinical exports and inspection copies from
   the named snapshot. _Evidence to advance:_ Required blockers prevent lock,
   the approved record can be retrieved by an authorized user, and the export is
   reproducible under its pinned rules, mappings, and source versions.
   [#5466](https://github.com/fderuiter/cadence-clinical/issues/5466) ·
   [#5592](https://github.com/fderuiter/cadence-clinical/issues/5592)

Work can run in parallel when the contract is already stable or the slices have
distinct owners. Changes to the same clinical write path, shared schema, or
verification harness have one coordinating issue and one implementation owner.
Follow the dependencies of the five architecture gates for qualification. Pilot
readiness is a separate decision based on intended use, validation, and
operational readiness.

## Engineering quality

- **Maintainability:** Keep the existing analyzers, warnings-as-errors, strict
  frontend typing, generated client boundaries, and layer checks. Change one
  coherent workflow per PR. Eliminate duplicate rule or authorization paths as
  the authoritative path is proven. [Contributor rules](../AGENTS.md) ·
  [.NET build settings](../Directory.Build.props)
- **Clinical data integrity:** Use PostgreSQL with restricted roles to exercise
  tenant and study scope, transactions, row-level security, stale revisions, and
  lock races. Include duplicate delivery and failure recovery for durable work.
  Keep quick deterministic tests for domain logic and real-host tests for
  workflows. [Testing](TESTING.md) ·
  [EF Core testing guidance](https://learn.microsoft.com/en-us/ef/core/testing/choosing-a-testing-strategy)
- **Secure development:** Keep dependencies pinned through existing lockfiles
  and package management. Adopt bounded jobs, least-privilege workflows, pinned
  actions, and dependency review through the existing CI backlog. Prioritize
  negative tests for authorization, sensitive records, consent, and blinding.
  [#5704](https://github.com/fderuiter/cadence-clinical/issues/5704) ·
  [NIST SSDF 1.1](https://csrc.nist.gov/pubs/sp/800/218/final) ·
  [OWASP ASVS 5.0](https://owasp.org/projects/asvs/) ·
  [GitHub Actions secure use](https://docs.github.com/en/actions/reference/security/secure-use)
- **Accessibility and responsiveness:** Use WCAG 2.2 AA as the engineering
  target for staff and participant journeys. Combine automated checks with
  keyboard, focus, error recovery, and representative assistive technology
  checks. Measure input feedback, server-confirmed saves, and failures on a
  declared test profile. [WCAG 2.2](https://www.w3.org/TR/WCAG22/)
- **Clinical interoperability:** Pin each supported schema, terminology release,
  and mapping version. Test USDM import and ODM export against representative
  examples. Keep unmapped, unsupported, and quarantined records reviewable.
  [CDISC Digital Data Flow](https://www.cdisc.org/ddf) ·
  [CDISC ODM](https://www.cdisc.org/standards/data-exchange/odm-xml/odm-v2-0)
- **Verification and traceability:** Map intended-use requirements to accepted
  behavior and executed evidence. Develop authorized access, attributable
  records, version-bound signatures, and reviewable copies in synthetic
  fixtures. Review applicability of clinical guidance and standards for the
  study and jurisdiction before qualifying use.
  [21 CFR Part 11](https://www.ecfr.gov/current/title-21/chapter-I/subchapter-A/part-11)
  ·
  [FDA electronic systems guidance](https://www.fda.gov/regulatory-information/search-fda-guidance-documents/electronic-systems-electronic-records-and-electronic-signatures-clinical-investigations-questions)
  ·
  [ICH E6(R3)](https://database.ich.org/sites/default/files/ICH_E6%28R3%29_Step4_FinalGuideline_2025_0106.pdf)
  · [ISO 14155:2026](https://www.iso.org/standard/14155)

## Working from the backlog

Start with the linked issue and verify its status and assumptions on `main`.
Declare the module owner, scope, and test that proves the change. Use synthetic
data and record the exact checks executed. When a mandatory check cannot run,
record the evidence gap and leave the requirement unqualified. Follow the
[contributing workflow](../CONTRIBUTING.md) and the [local CI guide](CI.md).
Reuse the existing priorities, requirements, and issue links when updating the
plan.

The
[former repository migration documents](architecture/repository-migration/MIGRATION_PLAN.md)
are historical references for ownership and earlier analysis. The open
verification and boundary issues remain useful where they apply to the
single-repository plan.

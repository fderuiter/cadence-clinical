# Capability ownership matrix

> [!NOTE] Historical reference for the former repository migration program. See
> the [project plan](../../PROJECT_PLAN.md) for current delivery and
> [ADR-2603](../../adr/2026-11-19-keep-cadence-in-one-repository.md) for the
> source layout decision.

**Status:** target mapping for a file-by-file migration inventory. This is _not_
a statement that source paths have moved or that every capability is
operational.

Canonical destination names omit the `cadence-` prefix; `clinical` means the
original `cadence-clinical` integration repository.

- **Canonical studies, sites, participants, observations, artifacts,
  provenance** — Authoritative target: `kernel`. Candidate current source:
  existing `packages/kernel`. Cross-boundary rule: others use typed
  IDs/contracts, never competing canonical tables.
- **Actor, delegation, training-qualified grants, consent scopes,
  IdentityVault** — Authoritative target: `kernel`. Candidate current source:
  `packages/kernel`; `apps/org` workflow. Cross-boundary rule: Kernel computes
  authority; Orbit administers people/training workflows.
- **Audit chain, temporal history, command pipeline, outbox, signatures** —
  Authoritative target: `kernel`. Candidate current source: `packages/kernel`.
  Cross-boundary rule: preserve atomic module+kernel clinical transaction per
  ADR-2600.
- **ProtocolVersion and authoritative clinical protocol semantics** —
  Authoritative target: `kernel`. Candidate current source: `packages/kernel`.
  Cross-boundary rule: Blueprint owns protocol editing/drafts/approvals UI, not
  canonical concept duplicate.
- **USDM authoring, protocol review, study-build and specifications** —
  Authoritative target: `study-design`. Candidate current source:
  `apps/designer`; `apps/knowledge`; `packages/ddf`. Cross-boundary rule:
  Capture executes approved specs, Blueprint builds them.
- **Standards registry authority, rule language/execution contracts** —
  Authoritative target: `kernel`. Candidate current source:
  `packages/kernel/src/Cadence.Standards`; `Cadence.Rules`. Cross-boundary rule:
  Blueprint authoring/governance; Lens consumes mappings; one execution
  authority.
- **EDC eCRF records, data queries, coding, SDV/SDR workflows** — Authoritative
  target: `clinical-data`. Candidate current source: `apps/execution`;
  `apps/execution/module`. Cross-boundary rule: keep source observation
  authority in kernel.
- **eSource data entry/EHR ingestion adapters** — Authoritative target:
  `clinical-data`. Candidate current source: `apps/execution`; `apps/interop`.
  Cross-boundary rule: interop transport shared in workspace where reusable;
  provenance in kernel.
- **Randomization scheme, assignment, unblinding, investigational supply** —
  Authoritative target: `interventions`. Candidate current source:
  `apps/execution/RTSM/supply`. Cross-boundary rule: Kernel persists
  authoritative arm Assertion; explicit blinded command path.
- **Device assignment, investigational kit/device accountability** —
  Authoritative target: `interventions`. Candidate current source:
  `apps/execution/device` code. Cross-boundary rule: scientific telemetry and
  derived endpoints are Lens.
- **CTMS, site startup, monitoring, visit reports, RBQM operations** —
  Authoritative target: `trial-operations`. Candidate current source:
  `apps/ctms`. Cross-boundary rule: Kernel owns shared clinical risk/findings;
  Assure owns quality CAPA.
- **eTMF/eISF/eReg, QC, sharing and expected documents** — Authoritative target:
  `trial-operations`. Candidate current source: `apps/etmf`; `apps/eisf`;
  `apps/fileshare`. Cross-boundary rule: generic blob storage stays platform
  host; artifact lineage kernel.
- **Payments, site contracts, operational tickets, staff admin** — Authoritative
  target: `trial-operations`. Candidate current source: `apps/payments`;
  `apps/tickets`; `apps/org`. Cross-boundary rule: no duplicate kernel grant or
  permission source.
- **eConsent, reconsent and subject portal** — Authoritative target:
  `participants`. Candidate current source: `apps/econsent`;
  `apps/subject-portal`. Cross-boundary rule: Kernel controls ConsentStatus
  scope and identity.
- **ePRO/eCOA, recruitment, participant engagement** — Authoritative target:
  `participants`. Candidate current source: `apps/interop` ePRO;
  `apps/subject-portal`. Cross-boundary rule: Kernel observation; Lens owns DHT
  scientific pipeline.
- **Safety cases, ICSR, E2B, signal/DSUR, SAE reconciliation** — Authoritative
  target: `safety`. Candidate current source: `apps/safety`. Cross-boundary
  rule: AE source observation remains clinical-data/kernel.
- **Laboratory and biospecimen integrations** — Authoritative target:
  `evidence`. Candidate current source: `apps/execution` lab; `apps/interop`.
  Cross-boundary rule: Kernel specimen & observation reference; code ownership
  needs file-level inventory.
- **Imaging, device telemetry, DHT, endpoint analysis** — Authoritative target:
  `evidence`. Candidate current source: `apps/imaging`; `apps/interop`.
  Cross-boundary rule: device accountability Allocate; raw scientific data in
  Lens.
- **Statistics, SDTM/ADaM/SEND dataset transformations and RWE** — Authoritative
  target: `evidence`. Candidate current source: `apps/interop/SCE`; execution
  exports. Cross-boundary rule: scientific outputs pin mapping and terminology
  versions; RIM is Assure.
- **QMS, CAPA, audits, CSV/CSA, regulatory submissions/RIM** — Authoritative
  target: `quality-regulatory`. Candidate current source: `apps/quality`;
  `docs/SDLC`; regulatory scaffold. Cross-boundary rule: Kernel owns basic
  findings/attestations; Orbit owns TMF.
- **Web shell, API gateway, AppHost, notifications, generic storage** —
  Authoritative target: `clinical`. Candidate current source: `apps/web`;
  `gateway`; `apphost`; `notifications`; `fileshare` storage. Cross-boundary
  rule: platform composition, not duplicate product business logic.
- **AI safety policies, proposals, provider routing** — Authoritative target:
  `kernel + clinical`. Candidate current source: `apps/intelligence`; kernel
  proposal framework. Cross-boundary rule: Kernel owns actor/proposal authority;
  product AI UX stays in each domain.

## Required resolution before extraction

1. `apps/execution` contains EDC and RTSM/supply/device behavior; do **not**
   move the whole directory as one domain.
2. `apps/interop` contains EHR/eSource, ePRO, telemetry and statistical
   computing. Split by behavior. Shared HTTP/transport utilities remain reusable
   platform code.
3. `apps/knowledge` spans standards, governed protocol knowledge and potentially
   regulatory knowledge. Assign individual classes before moving.
4. `packages/kernel` includes standards and rules assemblies. Preserve one
   canonical Registry/evaluator; distinguish authoring UI from execution
   authority.
5. `apps/fileshare` separates generic object storage/conversion from
   TMF-specific share grants/guest links.
6. `apps/org` administers staff but kernel owns grants, delegation and
   qualification authority.
7. The service-to-module fold-in plan under ADR-2610 still governs behavior
   until an explicit revised ADR is accepted.

## Next inventory format

For each file/project record: current path, source commit SHA, real maturity
(running/scaffold/dead/reference), owning product, canonical data tables,
externally visible API/events, dependencies, tests, risk level, proposed
destination, acceptance evidence, and old-source retirement PR. Mark uncertain
items `needs decision`; never infer ownership solely from folder name.

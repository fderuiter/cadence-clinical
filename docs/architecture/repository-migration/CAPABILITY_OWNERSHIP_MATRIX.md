# Capability ownership matrix

**Status:** target mapping for a file-by-file migration inventory. This is *not* a statement that source paths have moved or that every capability is operational.

Canonical destination names omit the `cadence-` prefix; `clinical` means the original `cadence-clinical` integration repository.

| Capability | Authoritative target | Candidate current source | Cross-boundary rule |
| --- | --- | --- | --- |
| Canonical studies, sites, participants, observations, artifacts, provenance | kernel | Existing packages/kernel | Others use typed IDs/contracts, never competing canonical tables |
| Actor, delegation, training-qualified grants, consent scopes, IdentityVault | kernel | packages/kernel; apps/org workflow | Kernel computes authority; Orbit administers people/training workflows |
| Audit chain, temporal history, command pipeline, outbox, signatures | kernel | packages/kernel | Preserve atomic module+kernel clinical transaction per ADR-2600 |
| ProtocolVersion and authoritative clinical protocol semantics | kernel | packages/kernel | Blueprint owns protocol editing/drafts/approvals UI, not canonical concept duplicate |
| USDM authoring, protocol review, study-build and specifications | study-design | apps/designer; apps/knowledge; packages/ddf | Capture executes approved specs, Blueprint builds them |
| Standards registry authority, rule language/execution contracts | kernel | packages/kernel/src/Cadence.Standards; Cadence.Rules | Blueprint authoring/governance; Lens consumes mappings; one execution authority |
| EDC eCRF records, data queries, coding, SDV/SDR workflows | clinical-data | apps/execution; apps/execution/module | Keep source observation authority in kernel |
| eSource data entry/EHR ingestion adapters | clinical-data | apps/execution; apps/interop | Interop transport shared in workspace where reusable; provenance in kernel |
| Randomization scheme, assignment, unblinding, investigational supply | interventions | apps/execution/RTSM/supply | Kernel persists authoritative arm Assertion; explicit blinded command path |
| Device assignment, investigational kit/device accountability | interventions | apps/execution/device code | Scientific telemetry and derived endpoints are Lens |
| CTMS, site startup, monitoring, visit reports, RBQM operations | trial-operations | apps/ctms | Kernel owns shared clinical risk/findings; Assure owns quality CAPA |
| eTMF/eISF/eReg, QC, sharing and expected documents | trial-operations | apps/etmf; apps/eisf; apps/fileshare | Generic blob storage stays platform host; artifact lineage kernel |
| Payments, site contracts, operational tickets, staff admin | trial-operations | apps/payments; apps/tickets; apps/org | No duplicate kernel grant or permission source |
| eConsent, reconsent and subject portal | participants | apps/econsent; apps/subject-portal | Kernel controls ConsentStatus scope and identity |
| ePRO/eCOA, recruitment, participant engagement | participants | apps/interop ePRO; apps/subject-portal | Kernel observation; Lens owns DHT scientific pipeline |
| Safety cases, ICSR, E2B, signal/DSUR, SAE reconciliation | safety | apps/safety | AE source observation remains clinical-data/kernel |
| Laboratory and biospecimen integrations | evidence | apps/execution lab; apps/interop | Kernel specimen & observation reference; code ownership needs file-level inventory |
| Imaging, device telemetry, DHT, endpoint analysis | evidence | apps/imaging; apps/interop | Device accountability Allocate; raw scientific data in Lens |
| Statistics, SDTM/ADaM/SEND dataset transformations and RWE | evidence | apps/interop/SCE; execution exports | Scientific outputs pin mapping and terminology versions; RIM is Assure |
| QMS, CAPA, audits, CSV/CSA, regulatory submissions/RIM | quality-regulatory | apps/quality; docs/SDLC; regulatory scaffold | Kernel owns basic findings/attestations; Orbit owns TMF |
| Web shell, API gateway, AppHost, notifications, generic storage | clinical | apps/web; gateway; apphost; notifications; fileshare storage | Platform composition, not duplicate product business logic |
| AI safety policies, proposals, provider routing | kernel + clinical | apps/intelligence; kernel proposal framework | Kernel owns actor/proposal authority; product AI UX stays in each domain |

## Required resolution before extraction
1. `apps/execution` contains EDC and RTSM/supply/device behavior; do **not** move the whole directory as one domain.
2. `apps/interop` contains EHR/eSource, ePRO, telemetry and statistical computing. Split by behavior. Shared HTTP/transport utilities remain reusable platform code.
3. `apps/knowledge` spans standards, governed protocol knowledge and potentially regulatory knowledge. Assign individual classes before moving.
4. `packages/kernel` includes standards and rules assemblies. Preserve one canonical Registry/evaluator; distinguish authoring UI from execution authority.
5. `apps/fileshare` separates generic object storage/conversion from TMF-specific share grants/guest links.
6. `apps/org` administers staff but kernel owns grants, delegation and qualification authority.
7. The service-to-module fold-in plan under ADR-2610 still governs behavior until an explicit revised ADR is accepted.

## Next inventory format
For each file/project record: current path, source commit SHA, real maturity (running/scaffold/dead/reference), owning product, canonical data tables, externally visible API/events, dependencies, tests, risk level, proposed destination, acceptance evidence, and old-source retirement PR. Mark uncertain items `needs decision`; never infer ownership solely from folder name.

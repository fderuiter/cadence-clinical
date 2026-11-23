# Moving the web app onto the .NET API

The web app (`apps/web`) was written against the Python services. Most of its
API calls target endpoints that no .NET service serves, so most screens fail
against the real backend. This page lists what works, what doesn't, and how a
screen moves over. The plan is epic #4833.

## Current state (2026-09-29)

Each route was traced through every store, client and component it imports, and
each API path it can call was matched against the specs the .NET services serve
(`docs/openapi/*.json`). A "real" match means the path exists, not that the
request and response shapes match.

### What the .NET services serve

- **Execution:** subjects (list by study with site/status filters and paging,
  grouped status counts via `GET /api/v1/execution/subjects/counts`, create,
  get, consent, state, demographics); forms (create, get, update, list by
  subject, complete, approve, unlock); observations (create, get, list by
  subject, SDV, SDV flag); sites (activate, compliance-status with persistent
  `is_activated`, `activated_at`, `activated_by`); eTMF completeness webhook
  (`POST /api/v1/execution/webhooks/etmf`); randomization schemes; RTSM IP kit
  inventory, seeding, scan validation, batch return reconciliation, and
  autologous CGT chain-of-custody.
- **Kernel API host:** read only (ADR-2540, `kernelApi.ts`): node summary, zoom
  path, children, command catalogue with dry-run decisions, CanI, search, a
  study's protocol versions and one version, Trace, dependents and amendment
  impact; decisions come from the caller's grants (ADR-2455), and objects the
  caller's grants don't cover come back masked (ADR-2457).
- **CTMS (`Core: true`, `ctmsApi.ts`):** site startup greenlight evaluation and
  certification (`GET/POST .../startup/sites/{siteId}/greenlight`,
  `GET .../startup/matrix`, `POST .../startup/matrix/batch-certify`), site
  milestones (`GET/POST/PUT /api/v1/ctms/site-milestones`), monitoring visits
  (`GET/POST /api/v1/ctms/monitoring-visits`, `/complete`, `/sign-off`), visit
  letters (`GET .../letters`, `POST /api/v1/ctms/letters/render`,
  `POST /api/v1/ctms/letters/compile`,
  `GET /api/v1/ctms/letters/{letterId}/pdf`), and Delegation of Authority
  (`GET .../doa/sites/{site_id}/log`, `POST .../doa/delegate`, `/revoke`,
  `/sign-off`, `GET .../export-pdf`), study/site scope-isolated via
  `CallerScope` (`ADR-2642`, `ADR-2643`).
- **eTMF (`Core: true`, `etmf.ts`):** document vault and binary object storage
  (`GET /api/v1/etmf/documents`, `GET /api/v1/etmf/documents/{documentId}`,
  `GET /api/v1/etmf/documents/{documentId}/content`,
  `POST /api/v1/etmf/ingest`), `DRAFT`-to-`TECHNICAL_QC` submission and DIA
  taxonomy updates (`POST /api/v1/etmf/documents/{documentId}/submit-qc`,
  `PATCH /api/v1/etmf/documents/{documentId}/taxonomy`), intelligence
  classification (`POST /api/v1/etmf/intelligence/classify`), QC staging and
  clinical QC review (`POST /api/v1/etmf/intelligence/stage-qc`,
  `GET /api/v1/etmf/intelligence/qc-queue`,
  `POST /api/v1/etmf/intelligence/qc-review/{docId}` — signs and posts
  completeness updates to Execution, `ADR-2643`), and study binder ZIP exports
  (`POST /api/v1/etmf/documents/export`, `GET /api/v1/etmf/export-jobs`,
  `GET /api/v1/etmf/export-jobs/{jobId}`,
  `GET /api/v1/etmf/export-jobs/{jobId}/download`).
- **Knowledge (`Core: true`, `knowledgeApi.ts`, `training.ts`):** contextual SOP
  resolution (`GET /api/v1/knowledge/contextual-help`), published SOP search and
  detail (`GET /api/v1/knowledge/articles`,
  `GET /api/v1/knowledge/articles/{article_id}`), categories
  (`GET /api/v1/knowledge/categories`), training curricula
  (`GET /api/v1/training/curricula`,
  `GET /api/v1/training/curricula/{curriculumId}`), assignments and batch
  assignments (`GET /api/v1/training/assignments`,
  `GET /api/v1/training/assignments/{assignmentId}`,
  `POST /api/v1/training/curricula/batch-assign`), and PI delegation log and
  bulk sign-off (`GET /api/v1/training/delegations`,
  `POST /api/v1/training/delegations/bulk-signoff`) (`ADR-2813`, `ADR-2641`,
  `ADR-2643`).
- **Tickets:** support ticket creation (`POST /api/v1/tickets` with
  `X-Change-Reason`, 6-queue routing, Dual-Target SLAs, PII rejection) and
  study/site-scoped listing (`GET /api/v1/tickets`,
  `GET /api/v1/tickets/{ticket_id}`) (`ADR-2641`).
- **Designer:** `GET /studies` (always empty), `POST /protocols/compile` (not
  routed by the gateway yet).
- **Notifications, Safety:** one render endpoint each (notification, ICSR).
- **Everything else (`ai`, `econsent`, `eisf`, `fileshare`, `imaging`,
  `interop`, `org`, `payments`, `quality`):** `GET /status` only.

Missing for any EDC screen to work end to end: study/visit schedule and form
definitions (Designer), and the current user's notifications (the app shell
fetched them on every screen).

### Screens

Calls = distinct API paths reachable from the screen. Missing = paths no .NET
service serves.

- **eCRF Data Capture (`/ecrf`):** 71 calls, 67 missing (interop 38, studies 15,
  execution 8, notifications 4, terminology 2). Needs client change (Execution
  forms/observations/queries/subjects exist) plus missing endpoints (visit
  schedule, form definitions).
- **Study Rules & Queries (`/rules`):** 19 calls, 15 missing (execution 6,
  studies 4, notifications 4, dictionaries 1). Query panel: client change;
  rules/edit checks: missing.
- **CTMS Operations, SSU (`/ctms`, `/ssu`):** 50 calls, 21 missing (ctms
  financials/CRA/recruitment 17, notifications 4). Site activation, startup
  greenlight, milestones, monitoring, compiled visit letters, delegation, and
  RTSM supply are live; financials, CRA allocation, recruitment updates, and
  adaptive dose escalation are gated (#4829).
- **Data Lock Console (`/data-lock`):** 0 calls (redirects), 0 missing.
  Consolidated into Clinical Canvas `Data → Locks` (`stage=data&section=locks`,
  `kernelApi.listStudyLocks`, `getStudyLockReadiness`, `applyLock`,
  `releaseLock`).
- **Lab Governance (`/labs`):** 9 calls, 9 missing (execution 9). Missing.
- **Medical Coding (`/coding`):** 11 calls, 11 missing (execution 5,
  dictionaries 2, notifications 4). Missing.
- **Protocol & SoA Designer (`/mdr`):** 14 calls, 10 missing (execution 6,
  notifications 4, plus SoA via Designer). Missing (Designer is a stub).
- **Protocol AI Digitizer (`/digitization`):** 5 calls, 5 missing (designer 1,
  notifications 4). Missing.
- **Amendments & Migration (`/amendment-diff`):** 25 calls, 21 missing
  (execution 10, designer 3, studies 3, mdr 1, notifications 4). Missing.
- **eConsent authoring, ICF builder (`/econsent-authoring`, `/icf-builder`):**
  33 / 19 calls, 29 / 19 missing (econsent, etmf, execution). Missing (subject
  consent POST exists; authoring does not).
- **eTMF Document Vault (`/etmf`):** 13 calls, 0 missing. Live on `etmf.ts`
  (`generated/etmf.ts`): scoped document listing, binary ingestion and preview
  streaming (`IStoragePort<byte[]>`), `DRAFT`-to-`TECHNICAL_QC` submission,
  inline taxonomy updates, AI classification, QC staging, clinical QC review,
  and authenticated ZIP binder exports (`ADR-2643`).
- **Part 11 Audit Ledger, Auditor (`/audit`, `/auditor`):** 22 / 7 calls, 22 / 7
  missing (etmf, execution, quality). Missing (no audit read API).
- **Quality & RBQM Cockpit (`/quality`):** 7 calls, 7 missing (quality 7).
  Missing.
- **Safety & DSUR (`/safety`):** 4 calls, 4 missing (safety 4). Missing.
- **Sensors, Telehealth/DTP, Lakehouse (`/sensors`, `/dct`, `/lakehouse`):** 42
  / 42 / 16 calls, all missing (interop). Missing.
- **Documents Hub (`/documents`):** 21 calls, 21 missing (fileshare 17).
  Missing.
- **Study Training (`/training`):** 7 calls, 0 missing. Live on `trainingApi`
  (`generated/knowledge.ts`): curricula, assignments, batch assignment, and PI
  delegation bulk sign-off (`ADR-2643`).
- **Knowledge & SOP Hub (`/knowledge`):** 4 calls, 4 missing (notifications 4;
  articles are hardcoded). Missing.
- **Tickets (`/tickets`):** 5 calls, 5 missing (tickets 1). Missing.
- **Notifications (`/notifications`):** 4 calls, 4 missing (notifications 4).
  Missing.
- **Participant Wallet, Lay Summaries (`/participant-wallet`,
  `/lay-summaries`):** 11 / 3 calls, all missing (payments). Missing.
- **DICOM viewer, Device & IVD, CEC adjudication (`/imaging`, `/devices`,
  `/cec`):** 0 calls, 0 missing. No API at all: hardcoded demo data (`/exports`
  consolidated into Clinical Canvas `Submit → Exports` via
  `kernelApi.streamStudyExport` and `streamEnrollmentCasebook`).

Result: no screen works end to end against the .NET backend. The eCRF, query,
SDV and site activation parts are the only ones with a real backend behind them.

## Moved so far

### eCRF (`/ecrf`)

- **Subject list & kernel form instances (ADR-2622, #4828)**
  (`executionApi.listSubjects`, `findKernelEnrollmentRef`, `ecrfFormsApi`,
  `ecrfQueriesApi`, `kernelApi.verifyObservation`,
  `kernelApi.retractAssertion`): outside a demo build the screen is only the
  role badge, the study bar (`?studyId=` or typed study code), the participant
  selector (showing the first 200 subjects in enrollment order, resolving the
  chosen participant's kernel enrollment via `src/canvas/kernelStudy.ts`, and
  linking staff with `canManageParticipants` to Clinical Canvas), and
  `EcrfFormPanel` (opening/loading kernel form instances, saving pages of
  values, source verification, field queries, and investigator e-signatures).
  Legacy enrolment, sample vitals, CDASH rules, protocol ingestion, EHR
  reconciliation, and the role switcher have no backend and appear only in a
  demo build.

### CTMS and SSU (`/ctms`, `/ssu`)

- **Site activation, Startup readiness, Milestones, Monitoring, Delegation,
  Supply** (`executionApi.getSiteComplianceStatus`, `executionApi.activateSite`,
  `ctmsApi.*`, `executionApi.*` RTSM & schemes, `etmfService.stageQcDocument`):
  promoted `ctms`, `etmf`, and `knowledge` to `Core: true` in `ServiceTopology`
  / AppHost and generated `src/api/generated/ctms.ts`, `etmf.ts`, and
  `knowledge.ts` (ADR-2610, ADR-2642, ADR-2643). Outside a demo build, `/ctms`
  and `/ssu` expose tabbed workspaces for **Site activation**, **Startup
  readiness**, **Site milestones**, **Monitoring visits** (with structured
  findings, GxP PDF letter compilation via `POST /api/v1/ctms/letters/compile`,
  and supervisory sign-off), **Delegation log**, and **Investigational supply**,
  while admitting `data_manager` (`ROLE_SPONSOR_DM`). `SiteActivationPanel`
  persists `is_activated`, `activated_at`, and `activated_by`, gates
  `Activate site` via `canActivateSite`, populates scoped pickers from token
  claims (`study_ids`, `site_id`), and renders UX-9 scope-assignment guidance on
  HTTP `403` (`FORBIDDEN_SCOPE`). Grant financials, CRA allocation, recruitment
  target updates, and adaptive dose escalation remain gated behind `demoBuild` /
  `allowUnbackedAction` (#4829).

### eTMF (`/etmf`) and Study Training (`/training`)

- **eTMF document vault, binary storage, taxonomy, exports & clinical QC
  (`ADR-2643`)** (`etmfService.getDocuments`, `ingestDocument`,
  `downloadDocumentContent`, `submitDocumentForQc`, `tagDocument`,
  `classifyIntelligence`, `stageQcDocument`, `getQcQueue`, `submitQcReview`,
  `exportDocuments`, `listExportJobs`, `getExportJob`, `downloadExportJob`):
  `DocumentManagerView.vue` provides a standalone study/site scope picker on
  `/etmf` (and inherits `:study-id` / `:site-id` inside Clinical Canvas),
  renders UX-9 scope-assignment guidance on HTTP `403` (`FORBIDDEN_SCOPE`),
  streams verified `SHA-256` document bytes in `PdfPreviewModal.vue`, downloads
  ZIP exports via authenticated `Blob` requests in `ExportJobDrawer.vue`,
  persists `DRAFT` -> `TECHNICAL_QC` submissions and inline DIA taxonomy edits,
  and enforces `authority.ts` role gates (`canReadEtmf`, `canStageEtmfQc`,
  `canTagEtmf`, `canReviewEtmfQc`, `canExportEtmf`). eTMF signs and forwards
  site document completeness snapshots to Execution
  (`POST /api/v1/execution/webhooks/etmf`) as `etmf_service`.
- **Study Training curricula, staff assignments & PI delegation bulk sign-off
  (`ADR-2643`)** (`trainingApi.listCurricula`, `getCurriculum`,
  `batchAssignCurriculum`, `listAssignments`, `getAssignmentDetail`,
  `listDelegations`, `bulkSignOffDelegations`): `training.ts` is generated over
  `docs/openapi/knowledge_openapi.json` (`generated/knowledge.ts`), passing
  `protocol_version` on curriculum queries and dropping unmounted Python-era
  progress/quiz/sign-off/certificate endpoints.

### Clinical Canvas (`/studies/:studyCode`, `/data-lock`, `/exports`)

- **Start & Conduct operational sections (`ADR-2642`, `ADR-2643`)**
  (`ctmsApi.*`, `etmfService.*`, `trainingApi.*`, `executionApi.*`): **Start**
  exposes _Site activation_, _Documents_ (`documents`), _Startup readiness_
  (`startup-greenlight`), _Site milestones_ (`site-milestones`), _Training_
  (`training`), and _Delegation log_ (`delegation`). **Conduct** exposes
  _Participants_, _Monitoring visits_ (`monitoring-visits`), and
  _Investigational supply_ (`supply`). Role-gated actions check canonical
  backend roles (`src/canvas/authority.ts`) so `cra` vs `monitor` vs
  `sponsor_dm` vs `investigator` vs `pharmacist` boundaries match
  `PermissionMatrix`.
- **Study home, Participants, Site activation** (`executionApi.listSubjects`,
  `executionApi.getSubjectCounts`, `executionApi.getSubjectDetail`,
  `executionApi.getSiteComplianceStatus`, `executionApi.activateSite`): the
  study-centric shell from [CLINICAL_CANVAS.md](../ux/CLINICAL_CANVAS.md) UX-1
  (`AppShell.vue`) wraps every authenticated screen (`StudyCanvasView.vue`,
  `EcrfView.vue`, `CtmsView.vue`, `OverviewView.vue`) in one persistent header
  with **Studies**, **Data collection** and **Site readiness** primary
  navigation, a single global **⌘K** command palette, the persona view switcher
  (UX-6), **Orientation** checklist/tour controls and **Help & SOPs**, with no
  legacy product sidebar, sandbox banner or telemetry footer. Study home counts
  participants by state in one summary call (`executionApi.getSubjectCounts`,
  `GET /api/v1/execution/subjects/counts`); each count opens the participants it
  counts. Participants pages 50 at a time with subject code search (`?q=`),
  state (`?status=`) and study site (`?site=`) filters; selecting one fills the
  inspector and puts its NodeRef (`participant:<uuid>`, ADR-2481) in the URL,
  and cold links resolve participant details by surrogate UUID or subject code.
  Participant simulation stays disabled naming #4888. Stages and sections with
  no service are empty, not mocked.
- **Participant data (#4828)** (`executionApi.listObservations`,
  `listFormSubmissions`, `verifyObservationSdv`, `flagObservationSdv`; queries
  through `ecrfQueriesApi.listForEnrollment` and `transition`, #5461): "Open
  participant's data" in the inspector opens `?participant=<NodeRef>`. It shows
  the participant's observations by visit, queries and form submissions
  (read-only). Selecting an observation fills the inspector, with source data
  verification (verify, sent with a fixed reason for change; flag with a reason,
  sent as the reason for change too) and raising a query for a CRA or data
  manager. Execution refuses SDV (403) to anyone else (#4920). Queries are the
  Execution module's (#5461): the record finds the participant's kernel
  enrollment and lists its queries from
  `/api/v1/execution/forms/queries?enrollment_id=`; it offers the steps the
  query's status allows, one request per query, with the typed text as
  `X-Change-Reason`, and the kernel authorizes each against the caller's grants.
  Queries are raised on the eCRF form panel, which knows the value's current
  revision. The client never sends a responder or resolver (#4920). Observations
  have no kernel NodeRef yet, so they are addressed by Execution id
  (`?observation=`). Forms cannot be opened until form definitions are served
  (#4727).
- **Adding participants, changing state** (`executionApi.addSubject`,
  `executionApi.changeSubjectState`): **Add participant** in Participants
  creates the participant in screening with its code, an optional site (the site
  filter in view by default) and a reason sent as `X-Change-Reason`; no
  demographics or arm are sent. The new participant is selected once the list
  can place it (the last in enrollment order). **Change state** in the inspector
  offers the transitions Execution's graph allows from the participant's state
  (`SubjectTransitionGraph.cs`; `tests/canvas/contracts.spec.ts` fails if the
  copy drifts), less unblinding, which needs its own workflow; the reason is
  sent as `X-Change-Reason`. Execution refuses enrolment at a site whose
  activation documents are incomplete, and the canvas shows its refusal. Both
  are offered only to the roles Execution accepts for them (investigators and
  coordinators, `canManageParticipants` in `src/canvas/authority.ts`, checked
  against `SubjectAuthority.cs` by `tests/canvas/contracts.spec.ts`); a data
  manager, monitor, auditor or sponsor admin sees neither, since Execution
  answers them 403 (#4943, #4981). Recording consent waits until protocol
  versions are served over HTTP: the request needs the version's index.
- **Trace, Impact, Act, Explain and study search (ADR-2540)**
  (`kernelApi.trace`, `kernelApi.dependents`, `kernelApi.getCommands`,
  `kernelApi.search`): **Trace** in the inspector lists the kernel's lineage
  walk back from the selected participant, edge by edge in plain words;
  **Impact** counts what depends on it by kind, expandable to objects, and says
  when the count is a lower bound. Objects the reader may not see stay in place
  as hidden, with their kind and reason, and are counted, so the numbers are the
  same for everyone (ADR-2471). **Act** in the inspector lists the kernel
  commands offered on the selected participant, each with the reader's dry-run
  decision and the kernel's reason; nothing is sent. **Explain** beside a
  command renders the kernel's explanation of that decision (claim, steps, every
  condition met or not, what would change it) as the kernel produced it
  (K-EXP-1). A refusal from the kernel host is shown as it is. Observations have
  no kernel NodeRef yet, so all four stay disabled on an observation. Each
  decision is the kernel authority evaluator's, from the reader's grants
  (ADR-2455). The ⌘K palette also lists studies found by kernel search (code or
  title, studies only, once typing pauses), each opening by its code; hidden
  studies are left out, and a failed search says so while opening by code still
  works.
- **Protocol versions, simulation and amendment impact (ADR-2540)**
  (`kernelApi.search`, `kernelApi.listProtocolVersions`,
  `kernelApi.getProtocolVersion`, `kernelApi.amendmentImpact`,
  `kernelApi.simulate`): **Protocol versions** in the design stage finds the
  study by its exact code with kernel search and lists its versions newest first
  (number, status, the version it amends, the amendment's intent); a version the
  reader may not see keeps its place as hidden, and the count says how many are
  hidden. Selecting a version puts its NodeRef in the URL and fills the
  inspector with its status, content hash, approval and withdrawal. **Impact**
  on an amendment is K-PROT-2 by element: the elements it adds, changes or
  removes, with the changed properties as JSON Pointers, then what depends on
  the version it amends; on a first version it is the plain dependents read.
  **Trace** works on a version as on a participant, and **Simulate** runs
  evaluate-only simulation and schedule matrix gap checks on the selected
  version.
- **Study activity (PX-11, ADR-2485)** (`kernelApi.search`,
  `kernelApi.activity`): **Recent activity** on study home lists what happened
  under the study, newest first, 25 at a time with **Show older activity**
  passing back the kernel's `next` cursor, and a family filter (Knowledge,
  Execution, Evidence, Trust, Quality, Intelligence). Each entry shows when, the
  event in plain words, its object and who did it; a protocol version opens in
  Protocol versions. An entry about something the reader may not see stays in
  place as hidden with the reason. A study the reader may not see is refused by
  the kernel, and the refusal is shown as it is. Entries appear a few seconds
  after the event (the kernel host's projector).
- **Scoped locks, readiness & streaming exports (#5466, #5592)**
  (`kernelApi.listStudyLocks`, `kernelApi.getStudyLockReadiness`,
  `kernelApi.applyLock`, `kernelApi.releaseLock`, `kernelApi.streamStudyExport`,
  `kernelApi.streamEnrollmentCasebook`): `/data-lock` and `/exports` are enabled
  in `AVAILABLE_SCREENS` (admitting `auditor` and `tmf_auditor` on `/exports`)
  and redirect into Clinical Canvas (`stage=data&section=locks` and
  `stage=submit&section=exports`). `LocksSection.vue` uses cascading
  site/enrollment/form/observation pickers (`kernelApi.getChildren`) with
  `K-AUTH-4` masked placeholders and live readiness blocker counts before Part
  11 step-up signing. `ExportsSection.vue` streams CSV, ODM v1.3.2 XML,
  Dataset-JSON v1.1.0, `sas_bundle` ZIP, `r_bundle` ZIP, and `study_archive` ZIP
  packages; `ParticipantRecord.vue` streams JSON, ODM XML, HTML, and certified
  `%PDF-1.7` casebooks.

### Help & SOPs drawer (all live screens)

- **Contextual SOP lookup, search & ticket escalation (`ADR-2641`)**
  (`knowledgeApi.resolveContextualHelp`, `knowledgeApi.listArticles`,
  `knowledgeApi.getArticle`, `knowledgeApi.listCategories`,
  `ticketsApi.createTicket`): loads published, locked SOP guidance
  (`SOP-STD-101`, `SOP-STD-102`, `SOP-ECRF-201`, `SOP-CTMS-302`, `SOP-SSU-205`)
  across `/studies`, `/studies/:studyCode`, `/ecrf` (including initial mount),
  `/ctms`, and `/ssu` for all 6 staff personas via Hierarchical Specificity
  Scoring. Failed lookups or searches render an accessible `role="alert"` banner
  with `#btn-contextual-help-retry` instead of empty-state copy. Escalations
  post to `POST /api/v1/tickets` with `X-Change-Reason`, show a confirmation
  toast with the returned ticket reference ID (`TKT-...`), and retain simulated
  `SIM-...` tickets in `sessionStorage` during isolated demo builds.

## Hidden screens

Screens with no .NET backend are hidden until their module serves the endpoints
they need:

- They are left out of primary navigation, the global ⌘K command palette,
  onboarding checklists/launchers and guided tours.
- Guided tours stay opt-in on sign-in (`isActive = false` until started from
  **Orientation** or the command palette) and visit only live, role-authorized
  routes.
- Opening a hidden route shows the "not available yet" page (`/unavailable`),
  which links to #4829 and always offers **Open study workspace** (`/studies`)
  alongside **Go to an available screen** (`/`).
- `/` lands each role on `/overview` by default, or when `bypassOverview` is
  enabled on its primary live workflow screen (`/ecrf`, `/ctms`) and falls back
  to `/studies` when the role has no dedicated non-canvas screen
  (`sponsor_designer`, `auditor`).
- The shell doesn't fetch notifications, because no .NET service serves them
  yet.

The list of available screens is `AVAILABLE_SCREENS` in
`apps/web/src/router/availability.js`. The hidden screens' code stays in the
repo as a reference for the UI, the way the Python services are the reference
for the .NET port.

The missing endpoints are tracked by module, not one issue per Python path:

- #4828: what the eCRF needs from Execution, Designer and Notifications (listing
  subjects, served and used by the eCRF, the visit schedule and form
  definitions, the user's notifications).
- #4829: every hidden screen, with the module and kernel roadmap phase
  (`docs/kernel/CLINICAL_KERNEL_SPECIFICATION.md` §15) that will give it a
  backend. #4787 decides which scaffold service becomes which module.

## Actions with no backend

Some regulated actions never called an API. They recorded the action in the
browser and reported success in every build (#4827). They now start with
`allowUnbackedAction(...)` (`apps/web/src/utils/unbackedActions.js`). Outside a
demo build the action is refused with a "not available yet, nothing was saved"
alert and no state changes; a demo build still runs it on sample data. The same
change stopped the wallet, lay summary, tickets, data lock, quality and safety
screens from showing sample data, or closing a dialog as if it worked, when
their API call fails.

- **Enrol a subject:** `stores/clinical.ts` `enrollSubject` (eCRF, CRC form).
- **Submit the eCRF form (sample form until #4727):** `views/EcrfView.vue`
  `submitEcrf`.
- **Record re-consent from the eCRF:** `composables/useConsentGating.js`.
- **Record SDV:** `views/EcrfView.vue` `saveChange`.
- **Raise, answer, close or reopen a query in the eCRF:** `views/EcrfView.vue`
  (the close prompt's re-authentication verified nothing).
- **Move a query in the rules screen:** `views/RulesView.vue`.
- **Ticket status transitions, comments, attachments, signature, audit export:**
  `views/TicketsView.vue`, `components/tickets/TicketSignModal.vue`
  (`TicketCreateModal.vue` and `ContextualHelpDrawer.vue` now submit to
  `ticketsApi.createTicket`).
- **Verify the safety snapshot seal, physician sign-off:**
  `components/clinical/AggregateSafetyConsole.vue`.
- **CEC reader assessment, chair sign-off:**
  `components/clinical/CecAdjudicationConsole.vue`.
- **Consent signature, publish an ICF version:** `stores/econsent.js`,
  `views/ICFBuilderView.vue`.
- **Commit a digitized protocol:** `views/ProtocolDigitizationView.vue`.
- **MedDRA/WHODrug code suggestions:**
  `components/crf/widgets/MedicalAutocodeWidget.vue` (no suggestions; the
  verbatim term is still captured).
- **RCA, CAPA, breach, KRI, QTL and dossier actions:**
  `views/QualityCockpitView.vue`.
- **Investigator grant financials (templates, fee grids, amendments,
  adjustments), CRA allocation, recruitment target updates:**
  `views/CtmsView.vue` (#4829).
- **Adaptive oncology dose escalation cohort transitions (BOIN / CRM / RAR):**
  `components/clinical/AdaptiveDoseEscalationConsole.vue` (#4829).

When a screen moves onto the real API, replace its `allowUnbackedAction` calls
with calls to the real endpoints.

Some actions produce an outcome only a service can compute, so not even a demo
build can run them on sample data. They call `refuseUnbackedAction(...)`, which
refuses in every build:

- subject screening (`stores/clinical.ts` `screenSubject`, from the eCRF);
- the cross-domain anomaly scan and candidate query adjudication
  (`views/RulesView.vue`).

Their Python-era client (`src/api/execution.ts`) is deleted, together with the
query ledger sync and lab alert calls, which no service serves and nothing
displayed. Every Execution call now goes through `executionApi.ts`. Adding a
participant is in the Clinical Canvas; the eCRF's enrolment form still enrols
sample subjects only in a demo build.

## Moving a screen onto the real API

1. Make sure every endpoint the screen needs is in `docs/openapi/*.json` and
   routed by the gateway. If one is missing, add it to the module's issue rather
   than stubbing it or adding a mock fallback. Check that its request and
   response schemas are complete too. A type with a custom JSON converter (a
   value object or a status) comes out as an empty schema unless the service
   describes it; Execution does this in
   `apps/execution/src/Infrastructure/OpenApi/ExecutionWireTypesOpenApiTransformer.cs`,
   so its IDs and codes are strings and its statuses are string enums.
2. Call it through the typed client generated from those specs, and delete the
   screen's Python-era client functions and their types. The types come from
   `apps/web/scripts/generate-api-types.js`: add the service to
   `GENERATED_SERVICES` and run `pnpm --filter web generate:api`, which writes
   `apps/web/src/api/generated/<service>.ts`. Put the calls in
   `apps/web/src/api/<service>Api.ts` (see `executionApi.ts`). When a checked-in
   file no longer matches its spec, the `api-clients` quality gate (run by
   pre-commit and `pnpm cadence check`) and `tests/api/generated_types.test.js`
   fail, so regenerate after `pnpm cadence sync-openapi-spec` changes a listed
   service's spec.
3. Give the screen a view test in `apps/web/tests/` that mounts it and covers
   its main flow against a mocked client. `tests/available_screen_tests.test.js`
   fails when a route in `AVAILABLE_SCREENS` has no test file that imports its
   view (#4529).
4. Add the route name to `AVAILABLE_SCREENS` and update this page and #4829.

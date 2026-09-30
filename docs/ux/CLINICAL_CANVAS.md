# Clinical Canvas: Interaction Model and Platform Requirements v0.1

|                  |                                                                                                                                                                         |
| ---------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Status**       | Proposed. Direction for all new Cadence UI; normative for the kernel read APIs it names once their module specs land.                                                   |
| **Scope**        | The primary interaction model of the Cadence web experience, and the kernel and API capabilities that model needs                                                       |
| **Builds on**    | [Kernel specification v0.2](../kernel/CLINICAL_KERNEL_SPECIFICATION.md) (cited below as `§n`), its [§15 roadmap](../kernel/CLINICAL_KERNEL_SPECIFICATION.md#15-roadmap) |
| **Mockup**       | [Clinical Canvas primary screen](https://claude.ai/artifact/YJJUUy1LaUz8mYRN4P9jDT) (clickable, ONC-401 sample data, light and dark)                                    |
| **Related work** | Epic #4833 (move `apps/web` onto the .NET API). This document sets the target the migrated client grows toward; §8 says what `apps/web` has built so far.                |

Rules tagged `UX-…` are design invariants. Rules tagged `K-…` are the kernel invariants they rely on.
Requirements tagged `PX-…` (platform exposure) are what the kernel and its APIs must provide for the screen to
work; §6 lists them and §7 names the decisions they force.

---

## 1. Thesis

Enterprise clinical software makes users navigate the vendor's product catalogue: _do I go to CTMS, EDC,
eTMF, Pinnacle 21, Safety or QMS?_ Cadence users should only have to know **which study they are working
on**. Everything else organises itself around that.

This is the kernel's founding invariant turned into UI. K-TRUTH-1 says no product owns clinical truth; the
interface therefore has no reason to be partitioned by product. EDC, CTMS, eTMF and conformance validation
are implementation concepts (§2.1, §12). The user sees the study, its lifecycle and the objects in it.

> **UX-1** No primary navigation element, page title or URL segment names a product (EDC, CTMS, eTMF, RTSM,
> P21, QMS, Safety system). Navigation names the study, the lifecycle stage and clinical objects.

Guiding principle: **simple at the surface, enormously deep underneath.** Density is available on request,
never imposed.

---

## 2. Principles

### 2.1 The study is the fundamental object

The top-level experience is a study home (`ONC-401 / Phase III Melanoma`), not an application launcher. A
portfolio view exists above it, but a user spends their day inside one study at a time.

### 2.2 Navigation follows the lifecycle

```
DESIGN ───── START ───── CONDUCT ───── DATA ───── ANALYZE ───── SUBMIT
```

| Stage   | Sections                                                                                             |
| ------- | ---------------------------------------------------------------------------------------------------- |
| Design  | Protocol, objectives and estimands, schedule of activities, collection specs, standards and mappings |
| Start   | Countries, site activation, essential documents, training                                            |
| Conduct | Participants, sites, visits, safety, supply, monitoring                                              |
| Data    | Collection, queries, external data, reconciliation, standards, validation                            |
| Analyze | ADaM, derivations, analysis, TLFs, traceability                                                      |
| Submit  | Datasets, Define-XML, reviewer guides, package                                                       |

Stages overlap in time (Conduct and Data run together), so the rail shows several stages as current. The
stages mirror the lifecycle in §0 of the kernel specification.

### 2.3 Everything is zoomable

The study is progressively zoomable and the UI keeps context while zooming:

```
Portfolio → Study → Country → Site → Participant → Visit → Assessment → Observation
ONC-401 / United States / Site 017 — Mayo Clinic / 017-0042 / Week 12 / Laboratory / Potassium
```

> **UX-2** The zoom path is always visible and every level is addressable. Moving between levels never
> opens a different application or loses the current selection.

The zoom path is a **presentation path**, not the data model. Activity is not tied to a site (K-LOC-1): a
home-health visit or an ePRO entry still sits under its responsible StudySite in the path because every
activity is attributed to one (§7.3), while its location is a property shown in the inspector.

### 2.4 One interaction model: navigator, workspace, inspector

```
┌─────────────┬──────────────────────────────────┬─────────────────┐
│ NAVIGATOR   │            WORKSPACE             │    INSPECTOR    │
│ stage       │  whatever you're working on:     │ properties      │
│ sections    │  study home, participant         │ history         │
│ zoom tree   │  timeline, SoA canvas, dataset,  │ trace · impact  │
│             │  validation, simulation          │ explain · act   │
└─────────────┴──────────────────────────────────┴─────────────────┘
```

> **UX-3** Every screen uses the three-pane model. The workspace changes; the navigator and the inspector
> behave the same everywhere. Selecting any object in any workspace fills the inspector with that object.

On narrow screens the panes stack (navigator, workspace, inspector) and selecting an object scrolls to the
inspector. The inspector is never a modal.

### 2.5 Command palette everywhere

`⌘K` opens one palette for **navigation, commands and questions**: search results, actions the user may run
in the current context, and "Ask Cadence". The application will have thousands of operations; menus do not
scale to that and the palette does.

> **UX-4** The palette lists only commands the current actor is authorised to run in the current context,
> or lists them disabled with an Explain affordance. It never offers an action that will fail on
> authorization without saying why.

### 2.6 Six universal verbs

| Verb         | Question                                    | Direction                        | Kernel basis                                                                                 |
| ------------ | ------------------------------------------- | -------------------------------- | -------------------------------------------------------------------------------------------- |
| **Search**   | Find anything.                              | —                                | Code resolution (K-ID-5), identity layers (K-IDN-1)                                          |
| **Trace**    | Where did this come from?                   | Backward through lineage         | Lineage graph (§9.6), provenance (§9.5)                                                      |
| **Impact**   | What depends on this?                       | Forward through lineage          | Lineage graph (§9.6), amendment impact (K-PROT-2)                                            |
| **Explain**  | Why is this happening?                      | Across authority, state, metrics | Authorization with missing evidence (K-AUTH-6), Applicability (§7.5), Findings (K-FIND-3)    |
| **Simulate** | What happens if I change this?              | A branch of the study            | Pure schedule (K-TIME-7), reproducible projections (K-LIN-3), deterministic rules (K-RULE-4) |
| **Act**      | Do something, with authorization and audit. | A command                        | Command pipeline (§5.1), reason for change (K-TX-3)                                          |

> **UX-5** Trace, Impact, Explain and Act are available from the inspector for every selectable object,
> or shown disabled. A verb is never hidden for one object type and present for another without a visible
> disabled state.

The verbs compose into one loop, which is the product's signature interaction:

```
         TRACE ◄──────► IMPACT        understand
               └────┬───┘
                 EXPLAIN              why
                    ▼
                 SIMULATE             what if
                    ▼
                  REVIEW              an accountable person looks
                    ▼
                   ACT                a command, authorised
                    ▼
                  AUDIT               recorded, traceable again
```

Trace becomes the universal answer to _why does this exist?_ It applies to an SAE, a consent, a protocol
deviation, a TLF cell, an SDTM variable, a query, a monitoring finding, a specimen, a payment trigger, a
signature and an AI recommendation. Impact is static analysis for clinical trials.

### 2.7 Role lenses, not role applications

An investigator, a CRA, a data manager and a statistician open the same study and see different lenses: a
work queue at the top of study home and a different default ordering of Needs Attention.

> **UX-6** A lens changes emphasis and defaults, never what the user is allowed to see or do. What a user
> may see and do comes from authority (§9.3). A lens is computed from the user's Grants and Delegations in
> scope, not selected from a role string.

### 2.8 Progressive disclosure instead of dashboards

One **Study Pulse** (enrollment, data, safety, sites, quality, supply, TMF, submission) replaces the
dashboard catalogue. Selecting a metric explains it recursively, each step narrowing scope:

```
Data health 97.4% → 3,482 incomplete observations → 72% at Site 017 → Lab CRF → Amendment 4 transition
```

> **UX-7** Every Pulse number drills down to the objects that produce it. A number that cannot be
> explained down to objects does not belong on the Pulse.

### 2.9 AI inside the workflow

No floating chatbot. Analysis appears where the problem is (on a group of validation findings, on a
metric, on an impact graph) and ends in a **Proposal**: a plan, its reasoning, its affected objects and a
simulation, with "no regulated data will be modified until approved" as a visible statement of fact (§11,
K-AI-1).

> **UX-8** AI output is always labelled as AI output, always links to the lineage it reasoned over
> ("Inspect reasoning"), and never presents an Execute control that bypasses an accountable human.

### 2.10 Protocol, schedule and participant as canvases

- **Protocol** has four views of one object: Document | Structure | Timeline | Dependencies. Document is
  a rendering (the PDF); Structure is the executable protocol (§6.2).
- **Schedule of activities** is an interactive grid. Hovering a cell shows its window, required
  specimens, dependencies and the data it generates, with Edit, Trace and Impact.
- **Participant** is a timeline of planned and actual events with an inspector, not twenty tabs.

### 2.11 Validation as diagnostics

Conformance validation reads like IDE diagnostics: severity counts, findings grouped by domain and rule,
expected versus observed, a root-cause link (mapping rule, CRF version, CT package) and actions (view
records, trace mapping, fix rule). Fixing the cause re-runs the affected rules and resolves findings by run
(K-FIND-1).

### 2.12 Explain instead of error codes

A blocked action says what is required, which requirements are met, which are not, and what it is waiting
on, with a link to each missing item. `AUTHORIZATION_FAILURE_314` is never user-facing text.

> **UX-9** Every denial and every disabled control can be explained in terms of evidence the user can
> open. Error codes (K-ERR-1) appear only as a secondary detail for support.

### 2.13 Design language

Linear × Figma × VS Code, with the calm of a cockpit: quiet neutral surfaces, one accent, semantic colour
only for state (good, warning, critical) and a separate colour reserved for AI. Monospaced type for
identifiers and dataset values, tabular numerals everywhere numbers align, keyboard shortcuts for the
verbs (`⌘K` palette, `⌘E` explain). Light and dark themes are designed together. Blinded information is
rendered as masked, never as absent (§6.13).

---

## 3. The primary screen

The [mockup](https://claude.ai/artifact/YJJUUy1LaUz8mYRN4P9jDT) draws the study home, one participant and
one simulation, all with ONC-401 sample data. Its **Platform** toggle annotates every element with the
capability it needs, matching §6.

| Area                   | What it shows                                                                                                                            |
| ---------------------- | ---------------------------------------------------------------------------------------------------------------------------------------- |
| Top bar                | Zoom path, `⌘K` search, platform-contract toggle, current user                                                                           |
| Study header           | Study name, current stage, lens selector, lifecycle rail                                                                                 |
| Navigator              | Sections for the selected stage; zoom tree from study to observation                                                                     |
| Workspace: study home  | Lens work queue, Study Pulse (six metrics), Needs Attention (three items), Activity stream with filters                                  |
| Workspace: participant | Timeline of 017-0042 with events (consent, masked randomization, SAE, Week 12), Week 12 laboratory results, a disabled Sign with Explain |
| Workspace: simulation  | Current versus simulated study for Amendment 4, the change set, validation inside the branch, Request approval                           |
| Inspector              | Header (kind, title, identifier) and the verb bar: Details, Trace, Impact, Explain, Simulate, Act                                        |

The sample tells one connected story, because that is what the platform must be able to do: Amendment 4
became effective at Site 017 six days ago, the transition policy requires re-consent, Site 017's tablets
still collect potassium with Lab CRF v12 in mg/dL, so 31 SDTM LB records fail a unit-conversion rule, which
blocks the investigator's Week 12 signature for participant 017-0042. The same chain is reachable from
Needs Attention, from the Data Pulse metric, from the finding, from the observation and from the disabled
Sign button. **Every entry point converges on the same lineage.**

---

## 4. Trace and Impact in detail

### 4.1 Trace

Backward from a selected object to what caused, required, governed and produced it:

```
ADSL.AGE 47 ─derivedFrom→ DM.AGE 47 ─standardizedFrom→ AGE observation 47 years
  ─collectedBy→ Demographics (017-0042) ─requiredBy→ Protocol v3 §6.2
  provenance: Site 017 · Sarah Johnson · 14 MAR 2027
```

Each node is clickable and navigates (zooms) to that object with the inspector filled. The chain shows edge
kinds from §9.6 (`derivedFrom`, `mappedBy`, `collectedBy`, `governedBy`, …) in plain words.

### 4.2 Impact

Forward from a selected object to everything that depends on it, aggregated by kind with counts, then
expandable to objects:

```
AMENDMENT 4 → 47 sites (7 pending) · 412 participants (83 affected) · 18 metadata elements (4 CRFs)
  4 CRFs → 3 SDTM mappings · 2 ADaM derivations · 1 eCOA instrument
```

Impact on a ProtocolVersion is K-PROT-2 (amendment impact is computable). Impact on anything else (a visit
window, a mapping rule, a CT package version, a rule version) is the same dependents query over the
lineage graph (PX-6).

---

## 5. Blinding, identity and consent in the UI

The canvas makes the kernel's access rules visible rather than working around them:

- **Blinding** (K-AUTH-4): an arm assignment the viewer may not see renders as "Randomized, arm masked",
  and a Trace through a blinded node shows the node as masked. The existence of the fact is not hidden;
  its content is.
- **Identity layers** (K-IDN-1): participants appear by `SubjectCode`. Search by name, MRN or date of birth
  requires `identity.reveal.<layer>` and is audited; without it such queries return nothing and say why.
- **Consent scope** (K-CONS-1): Trace and Impact over data used for a purpose (analysis, specimen use)
  show when a use is outside the participant's consent scope.

---

## 6. What the platform must expose

This is the list of capabilities the screen needs from the kernel and its APIs. Each is a read model or a
command surface over concepts the kernel specification already defines, unless the "Spec gap" column says
otherwise. Query shapes are illustrative; paths and payloads are fixed by each capability's module spec
when its roadmap phase starts (§15). Phase numbers refer to the §15 roadmap.

| ID    | Screen element                          | Capability                                                                                                                                                                                                       | Kernel basis                                              | Phase             | Spec gap                                                                                                |
| ----- | --------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------- | ----------------- | ------------------------------------------------------------------------------------------------------- |
| PX-1  | Zoom path, navigator tree               | Scope tree: children of any scope node (study, country, study site, participant, visit, assessment), with counts                                                                                                 | §3.1 hierarchy, §9.3 scope path, §7.4 events              | 1                 | Decided (ADR-2481, K-ID-7); `GET nodes/{ref}/path` and `/children` (ADR-2540); levels below participant arrive with their slices (#4776, #4727)          |
| PX-2  | Every selectable object, URLs, palette  | Universal object reference: `kind + id + version`, resolvable to a summary card and a canonical URL                                                                                                              | §3.2 identifiers, LineageNode (§9.6)                      | 0–1               | Decided: NodeRef `kind:id@version` (ADR-2481, K-ID-6); `GET nodes/{ref}` (ADR-2540)                                                   |
| PX-3  | Search, palette                         | Search across kernel objects by code, title and concept, authorised per actor, with code resolution                                                                                                              | K-ID-5, K-IDN-1                                           | 1                 | Implemented: `IKernelSearch` (ADR-2484), `GET search` (ADR-2540); participants and visits join as their slices land              |
| PX-4  | Palette actions, Act verb               | Command catalogue in context: commands the actor may run on the selected object, each with a dry-run authorization (`Permit` or `Deny(missing_evidence)`)                                                        | §5.1, §9.3, K-AUTH-6                                      | 1                 | Implemented: `IAuthorizationProbe.CanIAsync` and `GetCommandsAsync` (ADR-2483), `GET authorization/can-i` and `nodes/{ref}/commands` (ADR-2540)     |
| PX-5  | Trace                                   | Lineage traversal upstream from any node, with depth, edge kinds, `(valid_at, known_at)` and redaction of nodes the actor may not read                                                                           | §9.6, K-LIN-1..3, §9.5, K-AUTH-4                          | 2 (lineage store) | Read contract: `GET nodes/{ref}/lineage` (ADR-2540) over ADR-2471, masked objects in place              |
| PX-6  | Impact                                  | Dependents downstream from any node, aggregated by kind with counts, expandable to objects                                                                                                                       | §9.6, K-PROT-1, K-PROT-2                                  | 2                 | Resolved by D2 (#4841); `GET nodes/{ref}/dependents` and `protocol-versions/{ref}/impact` (ADR-2540)    |
| PX-7  | Explain on a denial or disabled control | Authorization explanation: each condition evaluated, met or unmet, with the evidence object behind it                                                                                                            | §9.3, K-AUTH-2, K-AUTH-5, K-AUTH-6                        | 1                 | Implemented: `AuthorizationDecision.Conditions` and `Explain` (ADR-2482); evaluator ADR-2455           |
| PX-8  | Explain on a state or metric            | State explanation: why an object is in its state (governing version, pending transition, finding root cause, metric drivers) as a chain of claims with evidence                                                  | §7.5, K-TIME-6, K-TIME-8, K-FIND-3                        | 1–2               | Model decided (ADR-2482); producers #4896                                                               |
| PX-9  | Study Pulse                             | Aggregates per study and scope (enrollment vs forecast, clean data, open SAEs, active sites, signals, TMF, submission readiness), each drillable to the objects behind it                                        | Projections with provenance (§11), QbD indicators (§10.4) | 1–3               | Pulse metrics as named, versioned indicator definitions (D6)                                            |
| PX-10 | Needs Attention                         | Attention feed: open Findings, Signals, Effectivity gaps, pending Applicability transitions, overdue safety follow-ups, Proposals awaiting approval, each linked to a node                                       | §7.1, §7.5, §10.3, §10.4, §11                             | 1–3               | Read model and ranking inputs (D6)                                                                      |
| PX-11 | Activity stream                         | Event read model filtered by scope, family and actor, authorised per reader                                                                                                                                      | §5.2 events, §5.3 audit                                   | 1                 | Implemented: `IActivityStream` (ADR-2485); HTTP endpoint and live push wait for #4780, #4781            |
| PX-12 | Inspector: History                      | Audit and attestation history of one object, including corrections and supersessions                                                                                                                             | §5.3, K-FACT-1, §9.5 Attestation                          | 0–1               | —                                                                                                       |
| PX-13 | Inspector: as-of                        | Every read above accepts `(valid_at, known_at)` so the inspector can show the object as it was known at a time                                                                                                   | §1 principle 2, K-TIME-*                                  | 0–1               | UI affordance only; kernel already bitemporal                                                           |
| PX-14 | Participant timeline                    | Planned schedule (pure, K-TIME-7) merged with ClinicalEvents, Applicability intervals, consent intervals and assertions (deviations, SAEs)                                                                       | §7.4, §7.5, §9.4                                          | 1                 | —                                                                                                       |
| PX-15 | Lens work queue                         | Per-actor work items in scope, derived from Grants and Delegations (signatures due, queries, visits, SAE follow-ups)                                                                                             | §9.3                                                      | 1                 | Work-queue definitions (D10)                                                                            |
| PX-16 | Simulate                                | Evaluate a proposed change (ProtocolVersion, visit window, mapping, rule pack) against current state without writing, returning a structural diff, changed schedules, changed projections and validation results | K-TIME-7, K-PROJ-2, K-LIN-3, K-RULE-4, K-PROT-1           | 2–3               | Decided: evaluate-only over candidate versions (ADR-2480, spec §11.1, K-SIM-1..5); structural diff built (#4866) |
| PX-17 | AI analysis, Proposed plan              | Proposal with plan, reasoning artifact, affected lineage nodes, simulation reference and requested capabilities; approval as Attestation                                                                         | §11, K-AI-1..3, K-PROV-3                                  | 3                 | — (Proposal is defined)                                                                                 |
| PX-18 | Validation diagnostics                  | Findings grouped by rule and target, expected vs observed, root-cause lineage, re-run on fix                                                                                                                     | §10.3, K-FIND-1..3                                        | 2                 | —                                                                                                       |
| PX-19 | Act                                     | Every command accepts a reason for change and an idempotency key, and returns the resulting object reference for the inspector                                                                                   | §5.1, K-TX-3, K-TX-5                                      | 0–1               | —                                                                                                       |
| PX-20 | Masked rendering                        | Every read returns "exists but masked" for blinded or identity-layer content instead of omitting it                                                                                                              | K-AUTH-4, K-IDN-1                                         | 1                 | Masking contract across APIs (D1)                                                                       |

The HTTP reads named above are `GET` endpoints under `/api/v1/kernel` on the kernel API host
([apps/kernel-api](../../apps/kernel-api/README.md), ADR-2540), routed by the gateway, in the kernel tenant found from
the caller's signed tenant code, with dry-run decisions from the caller's grants (ADR-2455). Until visibility is
decided from the authority model (#4781), every object is masked.

### 6.1 Order of delivery

The canvas can ship incrementally in roadmap order without faking data:

1. **With phase 0–1 (EDC on the kernel):** study home shell, lifecycle navigation, zoom tree (PX-1, PX-2),
   participant timeline (PX-14), inspector details and history (PX-12, PX-13), Act with reason for change
   (PX-19), Explain for authorization (PX-7), activity stream (PX-11), palette with command catalogue
   (PX-3, PX-4), first Pulse metrics (enrollment, sites, open SAEs).
2. **With phase 2 (knowledge and standards, lineage store):** Trace and Impact (PX-5, PX-6), validation
   diagnostics (PX-18), state explanations (PX-8), data and submission Pulse metrics.
3. **With phase 3 (quality and trust):** quality signals in Pulse and Needs Attention, AI Proposals (PX-17),
   simulation (PX-16) once D4 is decided.

Until a capability exists, its screen element is absent, not mocked. `apps/web` must not ship a Trace panel
fed by client-side guesses (K-TRUTH-1 applies to the UI too).

---

## 7. Decisions this forces

Drawing the screen surfaced platform questions the kernel specification does not answer yet. Each needs a
decision (an ADR, or a section in the relevant module spec) before its capability is built. Each has an
issue under epic #4719.

| #           | Decision                            | Recommendation                                                                                                                                                                                                                                                                                                                                                       |
| ----------- | ----------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| D1 (#4840)  | Lineage read contract and redaction | One kernel query `Lineage(node, direction, depth, edge_kinds, valid_at, known_at)` returning nodes and edges; nodes the actor cannot read are returned as `Masked(kind, reason)`, never dropped, so a trace never silently shortens.                                                                                                                                 |
| D2 (#4841)  | Impact beyond amendments            | Generalise K-PROT-2 to `Dependents(node, …)` over any LineageNode; amendment impact becomes one caller of it. Aggregation by kind happens in the kernel so counts are consistent across screens.                                                                                                                                                                     |
| D3 (#4842)  | Explanation model                   | `Explanation{ subject, claim, steps[ claim, evidence_ref[] ], unmet_conditions[], next_actions[] }`, produced by the component that owns the rule (authorization, Applicability, Finding, indicator). The UI renders it; it never composes explanations itself. **Decided in ADR-2482** (spec §5.5).                                                                                                      |
| D4 (#4843)  | Simulation                          | Start with **evaluate-only simulation**: run the pure functions the spec already guarantees (expected schedule K-TIME-7, projections K-PROJ-2, rules K-RULE-4) against a proposed change in memory, write nothing, and persist only the result as a Proposal artifact. Copy-on-write study branches are a later option if evaluate-only is not enough. **Decided in ADR-2480** (spec §11.1). |
| D5 (#4844)  | Activity stream                     | A reader-facing projection of domain events, filtered by scope and redacted per reader like any other read. Integration events stay the integration contract (K-EVT-2). **Decided in ADR-2485** (spec K-EVT-3): an asynchronous, payload-free projection of outbox envelopes, scoped by the subject's zoom path and masked per reader through D1 at read time.                                                                                                                                                                                              |
| D6 (#4845)  | Pulse and attention definitions     | Pulse metrics and attention rules are **Indicators** (§10.4): versioned Rules evaluated by the same engine, so a number is reproducible and explainable by construction. Ranking in Needs Attention is application logic over them.                                                                                                                                  |
| D7 (#4846)  | Object reference and zoom levels    | A public `NodeRef` (`kind:id@version`) for every LineageNode, used in URLs, the palette, trace links and events; the presentation zoom path is computed from NodeRef by the kernel (participant → responsible StudySite → country → study). **Decided in ADR-2481** (spec §3.4); `IKernelNavigator` implements it.                                                                                                                          |
| D8 (#4847)  | Dry-run authorization               | `Authorize` exposed as a read (`CanI(capability, resource)`) that returns the same Permit or Deny with conditions, audited at a lower tier than a real decision, so controls can render disabled with Explain. **Decided in ADR-2483** (spec K-AUTH-7).                                                                                                                                                       |
| D9 (#4848)  | Search                              | Kernel-scoped search index per tenant over codes, titles and concepts, with row-level security, excluding IdentityVault content unless the reveal capability applies. **Decided in ADR-2484** (spec K-ID-8): full-text search over the source tables themselves, not a copy.                                                                                                                                                                                                |
| D10 (#4849) | Lenses and work queues              | Work-queue definitions are data keyed by capability (for example, `observation.approve` due items), so a user's lens follows their Grants rather than a role name.                                                                                                                                                                                                   |

---

## 8. Relationship to the current web app

`apps/web` today is a Vue client typed against Python-era API specifications and is being moved onto the
real .NET API under epic #4833; only Execution is a real backend so far. This document does not ask that
work to change course. It adds three constraints for any screen built or rebuilt from now on:

1. Use the three-pane model (UX-3) and study-centric navigation (UX-1) for new screens.
2. Read clinical facts, explanations and lineage from the kernel; never reconstruct them in the client.
   Explanations are rendered from the kernel's `Explanation` (ADR-2482, K-EXP-1), never built in the web app.
3. Render authorization and blinding outcomes as the kernel returns them (UX-9, §5).

The first shell is in `apps/web` at `/studies/:studyCode` (`StudyCanvasView.vue`). It has the lifecycle
rail, the three panes, the zoom path and the `⌘K` palette, and shows only what Execution serves today:
participants by state on study home (an interim count from Execution's list totals until Pulse metrics are
Indicators, D6), the Participants section (adding a participant in screening, and moving a participant between
the states Execution allows, less unblinding), Site activation, and a participant's data (observations by visit
with source data verification, queries with their transitions, and form submissions). Selections are addressed by NodeRef in the
URL. For a selected participant, Trace lists the kernel's lineage walk back from it, edge by edge, and Impact
counts what depends on it by kind, expandable to objects; objects the reader may not see stay in place as hidden and
are counted (ADR-2540). Act lists the kernel's commands with the reader's dry-run decision and reason,
and Explain renders the kernel's explanation of a chosen decision (ADR-2540); neither sends a command. The `⌘K`
palette adds the studies kernel search finds by code or title (ADR-2484), leaving out any the reader may not see. Simulate,
and the kernel verbs on an observation (no kernel reference yet), are shown disabled, each
naming what it waits on, and stages with no service are empty (§6.1). [docs/web/API_MIGRATION.md](../web/API_MIGRATION.md) tracks what it
adds next.

---

## 9. Open questions

1. Where does the portfolio level live for users who work across many studies (CRAs, medical monitors)?
   A cross-study lens, or a portfolio home with the same three panes?
2. How much of Pulse is configurable per study, given D6 makes metrics versioned Rules?
3. ~~Does Simulate need multi-user review of a branch (comments on a diff), and if so, is that an application
   concern over Proposals?~~ Answered by ADR-2480 §4.5: review is application state over Proposals; the kernel
   provides the Proposal, the saved result Artifact and approval as an Attestation.
4. Offline eSource and ePRO clients (§13) cannot run the full inspector. Which verbs must work offline?

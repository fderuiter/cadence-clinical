# AI Agents in Cadence

AI agents do the work at every step of a study and stay inside one regulated
path: **agents read and propose; an accountable person approves; the change
executes as that person, with the agent, model and task configuration
recorded.** The decision is
[ADR-2630](../adr/2026-10-20-ai-agents-act-through-proposals-in-one-governed-runtime.md)
(kernel decision K037, spec §11); the work is tracked in epic
[#5545](https://github.com/fderuiter/cadence-clinical/issues/5545).

This page is the catalogue: the agents, the research-type packs, the autonomy
ladder and where each regulatory control lives. It is a plan: an agent is built
when its issue lands, and each entry's state says which exist.

The runtime, the Intelligence module (`apps/intelligence`, composed into the
kernel API host,
[#5374](https://github.com/fderuiter/cadence-clinical/issues/5374)), holds the
model port, versioned task configurations qualified per tenant with a signature
on evaluation evidence
([ADR-2631](../adr/2026-10-20-agent-task-configurations-are-versioned-and-qualified-per-tenant.md),
[#5385](https://github.com/fderuiter/cadence-clinical/issues/5385)), the
interaction ledger of agent runs, and per-tenant kill switches and token
budgets, under `/api/v1/ai`. The evaluation harness that produces qualification
evidence (stored evaluation runs) and the monitoring that suspends a
configuration are built
([ADR-2634](../adr/2026-10-20-ai-task-configurations-are-measured-by-versioned-evaluations-and-production-signals.md),
[#5548](https://github.com/fderuiter/cadence-clinical/issues/5548),
[#5574](https://github.com/fderuiter/cadence-clinical/issues/5574)), and so is
the kernel side of the agent actor and requester-scoped grants
([ADR-2632](../adr/2026-10-20-agents-act-for-one-person-and-approved-proposals-execute-as-their-approver.md),
[#4783](https://github.com/fderuiter/cadence-clinical/issues/4783)); the module
does not act as an agent actor yet. The agent tools, the citation verifier and
runs that write a Proposal follow in
[#5547](https://github.com/fderuiter/cadence-clinical/issues/5547); PHI
scrubbing, data residency, more providers and a local-only mode in
[#5550](https://github.com/fderuiter/cadence-clinical/issues/5550).

## Principles

1. AI output is never clinical fact until a person makes it one. Agents create
   kernel Proposals only (K-AI-1).
2. An agent acts for one person and reads with that person's grants, never wider
   (K-AI-3, K-AI-6).
3. Grounded or silent: every claim and value cites the kernel nodes the run read
   (K-AI-4).
4. The qualified unit is the task configuration, not the model (K-AI-5,
   ADR-2631).
5. Risk sets the oversight: model influence and decision consequence set the
   evidence and the autonomy level.
6. Every run is retained in the interaction ledger.
7. Agents live in the normal Canvas screens, are labelled as AI (UX-8), and the
   study works the same with AI off (K-AI-8).

## The autonomy ladder

- **Level 0 — Inform:** Answers, explains, summarizes, with citations. _Who
  acts:_ Nobody; read only. _Allowed for:_ Any task.
- **Level 1 — Draft:** Writes a Proposal holding the draft. _Who acts:_ A person
  edits, approves, then executes. _Allowed for:_ Any task, clinical data
  included.
- **Level 2 — Approve-to-execute:** Writes a Proposal holding the exact
  commands. _Who acts:_ A person approves, then sends each command as themself,
  naming the proposal (K-AI-2). _Allowed for:_ Commands the approver could run.
- **Level 3 — Standing grant:** Executes inside a narrow, time-boxed grant a
  person approved. _Who acts:_ The agent, each action reviewable. _Allowed for:_
  Operational commands only (K-AI-7).

## Agents by lifecycle stage

### Design

- **Protocol author** (Approver: Sponsor designer; Max level: `1`; State:
  Planned, replaces the template `GenerativeProtocolAuthor`): Drafts objectives,
  endpoints, estimands (ICH E9(R1)) and the schedule of activities as a USDM
  candidate version.
- **Amendment impact** (Approver: Sponsor designer; Max level: `0`; State:
  Planned): Runs evaluate-only simulation on a draft amendment and explains what
  changes.
- **CRF builder** (Approver: Data manager; Max level: `1`; State: Planned):
  Proposes CDASH-aligned collection specs and edit checks from the protocol.
- **Eligibility feasibility** (Approver: Sponsor designer; Max level: `0`;
  State: Planned): Tests criteria against connected real-world data and flags
  restrictive ones.

### Start-up

- **Document drafter** (Approver: Regulatory, IRB/EC owner; Max level: `1`;
  State: Planned): Drafts the consent form, plain-language summary and site
  packs, with a readability check.
- **TMF filer** (Approver: TMF owner; Max level: `2`; State: Planned):
  Classifies uploads to the TMF Reference Model and flags missing essential
  documents.
- **Site feasibility** (Approver: Clinical operations; Max level: `0`; State:
  Planned): Ranks candidate sites from performance history and enrolment
  projections.
- **Budget and contract** (Approver: Finance officer; Max level: `1`; State:
  Planned): Drafts site budgets and payment schedules from the schedule of
  activities.

### Conduct

- **Pre-screener** (Approver: Investigator; Max level: `1`; State: Planned):
  Checks a candidate's source data against eligibility; the investigator
  decides.
- **Source abstractor** (Approver: Site coordinator; Max level: `1`; State:
  Planned): Pulls values from EHR (FHIR) or source documents into proposed
  observations.
- **Data reviewer** (Approver: Data manager; Max level: `2`; State: Planned,
  first agent): Finds inconsistencies the edit checks miss and drafts queries.
- **Medical coder** (Approver: Medical coder; Max level: `2`; State: Planned):
  Suggests MedDRA and WHODrug codes with the dictionary path.
- **Central monitor** (Approver: Monitor, study lead; Max level: `2`; State:
  Planned): Watches KRIs and QTLs and proposes targeted monitoring (ICH E6(R3)).
- **Deviation detector** (Approver: Study lead, QA; Max level: `1`; State:
  Planned): Finds protocol deviations and proposes their classification.
- **Visit report writer** (Approver: Monitor; Max level: `1`; State: Planned):
  Drafts the monitoring visit report and follow-up letter.
- **Supply forecaster** (Approver: Supply manager; Max level: `0`; State:
  Planned): Forecasts drug or device supply per site.
- **Participant helper** (Approver: Site coordinator; Max level: `0`; State:
  Planned): Answers participant questions only from approved consent and visit
  information.

### Safety

- **Case intake** (Approver: Safety physician; Max level: `1`; State: Planned):
  Structures a reported event and checks seriousness criteria and reporting
  clocks.
- **Narrative writer** (Approver: Safety physician; Max level: `1`; State:
  Planned): Drafts the case narrative with every sentence linked to its record
  (PRD-SYS-052).
- **Signal scout** (Approver: Medical monitor; Max level: `0`; State: Planned):
  Looks for disproportionate events across sites and studies.

### Close-out

- **Lock readiness** (Approver: Data manager; Max level: `0`; State: Planned):
  Lists everything blocking database lock.
- **Mapping and conformance** (Approver: Programmer, data manager; Max level:
  `1`; State: Planned): Drafts SDTM and ADaM specs and explains each conformance
  finding through lineage.
- **Report writer** (Approver: Medical writer; Max level: `1`; State: Planned):
  Drafts clinical study report sections (ICH E3) from locked data.

### All stages

- **Inspector's guide** (Approver: Auditor, QA; Max level: `0`; State: Planned):
  Explains any record's history and why someone can or cannot act (K-EXP).

Level 3 never covers observations, eligibility, causality, seriousness, coding
approval, signatures, unblinding or any change to what the study claims about a
participant.

## Research-type packs

| Research type                | What its pack adds                                                                                                                               |
| ---------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------ |
| Drug and biologic            | IND/CTA documents, SUSAR and DSUR rules, ICH E2A and E2B(R3) clocks, WHODrug                                                                     |
| Medical device               | ISO 14155 investigation plan, device deficiencies, ADE/SADE/USADE classification, EU MDR Article 80 and FDA IDE reporting, device accountability |
| Diagnostic (IVD)             | Performance study design, specimen chain of custody, EU IVDR reporting                                                                           |
| Cell and gene therapy        | Long-term follow-up schedules, chain of identity, delayed adverse events                                                                         |
| Observational and real-world | No interventions; EHR and registry sources; no dosing or supply agents                                                                           |
| Investigator-initiated       | One person may hold several roles; separation of duties still refuses self-approval                                                              |

## How a run works

Steps 1 and 2 and the ledger record in step 5 are built; the tools, the citation
verifier and the Proposal a run writes (steps 3 to 5) come with
[#5547](https://github.com/fderuiter/cadence-clinical/issues/5547).

1. A person asks (Assist panel, command palette, a form) or a kernel event fires
   (new data, an SAE, an amendment, an indicator breach).
2. The runtime picks the newest task configuration version the tenant has
   qualified on its current content (or refuses, saying why) and checks the
   budget and kill switch.
3. The agent reads through tools that carry the requester's grants, narrowed by
   the configuration's allow-list.
4. The citation verifier drops anything that does not trace to a node the run
   read.
5. The runtime writes one kernel Proposal naming the model and configuration
   version, and records the run in the interaction ledger.
6. A person reviews the diff and citations and approves with an attestation.
   Approval runs nothing: the approver then sends each of the proposal's
   commands as themself, naming the proposal (`CommandMetadata.Proposal`,
   K-AI-2).

The kernel side of steps 3 and 6 is built
([ADR-2632](../adr/2026-10-20-agents-act-for-one-person-and-approved-proposals-execute-as-their-approver.md)):
an agent acts for one person and is bounded by that person's grants and
visibility, and the kernel checks each command sent under an approved proposal
against it and links it in provenance (`proposal_id`), events
(`ProposalExecuted`) and lineage (`ExecutedUnder`). Standing grants (level 3)
follow under the same ADR.

## The agent factory

Agents run as a factory
([ADR-2630 §4.8](../adr/2026-10-20-ai-agents-act-through-proposals-in-one-governed-runtime.md#48-the-agent-factory)):
agent and task definitions are versioned code tested in CI; context comes from
kernel reads under the requester's grants and from MCP and APIs; agents run in
sandboxed workers with pluggable inference and tenant data kept in the
deployment; orchestration routes each task to its qualified configuration under
budgets; and access is the Canvas, the subject portal and an MCP server.
Evaluation sets from real clinical workflows, a calibrated LLM judge and
production signals measure every configuration, and improvements arrive as
reviewed pull requests, never as self-modification.

## Inference and data sovereignty

[ADR-2636](../adr/2026-10-20-inference-is-pluggable-residency-bound-and-sandboxed.md)
decides where models run. Claude is reached through the Claude API, Claude
Platform on AWS, Amazon Bedrock, Google Cloud Vertex AI or Microsoft Foundry,
and a tenant can be limited to models the operator runs inside the deployment
through an OpenAI-compatible endpoint. One router applies each tenant's
inference policy (`LocalOnly`, `ResidencyBound` by default, or `Open`), the
tenant's residency, the supplier's approval status and the task configuration's
qualified routes, and records the route on the run. Every outbound request
refuses identity-layer content and scrubs direct identifiers from free text as a
protective control. Agent runs execute in a sandbox with no network beyond the
routed model endpoint and the kernel read ports. Each provider has a supplier
record the sponsor's quality unit approves (Annex 11 §3).

## Regulatory map

[ADR-2630 §4.10](../adr/2026-10-20-ai-agents-act-through-proposals-in-one-governed-runtime.md#410-regulatory-map)
maps 21 CFR Part 11, EU GMP Annex 11 and the draft Annex 22, ICH E6(R3), GAMP 5,
the FDA draft guidance on AI for regulatory decision-making, the FDA and EMA
Guiding Principles of Good AI Practice, the EMA reflection paper, the device
regulations, the EU AI Act, HIPAA and GDPR to the control and the place it
lives. Cadence supplies the controls; the sponsor owns the validation and
context-of-use argument for its own use.

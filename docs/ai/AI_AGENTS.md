# AI Agents in Cadence

AI agents do the work at every step of a study and stay inside one regulated path: **agents read and propose; an
accountable person approves; the change executes as that person, with the agent, model and task configuration
recorded.** The decision is [ADR-2630](../adr/2026-10-20-ai-agents-act-through-proposals-in-one-governed-runtime.md)
(kernel decision K037, spec §11); the work is tracked in epic
[#5545](https://github.com/fderuiter/cadence-clinical/issues/5545).

This page is the catalogue: the agents, the research-type packs, the autonomy ladder and where each regulatory control
lives. It is a plan: an agent is built when its row's issue lands, and the **State** column says which exist.

## Principles

1. AI output is never clinical fact until a person makes it one. Agents create kernel Proposals only (K-AI-1).
2. An agent acts for one person and reads with that person's grants, never wider (K-AI-3, K-AI-6).
3. Grounded or silent: every claim and value cites the kernel nodes the run read (K-AI-4).
4. The qualified unit is the task configuration, not the model (K-AI-5, ADR-2631).
5. Risk sets the oversight: model influence and decision consequence set the evidence and the autonomy level.
6. Every run is retained in the interaction ledger.
7. Agents live in the normal Canvas screens, are labelled as AI (UX-8), and the study works the same with AI off
   (K-AI-8).

## The autonomy ladder

| Level                | The agent                                                    | Who acts                                                                      | Allowed for                        |
| -------------------- | ------------------------------------------------------------ | ----------------------------------------------------------------------------- | ---------------------------------- |
| 0 Inform             | Answers, explains, summarizes, with citations                | Nobody; read only                                                             | Any task                           |
| 1 Draft              | Writes a Proposal holding the draft                          | A person edits, approves, then executes                                       | Any task, clinical data included   |
| 2 Approve-to-execute | Writes a Proposal holding the exact commands                 | A person approves, and the approval runs the commands as that person (K-AI-2) | Commands the approver could run    |
| 3 Standing grant     | Executes inside a narrow, time-boxed grant a person approved | The agent, each action reviewable                                             | Operational commands only (K-AI-7) |

## Agents by lifecycle stage

| Stage     | Agent                   | What it does                                                                                                    | Approver                 | Max level | State                                                      |
| --------- | ----------------------- | --------------------------------------------------------------------------------------------------------------- | ------------------------ | --------- | ---------------------------------------------------------- |
| Design    | Protocol author         | Drafts objectives, endpoints, estimands (ICH E9(R1)) and the schedule of activities as a USDM candidate version | Sponsor designer         | 1         | Planned (replaces the template `GenerativeProtocolAuthor`) |
| Design    | Amendment impact        | Runs evaluate-only simulation on a draft amendment and explains what changes                                    | Sponsor designer         | 0         | Planned                                                    |
| Design    | CRF builder             | Proposes CDASH-aligned collection specs and edit checks from the protocol                                       | Data manager             | 1         | Planned                                                    |
| Design    | Eligibility feasibility | Tests criteria against connected real-world data and flags restrictive ones                                     | Sponsor designer         | 0         | Planned                                                    |
| Start-up  | Document drafter        | Drafts the consent form, plain-language summary and site packs, with a readability check                        | Regulatory, IRB/EC owner | 1         | Planned                                                    |
| Start-up  | TMF filer               | Classifies uploads to the TMF Reference Model and flags missing essential documents                             | TMF owner                | 2         | Planned                                                    |
| Start-up  | Site feasibility        | Ranks candidate sites from performance history and enrolment projections                                        | Clinical operations      | 0         | Planned                                                    |
| Start-up  | Budget and contract     | Drafts site budgets and payment schedules from the schedule of activities                                       | Finance officer          | 1         | Planned                                                    |
| Conduct   | Pre-screener            | Checks a candidate's source data against eligibility; the investigator decides                                  | Investigator             | 1         | Planned                                                    |
| Conduct   | Source abstractor       | Pulls values from EHR (FHIR) or source documents into proposed observations                                     | Site coordinator         | 1         | Planned                                                    |
| Conduct   | Data reviewer           | Finds inconsistencies the edit checks miss and drafts queries                                                   | Data manager             | 2         | Planned (first agent)                                      |
| Conduct   | Medical coder           | Suggests MedDRA and WHODrug codes with the dictionary path                                                      | Medical coder            | 2         | Planned                                                    |
| Conduct   | Central monitor         | Watches KRIs and QTLs and proposes targeted monitoring (ICH E6(R3))                                             | Monitor, study lead      | 2         | Planned                                                    |
| Conduct   | Deviation detector      | Finds protocol deviations and proposes their classification                                                     | Study lead, QA           | 1         | Planned                                                    |
| Conduct   | Visit report writer     | Drafts the monitoring visit report and follow-up letter                                                         | Monitor                  | 1         | Planned                                                    |
| Conduct   | Supply forecaster       | Forecasts drug or device supply per site                                                                        | Supply manager           | 0         | Planned                                                    |
| Conduct   | Participant helper      | Answers participant questions only from approved consent and visit information                                  | Site coordinator         | 0         | Planned                                                    |
| Safety    | Case intake             | Structures a reported event and checks seriousness criteria and reporting clocks                                | Safety physician         | 1         | Planned                                                    |
| Safety    | Narrative writer        | Drafts the case narrative with every sentence linked to its record (PRD-SYS-052)                                | Safety physician         | 1         | Planned                                                    |
| Safety    | Signal scout            | Looks for disproportionate events across sites and studies                                                      | Medical monitor          | 0         | Planned                                                    |
| Close-out | Lock readiness          | Lists everything blocking database lock                                                                         | Data manager             | 0         | Planned                                                    |
| Close-out | Mapping and conformance | Drafts SDTM and ADaM specs and explains each conformance finding through lineage                                | Programmer, data manager | 1         | Planned                                                    |
| Close-out | Report writer           | Drafts clinical study report sections (ICH E3) from locked data                                                 | Medical writer           | 1         | Planned                                                    |
| All       | Inspector's guide       | Explains any record's history and why someone can or cannot act (K-EXP)                                         | Auditor, QA              | 0         | Planned                                                    |

Level 3 never covers observations, eligibility, causality, seriousness, coding approval, signatures, unblinding or any
change to what the study claims about a participant.

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

1. A person asks (Assist panel, command palette, a form) or a kernel event fires (new data, an SAE, an amendment, an
   indicator breach).
2. The runtime loads the qualified task configuration for the task and checks the budget and kill switch.
3. The agent reads through tools that carry the requester's grants, narrowed by the configuration's allow-list.
4. The citation verifier drops anything that does not trace to a node the run read.
5. The runtime writes one kernel Proposal naming the model and configuration version, and records the run in the
   interaction ledger.
6. A person reviews the diff and citations and approves with an attestation; the kernel executes the proposal's
   commands as that person (K-AI-2).

The kernel side of steps 3 and 6 is built ([ADR-2632](../adr/2026-10-20-agents-act-for-one-person-and-approved-proposals-execute-as-their-approver.md)):
an agent acts for one person and is bounded by that person's grants and visibility, and each command a person sends
under an approved proposal is linked to it in provenance, events and lineage. Standing grants (level 3) follow under
the same ADR.

## The agent factory

Agents run as a factory ([ADR-2630 §4.8](../adr/2026-10-20-ai-agents-act-through-proposals-in-one-governed-runtime.md#48-the-agent-factory)):
agent and task definitions are versioned code tested in CI; context comes from kernel reads under the requester's
grants and from MCP and APIs; agents run in sandboxed workers with pluggable inference and tenant data kept in the
deployment; orchestration routes each task to its qualified configuration under budgets; and access is the Canvas, the
subject portal and an MCP server. Evaluation sets from real clinical workflows, a calibrated LLM judge and production
signals measure every configuration, and improvements arrive as reviewed pull requests, never as self-modification.

## Regulatory map

[ADR-2630 §4.10](../adr/2026-10-20-ai-agents-act-through-proposals-in-one-governed-runtime.md#410-regulatory-map) maps
21 CFR Part 11, EU GMP Annex 11 and the draft Annex 22, ICH E6(R3), GAMP 5, the FDA draft guidance on AI for
regulatory decision-making, the FDA and EMA Guiding Principles of Good AI Practice, the EMA reflection paper, the device
regulations, the EU AI Act, HIPAA and GDPR to the control and the place it lives. Cadence supplies the controls; the
sponsor owns the validation and context-of-use argument for its own use.

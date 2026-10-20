# ADR-2630: AI Agents Act Through Proposals in One Governed Runtime

> [!NOTE]
> Detailed by [ADR-2632](2026-10-20-agents-act-for-one-person-and-approved-proposals-execute-as-their-approver.md):
> approving a proposal does not run its commands. The approver sends each command and names the approved proposal in
> `CommandMetadata.Proposal` (K-AI-2); the pipeline checks the link.

- **Status:** Accepted
- **Date:** 2026-10-20
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Issue:** #5546 (epic #5545)
- **Requirements:** PRD-SYS-051 (AI output needs human verification with a Part 11 signature and dual attribution),
  21 CFR 11.10(a), (d), (e), (f), (g), 11.50, 11.70
- **Amends:** kernel specification §11 (K-AI-4 to K-AI-8) and §17 (K037); builds on ADR-2489 (K033) and ADR-2610;
  partly supersedes ADR-2191 (the AI gateway's hosting and its `DRAFT_AI` record state)

---

## 1. Context & Problem Statement

AI agents are the hallmark feature of Cadence: agents inside the application that do the work at every step of a
study, for drugs, devices, diagnostics, cell and gene therapy and observational research, and stay within regulatory
boundaries. The integration must be seamless: agents appear where the work already is, not on a separate AI screen.

The kernel already holds the safety core. A `Proposal` records what an agent suggests and changes nothing; approval is
a Part 11 attestation over the proposal's content hash by a person who did not propose it; the authority evaluator
refuses every `Execute` and `Approve` verb to an `agent:` actor (ADR-2489, K033). Nothing else exists:

- `apps/ai_gateway` is a scaffold with a rule-based palette router and no model calls (#5374).
- Nothing records which model, prompt, tools and data sources produced a proposal, or whether that setup was ever
  shown to be fit for the task (#5385).
- An approved proposal cannot be executed from its approval (K-AI-2 is not built).
- Nothing stops an agent from stating a value it never read. A scaffold imaging feature fabricated measurements
  (#5422, fixed by #5447 under #5426).
- PRD-SYS-051 still describes a separate microservice and a `DRAFT_AI` state on regulated records.

This ADR decides how agents are built, run and governed across the platform, and where each regulatory control lives.

## 2. Decision Drivers & Constraints

- **K-AI-1..3, K-PROV-3:** agents read and propose only, through the same authorization as people, and every
  AI-assisted action records its model, plan and approver.
- **K-TRUTH-1:** no AI output is clinical truth until an accountable person makes it so.
- **K-SCOPE-1:** the AI domain (prompts, configurations, evaluation runs, cost) is an application concern; only what
  explains the provenance and authority of evidence belongs in the kernel.
- **K-XS-2:** the AI module references `Cadence.Kernel.Abstractions` and `Cadence.Kernel.Modules` only.
- **Regulation:** 21 CFR Part 11; EU GMP Annex 11 and the 2025 drafts of Annex 11 and Annex 22; ICH E6(R3); GAMP 5
  (2nd edition) and the ISPE GAMP guide on AI; the FDA draft guidance on AI to support regulatory decision-making for
  drugs and biologics (January 2025); the FDA and EMA Guiding Principles of Good AI Practice in Drug Development
  (January 2026); the EMA reflection paper on AI (2024); ISO 14155, EU MDR and IVDR for devices; the EU AI Act; HIPAA
  and GDPR.
- **Fold-in plan rev 2:** a provider-neutral port with a local-only switch per tenant, qualified task configurations,
  consequential outputs as Proposals, scrubbing as a protective control, and budgets that never block clinical work.
- **Pre-deployment:** breaking internal changes are fine; there is nothing to migrate.

## 3. Options Considered

### Option 1: A chat assistant beside the application

- ✅ Quick to build.
- ❌ It answers questions but does no work, sits outside the screens people use, and leaves every output as
  unattributed text a person copies into regulated records.

### Option 2: AI features inside each module

- ✅ Each module tunes its own prompts.
- ❌ Every module reinvents provider calls, PHI controls, cost limits, audit and qualification, and agent output would
  enter each module's records by its own rules. This is how #5422 fabricated measurements.

### Option 3: One governed agent runtime whose only output is a kernel Proposal (selected)

- ✅ Every agent, in every module, passes the same controls: qualified configuration, requester-scoped reads,
  citation check, retained interaction, Proposal, human attestation, execution as the person.
- ✅ Modules supply tools and agent definitions; the runtime and the kernel supply the governance.
- ❌ Agents cannot write anything directly, so each agent needs a Proposal shape for its output. This is the point.

## 4. Decision Outcome

Option 3. It is kernel decision **K037** for the kernel-facing rules (§4.9); the rest is the Intelligence module's
design.

### 4.1 The autonomy ladder

Each task configuration declares the highest level its agent may reach, and its qualification caps it.

| Level                | The agent                                                    | Who acts                                                                                                 | Allowed for                                                            |
| -------------------- | ------------------------------------------------------------ | -------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------- |
| 0 Inform             | Answers, explains, summarizes, with citations                | Nobody; read only                                                                                        | Any task                                                               |
| 1 Draft              | Writes a Proposal holding the draft                          | A person edits, approves, then executes                                                                  | Any task, clinical data included                                       |
| 2 Approve-to-execute | Writes a Proposal holding the exact commands                 | A person approves with an attestation, then sends the commands as themself, naming the proposal (K-AI-2) | Commands the approver could run                                        |
| 3 Standing grant     | Executes inside a narrow, time-boxed grant a person approved | The agent, under that grant, each action reviewable                                                      | Operational, non-clinical-truth commands only, after a security review |

Level 3 never covers observations, eligibility, causality, seriousness, coding approval, signatures, unblinding or any
command that changes what the study claims about a participant (K-AI-7). The owner chose on 2026-10-20 to allow level 3
for operational commands; standing grants change the authorizer and land with a security review (#4783). Until they
do, the existing `actor.agent_verb` rule keeps every agent at level 2 or below.

### 4.2 The Intelligence module

`apps/ai_gateway` becomes `apps/intelligence` (`Cadence.Modules.Intelligence`), an in-process module of the composition host (#5374). It
owns, in its own schema:

- **The model port:** `Microsoft.Extensions.AI` `IChatClient`. The first provider is Anthropic, defaulting to the
  latest Claude models; a deterministic fake provider serves every test, so CI never calls a model. A tenant can be
  switched to local models only.
- **Task configurations** (§4.3) and their evaluation runs.
- **The interaction ledger:** every run's inputs, tool calls, outputs, citations, token use and cost, the configuration
  version and the requesting person, retained as the agent's audit trail (21 CFR 11.10(e)). A hash proves integrity;
  identical regeneration is not promised.
- **Budgets and a kill switch** per tenant and per agent. A budget stops AI spend; it never blocks clinical work.

The module calls no kernel internals: it reads through kernel read ports and writes only by sending kernel commands
(`Propose`) through the module SDK. The person who approves a proposal sends the commands that execute it (ADR-2632).

### 4.3 Task configurations

The qualified unit is a versioned **task configuration**, not a model ID (#5385, detailed in ADR-2631). It holds the
model and version, prompt, tools, retrieval sources, output schema, evaluation set, acceptance criteria, autonomy
level, approver role and the FDA credibility-assessment fields: question of interest, context of use, model influence,
decision consequence and the resulting risk class. Its content hash changes when any of these change, and a changed
configuration must be qualified again before the router uses it. Qualification is an evaluation run against the
evaluation set, judged against the acceptance criteria and attested by a person. Every agent Proposal names its
configuration version (K-AI-5).

### 4.4 Tools and grounding

- Tools are typed functions over kernel reads (nodes, search, lineage and Trace, Explain, dry-run CanI,
  evaluate-only simulation) and module read ports. An agent acts for one person and reads with that person's grants,
  narrowed by its configuration's tool allow-list (K-AI-3, K-AI-6). No tool executes a command.
- Tool results enter the model context marked as data, never instructions.
- **Grounded or silent (K-AI-4).** Every claim and every value an agent proposes cites the kernel nodes it came from,
  and the runtime's citation verifier drops any claim or value that does not trace to a node the run actually read.
  An agent never produces a measurement, a value or a reference it did not read.
- PHI: IdentityVault data never reaches a model; free text is scrubbed before an outbound call as a protective
  control, not as proof of de-identification.

### 4.5 Triggers

An agent starts when a person asks (the Canvas inspector's Assist panel, the command palette, a form being filled)
or when a kernel event happens (new data, a serious adverse event, a protocol amendment, an indicator breach). Event
triggers run from the in-process event bus and outbox, debounced per study, each under a budget.

### 4.6 The experience

There is no separate AI product. Agents sit in the Canvas's normal panes: Assist in the inspector, natural language in
the palette, Proposals in Needs Attention (PX-10) and a review screen showing the diff, the citations and the
signature. AI output always carries the AI colour and label and links to what it reasoned over (UX-8). For high-risk
configurations the reviewer opens each cited source before signing. The study works the same with AI switched off.

### 4.7 Research types

Agents are the same for every kind of research; research-type packs add rules, terminology and documents: drug and
biologic (SUSAR, DSUR, ICH E2B(R3) clocks, WHODrug), medical device (ISO 14155, device deficiencies, ADE, SADE and
USADE, EU MDR Article 80, FDA IDE), diagnostic (EU IVDR, specimen custody), cell and gene therapy (long-term
follow-up, chain of identity) and observational research (no intervention, EHR and registry sources). The catalogue
is in [docs/ai/AI_AGENTS.md](../ai/AI_AGENTS.md).

### 4.8 The agent factory

Agents run as a factory: a measured, versioned production line for agent work. Its layers:

| Layer                 | In Cadence                                                                                                                                                                                               |
| --------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Definitions as code   | Each agent and task configuration is a versioned file in the repository, reviewed by pull request and tested in CI on the fake provider, so change control and validation evidence share one source      |
| Context               | Kernel reads through the requester's grants, lineage, search, Explain and documents; outside sources (FHIR, registries) through MCP and APIs; study memory is a projection, never a second copy of truth |
| Compute and inference | Sandboxed agent workers with no network beyond the model port and kernel read ports; pluggable providers; tenant data stays in the deployment, with a local-only switch                                  |
| Orchestration         | Routing to the qualified configuration and model mix per task; event triggers, queues, retries, budgets, step limits, kill switch                                                                        |
| Access                | The Canvas, the subject portal, an MCP server for outside agents, notifications to the team's tools; every door ends in the same Proposal and signature                                                  |

Measurement runs across the layers: evaluation sets built from de-identified, reviewed clinical cases plus adversarial
cases; deterministic checks first and an LLM judge only with a written rubric calibrated against human labels;
production signals per configuration version (acceptance, edit distance, rejection reasons, citation failures, cost,
latency). Improvements to prompts, tools or the model mix arrive as pull requests carrying their evaluation results
and are qualified like any change; **nothing modifies itself in production.**

### 4.9 Kernel rules (K037)

- **K-AI-4** An agent Proposal's claims and values cite kernel nodes its run read; uncited content never reaches a
  Proposal.
- **K-AI-5** An agent Proposal records its task configuration version alongside `ModelId` (extends K-PROV-3).
- **K-AI-6** An agent acts for exactly one requesting person, and its reads are bounded by that person's grants.
- **K-AI-7** A standing grant to an agent may name only commands the command catalogue marks operational; no
  standing grant covers a command that writes clinical truth, an attestation or an unblinding.
- **K-AI-8** No kernel behaviour and no module workflow depends on an agent being available.

### 4.10 Regulatory map

| Requirement                                                        | Control                                                                                                                        | Where                                        |
| ------------------------------------------------------------------ | ------------------------------------------------------------------------------------------------------------------------------ | -------------------------------------------- |
| 21 CFR 11.10(a) validation                                         | Task configuration qualification with evaluation evidence; IQ/OQ/PQ per configuration version                                  | Intelligence module, ADR-2631                |
| 21 CFR 11.10(d), (g) access and authority                          | Kernel authorizer; agents refused Execute and Approve; reads as the requester                                                  | Kernel (K-AI-1, K-AI-6)                      |
| 21 CFR 11.10(e) audit trail                                        | Kernel audit and events for Proposals, approvals and executed commands; interaction ledger for each run                        | Kernel, Intelligence module                  |
| 21 CFR 11.10(f) operational checks                                 | Propose, approve (not by the proposer), execute; expiry                                                                        | Kernel Proposal lifecycle                    |
| 21 CFR 11.50, 11.70 signatures                                     | Approval is an attestation bound to the content hash                                                                           | Kernel (ADR-2489)                            |
| EU GMP Annex 11 and its 2025 draft                                 | Risk class per configuration, change control by requalification, the provider as a supplier                                    | Intelligence module                          |
| EU GMP Annex 22 (draft)                                            | Intended use, test data, metrics, change control, human oversight; generative output kept to levels 0 and 1 for critical tasks | Task configuration, autonomy ladder          |
| ICH E6(R3)                                                         | Risk-proportionate oversight, data governance, investigator decides for their participants                                     | Ladder, approver roles, attributable actions |
| GAMP 5 (2nd ed.), ISPE GAMP AI guide                               | Configuration lifecycle with evaluation evidence and periodic review                                                           | ADR-2631                                     |
| FDA draft guidance on AI for regulatory decision-making (Jan 2025) | Question of interest, context of use, model risk, credibility plan and results per configuration                               | Task configuration                           |
| FDA and EMA Guiding Principles of Good AI Practice (Jan 2026)      | Human-centric (approval), risk-based (ladder), context of use, data governance, life cycle (monitoring and requalification)    | This ADR and ADR-2631                        |
| EMA reflection paper on AI (2024)                                  | High-risk configurations flagged for the sponsor's regulatory review                                                           | Task configuration risk class                |
| ISO 14155, EU MDR and IVDR, FDA device guidance                    | Device and diagnostic packs; Cadence's agents support the investigation and are never the investigational device               | Research-type packs                          |
| EU AI Act                                                          | Labelling, logging, human oversight and provider documentation built in; high-risk status assessed per use                     | Canvas, ledger, ADR-2631                     |
| HIPAA, GDPR                                                        | No IdentityVault data to models; scrubbing; local-only tenants; no training on customer data                                   | Model port                                   |

Cadence supplies these controls; a sponsor still owns the validation and context-of-use argument for its own use.
The FDA guidance and Annex 22 were drafts when this ADR was written; the regulatory map is reviewed when they are
final.

## 5. Consequences & Trade-offs

- **Positive:** one path for every agent; regulated data changes only through a person's signature; each control is
  in one place and becomes a test.
- **Negative:** every agent needs a Proposal shape and a qualified configuration before it can be used, and approval
  adds a step to work an agent could do alone.
- **Mitigation Strategy:** level 2 lets one signature execute a whole batch of commands; level 3 standing grants
  remove the step for operational work; the review screen keeps approval to one screen.

## 6. Implementation & Verification

- **Slices** (epic #5545, one PR each):
  - this ADR, the spec, `docs/ai/AI_AGENTS.md` and PRD-SYS-051 (#5546);
  - the Intelligence module, model port, fake provider, ledger, budgets and kill switch (#5374);
  - task configurations and qualification (#5385, ADR-2631);
  - the K-AI-2 execution link, requester-scoped agent grants and standing grants (#4783, ADR-2632);
  - the tool layer, citation verifier and MCP server (#5547, ADR-2633);
  - evaluation and factory measurement (#5548, ADR-2634);
  - the Canvas experience (#5549, ADR-2635);
  - inference providers, sandboxing and sovereignty (#5550, ADR-2636);
  - then the agents by lifecycle stage, event triggers and research-type packs.
- **Verification Plan:**
  - each K-AI rule above is a test against PostgreSQL;
  - an agent's attempt to execute or approve is refused;
  - a fabricated value is dropped by the citation verifier;
  - an unqualified or changed configuration is refused;
  - every test runs on the deterministic fake provider; CI never calls a model.

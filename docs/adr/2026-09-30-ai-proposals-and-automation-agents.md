# ADR-2489: AI Proposals and Automation Agents

- **Status:** Accepted
- **Date:** 2026-09-30
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Spec §11 treats AI as an actor that reads and proposes. It never executes on its own. An `AutomationAgent` actor kind
exists, and the authority evaluator already denies agents every `Execute` and `Approve` command (`actor.agent_verb`,
ADR-2455). What is missing is the thing an agent produces: nothing records a **Proposal**. As a result:

- `apps/execution` models an AI-drafted query as a query status (`QueryStatus.DraftAi`, inventory item 10, gap G11);
- the Canvas has nowhere to put "AI analysis, proposed plan" (PX-17) or "Proposals awaiting approval" (PX-10);
- a saved simulation result has no object to hang from (ADR-2480 §4.5, K-SIM-5).

Issue #4783 asks for the Proposal, the agent actor, K-AI-1..3 and K-PROV-3. This ADR decides the Proposal object, its
lifecycle and its approval, and it places the execution link (K-AI-2) and the query migration in later slices.

## 2. Decision Drivers & Constraints

- **K-AI-1.** No code path lets an `AutomationAgent` invoke `Execute` or `Approve` without a human attestation or a
  standing grant reference.
- **K-AI-2.** Every executed proposal links proposal → approval → activity → resulting events.
- **K-AI-3.** Agents read through the same authorization as people.
- **K-PROV-3.** AI-assisted actions record the model identifier, the prompt or plan artifact and the approving actor.
- **K-SIM-5.** Approving a proposal does not apply it.
- **UX-8.** AI output is labelled as AI and never shows an Execute control that bypasses an accountable person.
- **21 CFR 11.70.** An approval is a signature bound to the exact content approved.
- **Requirements:** PRD-SYS-051 (AI output needs human verification with a Part 11 signature and dual attribution).

## 3. Options Considered

### Option 1: An AI status on each record type (status quo)

- ✅ No new object.
- ❌ Every application reinvents AI drafts. Nothing links an AI suggestion to the approval and the change it led to,
  and the draft sits inside regulated data before a person approves it.

### Option 2: A kernel Proposal with an attested approval (selected)

- ✅ One object for every AI or human suggestion, with no effect on kernel state until someone executes it.
- ✅ Approval is an ordinary attestation, so it reuses Part 11 signing and the verifier.
- ❌ Executing an approved proposal is still a separate step, and K-AI-2 linking needs command metadata (later slice).

### Option 3: Let agents execute with a co-signature on the same command

- ❌ It makes the agent the actor of a regulated change, which K-AI-1 and UX-8 rule out.

## 4. Decision Outcome

Option 2. It is kernel decision **K033**.

### 4.1 The Proposal

A `Proposal` is a kernel object in a study (NodeRef kind `proposal`, a lineage node). It holds:

| Field                   | Meaning                                                                                                            |
| ----------------------- | ------------------------------------------------------------------------------------------------------------------ |
| `Intent`                | What the proposal would achieve, in one to 2,000 characters                                                        |
| `Plan`                  | The artifact version that holds the plan and its reasoning (an Evidence artifact of the same study or tenant-wide) |
| `Affected`              | The objects it would change or depends on, 0 to 200 `NodeRef`s                                                     |
| `SimulatedImpact`       | Optional: the result hash of the simulation run over it (ADR-2480)                                                 |
| `RequestedCapabilities` | 1 to 20 capability tokens a person would use to execute it, each a known `Execute` or `Approve` command            |
| `ExpiresAt`             | After this it can no longer be approved; at most 90 days after it is proposed                                      |
| `ProposedBy`            | The actor who proposed it, a person or an automation agent                                                         |
| `ModelId`               | The model that produced it, required when `ProposedBy` is an agent and refused otherwise (K-PROV-3)                |

- Its content hash covers every field above, so an approval signs exactly what was proposed.
- A proposal never changes once proposed. Only its status changes.
- **It has no effect on kernel state.** Nothing reads a proposal to decide anything else, and approving one does not
  run its plan (K-SIM-5).

### 4.2 Lifecycle

`Open` → `Approved` | `Rejected` | `Withdrawn`. An open proposal past `ExpiresAt` reads as `Expired`. Expiry is
derived on read, so it needs no scheduled job.

| Command            | Capability                   | Rule                                                                                                                                                      |
| ------------------ | ---------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Propose`          | `proposal.create`, Propose   | The plan artifact version exists and belongs to the study or the tenant; the capabilities are known Execute or Approve commands; an agent names its model |
| `ApproveProposal`  | `proposal.approve`, Approve  | The proposal is open and not expired; an `Approved` attestation by the acting person signs its content hash; the approver did not propose it              |
| `RejectProposal`   | `proposal.reject`, Approve   | The proposal is open; a reason is required (the command's reason for change)                                                                              |
| `WithdrawProposal` | `proposal.withdraw`, Propose | The proposal is open, and only its proposer may withdraw it                                                                                               |

- Each raises an event: `ProposalCreated`, `ProposalApproved`, `ProposalRejected` or `ProposalWithdrawn`, in the
  Intelligence family.
- A proposal is placed on its study's scope path, so read authorization and node visibility treat it like any other
  study object (K-AI-3).

### 4.3 Agents (K-AI-1, K-AI-3)

- `Actor.AutomationAgent(name)` builds an `agent:` actor.
- The evaluator's existing rule is K-AI-1 for this slice: an agent is denied every `Execute` and `Approve` command,
  including `proposal.approve` and `proposal.reject`. The authorizer does not change.
  - The standing-grant exception (spec §11 (b)) is **not** built. Until it is, an agent can never execute, which is
    stricter than K-AI-1 requires.
- An agent can propose only with a grant for `proposal.create` in the study, issued by a person. An agent reads
  through the same evaluator and `INodeVisibility` as a person.
- A proposal names only objects its proposer may see: the plan and every affected object pass through
  `INodeVisibility` for the proposer, and a hidden one refuses the proposal (`K.Proposal.NotVisible`) without saying
  which. A proposal therefore never links into scope its proposer cannot read.
- Attestations are signed only by people (existing rule), so an agent cannot produce the approval either.

### 4.4 Provenance (K-PROV-3)

- The proposal records the proposing actor, the model and the plan artifact.
- The approval records the approving person and the attestation.
- The command pipeline records every command's actor, so the agent is in provenance as the actor of `Propose`.
- The lineage edges are `Proposal DerivedFrom Plan` and `Proposal DefinedBy` each affected object version. Lineage
  joins versions, so an affected reference without a version is kept on the proposal only.

### 4.5 Later slices

- **K-AI-2 execution link.** A command gains optional metadata naming the approved proposal it executes. The pipeline
  then:
  - checks that the proposal is approved, unexpired and requested that command's capability;
  - records the link in provenance and lineage;
  - raises `ProposalExecuted`.
- **Standing grants** for narrow, time-boxed agent execution (spec §11 (b)). These change the authorizer and need a
  security review.
- **Agent grants scoped to the requesting human.**
- **`QueryStatus.DraftAi` migration.** It is unreachable today (`ClinicalQuery.Raise` allows only Candidate or Open).
  AI-drafted queries become Proposals whose plan is the draft query.
- **Spec §14 scenario 4** end to end, once the execution link exists.
- **A Proposal work-queue source** for Needs Attention (PX-10).

## 5. Consequences & Trade-offs

- **Positive:** one audited object for every AI suggestion, approved with a Part 11 signature bound to its content,
  with no path from an agent to regulated data.
- **Negative:** an approved proposal still needs a person to execute each change until the execution link lands.
- **Mitigation Strategy:** the execution link is the next slice, and the Canvas shows the approved proposal next to the
  commands it requested.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/kernel`:
  - Abstractions:
    - `ProposalId`, `NodeKinds.Proposal`;
    - `Actor.AutomationAgent`;
    - the commands, errors and state (`Commands/ProposalCommands.cs`);
    - the events (`Events/ProposalEvents.cs`) and their Intelligence activity family.
  - Domain: the `Proposal` entity.
  - Application:
    - the handlers;
    - the attestation target check for `proposal`.
  - Persistence:
    - the `proposals` table (content columns immutable) and its migration;
    - the scope path for visibility and navigation.
  - `apps/web`: the `proposal` NodeRef mirror.
- **Verification Plan:**
  - Unit tests:
    - validation;
    - the lifecycle and expiry;
    - the content hash;
    - an agent must name a model and a person must not.
  - Postgres tests:
    - an agent proposes and a person approves with an attestation, and the proposal changes nothing else (PRD-SYS-051);
    - an agent cannot approve or reject (K-AI-1);
    - a proposer cannot approve their own proposal;
    - a tampered or expired proposal cannot be approved;
    - only the proposer withdraws;
    - a plan or affected object hidden from the proposer is refused;
    - lineage and visibility place the proposal in its study.

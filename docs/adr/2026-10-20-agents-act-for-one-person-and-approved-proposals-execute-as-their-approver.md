# ADR-2632: Agents Act for One Person, and Approved Proposals Execute as Their Approver

- **Status:** Accepted
- **Date:** 2026-10-20
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Issue:** #4783 (epic #5545)
- **Requirements:** PRD-SYS-051, 21 CFR 11.10(d), (e), (g), 11.50, 11.70
- **Amends:** kernel specification §11 (K-AI-2, K-AI-6, K-AI-7); details K037 ([ADR-2630](2026-10-20-ai-agents-act-through-proposals-in-one-governed-runtime.md)); builds on ADR-2489 (K033)
  and ADR-2483

---

## 1. Context & Problem Statement

ADR-2630 makes the kernel `Proposal` the only way an AI agent's work reaches clinical data: an agent proposes, a person
approves with a Part 11 attestation over the proposal's content hash, and the change executes as that person. ADR-2489
built the first half. Three kernel pieces were missing:

- **K-AI-2:** nothing connected an approved proposal to the command that carried it out. A person could approve a
  proposal and then run any command, and the audit trail could not show which change came from which proposal, agent or
  model.
- **K-AI-6:** an agent actor (`agent:<name>`) was authorized by its own grants alone. Nothing tied a run to the person
  who asked for it, so an agent could read and propose across every scope its grants covered, wider than the person it
  worked for.
- **K-AI-7:** the owner chose on 2026-10-20 to allow standing grants for operational commands, and confirmed it in
  writing after the regulatory review in §4.4. The kernel had no notion of an operational command and no way to grant
  an agent an `Execute` capability safely.

## 2. Decision Drivers & Constraints

- **21 CFR 11.10(e), ALCOA+:** every record says who acted, for whom, and why; an executed proposal is traceable from
  the proposal to its approval, the command and the events it raised.
- **21 CFR 11.10(d), (g):** an agent may never exceed the authority of the person it acts for.
- **21 CFR 11.50, 11.70:** an agent never signs; approval stays an attestation by a person, bound to the content hash.
- **ADR-2483:** one authorization evaluator decides both real commands and dry runs.
- **Pre-deployment:** breaking internal changes are fine.

## 3. Options Considered

### Option 1: An "execute proposal" command that runs the proposal's plan

- ✅ One call executes a whole plan.
- ❌ The kernel would have to interpret plan artifacts, which hold free-form content, and would run commands the person
  never sent. Each command's own authorization, validation and audit would happen inside another command.

### Option 2: Each command names the proposal it executes in its metadata (selected)

- ✅ Every command still goes through the normal pipeline as the person who sends it: their authorization, validators,
  handler and audit. The proposal adds a check and a link and never widens authority.
- ✅ One approved proposal can cover a batch of commands until it expires (autonomy level 2).
- ❌ The client sends each command itself. This is the point: the person's client shows what runs.

For requester scoping, the alternatives were a separate "acting for" service (it could be forgotten on one read path)
or carrying the requester on the agent actor itself. The actor carries it, so every path that sees the actor (the
pipeline, the evaluator, visibility, dry runs, provenance) sees the requester.

## 4. Decision Outcome

### 4.1 The execution link (K-AI-2)

`CommandMetadata` gains `Proposal`, the approved proposal a command executes. After authorizing the command as the
acting person and checking idempotency, the pipeline refuses it unless:

| Check                                                                | Refusal                             |
| -------------------------------------------------------------------- | ----------------------------------- |
| The proposal exists in the tenant                                    | `K.Proposal.ExecutionUnknown`       |
| It is approved                                                       | `K.Proposal.NotApproved`            |
| It has not expired                                                   | `K.Proposal.ExecutionExpired`       |
| The acting person approved it, acting as themselves                  | `K.Proposal.NotApprover`            |
| It requested the command's capability                                | `K.Proposal.CapabilityNotRequested` |
| The command's object belongs to the proposal's study, or to no study | `K.Proposal.OutsideStudy`           |

On success the same transaction records the link:

- the provenance activity's `proposal_id`;
- a `ProposalExecuted` event (Intelligence family) in that activity, naming the command, capability, object, executor,
  approval attestation, proposing agent, model and requesting person;
- an `ExecutedUnder` lineage edge from the approved proposal version to the object version the command wrote.

The chain reads proposal → approval attestation (on the proposal) → activity → the events that activity raised. The
request hash covers the proposal, so the same idempotency key under another proposal is an idempotency conflict.

### 4.2 Agents act for one person (K-AI-6)

An `Actor` of kind `AutomationAgent` carries `RequestedBy`, the person it acts for (`Actor.ActingFor(person)`). Only an
agent may carry one, and it must be a person. Grants and proposals name the agent as an identity, without a requester.

- **Authorization:** an agent acting for no one is refused everything (`actor.requester`). Otherwise the evaluator
  checks the agent's own grants (without qualification requirements, which describe a person's training) and then
  evaluates the same capability and resource for the person. Both must permit, and the decision lists both sets of
  conditions. `Execute` and `Approve` stay refused to agents (`actor.agent_verb`) until §4.3.
- **Visibility:** an agent sees an object only when both it and the person would; acting for no one, it sees nothing.
- **Records:** a proposal by an agent stores `requested_by_id` in its content hash (the database requires it exactly
  for agents and fixes it with the other content columns), and the provenance activity stores `requested_by`. Only the
  same agent acting for the same person may withdraw the proposal.

### 4.3 Standing grants (K-AI-7)

A standing grant lets an agent run one operational command without a per-action approval (autonomy level 3). It is
delivered in a second pull request under this ADR, with a security review. Its rules:

- The command catalogue marks a command `Operational` on its `[KernelCapability]`. Only `Execute` commands that write
  no clinical truth, signature or unblinding may be marked; a test fixes the list.
- A standing grant names one agent, the one person it acts for, one operational capability and a study or study-site
  scope, with a valid period of at most 30 days. It carries no grant option, no role and no parent.
- A person issues it with an `Approved` attestation over the grant's content hash; an agent or a service account
  cannot.
- At use, the agent must be acting for the grant's person, the capability must still be operational, and the person
  must still hold the capability (§4.2). Revoking the grant stops it at once.
- `Approve` capabilities are never available to an agent.

### 4.4 Regulatory reading

The owner asked for the decision to be read against the regulations before standing grants are built:

| Source                                  | What it requires                                                 | How this ADR meets it                                                                       |
| --------------------------------------- | ---------------------------------------------------------------- | ------------------------------------------------------------------------------------------- |
| 21 CFR 11.10(e), 11.50, 11.70           | Attributable records; signatures by people, bound to the record  | Agent, requester, grant and approval recorded on every action; agents never sign or approve |
| FDA draft guidance on AI (January 2025) | Risk from model influence and decision consequence               | Level 3 has high model influence, so only low-consequence operational commands qualify      |
| EU GMP Annex 22 (draft)                 | No generative models in critical uses; human oversight otherwise | No clinical-truth command can be operational; every action is reviewable and revocable      |
| ICH E6(R3)                              | Sponsor and investigator stay responsible for trial decisions    | Eligibility, causality, seriousness, coding and signatures stay with people                 |
| EU AI Act                               | Effective human oversight and logging                            | Time-boxed, person-approved, revocable grants; every action logged with its grant           |

## 5. Consequences & Trade-offs

- **Positive:** every executed proposal is traceable end to end; an agent can never see or do more than the person it
  works for; standing grants have a fixed, testable boundary.
- **Negative:** an agent run must always name a person, including event-triggered runs, which name the person
  responsible for the study task; agent and person both need grants.
- **Mitigation Strategy:** the Intelligence module (#5374) builds the agent actor from the signed-in person, or from
  the person a trigger configuration names, so callers never assemble it by hand.

## 6. Implementation & Verification

- **Slices** (#4783):
  - the execution link and requester-scoped agents, with migration `AgentRequesterAndProposalExecution`;
  - standing grants and the operational catalogue, with a security review.
- **Verification Plan** (against PostgreSQL):
  - an approver executes a proposal's command and provenance, the `ProposalExecuted` event and lineage link them;
  - every unlinked execution in §4.1 is refused and writes nothing, and a proposal never widens authority;
  - an agent acting for no one, or for a person without the capability, is refused and sees nothing outside the
    person's scope;
  - (standing grants) an agent executes an operational command inside its grant and is refused outside it, after
    revocation, after expiry, for another person, and for every non-operational command.

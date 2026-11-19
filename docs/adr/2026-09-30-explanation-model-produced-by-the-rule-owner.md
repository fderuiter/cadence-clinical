# ADR-2482: Explanation Model Produced by the Rule Owner

- **Status:** Accepted
- **Date:** 2026-09-30
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

The Clinical Canvas Explain verb answers "why": why an action is denied or a control disabled (Canvas doc PX-7),
and why an object is in its state or a metric has its value (PX-8). Issue #4842 (decision D3) records the gaps:

- The kernel's authorization result is `Deny(reason)`. It names what is missing (K-AUTH-6) but not what was
  satisfied, so the UI cannot draw the checklist of met and unmet conditions users need to see where they stand.
- There is no general explanation shape. Applicability (§7.5), Findings (K-FIND-3) and Indicators (§10.4) will
  each need to say why, and without one model each would invent its own.
- Without a kernel model, the web app would compose explanations from the data it happens to have. Those
  explanations would drift from the real rules, which is the reconstruction the Canvas doc §8 forbids.

## 2. Decision Drivers & Constraints

- **K-TRUTH-1 in the UI.** An explanation is a statement about clinical truth, so it comes from the component that
  decided, not from a renderer.
- **K-AUTH-2, K-AUTH-6.** Denials are audited with the evidence that justified them and name what is missing.
- **Determinism.** Explaining the same decision over the same evidence gives the same answer, so an explanation
  shown to a user can be reproduced for an inspector.
- **Evidence is navigable.** Every piece of evidence must open in the inspector, so it is a NodeRef (ADR-2481).
- **K-EVT-1.** Anything added to the `DecisionDenied` payload is additive.
- **Requirements:** PRD-SYS-001 (audit trail): the conditions a denial rested on are recorded in the audit chain.

## 3. Options Considered

### Option 1: Keep a reason string and let the UI explain

- **Cons:**
  - ❌ The UI cannot know which conditions were met, and would guess from its own data.
  - ❌ Explanations drift from the rules the moment either changes.

### Option 2: Each producer returns its own explanation type

- **Cons:**
  - ❌ The inspector needs one renderer per producer, and cross-cutting features (copy as text, audit, the
    "what would change this" list) are rebuilt each time.

### Option 3: One kernel `Explanation` type, produced by the rule owner (selected)

- **Overview:** a single record in `Cadence.Kernel.Abstractions` that every producer returns. The UI has one
  renderer. Producers build it from the same evaluation that made the decision.
- **Pros:**
  - ✅ One shape, one renderer, and the explanation cannot disagree with the decision because both come from one
    evaluation.
- **Cons:**
  - ❌ The shape must be general enough for authorization, state, findings and metrics. The fields below cover all
    four; anything producer-specific goes in step claims and evidence.

## 4. Decision Outcome

- **Chosen Option:** Option 3.
- **Justification:** it puts the explanation where the rule is and gives the Canvas one thing to render.

### 4.1 Shape

```
Explanation{ subject: NodeRef?, claim, producer,
             steps[ { claim, evidence[NodeRef] } ],
             conditions[ { code, description, met, evidence[NodeRef] } ],   // unmet_conditions = conditions where !met
             next_actions[ { description, capability?, target: NodeRef? } ] }
```

- `subject` is the object explained, or null when the action has no object yet (creating one).
- `claim` is the conclusion in one sentence. `producer` names the component and, for rule-driven producers, the
  rule version (`kernel.authorization`, `quality.finding/<rule-id>@<version>`).
- `conditions` holds every condition evaluated, met and unmet, in evaluation order. `unmet_conditions` from D3 is
  derived from it rather than stored twice. `code` is stable for localisation and tests.
- `next_actions` says what would change the outcome, when the producer knows. For authorization, each unmet
  condition is one next action.
- Types: `Cadence.Kernel.Explanations.Explanation`, `ExplanationStep`, `EvaluatedCondition`, `NextAction`. List
  equality is by content, so equal evidence gives equal explanations.

### 4.2 Rules

- **K-EXP-1** The component that owns a rule produces its explanation from the same evaluation that made the
  decision. Applications and the UI render explanations and never compose them.
- **K-EXP-2** Evidence is a NodeRef, pinned to the version the producer read where it has one. Evidence the reader
  may not see is masked under the D1 contract (#4840), never dropped, so a checklist never silently shortens.
- **K-EXP-3** An explanation is a pure function of the evaluated evidence and the producer's rule version. The
  same inputs give an equal explanation; changed evidence changes it.

### 4.3 Authorization

- `AuthorizationDecision` gains `Conditions`: every condition the evaluator checked, met or unmet, each with its
  evidence (grant, delegation, qualification, lock). `Permit` and `Deny` take them as optional arguments, so
  existing callers are unchanged. `Explain(action, subject)` turns a decision into an `Explanation`.
- A denied command returns `K.auth.denied` (403) with the explanation on `KernelError.Explanation`.
- The `DecisionDenied` event payload and the denial's audit entry gain `conditions[{code, description, met,
evidence}]` alongside the existing `reason` (additive, K-EVT-1; schema version unchanged). An auditor sees what
  the denial rested on, not only its message (K-AUTH-2).
- The evaluator that fills these conditions from Grants, Delegations and Qualifications is #4781. Until then the
  kernel's authorizer returns the conditions its policy supplies.

### 4.4 Other producers

Applicability (§7.5, #4777), Findings (K-FIND-3, #4812) and Indicators (§10.4, #4811) return the same type when
they are built. Their producers and the test that each explanation follows its evidence are tracked in
follow-up #4896. The CanI dry run (D8, #4847) returns an authorization explanation without executing.

## 5. Consequences & Trade-offs

- **Positive Impact:** one explanation contract for the whole platform; denials become actionable checklists;
  audit records the conditions a denial rested on.
- **Negative Impact / Technical Debt:** `conditions` in the audit payload grows each denial entry. Conditions are
  few (typically under ten) and carry references, not documents.
- **Mitigation Strategy:** producers that do not exist yet adopt the type as they land; the ADR and spec rule
  K-EXP-1 make that a review requirement.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/kernel`:
  - `Cadence.Kernel.Abstractions/Explanations` (the types) and `KernelError.Explanation`;
  - `AuthorizationDecision.Conditions` and `Explain` in `Cadence.Kernel.Application/Commands/Ports.cs`;
  - `KernelCommandPipeline` returns the explanation; `KernelCommandTransaction.RecordDenialAsync` records the
    conditions.
- **Verification Plan:**
  - `ExplanationTests`: met and unmet conditions with evidence, next actions from unmet conditions, permits, and an
    explanation that changes when a training record is renewed and stays equal when the evidence is the same.
  - `KernelExplanationPostgresTests`: a denied command returns the explanation, and its audit entry and
    `DecisionDenied` event carry the reason and every condition with its evidence.

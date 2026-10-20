# ADR-2634: AI Task Configurations Are Measured by Versioned Evaluations and Production Signals

- **Status:** Accepted
- **Date:** 2026-10-20
- **Authors:** @fderuiter, Claude Code
- **Deciders:** @fderuiter
- **Requirements:** PRD-SYS-051 (agent Proposals name the qualified task configuration version that wrote them)
- **Related:** [ADR-2630](2026-10-20-ai-agents-act-through-proposals-in-one-governed-runtime.md) (agents act through Proposals in one governed runtime, #5546), ADR-2631 (task
  configurations, #5385), [ADR-2489](2026-09-30-ai-proposals-and-automation-agents.md),
  [ADR-2513](2026-09-30-form-approval-hashes-the-rfc-8785-canonical-payload.md),
  [ADR-2532](2026-10-16-feature-branches-never-commit-the-gxp-snapshot.md), issue #5548, epic #5545

---

## 1. Context & Problem Statement

ADR-2630 makes the task configuration, not the model, the unit that is qualified: model and version, prompt, tools,
retrieval sources, output schema, evaluation set and acceptance criteria. Qualification (#5385) needs evidence that a
configuration version does what its context of use claims, and the evidence has to stay true after release, when real
studies, real documents and real reviewers meet the agent.

Nothing in Cadence defines how an agent is measured. The scaffold `apps/ai_gateway` has no model calls and no tests,
and the imaging "AI" segmentation that merged on 2026-10-19 fabricated measurements (#5426) without any check that
would have caught it. Regulators ask for the same things a software factory measures: 21 CFR 11.10(a) validation for
accuracy and consistent intended performance, Annex 11 §4 and draft Annex 22 for test data that represents the
intended use and for ongoing performance monitoring, GAMP 5 (second edition) for risk-based testing of configured
components, and the FDA (January 2025) and EMA (September 2024) AI guidance for a context-of-use credibility
assessment with life-cycle monitoring.

The user asked for the factory's measurement and self-improvement loop (benchmarks from real workflows, LLM-as-judge
scoring, production signals, factory engineering) to be part of the design. The open question is how to do that so
the numbers are evidence a qualification can attest, rather than dashboards.

## 2. Decision Drivers & Constraints

- **Evidence, not impressions:** an evaluation result must be reproducible from versioned inputs and hashed, so an
  attestation can bind to it (spec §9.5, 21 CFR 11.70).
- **Deterministic first:** what can be checked by code (schema, citations, grounding of every value, refusal of
  injected instructions) is checked by code. A model's opinion is the last and least trusted check.
- **No fabricated values:** every value in an agent's output traces to a node the agent read in that run (#5426).
- **No PHI in the repository:** evaluation cases are de-identified and reviewed before they are committed; the
  `deid` gate scans them like any other file.
- **CI never calls a model:** tests use the deterministic fake provider; real providers run only on demand.
- **Nothing changes itself in production:** improvement goes through a pull request and requalification.
- **Kernel scope (K-SCOPE-1):** evaluation runs and production signals describe AI configurations, which exist only in
  the Intelligence application. They are module state, not kernel concepts; the kernel sees them only as the target of
  an attestation and through Proposal outcomes.

## 3. Options Considered

### Option 1: Versioned evaluation sets, deterministic checks, a calibrated judge and per-version production signals (Selected)

- **Pros:**
  - ✅ Every run is reproducible from hashed inputs, so qualification attests a record, not a number on a screen.
  - ✅ Most failure modes (fabricated values, missing citations, followed injections) are caught without a model.
  - ✅ Production behaviour is tied to the configuration version that produced it, so drift has an owner and a remedy.
- **Cons:**
  - ❌ Building reviewed, de-identified cases takes clinical reviewers' time for every agent.
  - ❌ A judge needs its own human-labelled calibration sample before its scores count.

### Option 2: An LLM judge scores everything

- **Pros:**
  - ✅ Fast to start; no deterministic checks to write.
- **Cons:**
  - ❌ The judge can share the agent's blind spots, and its scores drift with its own model version.
  - ❌ An uncalibrated score is not evidence of accuracy under 11.10(a).

### Option 3: Measure only in production (acceptance rate after release)

- **Pros:**
  - ✅ Reflects real use.
- **Cons:**
  - ❌ Nothing to qualify against before release; the first users become the test set.
  - ❌ Adversarial behaviour (prompt injection, conflicting sources) is rare in production and would go unmeasured.

## 4. Decision Outcome

- **Chosen Option:** Option 1.

### 4.1 Evaluation sets are versioned files

- An evaluation set is a JSON file in the repository at `apps/intelligence/evaluation/sets/<set-id>/v<version>.json`,
  reviewed by pull request. A published version is never edited; a change is a new version.
- Its identity is `(setId, version, contentHash)`, where the hash is the kernel content hash (`sha256:` over the
  canonical JSON, the same canonical form as ADR-2513). A task configuration names the exact set version and hash it
  is qualified against.
- Each case is one of two kinds:
  - **Reviewed:** built from a real, de-identified clinical case whose correct answer a qualified person confirmed (a
    query that was right, a narrative a safety physician corrected). The case records its de-identification method
    (Safe Harbor, expert determination or synthetic), who reviewed it and when.
  - **Adversarial:** authored to attack the agent, in a named category: prompt injection, missing data, conflicting
    sources, out-of-scope request or blinding. A canary string in an injected instruction lets a check prove the agent
    did not follow it.
- A case holds the task input, the context nodes the agent may read (kernel node references with their content), and
  the expectation: whether the agent should propose, abstain or refuse, the expected output fields, the citations it
  must make, and text it must never produce.
- The loader refuses a set with duplicate case ids, a reviewed case without de-identification provenance, an
  adversarial case without a category, or a hash that does not match its content.

### 4.2 Checks run deterministic first

The harness runs these checks on every case, in order, and the first failure fails the case:

1. **Outcome:** the agent proposed, abstained or refused as expected.
2. **Output schema:** every required field is present and no unknown field appears.
3. **Fields:** each expected field matches (exact, case-insensitive or numeric with a tolerance).
4. **Citations:** every citation names a context node of the case, and every required citation is present.
5. **Grounding:** every number in the output appears in a node the output cites. This is the check that would have
   caught #5426.
6. **Forbidden content:** no forbidden string (an injection canary, a blinded arm, a participant's name) appears.

### 4.3 An LLM judge counts only with a calibrated rubric

- A rubric is a versioned file at `apps/intelligence/evaluation/rubrics/<rubric-id>/v<version>.json`: criteria with a
  written description per score level, a pass score per criterion, and the agreement the judge must reach with people.
- The judge is itself a task configuration with a version. Its scores count only when a calibration record exists for
  that exact `(rubric hash, judge configuration version)`: the judge scored a human-labelled sample of at least the
  rubric's minimum size, and Cohen's kappa on pass or fail per criterion reached the rubric's threshold (0.6 unless the
  rubric sets more). The calibration record is hashed and stored with the evaluation run.
- A judge scores only cases that passed every deterministic check, and only the criteria a check cannot decide
  (clarity, completeness of a narrative). A judge never overrides a failed deterministic check.

### 4.4 Evaluation runs are hashed records that qualification attests

- The harness runs one configuration version against one set version on one provider and writes an evaluation run
  record: the configuration, set, rubric and calibration identities, the provider and model id, the start and end time
  from `IClock`, each case's check results, the metrics and the acceptance verdict, all under a content hash.
- Acceptance criteria come from the task configuration: a minimum pass rate on reviewed cases and on the whole set, a
  maximum mean latency and cost per case. Two floors apply to every configuration whatever it declares: every
  adversarial case passes, and no case fails grounding or citations.
- In CI the harness runs on the deterministic fake provider and proves the machinery. A real provider runs on demand
  (`CADENCE_AI_EVAL_PROVIDER`), never in CI, and its run record is the evidence qualification (#5385) attests with a
  kernel attestation over the record's hash.
- The run record is stored in the Intelligence schema by `RecordEvaluationRun` (#5574) before anyone can qualify on
  it. The server admits a record only when it evaluated a shipped configuration version on its current content against
  exactly its pinned set, acceptance criteria and provider, no case is marked passed with a failed check, and its
  metrics and verdict are what the harness computes from its case results. It stores the canonical JSON and its content
  hash, and qualification names the stored run by id, never a caller-stated pass.

### 4.5 Production signals per configuration version

- The Intelligence module records one outcome per Proposal an agent wrote: the configuration version, whether a person
  approved, rejected or let it expire, the normalized edit distance between what the agent drafted and what was
  approved, a coded rejection reason (incorrect, unsupported by source, incomplete, wrong scope, unsafe, other),
  citations the verifier dropped, tokens, cost and latency.
- The monitor computes, per configuration version over a rolling window: acceptance rate, mean edit distance, the
  rejection-reason mix, citation failure rate, mean cost and 95th percentile latency.
- Thresholds come from the task configuration's monitoring section. A rate breaches only when it is confidently
  outside its threshold (the 95% Wilson interval lies wholly past it) and the window holds at least the minimum
  sample, so a few unlucky proposals do not trip it.
- A breach raises a Quality signal for the Quality module to triage. Safety breaches (citation failures, an unsafe
  rejection, acceptance confidently below the floor) also suspend the configuration: routing stops using it, and only
  a new qualification lifts the suspension. Cost and latency breaches only warn, because a budget never blocks
  clinical work.

### 4.6 Improvements are pull requests

- The factory may suggest a better prompt, tool list or model mix, but only as a pull request that changes the
  configuration file and carries an evaluation run of the candidate against the same set version as the current
  qualified run, with a comparison of the two. A regression on any adversarial case or on grounding blocks it.
- No runtime path writes a prompt, a configuration or an evaluation set. The configuration registry loads only
  versioned, qualified configurations.

### 4.7 Where it lives

- Code: `apps/intelligence/src/Evaluation/` in `Cadence.Modules.Intelligence`, registered by
  `AddIntelligenceEvaluation()`. Tests: `tests/Cadence.UnitTests/Intelligence/Evaluation/`.
- Guide: [docs/ai/EVALUATION.md](../ai/EVALUATION.md).

## 5. Consequences & Trade-offs

- **Positive Impact:**
  - Qualification has a reproducible, hashed record to attest for every configuration version.
  - Fabricated values and followed injections fail a deterministic check before any person sees them.
  - Drift after release has a measured trigger and a safe default (suspend, requalify).
- **Negative Impact / Technical Debt:**
  - Every agent needs reviewed, de-identified cases before it can be qualified, which costs reviewer time.
  - A judge needs a human-labelled sample per rubric and judge version; changing the judge model means recalibrating.
  - Until the model port lands (#5374) the harness runs only on the fake provider and scripted targets.
- **Mitigation Strategy:**
  - Start each agent with a small reviewed set plus the shared adversarial categories, and grow it from real
    rejections (a rejected Proposal is a candidate case once de-identified and reviewed).
  - Prefer deterministic checks; use a judge only for criteria that need one.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `apps/intelligence` (`Cadence.Modules.Intelligence.Evaluation`), the
  configuration registry (ADR-2631) and the Quality module through the ports in §4.5.
- **Verification Plan:** tests in `tests/Cadence.UnitTests/Intelligence/Evaluation/` prove the set loader's
  refusals, each deterministic check (including the grounding check against fabricated values), kappa and the
  calibration rule, the refusal of a real provider and of an uncalibrated judge, both acceptance floors, the run
  record's hash round trip, the comparison's blocking regressions, the Wilson-bounded breaches and the suspension.
- **Regulatory mapping:**
  - 21 CFR 11.10(a), Annex 11 §4, draft Annex 22: the evaluation run record is the validation evidence per
    configuration version; production signals are the ongoing performance monitoring.
  - 21 CFR 11.70: qualification attests the run record's content hash.
  - GAMP 5: sets, rubrics and configurations are versioned configuration items under change control (pull requests).
  - HIPAA and GDPR: cases are de-identified and reviewed before commit, and the `deid` gate scans them.

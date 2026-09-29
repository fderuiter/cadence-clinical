# ADR-2251: Kernel Authority Computed from Evidence via a Hand-Coded Policy Evaluator (K004)

- **Status:** Accepted
- **Date:** 2026-09-29
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Every kernel command must be authorized against actor × capability × scope × time × resource state
(kernel specification §9.3). In clinical research, whether someone may act is not a role lookup. Whether
a coordinator may sign eligibility for a participant depends on an active delegation at that site, current GCP
and protocol-specific training, a valid credential, the StudySite being active, and the protocol version in
effect. It must also be possible to answer the question **retrospectively**: *was this action permitted at time
t, as known at time k?* ICH E6(R3) and 21 CFR Part 11 §11.10(d)/(g) require authority checks and their audit
(PRD-SYS-053).

Today, authorization is a mix of role strings (`User.Role`), per-module permission matrices (several 2026-07/08
ADRs), a `SiteComplianceCache` in `apps/execution`, and two separate DOA models (org and ctms). None of them is
time-aware or evidence-based.

## 2. Decision Drivers & Constraints

- **Driver 1 (bitemporal decisions):** evaluate grants, delegations and qualifications `as_of(at, known_at)`, so a retroactive DOA cannot validate a past action (K-AUTH-3).
- **Driver 2 (evidence-based):** capabilities follow from delegation, training, credentials and activation evidence held in the kernel, not copied into an external store.
- **Driver 3 (auditability):** every permit and deny is recorded with its inputs, and a denial names the missing evidence (K-AUTH-2, K-AUTH-6).
- **Driver 4 (same transaction):** authorization reads the same snapshot the command writes against.
- **Constraint:** small, reviewable code that a GxP validation package can cover; Native AOT friendly.

## 3. Options Considered

### Option 1: Hand-coded evaluator behind `IAuthorizer` over kernel tables (Selected)

- **Overview:** Grants, Delegations, Qualifications and conditions are kernel data. `Cadence.Kernel.Application`
  implements `IAuthorizer.Authorize(actor, capability, resource, at, knownAt)` as a fixed decision procedure
  (spec §9.3 steps 1–5) using bitemporal kernel queries in the command's transaction.
- **Pros:**
  - ✅ Time travel for free: the same bitemporal tables answer "permitted then, as known then".
  - ✅ One transaction and one snapshot; no cache coherence between a policy store and kernel state.
  - ✅ Decision tables are easy to test exhaustively and to include in validation evidence.
- **Cons:**
  - ❌ We own the evaluator's correctness and performance.
  - ❌ Policy changes to the procedure itself need a release (grant and condition *data* do not).

### Option 2: OpenFGA / Zanzibar-style relationship store

- **Overview:** Model scopes and grants as relationship tuples in an external service.
- **Pros:**
  - ✅ Scales to very large relationship graphs.
- **Cons:**
  - ❌ No bitemporal model; retrospective evaluation needs a second history system.
  - ❌ Evidence (training, credentials, activation state) must be mirrored into tuples, recreating sync drift.
  - ❌ A separate service outside the kernel transaction.

### Option 3: Cedar or OPA/Rego policies

- **Overview:** Express conditions in a policy language evaluated in-process (Cedar) or as a sidecar (OPA).
- **Pros:**
  - ✅ Declarative, analysable policies.
- **Cons:**
  - ❌ Still needs all evidence passed in as entities per request; time travel remains our problem.
  - ❌ Immature .NET bindings (Cedar) or network hop and extra infrastructure (OPA).

## 4. Decision Outcome

- **Chosen Option:** Option 1.
- **Justification:** The hard part of clinical authority is evidence and time, which already live in the kernel's
  bitemporal tables. An external engine would add a second source of truth without solving either. The
  evaluator sits behind `IAuthorizer`, so Cedar or OpenFGA can be introduced later behind the same port if grant
  volume or policy complexity demands it (revisit trigger: measured p95 authorization latency > 20 ms or policy
  conditions that cannot be expressed as data).

Model summary: `Grant(actor, capability, scope, effective_period, conditions)`, `Delegation` (a grant issued
under grant option), `Qualification(actor, kind, validity, evidence artifact)`, `Role` as an administrative
bundle only. Capabilities carry a verb class (`Read | Propose | Execute | Approve`). Blinding, consent scope,
locks and StudySite/Effectivity state are conditions.

## 5. Consequences & Trade-offs

- **Positive Impact:** One authorization path for humans, services and AI agents (K-AI-3); denials tell the user
  what evidence is missing; retrospective questions are answerable for inspections.
- **Negative Impact / Technical Debt:** The per-module permission-matrix ADRs, the org/ctms DOA tables and
  `SiteComplianceCache` become superseded as #4781 migrates each module; until then two systems coexist.
- **Mitigation Strategy:** Supersession is annotated per `AGENTS.md` §12 as each module moves. K-XS-1 forbids new
  `Role`/`Delegation` types outside the kernel.

## 6. Implementation & Verification

- **Affected Assemblies:** `Cadence.Kernel.Abstractions` (`IAuthorizer`, capability tokens), `Cadence.Kernel.Domain`
  (grant/delegation/qualification aggregates), `Cadence.Kernel.Application` (evaluator, pipeline step).
- **Tracked by:** #4781 (authority), #4779 (pipeline port), #4731 (spec §14 tests 3 and 12).
- **Verification Plan:**
  - Decision-table unit tests per capability class.
  - Time-travel tests: an action outside an active delegation is denied, and a later retroactive DOA leaves the
    historical decision denied.
  - Expired protocol training denies eligibility signature and names the expired qualification.

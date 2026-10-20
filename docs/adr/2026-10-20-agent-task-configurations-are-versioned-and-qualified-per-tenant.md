# ADR-2631: Agent Task Configurations Are Versioned and Qualified per Tenant

- **Status:** Accepted
- **Date:** 2026-10-20
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Issue:** #5385 (epic #5545)
- **Requirements:** PRD-SYS-051, 21 CFR 11.10(a), (e), (k), 11.50, 11.70; EU GMP Annex 11 §4, §10
- **Builds on:** ADR-2630 §4.3 (task configurations), K-AI-5

---

## 1. Context & Problem Statement

ADR-2630 makes the **task configuration**, not a model ID, the unit an agent is qualified on: the model, prompt,
tools, retrieval sources, output schema, evaluation set, acceptance criteria, autonomy level, approver role and the
FDA credibility-assessment fields. Before this decision the Intelligence module (#5374) had a configuration with a
content hash but nothing that said whether a version was fit for use. Any registered configuration could run, and a
changed prompt ran at once.

Regulators expect the opposite. 21 CFR 11.10(a) asks for validation of the system for its intended use, Annex 11 asks
for change control and a risk-based validation, and the FDA's draft guidance on AI for regulatory decision-making
(January 2025) asks for a credibility assessment whose depth follows the model risk, set by model influence and
decision consequence. The EMA reflection paper and the FDA and EMA guiding principles (January 2026) ask for a
defined context of use and life-cycle monitoring.

## 2. Decision Drivers & Constraints

- A changed prompt, tool list or model must not run on an earlier qualification.
- Qualification is a person's signed decision, never an agent's (the Approve verb is refused to `agent:` actors).
- The sponsor owns validation for its context of use, so qualification is per tenant.
- Monitoring (#5548) must be able to take a configuration out of use without a deployment.
- The module stays on the kernel module SDK only (K-XS-2, ADR-2600) and writes only through kernel commands.

## 3. Options Considered

### Option 1: Code-defined versions, per-tenant signed qualification (Selected)

- **Overview:** configurations are defined in code and reviewed like any change; a tenant qualifies a version on
  evaluation evidence with an electronic signature; the runtime uses only qualified, unchanged versions.
- **Pros:**
  - ✅ Every configuration change is a reviewed commit and a new version, so change control is the normal workflow.
  - ✅ The signature binds a named person to the exact content hash (11.50, 11.70).
- **Cons:**
  - ❌ A tenant cannot author its own configuration; it can only qualify, suspend or not use the published ones.

### Option 2: Configurations stored and edited in the database

- **Overview:** tenants author configurations at runtime.
- **Cons:**
  - ❌ Prompt and tool changes would bypass code review and CI. Revisit when a tenant needs its own tasks.

### Option 3: The model ID as the qualified unit, qualified once per deployment

- **Cons:**
  - ❌ A prompt or tool change would not require requalification, and one sponsor's validation would stand for all.

## 4. Decision Outcome

**Chosen option:** Option 1.

### 4.1 The configuration

`TaskConfiguration` holds the run fields (key, version, provider, model or tier, instructions, output limit, autonomy
level) and the governance fields: tools, retrieval sources, output schema, the pinned evaluation set (id, version,
content hash), acceptance criteria, approver role and the credibility assessment (question of interest, context of
use, model influence, decision consequence). Its `ContentHash` is the kernel content hash (`sha256:` and hex) over all
of them, so it is the same form as every record a kernel attestation signs. Several versions of a task may be
registered; registering one version twice, or a malformed one, fails at startup. A published version never changes.

The risk class follows the FDA influence-by-consequence matrix: high when both are high or one is high and the other
medium, low when both are low or one is low and the other medium, otherwise medium.

### 4.2 Qualification

A `TaskQualification` (table `intelligence.task_qualifications`, under tenant Row-Level Security, no delete for the
application role) records one tenant's qualification of one version: the content hash it covers, the risk class, the
evaluation run reference, the evaluation set hash, the run summary, the attestation id and who qualified it when.

`QualifyTaskConfiguration` (capability `task_configuration.qualify`, verb `Approve`, so only a person can qualify)
succeeds only when:

1. the version is registered and its current content hash is the one named;
2. it is complete enough to qualify: an evaluation set, acceptance criteria, a complete credibility assessment, and an
   approver role from autonomy level Draft up;
3. the evaluation evidence ran against the pinned set's hash and met the acceptance criteria;
4. the caller signed an `Approved` kernel attestation over target kind `task_configuration`, the version's derived id
   and its content hash.

`SuspendTaskConfiguration` (capability `task_configuration.suspend`, verb `Execute`) takes a version out of use with a
reason; it is idempotent. Only a new qualification, on new evidence and a new signature, restores it. Monitoring calls
it through the `ITaskConfigurationSuspension` port.

### 4.3 Use at runtime

`AgentRunner` routes a request to the newest version the tenant has qualified on its current content. When none is
usable it refuses with `M.Intelligence.Configuration.Unqualified` and says why for the newest version: not qualified,
suspended (with the reason) or changed since qualification (with both hashes). `StartAgentRun` re-checks inside its
transaction and refuses a configuration that differs from the registered version. `GET /api/v1/ai/tasks` lists each
version with the tenant's qualification and whether it is usable.

### 4.4 The attestation seam in the module SDK

`IAttestationVerifier` and `IAttestationTargetCheck` move from kernel Application to `Cadence.Kernel.Modules`, with
`AddModuleAttestationTarget<TCheck>()`, so a module can require a kernel signature over its own records without
referencing kernel internals. The Intelligence module registers the `task_configuration` target check, which confirms
the version exists and its content hash is current when someone signs it.

## 5. Consequences & Trade-offs

- **Positive:** no agent runs on a configuration nobody qualified, and changing anything about a configuration takes
  it out of use until a person signs the new version. Each qualification and suspension is a version on the kernel
  audit chain and raises an event (`intelligence.TaskConfigurationQualified`, `intelligence.TaskConfigurationSuspended`).
- **Negative:** every new task needs an evaluation set and a qualification before use, in every tenant.
- **Open:**
  - The evaluation evidence is stated by the qualifier until the evaluation harness (#5548) stores runs; then the
    command will read the stored run instead of taking `passed` from the request. The signature makes the qualifier
    accountable in the meantime.
  - There is no HTTP step-up signing route yet: the qualification route takes the id of an attestation made with the
    kernel `Attest` command.
  - Evidence thresholds per risk class come with the harness; the risk class is recorded now.
  - K-AI-5 stays in the rule-coverage baseline until agent Proposals record the configuration reference (the first
    agents).

## 6. Implementation & Verification

- **Affected:** `apps/intelligence` (domain, commands, runner, endpoints, migration `TaskQualifications`),
  `packages/kernel` (`Cadence.Kernel.Modules` attestation seam).
- **Verification:** `tests/Cadence.UnitTests/Intelligence` (hash coverage, risk matrix, qualification gaps, usability,
  runner routing and refusals) and `tests/Cadence.IntegrationTests/Intelligence` on PostgreSQL 18 as the application
  role (unqualified, changed and suspended configurations refused; the qualifier's own signature, accepted evidence
  and a complete configuration required; Row-Level Security; no delete).

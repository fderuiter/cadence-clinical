# ADR-2254: Rules as Versioned Data, Continuous Standards Conformance and a Unified Rules Engine (K008)

- **Status:** Accepted
- **Date:** 2026-09-29
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Cadence owns the lifecycle from protocol design through data capture to submission. The usual industry
pipeline (protocol → CRF build → collection → export → mapping specification → SDTM transformation →
conformance validation → fix mapping → regenerate) discovers standards problems months after design. Because
one platform owns both ends, conformance can be checked when a CRF field is designed and datasets can be
generated and validated continuously (spec §6.3, §10).

Rules exist today in several unrelated forms: EDC edit checks (`AstEditCheckEngine` in `apps/execution`),
lab-range logic, and ad-hoc checks in services. Findings are text or module-specific records. Submission-grade
validation (SDTM, SEND, ADaM, Define-XML) is absent. This must be traceable under 21 CFR Part 11
(PRD-SYS-053) and support CDISC-based design (PRD-MDR-001, PRD-DDF-001).

## 2. Decision Drivers & Constraints

- **Driver 1 (one rule model):** edit checks, standards conformance, sponsor/study rules and quality indicators share one expression language and one finding model.
- **Driver 2 (rules evolve independently):** new standard versions and agency rules ship as data, not platform releases.
- **Driver 3 (reproducibility):** same rule version + same inputs → same findings.
- **Driver 4 (intellectual property):** rules implement public standards and agency requirements, never a vendor's proprietary implementations.
- **Driver 5 (scale):** millions of records, streaming, deterministic, parallel evaluation.

## 3. Options Considered

### Option 1: Versioned rule packs over one AST, evaluator behind a port, Rust runtime as a later seam (Selected)

- **Overview:** `Rule(id, version, authority, source citation, applicability, expression AST, severity,
explanation, remediation, effective period)` grouped into attested `RulePack`s (CDISC SDTM/SEND/ADaM/Define-XML;
  FDA, PMDA, NMPA; sponsor; study). A `ValidationRun` evaluates packs at a lifecycle stage and yields
  `ValidationFinding`s with stable identity across runs. The evaluator is `IRuleEvaluator`; the .NET
  implementation evolves from `AstEditCheckEngine`. A Rust runtime (CLI, server, WASM) may replace it behind
  the same port when a measured workload needs it.
- **Pros:**
  - ✅ Design-time, collection-time, standardization, analysis and submission checks use one engine.
  - ✅ Findings are structured, traceable through lineage, and carry workflow (Open → Resolved/Accepted/Explained).
  - ✅ Rule content can be reviewed and versioned like any regulated specification.
- **Cons:**
  - ❌ Building and maintaining rule packs from public specifications is substantial work.

### Option 2: Integrate an external validator and import its reports

- **Pros:**
  - ✅ Fast to reach submission-grade coverage.
- **Cons:**
  - ❌ Validation stays a late batch step on exported files; no design-time checks or lineage.
  - ❌ Findings are text, disconnected from kernel facts; licensing ties the platform to a vendor.

### Option 3: Compile rules into application code per module

- **Cons:**
  - ❌ Every rule change is a release; no shared finding model; duplicates today's fragmentation.

## 4. Decision Outcome

- **Chosen Option:** Option 1.
- **Justification:** It turns standards conformance into a continuous property of the platform, reuses the existing
  AST engine, and keeps the performance-critical evaluator replaceable without changing the model.

Rules fixed by this ADR: K-RULE-1 (rules are data), K-RULE-2 (public standards only), K-RULE-3 (one AST behind
`IRuleEvaluator`), K-RULE-4 (deterministic), K-STD-1/2 (mapping before capture; design-time conformance),
K-FIND-1..3 (finding identity, attested acceptance, lineage). Edit checks never reject a well-typed clinical fact;
they raise findings and queries (spec §10.2).

## 5. Consequences & Trade-offs

- **Positive Impact:** No database-lock surprise; findings participate in workflow and reviewer guides; the rules
  engine is the clearest place for a future Rust component.
- **Negative Impact / Technical Debt:** `AstEditCheckEngine`'s port must be retyped away from `ClinicalObservation`
  and SDTM codes (inventory §3 item 9).
- **Mitigation Strategy:** Start with EDC edit checks and a small CDISC CT/UCUM pack on the new model, then add
  standards packs per roadmap phase 2.

## 6. Implementation & Verification

- **Affected Assemblies:** `Cadence.Rules.*` (model, loader, `IRuleEvaluator`), `Cadence.Kernel.Domain`
  (`ValidationFinding`), `Cadence.Standards.*`, `apps/execution` edit checks.
- **Tracked by:** #4728 (edit checks), roadmap phase 2 issues.
- **Verification Plan:**
  - Golden tests: each rule pack ships fixtures with expected findings; evaluation is byte-stable.
  - Re-running validation after a fix resolves the finding instead of duplicating it (spec §14 test 10).
  - A CollectionSpec with no conformant representation is blocked at publish (spec §14 test 8).

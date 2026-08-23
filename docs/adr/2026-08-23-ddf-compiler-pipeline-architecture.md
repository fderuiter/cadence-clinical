# ADR-2212: DDF Compiler Pipeline Architecture

* **Status:** Accepted
* **Date:** 2026-08-23
* **Authors:** @fderuiter
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

The previous Digital Data Flow (DDF) compilation process in `apps/execution/translator.py` was an 830-line procedural monolithic script (`process_translation`). It coupled USDM schema validation, fallback metadata generation, CDISC ODM XML layout rendering, OpenRosa XForms XPath rule compilation, Playwright accessibility checking, and asynchronous database transaction/audit logging into a single tightly coupled function. This prevented isolated in-memory unit testing, duplicated rule parsing logic across `apps/designer` and `apps/execution`, and created high friction when adding new CDISC USDM specification versions.

## 2. Decision Drivers & Constraints

* **GxP & CDISC Compliance (PRD-DDF-001, 21 CFR Part 11)**: CDISC USDM protocol specifications and rule conditions must compile deterministically into ODM and OpenRosa XForms with strict audit trail tracking.
* **Deep Module Design**: The compiler should present a minimal, pure in-memory interface (`DDFCompiler.compile(...)`) hiding complex multi-stage pipeline machinery.
* **Zero Sibling Database Imports**: `apps/designer` and `apps/execution` must not import each other's persistence models; shared transformation logic belongs in shared platform packages (e.g. `packages.ddf`).
* **Sub-Second Test Velocity**: Unit testing compilation pipelines must run in-memory without database engines or browser automation overhead (`cadence test --fast`).

## 3. Options Considered

1. **Option A (Selected)**: Create a shared, zero-database platform package (`packages.ddf`) with an explicit Intermediate Representation (IR), 5 isolated pipeline stages, and an injectable `AccessibilityValidatorPort`. Provide a thin `DDFJobRunner` adapter in `apps/execution` to manage database transactions and audit trails, while consolidating Designer rule ASTs.
2. **Option B**: Maintain the compiler within `apps/execution` as helper functions, exposing execution HTTP endpoints for Designer preview flows.
3. **Option C**: Dynamic DAG middleware engine streaming translation events.

## 4. Decision Outcome

Chosen option: **Option A**.
We extract the DDF compiler into a dedicated package featuring:
- **Pure In-Memory Interface**: `DDFCompiler.compile(study_id, payload, options) -> DDFCompilationResult`
- **5 Pipeline Stages**:
  1. `USDMNormalizerStage`: Schema validation, metadata fallback synthesis, USDM version resolution.
  2. `DDFSemanticIRBuilderStage`: Builds strongly-typed `DDFIntermediateRepresentation` decoupling ingestion from rendering.
  3. `XPathRuleCompilerStage`: Recursively compiles `ExpressionNode` rule trees into OpenRosa XPath expressions.
  4. `XmlLayoutRendererStage`: Jinja2 template rendering for CDISC ODM v1.3.2 and OpenRosa XForms.
  5. `LayoutAccessibilityValidatorStage`: Validates form layouts via `AccessibilityValidatorPort` protocol.
- **Unified Rule Engine**: Consolidate `ExpressionNode` and rule evaluation in unified rule submodules for both Designer and Execution.
- **Execution Job Adapter**: `DDFJobRunner` in `apps/execution` wraps `DDFCompiler` with database transactions and Part 11 audit logging.
- **Backward Compatibility**: `apps/execution/translator.py` acts as a thin facade re-exporting legacy symbols.

## 5. Consequences & Trade-offs

* **Positive**:
  - In-memory sub-millisecond compilation testing without spinning up PostgreSQL or Playwright.
  - Complete elimination of AST/rule parsing duplication between `apps/designer` and `apps/execution`.
  - Zero database transaction leakage into transformation logic.
* **Negative**:
  - Requires maintaining an explicit Intermediate Representation (IR) layer between USDM and XML layouts.

## 6. Implementation & Verification

* Package architecture: shared compiler modules (`compiler.py`, `models.py`, `ports.py`, `stages/`, `rules/`, `templates/`).
* Adapters: `apps/execution/translator.py` facade.
* Unified Designer rules: `apps/designer/rules.py` and `apps/designer/inverse_mapper.py` utilize canonical AST definitions.
* Verification: `scripts/tests/test_translator.py`, `scripts/tests/test_translation_recovery.py`.


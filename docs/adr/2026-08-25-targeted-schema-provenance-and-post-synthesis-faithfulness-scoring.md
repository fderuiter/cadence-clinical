# ADR-2404: Targeted Schema Provenance and Post Synthesis Faithfulness Scoring

Also known as: ADR-2217 (former number)

- **Status:** Accepted
- **Date:** 2026-08-25
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

During protocol digitization and synthesis of eCRF forms and Schedule of Activities (SoA) matrices, maintaining strict end-to-end provenance to source protocol section and page coordinates is essential for regulatory compliance (21 CFR Part 11) and auditability. Synthesized forms and compiled SoA cells without explicit provenance metadata lack grounding verification, leading to potential hallucination risks or untraceable field definitions.

The system requires:
1. Extending eCRF form schemas, field definitions, and SoA cell representations with granular citation coordinate metadata (`section_number`, `page_number`, `citation_marker`).
2. Standardizing verbatim citation marker syntax across all synthesis modules (`[Protocol {version}, Section {section}, Page {page}]` or `[Protocol {version}, Page {page}]`).
3. An automated post-synthesis faithfulness evaluator computing mathematical confidence scores against a groundedness threshold (`0.85`) under a strict PRD-SUB-005.0s SLA without blocking clinical authoring workflows.

Applicable system requirements: `PRD-CRF-004`, `PRD-DDF-001`, `PRD-SYS-001`, `PRD-MDR-007`.

## 2. Decision Drivers & Constraints

* **Regulatory Traceability (21 CFR Part 11):** Every synthesized eCRF field and SoA matrix cell must link back to specific section and page coordinates in the source protocol.
* **Non-Blocking User Workflow:** Faithfulness scores below threshold (`0.85`) should flag outputs for review (`FLAGGED_LOW_FAITHFULNESS`) and populate user warnings rather than failing pipeline execution.
* **Performance SLA:** Evaluator execution must complete in PRD-SUB-005.0 seconds.
* **API Response Transparency:** Verification metrics (`faithfulness_score`, `is_faithful`, `verification_status`, `citation_coverage`, `warnings`) must be included in API response DTOs (`CommitUSDMResponse`, `DAGJobStatusResponse`, `USDMProtocolExtractionResponse`).

## 3. Options Considered

### Option 1: **Option A (Selected): Integrated Schema Provenance Metadata & Weighted Post-Synthesis Evaluator**

- **Overview:** - Attach optional citation coordinate fields directly to `SynthesizedECRFForm`, `SynthesizedField`, `SoACellView`, and `SoACompiler`.
   - Format citation markers deterministically using standardized version/section/page templates.
   - Execute `evaluate_synthesis_faithfulness` post-synthesis using a weighted calculation: Citation Validity (50%), Protocol Version Alignment (30%), and Lexical Grounding Overlap (20%).
- ***Pros:***
  - ✅ Satisfies architectural requirements
- ***Cons:***
  - ❌ Requires implementation and maintenance overhead

### Option 2: **Option B: External Post-Processing Pipeline & Async Verification Worker**

- **Overview:** - Delegate provenance tracking and scoring to a secondary async service.
   - Pros: Decouples evaluation from synthesis engine.
   - Cons: Adds service boundary latency and complexity, preventing synchronous feedback in API responses.
- ***Pros:***
  - ✅ Satisfies architectural requirements
- ***Cons:***
  - ❌ Requires implementation and maintenance overhead


## 4. Decision Outcome

- **Chosen Option:** **Option A** because it enforces immediate, deterministic citation formatting and inline faithfulness evaluation during synthesis while guaranteeing sub-5s response SLAs and complete contract alignment across API DTOs.
- **Justification:** Selected as primary architectural approach.

## 5. Consequences & Trade-offs

* **Positive:**
  - Full end-to-end traceability from synthesized eCRFs and SoA matrices back to protocol source sections and page numbers.
  - Mathematical confidence scoring against 0.85 threshold ensures immediate awareness of ungrounded or low-confidence synthesis results.
  - DTO metadata payloads provide explicit verification indicators across API endpoints.
* **Negative:**
  - Slightly larger response payloads and schema models due to added coordinate metadata fields.

## 6. Implementation & Verification

* **Modified Domains & Services:**
  - `apps/designer/domain/digitization_models.py` & `apps/designer/domain/digitization_dag_models.py`: Added `section_number`, `page_number`, `citation_marker`, and `FaithfulnessVerificationMetadata`.
  - `apps/designer/domain/synthesis/crf_synthesizer.py`: Added citation formatting and section/page parsing for synthesized forms and fields.
  - `apps/designer/domain/synthesis/soa_compiler.py`: Added citation coordinate attachment across SoA matrix cells.
  - `apps/designer/domain/synthesis/faithfulness.py`: Implemented `evaluate_synthesis_faithfulness` evaluation engine.
  - `apps/designer/application/services/digitization_service.py` & `digitization_dag_service.py`: Integrated faithfulness verification into synthesis workflows.
  - `apps/designer/presentation/routers/digitization.py`: Updated DTO response representations.
* **Verification Tests:**
  - Unit tests in `apps/designer/tests/test_schema_provenance_faithfulness.py` validating schema provenance coordinates, citation formatting, faithfulness scoring, SLA targets, and DTO integration under requirements `@req:PRD-CRF-004`, `@req:PRD-DDF-001`, `@req:PRD-SYS-001`, `@req:PRD-MDR-007`.


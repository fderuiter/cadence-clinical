# ADR-2193: Multimodal eTMF and eISF Document Intelligence and DIA Reference Model Classifier

* **Status:** Accepted
* **Date:** 2026-08-21
* **Authors:** @fderuiter
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Clinical Trial Master File (eTMF) and Investigator Site File (eISF) management requires processing and filing vast volumes of heterogeneous regulatory documents (e.g. FDA Form 1572, Financial Disclosures, Delegation of Authority logs, Informed Consent Forms, Protocol Sign-offs, Laboratory Certificates). In manual workflows, regulatory filing is labor-intensive, error-prone, and vulnerable to missing signatures, incorrect taxonomy classification, and delayed inspection readiness. A unified, multimodal document intelligence and classification engine is needed to satisfy **PRD-TMF-006** by automating layout extraction, regulatory metadata parsing, signature completeness verification, multi-signal DIA TMF Reference Model taxonomy classification, and staging for Clinical Research Associate (CRA) Quality Control (QC) under FDA 21 CFR Part 11 dual-attribution standards.

## 2. Decision Drivers & Constraints

* **Multi-Signal Taxonomy Classification (DIA TMF Reference Model v3.2.0)**: Must resolve documents across all 11 DIA zones, sections, and canonical artifact codes combining visual layout cues (e.g. OMB form headers), semantic keywords, artifact code tokens, and AI Gateway inference.
* **Deterministic Fallback & 100ms Microservice SLA**: The classifier must operate reliably with deterministic local heuristics, scoring signals, and fast pattern matching, falling back smoothly when external AI models are unavailable.
* **FDA 21 CFR Part 11 Dual-Attribution & CRA Quality Control**: AI cannot autonomously commit regulated clinical records into active/approved states. Classified documents must enter a `DRAFT_AI` or `TECHNICAL_QC` state with comprehensive AI generation manifests and require human CRA review, electronic signature verification, and mandatory reasons for change.
* **Signature Completeness & Regulatory Verification**: Must automatically detect required signature blocks (PI signatures, sponsor sign-offs, subject consent), evaluate digital vs. physical signature manifestations, and flag discrepancies before clinical milestone transitions.
* **eISF to eTMF Cross-System Mapping**: Site documents uploaded into eISF binders must automatically map to standard DIA eISF folders and propagate cleanly to corresponding Sponsor eTMF artifact codes (`05.02.01` Form 1572, `05.02.02` Financial Disclosure, `05.02.04` DOA Log, `05.02.05` ICF, `05.02.98` Medical License).

## 3. Options Considered

1. **Option A (Selected): Dedicated Domain-Driven Document Intelligence Pipeline with Multi-Signal Scoring and Dual-Attribution CRA Staging**: Implement modular domain services (`DocumentIntelligenceParser`, `DIAReferenceModelClassifier`, `RegulatoryMetadataExtractor`, `SignatureCompletenessAnalyzer`) within `apps/etmf` and `apps/eisf`. Use multi-signal confidence scoring (0.0 to 1.0) and integrate with `apps/ai_gateway` for structured inference while preserving complete offline deterministic execution. Stage artifacts in a dedicated CRA QC queue with 21 CFR Part 11 audit trails.
2. **Option B: Purely External Cloud OCR & LLM Vendor Processing**: Offload all document parsing and classification to cloud vendor APIs without local domain layout rules or offline fallback.
3. **Option C: Rigid Keyword Matching Without Multimodal Layout or Signature Intelligence**: Rely exclusively on exact substring alias maps and filename string matching.

## 4. Decision Outcome

Chosen option: **Option A**.
Option A combines the speed and determinism of local layout and heuristic scoring with the semantic power of AI Gateway inference. It maintains strict GxP boundary isolation, ensures zero vendor lock-in, and guarantees 21 CFR Part 11 compliance by requiring human CRA adjudication on staged artifacts before final approval.

## 5. Consequences & Trade-offs

* **Positive**: High accuracy multi-signal classification combining layout topologies, OMB numbers, semantic tokens, and AI Gateway structured extraction.
* **Positive**: Automated signature completeness detection prevents filing unsigned regulatory forms.
* **Positive**: 21 CFR Part 11 compliant CRA Quality Control workflows with full audit logging and electronic signature verification.
* **Positive**: Seamless eISF-to-eTMF cross-system classification mapping.
* **Negative**: Requires maintaining signature requirement tables and layout heuristics for standard regulatory form templates.

## 6. Implementation & Verification

* Implement domain intelligence models in `apps/etmf/domain/intelligence_models.py`.
* Implement document parser and layout analyzer in `apps/etmf/domain/services/document_intelligence_parser.py`.
* Implement DIA Reference Model classifier in `apps/etmf/domain/services/dia_classifier.py`.
* Implement regulatory metadata extractor in `apps/etmf/domain/services/metadata_extractor.py`.
* Implement signature analyzer in `apps/etmf/domain/services/signature_analyzer.py`.
* Implement application use cases in `apps/etmf/application/document_intelligence_use_case.py`.
* Implement REST router in `apps/etmf/presentation/routers/intelligence.py` and register in `apps/etmf/main.py`.
* Implement eISF document intelligence integration in `apps/eisf/presentation/routers/eisf.py`.
* Update web UI in `apps/web/src/views/DocumentManagerView.vue` with document intelligence inspector and CRA QC review controls.
* Verification through automated unit, integration, and GxP traceability tests under `apps/etmf/tests/test_document_intelligence.py` and `apps/eisf/tests/test_eisf_intelligence.py`.

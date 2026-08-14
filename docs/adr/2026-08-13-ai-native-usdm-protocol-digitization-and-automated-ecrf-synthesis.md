# ADR-2174: AI-Native USDM Protocol Digitization and Automated eCRF Synthesis

- **Status:** Accepted
- **Date:** 2026-08-13
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Clinical study setup traditionally requires 8 to 16 weeks of manual effort by cross-functional study teams to read lengthy protocol documents (PDF/DOCX), manually extract study arms, epochs, visit schedules, and inclusion/exclusion criteria, and manually program data collection screens and edit checks into an EDC system.

To achieve Cadence's automated Digital Data Flow (DDF) vision, we need an automated pipeline that ingests unstructured clinical protocol documents, extracts structured parameters conforming to CDISC USDM v4.0 and CDASH specifications, populates the Neo4j knowledge graph, and automatically synthesizes production-ready CDASH eCRF forms, VAS pain sliders, 74-zone SNOMED CT body maps, and validation rules in under 60 seconds (PRD-DDF-001).

## 2. Decision Drivers & Constraints

- **Strict CDISC Standards:** Direct compliance with CDISC USDM v4.0 graph taxonomy and CDASHIG v2.3 domain specifications (VS, EG, LB, AE, CM, DM, PE, QS, EX).
- **21 CFR Part 11 & GxP Traceability:** Full audit attribution, immutable timestamps, and mandatory user change justifications on all graph and study version modifications (PRD-SYS-001, PRD-DDF-001).
- **High Performance & Determinism:** Pipeline execution must complete in under 60 seconds, with zero external network dependency in offline test and sandboxed environments.
- **AST Validation & Static Cycle Detection:** All synthesized skip-logic rules must undergo cycle detection to prevent recursive dependency deadlocks in the EDC runtime (PRD-CRF-005).

## 3. Options Considered

1. **Integrated Multi-Stage LLM & Heuristic Extraction Pipeline (Selected):** Ingests document bytes using PyMuPDF and python-docx, uses structured Pydantic v2 schemas for LLM extraction with deterministic heuristic fallback, populates Neo4j graph nodes and relationships atomically, and synthesizes CDASH eCRFs and interactive Schedule of Activities.
2. **Pure Manual Form Authoring in MDR:** Requires study teams to transcribe protocol parameters manually into the UI. High friction and time to study launch.
3. **Unstructured Vector RAG without Schema Enforcement:** Ingests protocol chunks into a vector database without strict USDM ontology mapping, leading to non-deterministic schemas and broken EDC form generation.

## 4. Decision Outcome

Chosen option: **Option 1 (Integrated Multi-Stage LLM & Heuristic Extraction Pipeline)** because it satisfies PRD-DDF-001, enforces CDISC USDM v4.0 structural consistency, provides deterministic offline test execution, and generates verified CDASH eCRF forms with Part 11 audit logging.

## 5. Consequences & Trade-offs

- **Positive:**
  - Study setup time reduced from 8-16 weeks to under 60 seconds.
  - Direct Neo4j USDM v4.0 graph population without orphaned nodes.
  - Automatic CDASH eCRF generation including VAS sliders and 74-zone SNOMED CT body maps.
  - Robust offline testing and zero flaky external LLM network calls in CI.
- **Negative:**
  - Requires maintaining CDASH catalog mappings and heuristic fallback rules as clinical trial taxonomies evolve.

## 6. Implementation & Verification

- **Data Models:** `apps/designer/domain/digitization_models.py`
- **Extraction & Synthesis Service:** `apps/designer/application/services/digitization_service.py`
- **Neo4j Graph Writer:** `apps/designer/infrastructure/neo4j_usdm_writer.py`
- **API Endpoints:** `apps/designer/presentation/routers/digitization.py`
- **Frontend Web UI:** `apps/web/src/views/ProtocolDigitizationView.vue`, `apps/web/src/components/clinical/SoAMatrixEditor.vue`, `apps/web/src/components/clinical/ArmVisualizer.vue`, `apps/web/src/components/clinical/IECriteriaTable.vue`
- **Automated Tests:** `apps/designer/tests/test_ai_usdm_digitization.py` verifying entity extraction, Neo4j graph integrity, cycle detection, and sub-60-second synthesis.

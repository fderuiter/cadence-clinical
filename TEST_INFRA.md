# Test Infrastructure: "Zero-Click" USDM Study Build & Automated Synthesis

## 1. Overview

This document specifies the testing architecture, tiered verification strategy, test harness design, and Requirement Traceability Matrix (RTM) alignment for the **Zero-Click Study Build** capability within the Cadence Clinical Research Platform.

The test suite is implemented in:
```
apps/designer/tests/test_zero_click_usdm_build.py
```

---

## 2. Test Pyramid & 4-Tier Strategy

```
                      ┌──────────────────────────────────────────────┐
                      │                   TIER 4                     │
                      │         Real-World Scenarios & SLA           │
                      │   (Phase II Oncology, < 5.0s Benchmark,      │
                      │          21 CFR Part 11 Audit Trail)         │
                      └───────────────────────┬──────────────────────┘
                                              │
                                              ▼
                      ┌──────────────────────────────────────────────┐
                      │                   TIER 3                     │
                      │         Cross-Feature Combinations           │
                      │   (End-to-End Ingest -> Synthesize eCRFs     │
                      │       -> Compile SoA -> Seed eTMF EDL)       │
                      └───────────────────────┬──────────────────────┘
                                              │
                                              ▼
                      ┌──────────────────────────────────────────────┐
                      │                   TIER 2                     │
                      │          Boundary & Corner Cases             │
                      │   (Atomic Rollback, Malformed USDM,          │
                      │      Unmapped Domains, Cyclic Logic)         │
                      └───────────────────────┬──────────────────────┘
                                              │
                                              ▼
                      ┌──────────────────────────────────────────────┐
                      │                   TIER 1                     │
                      │            Core Feature Coverage             │
                      │   (USDM Graph Ingestion, eCRF Synthesis,     │
                      │       SoA Compilation, DIA TMF Seeding)      │
                      └──────────────────────────────────────────────┘
```

---

## 3. Tiered Test Inventory

| Tier | Test Function | Target Capabilities | Requirement Traceability |
| :--- | :--- | :--- | :--- |
| **Tier 1** | `test_tier1_usdm_graph_ingestion_transactional` | Transactional parsing & Cypher graph creation of `Study`, `StudyDesign`, `StudyEpoch`, `StudyArm`, `Encounter`, `Activity`, `EligibilityCriterion` with relational graph semantics (`HAS_EPOCH`, `HAS_ARM`, `CONTAINS_ENCOUNTER`, `HAS_ACTIVITY`, `PERFORMS`, `HAS_CRITERION`). | `@req:PRD-SYS-001`<br>`@req:PRD-DDF-001` |
| **Tier 1** | `test_tier1_ecrf_layout_synthesis_engine` | Automated CDASH form synthesis across domains (`VS`, `EG`, `LB`, `QS`, `PE`, `DM`, `AE`), widget rendering (`vas_slider`, `body_map_74_zone`), and declarative edit checks (`VS_SYSBP > VS_DIABP`, `EG_QTC <= 500`). | `@req:PRD-CRF-004` |
| **Tier 1** | `test_tier1_soa_matrix_compilation_from_graph` | Dynamic Schedule of Activities (SoA) visit-vs-procedure matrix compilation from graph `PERFORMS` edges into `SoAMatrixView` projection. | `@req:PRD-MDR-007` |
| **Tier 1** | `test_tier1_etmf_edl_seeding_milestones_and_zones` | Automated DIA TMF Reference Model 11-Zone catalog resolution and mandatory Expected Document List (EDL) seeding across trial lifecycle milestones (`INITIATION`, `CONDUCT`, `CLOSEOUT`). | `@req:PRD-TMF-001` |
| **Tier 2** | `test_tier2_atomic_rollback_on_invalid_usdm_payload` | Error handling and atomic rejection of corrupted, non-dict, or incomplete USDM JSON payloads. | `@req:PRD-SYS-001` |
| **Tier 2** | `test_tier2_edge_cases_empty_and_unmapped_entities` | Graceful fallback on unmapped CDASH domains (auto-generating default status/comments fields) and AST cycle detection for skip-logic rules. | `@req:PRD-DDF-001`<br>`@req:PRD-CRF-004` |
| **Tier 3** | `test_tier3_end_to_end_zero_click_build_pipeline` | Complete interconnected pipeline: Ingest USDM -> Populate Graph -> Synthesize eCRFs -> Compile SoA Matrix -> Seed DIA TMF EDL. | `@req:PRD-SYS-001`<br>`@req:PRD-DDF-001`<br>`@req:PRD-MDR-007`<br>`@req:PRD-TMF-001` |
| **Tier 4** | `test_tier4_phase2_oncology_real_world_protocol` | Complex real-world Phase II Oncology study: multi-epoch, multi-arm, 74-zone SNOMED CT body map, VAS pain slider, cardiac safety QTc monitoring, CTCAE grading, and DIA TMF EDL. | `@req:PRD-DDF-001`<br>`@req:PRD-CRF-004` |
| **Tier 4** | `test_tier4_execution_performance_benchmark_under_5s` | Non-functional performance benchmark asserting end-to-end extraction and synthesis pipeline executes in < 5.0 seconds. | `@req:PRD-DDF-001` |
| **Tier 4** | `test_tier4_part11_gxp_audit_and_change_justification` | 21 CFR Part 11 compliance enforcing gateway signature verification, user identity attribution, and rejection of empty/missing change justifications (HTTP 400 / 403). | `@req:PRD-SYS-001` |

---

## 4. Test Environment & Mock Graph Driver

The test suite runs with 100% test isolation and zero external network dependencies:
- **Neo4j Emulation**: Leverages `MockGraphDriver` (`packages/database/mock_graph.py`) to simulate asynchronous Cypher graph execution, recording all created sessions, nodes, and relationships in memory.
- **HMAC Gateway Security**: Test helper `get_gateway_auth_headers` generates authentic v2 gateway cryptographic HMAC-SHA256 signatures for `GatewayAuthMiddleware` verification.
- **Deterministic Heuristics**: In the absence of live LLM endpoints (`LLM_API_KEY`), the extraction service uses high-fidelity heuristic parsing for fast, reproducible CI test execution.

---

## 5. Execution Commands

```bash
# Run the Zero-Click Study Build test suite with verbose output
uv run pytest -o addopts="" apps/designer/tests/test_zero_click_usdm_build.py -v

# Run code style and import ordering verification
uv run ruff check apps/designer/tests/test_zero_click_usdm_build.py
uv run ruff format --check apps/designer/tests/test_zero_click_usdm_build.py

# Run workspace-wide GxP compliance synchronization
uv run python scripts/sync_gxp.py
```

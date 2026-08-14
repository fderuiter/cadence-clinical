# E2E Test Infra: Cadence Clinical Phase 1 Deliverables

## Test Philosophy
- Opaque-box, requirement-driven testing based on `ORIGINAL_REQUEST.md`.
- Derived from user requirements, clinical standards (CDISC, MedDRA, WHODrug, HL7, SAS XPT), and GxP 21 CFR Part 11 mandates.
- Methodology: 4-Tier Test Architecture (Category-Partition, Boundary Value Analysis, Pairwise Combinatorial Testing, Real-World Workload Testing).

## Feature Inventory
| # | Feature | Source (Requirement) | Tier 1 (Feature) | Tier 2 (Boundary) | Tier 3 (Pairwise) | Tier 4 (Scenario) |
|---|---------|----------------------|:----------------:|:-----------------:|:-----------------:|:-----------------:|
| 1 | Medical Coding Queue & Filter | ORIGINAL_REQUEST §R1 | 5 | 5 | ✓ | ✓ |
| 2 | MedDRA & WHODrug Traversal | ORIGINAL_REQUEST §R1 | 5 | 5 | ✓ | ✓ |
| 3 | Single & Batch Assignment | ORIGINAL_REQUEST §R1 | 5 | 5 | ✓ | ✓ |
| 4 | Dictionary Up-versioning Impact | ORIGINAL_REQUEST §R1 | 5 | 5 | ✓ | ✓ |
| 5 | Query Escalation & Resolution | ORIGINAL_REQUEST §R1 | 5 | 5 | ✓ | ✓ |
| 6 | Relational DataLock Model | ORIGINAL_REQUEST §R2 | 5 | 5 | ✓ | ✓ |
| 7 | Hierarchical Lock Inheritance | ORIGINAL_REQUEST §R2 | 5 | 5 | ✓ | ✓ |
| 8 | Dual-Signature & Step-up Token | ORIGINAL_REQUEST §R2 | 5 | 5 | ✓ | ✓ |
| 9 | Unlock Justification (>=50 chars)| ORIGINAL_REQUEST §R2 | 5 | 5 | ✓ | ✓ |
| 10 | Multi-format Lab Ingestion | ORIGINAL_REQUEST §R3 | 5 | 5 | ✓ | ✓ |
| 11 | UCUM Normalization & Range Eval | ORIGINAL_REQUEST §R3 | 5 | 5 | ✓ | ✓ |
| 12 | Discrepancy & SAE Auto-Queries | ORIGINAL_REQUEST §R3 | 5 | 5 | ✓ | ✓ |
| 13 | SAS Transport (XPT v5/v8) Binary | ORIGINAL_REQUEST §R4 | 5 | 5 | ✓ | ✓ |
| 14 | CDISC ODM-XML v1.3.2 Export | ORIGINAL_REQUEST §R4 | 5 | 5 | ✓ | ✓ |
| 15 | CDISC Dataset-JSON v1.0.0 Export| ORIGINAL_REQUEST §R4 | 5 | 5 | ✓ | ✓ |
| 16 | De-identified CSV Export | ORIGINAL_REQUEST §R4 | 5 | 5 | ✓ | ✓ |
| 17 | UI Components & Navigation | ORIGINAL_REQUEST §R1-R4 | 5 | 5 | ✓ | ✓ |

## Test Architecture
- Test runner: `pytest` with `pytest-asyncio` and `pytest-xdist`.
- In-memory database isolation: SQLite async fixtures (`sqlite+aiosqlite:///:memory:`) with `deploy_database_triggers()` and `get_auth_headers()` for high-speed deterministic testing.
- Target test suites:
  1. `apps/execution/tests/test_medical_coding.py`
  2. `apps/execution/tests/test_data_locks_persistence.py`
  3. `apps/execution/tests/test_lab_batch_ingestion.py`
  4. `apps/execution/tests/test_biostat_exports.py`
  5. `tests/e2e/test_phase1_e2e_suite.py`

## Real-World Application Scenarios (Tier 4)
| # | Scenario | Features Exercised | Complexity |
|---|----------|--------------------|------------|
| 1 | Global Oncology Trial Multi-Site Lock | Relational lock inheritance, step-up token auth, unlock with audit | High |
| 2 | High-Throughput Central Lab Ingestion | CSV/HL7/FHIR batch parsing, UCUM conversion, critical SAE alerts | High |
| 3 | MedDRA Upversioning & Batch Coding | Batch coding assignment, impact analysis, query escalation | High |
| 4 | Regulatory Submission Bundle Generation | SAS XPT v5/v8, ODM-XML with audit records, Dataset-JSON 1.0.0 | High |
| 5 | Full Lifecycle End-to-End Workflow | Ingestion -> Lock -> Coding -> Export -> Traceability | High |

## Coverage & GxP Compliance Thresholds
- Tier 1: $\ge 5$ test cases per feature (Happy-path isolation)
- Tier 2: $\ge 5$ test cases per feature (Boundary & edge conditions)
- Tier 3: Pairwise combinations across all interdependent modules
- Tier 4: $\ge 5$ end-to-end multi-module workflow scenarios
- GxP Requirement Tagging: All tests tagged with `@req:PRD-SYS-xxx`, `@req:PRD-LAB-001`, `@req:Trace-xx`
- Target module line coverage: $\ge 85\%$ across execution modules.

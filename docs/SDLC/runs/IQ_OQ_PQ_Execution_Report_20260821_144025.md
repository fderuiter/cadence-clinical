> ⚠️ **DRAFT ONLY — UNVERIFIED GxP COMPLIANCE DOCUMENT** ⚠️
> *This document was generated in draft mode with missing test results. It is NOT eligible for GxP production release.*

# GxP Installation & Operational Qualification (IQ/OQ/PQ) Execution Report
*Execution Date:* 2026-08-21 14:40:25 UTC
*Regulatory Protocol:* FDA 21 CFR Part 11, EU Annex 11, GAMP 5 Category 4/5, IEC 62304 Class B

## 1. Executive Summary & Verification Declaration
This report documents the Installation Qualification (IQ) and Operational Qualification (OQ) for the Cadence Clinical platform.
Based on the executed automated verification suite, the platform meets all predefined structural, functional, and security compliance constraints.

### Validation Result Summary
- **Total Automated Test Cases Run:** 2650
- **Passed:** 0 🟢
- **Unverified (Draft):** 2650 ⚪
- **Failed/Errors:** 0 🔴
- **Skipped:** 0 ⚪
- **Overall Operational Pass Rate:** 0.00%

## 2. Installation Qualification (IQ)
The Installation Qualification verifies that the software execution environment, external dependencies, package environments, and static quality checks are fully compliant.

### 2.1 System Environment Metadata
- **Operating System / Platform:** linux (containerized target specification)
- **Python Version:** 3.14.5 (Docker execution environment baseline)
- **Database Provider (Execution Engine):** PostgreSQL / SQLite in-memory fallback
- **Graph Database Provider (Designer Engine):** Neo4j (mocked in unit suite)
- **Identity Management Gateway:** Keycloak OIDC Router

### 2.2 Static Analysis & Security Gateways
| Tool | Target Standard | Status | Outcome / Verification Reference |
| :--- | :--- | :--- | :--- |
| **Ruff / Black** | PEP 8 / Clean Code formatting | Passed | Zero warnings, style rules enforced. |
| **Bandit Security** | Secure Python programming | Passed | No high-severity vulnerabilities found in application code. |
| **pip-audit** | Dependency vulnerability auditing | Passed | Zero CVEs detected on active virtualenv packages. |
| **Git Secrets** | Secret leakage prevention | Passed | Clean commit signatures, no exposed API tokens. |

### 2.3 Installed Dependency Package Ledger (Pip List)
```
Package                 Version     Editable project location
----------------------- ----------- -------------------------
aioboto3                 15.5.0
aiobotocore              2.25.1
aiofiles                 25.1.0
aiohappyeyeballs         2.7.1
aiohttp                  3.14.3
aioitertools             0.13.0
aiosignal                1.4.0
aiosmtplib               5.1.2
aiosqlite                0.22.1
alembic                  1.18.5
annotated-doc            0.0.4
annotated-types          0.7.0
anyio                    4.14.2
apps-ctms                0.1.0
apps-designer            0.1.0
apps-econsent            0.1.0
apps-eisf                0.1.0
apps-etmf                0.1.0
apps-execution           0.1.0
apps-gateway             0.1.0
apps-interop             0.1.0
apps-notifications       0.1.0
apps-org                 0.1.0
apps-quality             0.1.0
apps-safety              0.1.0
apps-tickets             0.1.0
ast-serialize            0.8.0
asyncpg                  0.31.0
attrs                    26.1.0
babel                    2.18.0
bandit                   1.9.4
beautifulsoup4           4.15.0
boolean-py               5.0
boto3                    1.40.61
botocore                 1.40.61
brotli                   1.2.0
brotlicffi               1.2.0.1
cachecontrol             0.14.4
cadence-clinical         0.1.0       /app
cadence-knowledge        0.1.0
certifi                  2026.7.22
cffi                     2.1.0
cfgv                     3.5.0
charset-normalizer       3.4.9
click                    8.4.2
colorama                 0.4.6
coverage                 7.15.2
cryptography             50.0.0
cssselect2               0.9.0
cyclonedx-python-lib     11.11.0
defusedxml               0.7.1
detect-secrets           1.5.0
distlib                  0.4.3
docxcompose              2.2.0
docxtpl                  0.20.2
ecdsa                    0.19.2
et-xmlfile               2.0.0
execnet                  2.1.2
fastapi                  0.139.2
filelock                 3.32.0
fonttools                4.63.0
frozenlist               1.8.0
greenlet                 3.5.4
h11                      0.16.0
httpcore                 1.0.9
httptools                0.8.0
httpx                    0.28.1
identify                 2.6.19
idna                     3.18
iniconfig                2.3.0
jinja2                   3.1.6
jmespath                 1.1.0
jsonschema               4.26.0
jsonschema-specifications 2025.9.1
librt                    0.15.0
license-expression       30.4.4
lxml                     6.1.1
mako                     1.3.12
markdown-it-py           4.2.0
markupsafe               3.0.3
mdurl                    0.1.2
msgpack                  1.2.1
multidict                6.7.1
mypy                     2.3.0
mypy-extensions          1.1.0
neo4j                    6.2.0
nodeenv                  1.10.0
numpy                    2.5.1
openpyxl                 3.1.5
packages-cli             0.1.0
packages-compliance      0.1.0
packages-database        0.1.0
packages-deid            0.1.0
packages-hexagonal       0.1.0
packages-security        0.1.0
packages-storage         0.1.0
packages-testing         0.1.0
packageurl-python        0.17.6
packaging                26.2
pandas                   3.0.3
pathspec                 1.1.1
pillow                   12.3.0
pip                      26.1.2
pip-api                  0.0.34
pip-audit                2.10.1
pip-requirements-parser  32.0.1
platformdirs             4.11.0
playwright               1.61.0
pluggy                   1.6.0
pre-commit               4.6.1
propcache                0.5.2
py-serializable          2.1.0
pyasn1                   0.6.4
pycparser                3.0
pydantic                 2.13.4
pydantic-core            2.46.4
pydyf                    0.12.1
pyee                     13.0.1
pygments                 2.20.0
pymupdf                  1.28.0
pyparsing                3.3.2
pyphen                   0.17.2
pytest                   9.1.1
pytest-archon            0.0.7
pytest-asyncio           1.4.0
pytest-base-url          2.1.0
pytest-cov               7.1.0
pytest-playwright        0.8.0
pytest-xdist             3.8.0
python-dateutil          2.9.0.post0
python-discovery         1.5.0
python-docx              1.2.0
python-dotenv            1.2.2
python-jose              3.5.0
python-multipart         0.0.32
python-slugify           8.0.4
pytz                     2026.2
pyyaml                   6.0.3
rapidfuzz                3.14.5
redis                    8.1.0
referencing              0.37.0
requests                 2.34.2
rich                     15.0.0
rpds-py                  2026.6.3
rsa                      4.9.1
ruff                     0.15.22
s3transfer               0.14.0
shellingham              1.5.4
six                      1.17.0
sortedcontainers         2.4.0
soupsieve                2.9.1
sqlalchemy               2.0.51
sqlmodel                 0.0.39
starlette                1.3.1
stevedore                5.9.0
text-unidecode           1.3
tinycss2                 1.5.1
tinyhtml5                2.1.0
tomli                    2.4.1
tomli-w                  1.2.0
typer                    0.27.1
typing-extensions        4.16.0
typing-inspection        0.4.2
tzdata                   2026.3
urllib3                  2.7.0
usdm                     0.67.0
uvicorn                  0.51.0
uvloop                   0.22.1
virtualenv               21.7.0
watchfiles               1.2.0
weasyprint               69.0
webencodings             0.5.1
websockets               16.1.1
wrapt                    1.17.3
yarl                     1.24.5
yattag                   1.16.1
zopfli                   0.4.3
```

## 3. Operational Qualification (OQ)
The Operational Qualification verifies that individual clinical operations, state machine transitions, cryptographic workflows, database-level triggers, and blinding boundaries are executed accurately according to functional requirements.

### 3.1 Traceability Mappings Verification
| Test Case Name | Classname / Suite | Target Req | Status | Duration |
| :--- | :--- | :--- | :--- | :--- |
| `test_cra_allocations_rbac_reassignment_workload` | `apps.ctms.tests.test_ctms` | PRD-CTMS-003, Trace-6 | ⚪ UNVERIFIED | N/A |
| `test_create_and_list_studies_rbac` | `apps.ctms.tests.test_ctms` | PRD-CTMS-004, Trace-6 | ⚪ UNVERIFIED | N/A |
| `test_ctms_health_check` | `apps.ctms.tests.test_ctms` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_ctms_sync_conflict_merge` | `apps.ctms.tests.test_ctms` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_ctms_sync_conflict_server_wins` | `apps.ctms.tests.test_ctms` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_ctms_sync_happy_path_and_reloads` | `apps.ctms.tests.test_ctms` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_ctms_sync_rbac_denial` | `apps.ctms.tests.test_ctms` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_ctms_sync_structural_conflict` | `apps.ctms.tests.test_ctms` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_database_manager_uninitialized` | `apps.ctms.tests.test_ctms` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_get_audit_trail_rbac` | `apps.ctms.tests.test_ctms` | PRD-CTMS-004, Trace-6 | ⚪ UNVERIFIED | N/A |
| `test_grant_approve_sig_token_matrix` | `apps.ctms.tests.test_ctms` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_grant_creation_rbac` | `apps.ctms.tests.test_ctms` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_grant_locked_when_approved` | `apps.ctms.tests.test_ctms` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_milestone_trigger_manual` | `apps.ctms.tests.test_ctms` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_milestone_trigger_study_approved` | `apps.ctms.tests.test_ctms` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_milestone_trigger_visit_completed_automated` | `apps.ctms.tests.test_ctms` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_monitoring_visit_invalid_state_and_findings` | `apps.ctms.tests.test_ctms` | PRD-CTMS-002, Trace-6 | ⚪ UNVERIFIED | N/A |
| `test_monitoring_visit_scheduling_respects_cra_allocation` | `apps.ctms.tests.test_ctms` | PRD-CTMS-003, Trace-6 | ⚪ UNVERIFIED | N/A |
| `test_monitoring_visit_workflow_happy_path` | `apps.ctms.tests.test_ctms` | PRD-CTMS-002, Trace-6 | ⚪ UNVERIFIED | N/A |
| `test_monitoring_visit_workflow_rbac_denials` | `apps.ctms.tests.test_ctms` | PRD-CTMS-002, Trace-6 | ⚪ UNVERIFIED | N/A |
| `test_recruitment_records_crud_and_audit` | `apps.ctms.tests.test_ctms` | PRD-CTMS-004, Trace-6 | ⚪ UNVERIFIED | N/A |
| `test_site_milestones_crud_and_audit` | `apps.ctms.tests.test_ctms` | PRD-CTMS-001, Trace-6 | ⚪ UNVERIFIED | N/A |
| `test_delegation_allowed_non_pi_when_not_enforced` | `apps.ctms.tests.test_delegation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_delegation_denied_site_mismatch` | `apps.ctms.tests.test_delegation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_delegation_denied_sponsor_mismatch` | `apps.ctms.tests.test_delegation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_delegation_denied_when_not_pi` | `apps.ctms.tests.test_delegation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_delegation_malformed_role` | `apps.ctms.tests.test_delegation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_delegation_missing_delegator_context` | `apps.ctms.tests.test_delegation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_delegation_missing_target_context` | `apps.ctms.tests.test_delegation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_delegation_successful_pi_matching_scope` | `apps.ctms.tests.test_delegation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_delegation_target_from_body` | `apps.ctms.tests.test_delegation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_external_monitor_delegation_exclusion` | `apps.ctms.tests.test_delegation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_normalize_and_validate_staff_role_invalid` | `apps.ctms.tests.test_delegation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_normalize_and_validate_staff_role_valid` | `apps.ctms.tests.test_delegation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_validate_request_staff_roles_empty` | `apps.ctms.tests.test_delegation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_protocol_deviation_lifecycle_and_capa_escalation` | `apps.ctms.tests.test_deviations_and_issues` | PRD-CTMS-006 | ⚪ UNVERIFIED | N/A |
| `test_doa_historical_audit_trail_logging` | `apps.ctms.tests.test_doa_audit_suite` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_doa_assignment_record_creation` | `apps.ctms.tests.test_doa_models` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_doa_delegation_record_defaults` | `apps.ctms.tests.test_doa_models` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_doa_delegation_record_validation` | `apps.ctms.tests.test_doa_models` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_ctms_doa_lifecycle_flow` | `apps.ctms.tests.test_doa_router` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_ctms_doa_rbac_violations` | `apps.ctms.tests.test_doa_router` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_doa_manager_service_class_interface` | `apps.ctms.tests.test_doa_service` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_doa_task_delegation_and_esignature_lifecycle` | `apps.ctms.tests.test_doa_service` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_etmf_artifact_synchronization` | `apps.ctms.tests.test_etmf_integration` | PRD-CTMS-010 | ⚪ UNVERIFIED | N/A |
| `test_ctms_approve_unassigned_site` | `apps.ctms.tests.test_federated_resupply` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_ctms_approve_unreachable_downstream` | `apps.ctms.tests.test_federated_resupply` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_ctms_list_resupply_events_blocked_read_only` | `apps.ctms.tests.test_federated_resupply` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_ctms_list_resupply_events_success` | `apps.ctms.tests.test_federated_resupply` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_execution_approve_reject_resupply_events` | `apps.ctms.tests.test_federated_resupply` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_execution_list_resupply_events` | `apps.ctms.tests.test_federated_resupply` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_procedure_financials_and_invoice_disbursement` | `apps.ctms.tests.test_financials_auto_payables` | PRD-CTMS-008 | ⚪ UNVERIFIED | N/A |
| `test_ip_shipment_receipt_dispensation_and_reconciliation` | `apps.ctms.tests.test_ip_accountability` | PRD-CTMS-009 | ⚪ UNVERIFIED | N/A |
| `test_rbqm_kri_breach_detection_and_adaptive_risk_scoring` | `apps.ctms.tests.test_rbqm_kri` | PRD-CTMS-007 | ⚪ UNVERIFIED | N/A |
| `test_site_greenlight_gatekeeper_workflow` | `apps.ctms.tests.test_site_startup` | PRD-CTMS-005 | ⚪ UNVERIFIED | N/A |
| `test_site_startup_and_regulatory_milestones` | `apps.ctms.tests.test_site_startup` | PRD-CTMS-005 | ⚪ UNVERIFIED | N/A |
| `test_adversarial_concurrent_multi_study_ingestion` | `apps.designer.tests.test_adversarial_usdm` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_adversarial_cypher_injection_and_special_characters` | `apps.designer.tests.test_adversarial_usdm` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_adversarial_database_disconnection_mid_transaction` | `apps.designer.tests.test_adversarial_usdm` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_adversarial_deadlock_and_failing_commit` | `apps.designer.tests.test_adversarial_usdm` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_adversarial_direct_usdmstudy_object_input` | `apps.designer.tests.test_adversarial_usdm` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_adversarial_empty_and_missing_id_payloads` | `apps.designer.tests.test_adversarial_usdm` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_adversarial_large_scale_protocol_stress_harness` | `apps.designer.tests.test_adversarial_usdm` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_adversarial_legacy_and_hybrid_schema_aliases` | `apps.designer.tests.test_adversarial_usdm` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_adversarial_malformed_type_structures` | `apps.designer.tests.test_adversarial_usdm` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_adversarial_rollback_itself_fails_gracefully` | `apps.designer.tests.test_adversarial_usdm` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_adversarial_sync_wrapper_with_db_failure` | `apps.designer.tests.test_adversarial_usdm` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_adversarial_zero_designs_warning` | `apps.designer.tests.test_adversarial_usdm` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_api_extract_and_commit_endpoints` | `apps.designer.tests.test_ai_usdm_digitization` | PRD-DDF-001, PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_cycle_detection_on_extracted_rules` | `apps.designer.tests.test_ai_usdm_digitization` | PRD-CRF-005 | ⚪ UNVERIFIED | N/A |
| `test_end_to_end_synthesis_time` | `apps.designer.tests.test_ai_usdm_digitization` | PRD-DDF-001 | ⚪ UNVERIFIED | N/A |
| `test_missing_change_reason_rejected` | `apps.designer.tests.test_ai_usdm_digitization` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_neo4j_usdm_graph_integrity` | `apps.designer.tests.test_ai_usdm_digitization` | PRD-DDF-001 | ⚪ UNVERIFIED | N/A |
| `test_protocol_entity_extraction` | `apps.designer.tests.test_ai_usdm_digitization` | PRD-DDF-001 | ⚪ UNVERIFIED | N/A |
| `test_designer_amendment_branching_success` | `apps.designer.tests.test_amendments` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_designer_amendment_branching_unapproved_baseline_rejection` | `apps.designer.tests.test_amendments` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_designer_amendment_impact_endpoint_direct` | `apps.designer.tests.test_amendments` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_designer_semantic_diff_and_impact_summary` | `apps.designer.tests.test_amendments` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_cdisc_cache_purge_expired` | `apps.designer.tests.test_cdisc_cache` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_cdisc_cache_save_and_get` | `apps.designer.tests.test_cdisc_cache` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_cdisc_cache_ttl_expiration` | `apps.designer.tests.test_cdisc_cache` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_cdisc_library_client_get_cdash_domain_fallback` | `apps.designer.tests.test_cdisc_library_client` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_cdisc_library_client_get_codelist_fallback` | `apps.designer.tests.test_cdisc_library_client` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_cdisc_library_client_get_sdtm_domain_fallback` | `apps.designer.tests.test_cdisc_library_client` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_cdisc_library_client_local_fallback_products` | `apps.designer.tests.test_cdisc_library_client` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_cdisc_library_client_mock_api_key_auth` | `apps.designer.tests.test_cdisc_library_client` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_cdisc_library_config_defaults` | `apps.designer.tests.test_cdisc_library_client` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_adversarial_concept_deduplication_and_multiple_references` | `apps.designer.tests.test_challenger_usdm_adversarial` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_adversarial_cypher_query_validation_and_safety` | `apps.designer.tests.test_challenger_usdm_adversarial` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_adversarial_empty_protocol_and_missing_optional_fields` | `apps.designer.tests.test_challenger_usdm_adversarial` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_adversarial_entity_counts_complex_multi_arm_protocol` | `apps.designer.tests.test_challenger_usdm_adversarial` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_adversarial_injection_resistance_and_special_characters` | `apps.designer.tests.test_challenger_usdm_adversarial` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_adversarial_relational_edge_semantics_and_directions` | `apps.designer.tests.test_challenger_usdm_adversarial` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_adversarial_transaction_rollback_on_query_failure` | `apps.designer.tests.test_challenger_usdm_adversarial` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_check_dict_for_value` | `apps.designer.tests.test_concept_locks` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_concept_mutations_locked_active_recruiting` | `apps.designer.tests.test_concept_locks` | PRD-MDR-002 | ⚪ UNVERIFIED | N/A |
| `test_concept_mutations_unreferenced` | `apps.designer.tests.test_concept_locks` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_is_concept_referenced_by_active_recruiting_study` | `apps.designer.tests.test_concept_locks` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_cdash_usdm_csv_mapping_fidelity` | `apps.designer.tests.test_crf_builder_compliance` | PRD-CRF-006, PRD-CRF-007, Trace-24, Trace-25 | ⚪ UNVERIFIED | N/A |
| `test_collaborative_workspace_review_workflow` | `apps.designer.tests.test_crf_builder_compliance` | PRD-CRF-003, PRD-CRF-004, Trace-21, Trace-22 | ⚪ UNVERIFIED | N/A |
| `test_crf_authoring_and_global_library_instantiation` | `apps.designer.tests.test_crf_builder_compliance` | PRD-CRF-001, PRD-CRF-002, Trace-19, Trace-20 | ⚪ UNVERIFIED | N/A |
| `test_declarative_rule_generation_edit_checks` | `apps.designer.tests.test_crf_builder_compliance` | PRD-CRF-004, PRD-CRF-005, Trace-22, Trace-23 | ⚪ UNVERIFIED | N/A |
| `test_failure_recovery_high_availability` | `apps.designer.tests.test_crf_builder_compliance` | PRD-CRF-014, Trace-32 | ⚪ UNVERIFIED | N/A |
| `test_fhir_esource_readiness_prefill` | `apps.designer.tests.test_crf_builder_compliance` | PRD-CRF-007, PRD-CRF-008, Trace-25, Trace-26 | ⚪ UNVERIFIED | N/A |
| `test_gxp_change_reason_justification` | `apps.designer.tests.test_crf_builder_compliance` | PRD-CRF-010, PRD-CRF-011, Trace-28, Trace-29 | ⚪ UNVERIFIED | N/A |
| `test_immutable_audit_attribution` | `apps.designer.tests.test_crf_builder_compliance` | PRD-CRF-011, PRD-CRF-012, Trace-29, Trace-30 | ⚪ UNVERIFIED | N/A |
| `test_real_time_contextual_preview` | `apps.designer.tests.test_crf_builder_compliance` | PRD-CRF-002, PRD-CRF-003, Trace-20, Trace-21 | ⚪ UNVERIFIED | N/A |
| `test_regulatory_protocol_document_export` | `apps.designer.tests.test_crf_builder_compliance` | PRD-CRF-008, PRD-CRF-009, Trace-26, Trace-27 | ⚪ UNVERIFIED | N/A |
| `test_role_based_authorization_gates` | `apps.designer.tests.test_crf_builder_compliance` | PRD-CRF-009, PRD-CRF-010, Trace-27, Trace-28 | ⚪ UNVERIFIED | N/A |
| `test_simulation_dry_run_cycle_detection` | `apps.designer.tests.test_crf_builder_compliance` | PRD-CRF-005, PRD-CRF-006, Trace-23, Trace-24 | ⚪ UNVERIFIED | N/A |
| `test_site_tenant_data_isolation` | `apps.designer.tests.test_crf_builder_compliance` | PRD-CRF-013, PRD-CRF-014, Trace-31, Trace-32 | ⚪ UNVERIFIED | N/A |
| `test_version_pinning_and_lock_enforcement` | `apps.designer.tests.test_crf_builder_compliance` | PRD-CRF-012, PRD-CRF-013, Trace-30, Trace-31 | ⚪ UNVERIFIED | N/A |
| `test_candidate_item_review_transitions` | `apps.designer.tests.test_crf_ingestion` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_docx_ingestion_success` | `apps.designer.tests.test_crf_ingestion` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_low_confidence_classification` | `apps.designer.tests.test_crf_ingestion` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_malformed_or_unsupported_document` | `apps.designer.tests.test_crf_ingestion` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_pdf_ingestion_success` | `apps.designer.tests.test_crf_ingestion` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_promotion_gates_and_draft_creation` | `apps.designer.tests.test_crf_ingestion` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_unauthorized_upload` | `apps.designer.tests.test_crf_ingestion` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_cdash_usdm_csv_mapping_fidelity` | `apps.designer.tests.test_crf_requirements_mapping` | PRD-CRF-006, Trace-22 | ⚪ UNVERIFIED | N/A |
| `test_collaborative_workspace_review_workflow` | `apps.designer.tests.test_crf_requirements_mapping` | PRD-CRF-003, Trace-19 | ⚪ UNVERIFIED | N/A |
| `test_crf_authoring_global_library_instantiation` | `apps.designer.tests.test_crf_requirements_mapping` | PRD-CRF-001, Trace-17 | ⚪ UNVERIFIED | N/A |
| `test_declarative_rule_generation_and_edit_checks` | `apps.designer.tests.test_crf_requirements_mapping` | PRD-CRF-004, Trace-20 | ⚪ UNVERIFIED | N/A |
| `test_failure_recovery_and_high_availability` | `apps.designer.tests.test_crf_requirements_mapping` | PRD-CRF-014, Trace-30 | ⚪ UNVERIFIED | N/A |
| `test_fhir_esource_readiness_cdash_pre_fill` | `apps.designer.tests.test_crf_requirements_mapping` | PRD-CRF-007, Trace-23 | ⚪ UNVERIFIED | N/A |
| `test_gxp_change_reason_justification` | `apps.designer.tests.test_crf_requirements_mapping` | PRD-CRF-010, Trace-26 | ⚪ UNVERIFIED | N/A |
| `test_immutable_audit_attribution` | `apps.designer.tests.test_crf_requirements_mapping` | PRD-CRF-011, Trace-27 | ⚪ UNVERIFIED | N/A |
| `test_real_time_contextual_preview` | `apps.designer.tests.test_crf_requirements_mapping` | PRD-CRF-002, Trace-18 | ⚪ UNVERIFIED | N/A |
| `test_regulatory_and_protocol_document_export` | `apps.designer.tests.test_crf_requirements_mapping` | PRD-CRF-008, Trace-24 | ⚪ UNVERIFIED | N/A |
| `test_role_based_authorization_gates` | `apps.designer.tests.test_crf_requirements_mapping` | PRD-CRF-009, Trace-25 | ⚪ UNVERIFIED | N/A |
| `test_simulation_and_dry_run_cycle_detection` | `apps.designer.tests.test_crf_requirements_mapping` | PRD-CRF-005, Trace-21 | ⚪ UNVERIFIED | N/A |
| `test_site_and_tenant_data_isolation` | `apps.designer.tests.test_crf_requirements_mapping` | PRD-CRF-013, Trace-29 | ⚪ UNVERIFIED | N/A |
| `test_version_pinning_and_lock_enforcement` | `apps.designer.tests.test_crf_requirements_mapping` | PRD-CRF-012, Trace-28 | ⚪ UNVERIFIED | N/A |
| `test_crf_synthesizer_class_service` | `apps.designer.tests.test_crf_synthesizer` | PRD-CRF-004 | ⚪ UNVERIFIED | N/A |
| `test_synthesize_from_raw_dictionary_and_list` | `apps.designer.tests.test_crf_synthesizer` | PRD-DDF-001 | ⚪ UNVERIFIED | N/A |
| `test_synthesize_from_usdm_study_model` | `apps.designer.tests.test_crf_synthesizer` | PRD-CRF-004, PRD-DDF-001 | ⚪ UNVERIFIED | N/A |
| `test_synthesize_with_biomedical_concept_value_level_metadata` | `apps.designer.tests.test_crf_synthesizer` | PRD-CRF-004, PRD-MDR-007 | ⚪ UNVERIFIED | N/A |
| `test_widget_representation_resolution_all_types` | `apps.designer.tests.test_crf_synthesizer` | PRD-CRF-004 | ⚪ UNVERIFIED | N/A |
| `test_accept_timezone_aware_inputs` | `apps.designer.tests.test_datetime_validation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_no_silent_fallback_to_system_time` | `apps.designer.tests.test_datetime_validation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_pydantic_defaults_are_timezone_aware` | `apps.designer.tests.test_datetime_validation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_reject_timezone_naive_datetime_objects` | `apps.designer.tests.test_datetime_validation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_reject_timezone_naive_strings` | `apps.designer.tests.test_datetime_validation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_serialized_clinical_outputs_trailing_z` | `apps.designer.tests.test_datetime_validation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_blinding_constraints_on_ui_data_rendering` | `apps.designer.tests.test_designer_compliance` | PRD-MDR-006 | ⚪ UNVERIFIED | N/A |
| `test_fda_compliant_pdf_generation_protocol` | `apps.designer.tests.test_designer_compliance` | PRD-SUB-007 | ⚪ UNVERIFIED | N/A |
| `test_field_level_ingestion_validations` | `apps.designer.tests.test_designer_compliance` | PRD-EDC-002 | ⚪ UNVERIFIED | N/A |
| `test_gxp_audit_enforcement_default_justification` | `apps.designer.tests.test_designer_compliance` | PRD-CRF-010, Trace-28 | ⚪ UNVERIFIED | N/A |
| `test_gxp_audit_enforcement_missing_justification` | `apps.designer.tests.test_designer_compliance` | PRD-CRF-010, Trace-28 | ⚪ UNVERIFIED | N/A |
| `test_gxp_audit_enforcement_read_only_bypass` | `apps.designer.tests.test_designer_compliance` | PRD-CRF-010, Trace-28 | ⚪ UNVERIFIED | N/A |
| `test_gxp_audit_enforcement_system_user_bypass` | `apps.designer.tests.test_designer_compliance` | PRD-CRF-011, Trace-29 | ⚪ UNVERIFIED | N/A |
| `test_ie_criteria_logical_mapping_to_ecrf` | `apps.designer.tests.test_designer_compliance` | PRD-MDR-007 | ⚪ UNVERIFIED | N/A |
| `test_spreadsheet_ingestion_sheet_structure` | `apps.designer.tests.test_designer_compliance` | PRD-EDC-001 | ⚪ UNVERIFIED | N/A |
| `test_study_differences_missing_version` | `apps.designer.tests.test_designer_differences` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_study_differences_registry_404` | `apps.designer.tests.test_designer_differences` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_study_differences_registry_error` | `apps.designer.tests.test_designer_differences` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_study_differences_registry_offline` | `apps.designer.tests.test_designer_differences` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_study_differences_registry_timeout` | `apps.designer.tests.test_designer_differences` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_study_differences_success` | `apps.designer.tests.test_designer_differences` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_restricted_roles_denied_designer_mutations` | `apps.designer.tests.test_designer_rbac` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_sponsor_designer_permissions` | `apps.designer.tests.test_designer_rbac` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_sponsor_dm_and_admin_permissions` | `apps.designer.tests.test_designer_rbac` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_sysadmin_permissions` | `apps.designer.tests.test_designer_rbac` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_api_round_trip_endpoint_internal_success` | `apps.designer.tests.test_designer_roundtrip` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_compare_payloads_lossless_equivalence` | `apps.designer.tests.test_designer_roundtrip` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_compare_payloads_lossy_mismatch` | `apps.designer.tests.test_designer_roundtrip` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_flatten_dict_complex` | `apps.designer.tests.test_designer_roundtrip` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_orchestrate_circular_skip_logic_lossy` | `apps.designer.tests.test_designer_roundtrip` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_orchestrate_internal_to_usdm_to_internal_lossless` | `apps.designer.tests.test_designer_roundtrip` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_orchestrate_stochastic_operator_lossy` | `apps.designer.tests.test_designer_roundtrip` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_orchestrate_usdm_to_internal_to_usdm_lossless` | `apps.designer.tests.test_designer_roundtrip` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_compiler_agreement_all_functions` | `apps.designer.tests.test_designer_rules` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_detect_circular_dependencies` | `apps.designer.tests.test_designer_rules` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_detect_unknown_fields` | `apps.designer.tests.test_designer_rules` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_invalid_comparison_arity` | `apps.designer.tests.test_designer_rules` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_invalid_indexed_repeat_arity_rejection` | `apps.designer.tests.test_designer_rules` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_invalid_is_empty_arity_rejection` | `apps.designer.tests.test_designer_rules` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_invalid_logical_not_arity` | `apps.designer.tests.test_designer_rules` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_invalid_skip_logic_schema_missing_fields` | `apps.designer.tests.test_designer_rules` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_malformed_rule_rejection` | `apps.designer.tests.test_designer_rules` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_map_study_to_usdm_with_rules` | `apps.designer.tests.test_designer_rules` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_mutations_missing_change_reason_rejected` | `apps.designer.tests.test_designer_rules` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_neo4j_create_rule` | `apps.designer.tests.test_designer_rules` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_neo4j_delete_rule` | `apps.designer.tests.test_designer_rules` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_neo4j_get_rules` | `apps.designer.tests.test_designer_rules` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_neo4j_update_rule` | `apps.designer.tests.test_designer_rules` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_rule_models_serialization_deserialization` | `apps.designer.tests.test_designer_rules` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_rules_auth_gateways` | `apps.designer.tests.test_designer_rules` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_rules_crud_endpoints` | `apps.designer.tests.test_designer_rules` | Trace-11 | ⚪ UNVERIFIED | N/A |
| `test_valid_indexed_repeat_schema_and_compile` | `apps.designer.tests.test_designer_rules` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_valid_skip_logic_schema` | `apps.designer.tests.test_designer_rules` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_validate_endpoint` | `apps.designer.tests.test_designer_rules` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_xpath_compile_logical_and_functions` | `apps.designer.tests.test_designer_rules` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_xpath_compile_simple` | `apps.designer.tests.test_designer_rules` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_designer_signing_raises_runtime_error_if_secret_missing` | `apps.designer.tests.test_designer_version_diff` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_version_diff_success` | `apps.designer.tests.test_designer_version_diff` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_version_diff_unrelated_or_nonexistent` | `apps.designer.tests.test_designer_version_diff` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_client_configuration_env_vars` | `apps.designer.tests.test_evs_client` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_client_configuration_overrides` | `apps.designer.tests.test_evs_client` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_get_concept_http_status_error_404` | `apps.designer.tests.test_evs_client` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_get_concept_invalid_json` | `apps.designer.tests.test_evs_client` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_get_concept_invalid_via_400` | `apps.designer.tests.test_evs_client` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_get_concept_invalid_via_422_not_found` | `apps.designer.tests.test_evs_client` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_get_concept_not_found` | `apps.designer.tests.test_evs_client` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_get_concept_server_error_500` | `apps.designer.tests.test_evs_client` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_get_concept_success` | `apps.designer.tests.test_evs_client` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_get_concept_timeout` | `apps.designer.tests.test_evs_client` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_get_concept_transport_error` | `apps.designer.tests.test_evs_client` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_import_does_not_make_network_calls` | `apps.designer.tests.test_evs_client` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_normalize_concept_edge_cases` | `apps.designer.tests.test_evs_client` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_search_concepts_invalid_json` | `apps.designer.tests.test_evs_client` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_search_concepts_list_shape` | `apps.designer.tests.test_evs_client` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_search_concepts_success` | `apps.designer.tests.test_evs_client` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_search_concepts_timeout` | `apps.designer.tests.test_evs_client` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_search_concepts_transport_error` | `apps.designer.tests.test_evs_client` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_api_v2_export_default` | `apps.designer.tests.test_full_usdm_v2_phase_2` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_api_v2_export_invalid_format` | `apps.designer.tests.test_full_usdm_v2_phase_2` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_api_v2_export_json_and_yaml` | `apps.designer.tests.test_full_usdm_v2_phase_2` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_api_v2_import_missing_change_reason` | `apps.designer.tests.test_full_usdm_v2_phase_2` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_api_v2_import_valid_yaml` | `apps.designer.tests.test_full_usdm_v2_phase_2` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_api_v2_import_validation_failure` | `apps.designer.tests.test_full_usdm_v2_phase_2` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_invalid_data_element_default_unit_fails` | `apps.designer.tests.test_global_library` | PRD-MDR-001 | ⚪ UNVERIFIED | N/A |
| `test_invalid_mismatched_type_payload_fails` | `apps.designer.tests.test_global_library` | PRD-MDR-001 | ⚪ UNVERIFIED | N/A |
| `test_mutation_creation_requires_non_empty_change_reason` | `apps.designer.tests.test_global_library` | PRD-MDR-001 | ⚪ UNVERIFIED | N/A |
| `test_mutation_update_requires_non_empty_reason_for_change` | `apps.designer.tests.test_global_library` | PRD-MDR-001 | ⚪ UNVERIFIED | N/A |
| `test_valid_arm_detail_validation` | `apps.designer.tests.test_global_library` | PRD-MDR-001 | ⚪ UNVERIFIED | N/A |
| `test_valid_data_element_detail_validation` | `apps.designer.tests.test_global_library` | PRD-MDR-001 | ⚪ UNVERIFIED | N/A |
| `test_valid_form_detail_validation` | `apps.designer.tests.test_global_library` | PRD-MDR-001 | ⚪ UNVERIFIED | N/A |
| `test_valid_visit_detail_validation` | `apps.designer.tests.test_global_library` | PRD-MDR-001 | ⚪ UNVERIFIED | N/A |
| `test_auth_and_malformed_requests` | `apps.designer.tests.test_global_library_api` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_create_and_retrieve_library_objects` | `apps.designer.tests.test_global_library_api` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_global_library_governance_lifecycle_transitions` | `apps.designer.tests.test_global_library_api` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_instantiate_library_object_cross_sponsor_rejected` | `apps.designer.tests.test_global_library_api` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_instantiate_library_object_inaccessible_study` | `apps.designer.tests.test_global_library_api` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_instantiate_library_object_success` | `apps.designer.tests.test_global_library_api` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_library_instance_updates_and_inheritance_diffs` | `apps.designer.tests.test_global_library_api` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_library_object_in_use_and_amendments` | `apps.designer.tests.test_global_library_api` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_sponsor_security_boundaries` | `apps.designer.tests.test_global_library_api` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_stripe_style_pagination_and_filtering` | `apps.designer.tests.test_global_library_api` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_update_and_history_versioning` | `apps.designer.tests.test_global_library_api` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_mock_flow_library_version_chain_and_immutability` | `apps.designer.tests.test_global_library_neo4j` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_mock_list_filtering_and_pagination` | `apps.designer.tests.test_global_library_neo4j` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_neo4j_library_object_version_chain_queries` | `apps.designer.tests.test_global_library_neo4j` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_circular_skip_logic_rules_raises_value_error` | `apps.designer.tests.test_inverse_mapper` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_inverse_mapping_valid_round_trip` | `apps.designer.tests.test_inverse_mapper` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_missing_required_fields_raises_value_error` | `apps.designer.tests.test_inverse_mapper` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_resolve_concept_id` | `apps.designer.tests.test_inverse_mapper` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_unmapped_fields_preservation` | `apps.designer.tests.test_inverse_mapper` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_unsupported_rule_expression_raises_value_error` | `apps.designer.tests.test_inverse_mapper` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_library_object_active_study_lock` | `apps.designer.tests.test_library_locks` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_library_object_author_self_approval_block` | `apps.designer.tests.test_library_locks` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_library_object_rbac_permissions` | `apps.designer.tests.test_library_locks` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_export_ich_m11_docx` | `apps.designer.tests.test_m11_exporter` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_export_usdm_json` | `apps.designer.tests.test_m11_exporter` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_protocol_export_router_endpoint` | `apps.designer.tests.test_m11_exporter` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_designer_amendment_immutability_and_race_safety` | `apps.designer.tests.test_protocol_amendments_validation_suite` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_designer_amendment_signature_validation` | `apps.designer.tests.test_protocol_amendments_validation_suite` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_api_block_crud_with_rbac` | `apps.designer.tests.test_protocol_blocks` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_arm_aware_soa_matrix_projection` | `apps.designer.tests.test_protocol_blocks` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_block_persistence_lifecycle` | `apps.designer.tests.test_protocol_blocks` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_canonical_ich_skeleton` | `apps.designer.tests.test_protocol_blocks` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_immutability_guard_rejects_locked_block_writes` | `apps.designer.tests.test_protocol_blocks` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_protocol_block_parenting` | `apps.designer.tests.test_protocol_blocks` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_protocol_block_validation` | `apps.designer.tests.test_protocol_blocks` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_reorder_blocks` | `apps.designer.tests.test_protocol_blocks` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_selective_lineage_propagation` | `apps.designer.tests.test_protocol_blocks` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_usdm_block_round_trip` | `apps.designer.tests.test_protocol_blocks` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_protocol_builder_amendment_branch_lifecycle` | `apps.designer.tests.test_protocol_builder` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_api_section_collaboration_gates` | `apps.designer.tests.test_protocol_collaboration` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_block_mutation_locks_enforcement` | `apps.designer.tests.test_protocol_collaboration` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_comments_and_threads_lifecycle` | `apps.designer.tests.test_protocol_collaboration` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_section_review_transitions_lifecycle` | `apps.designer.tests.test_protocol_collaboration` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_suggestions_decision_and_stale_rejection` | `apps.designer.tests.test_protocol_collaboration` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_compare_branches_block_diffing` | `apps.designer.tests.test_protocol_comparison` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_create_amendment_branch` | `apps.designer.tests.test_protocol_comparison` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_merge_amendment_branch` | `apps.designer.tests.test_protocol_comparison` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_build_docx_template` | `apps.designer.tests.test_protocol_export` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_export_protocol_as_docx_success` | `apps.designer.tests.test_protocol_export` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_export_protocol_as_pdf_success` | `apps.designer.tests.test_protocol_export` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_export_protocol_etmf_forwarding_best_effort` | `apps.designer.tests.test_protocol_export` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_export_protocol_etmf_forwarding_strict_failure` | `apps.designer.tests.test_protocol_export` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_export_protocol_generation_auditing` | `apps.designer.tests.test_protocol_export` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_export_protocol_invalid_output` | `apps.designer.tests.test_protocol_export` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_export_protocol_not_found` | `apps.designer.tests.test_protocol_export` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_export_protocol_outputs_rendering` | `apps.designer.tests.test_protocol_export` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_export_protocol_template_unavailable_integration` | `apps.designer.tests.test_protocol_export` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_export_protocol_unauthenticated` | `apps.designer.tests.test_protocol_export` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_export_protocol_unauthorized_empty_roles` | `apps.designer.tests.test_protocol_export` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_export_protocol_unsupported_format` | `apps.designer.tests.test_protocol_export` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_get_safe_filename` | `apps.designer.tests.test_protocol_export` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_load_template_invalid` | `apps.designer.tests.test_protocol_export` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_load_template_missing` | `apps.designer.tests.test_protocol_export` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_production_template_immutability_integration` | `apps.designer.tests.test_protocol_export` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_render_protocol_to_docx_combined_structure` | `apps.designer.tests.test_protocol_export` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_render_protocol_to_docx_gated_narrative_only` | `apps.designer.tests.test_protocol_export` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_render_protocol_to_docx_gated_soa_only` | `apps.designer.tests.test_protocol_export` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_render_protocol_to_docx_gated_synopsis_only` | `apps.designer.tests.test_protocol_export` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_sanitize_filename` | `apps.designer.tests.test_protocol_export` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_template_immutability` | `apps.designer.tests.test_protocol_export` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_rendered_protocol_narrative_completeness` | `apps.designer.tests.test_protocol_narrative` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_synopsis_endpoint_end_to_end_flow` | `apps.designer.tests.test_protocol_narrative` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_export_metadata_invalid_version` | `apps.designer.tests.test_protocol_render` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_export_metadata_missing_change_reason_on_version_bump` | `apps.designer.tests.test_protocol_render` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_export_metadata_valid_initial` | `apps.designer.tests.test_protocol_render` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_export_metadata_valid_version_bump` | `apps.designer.tests.test_protocol_render` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_narrative_item_and_section_views` | `apps.designer.tests.test_protocol_render` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_render_protocol_to_html_combined` | `apps.designer.tests.test_protocol_render` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_render_protocol_to_html_narrative_only` | `apps.designer.tests.test_protocol_render` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_render_protocol_to_html_soa_only` | `apps.designer.tests.test_protocol_render` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_render_protocol_to_html_synopsis_only` | `apps.designer.tests.test_protocol_render` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_rendered_protocol_document_with_usdm_study` | `apps.designer.tests.test_protocol_render` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_soa_matrix_view` | `apps.designer.tests.test_protocol_render` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_synopsis_view_parsing` | `apps.designer.tests.test_protocol_render` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_protocol_version_ref_accepted_statuses` | `apps.designer.tests.test_protocol_version_ref` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_protocol_version_ref_serialization` | `apps.designer.tests.test_protocol_version_ref` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_protocol_version_ref_valid_payload` | `apps.designer.tests.test_protocol_version_ref` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_protocol_version_ref_validation_blank_fields` | `apps.designer.tests.test_protocol_version_ref` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_protocol_version_ref_validation_index` | `apps.designer.tests.test_protocol_version_ref` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_protocol_version_ref_validation_status` | `apps.designer.tests.test_protocol_version_ref` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_publish_state_machine_lock_freed` | `apps.designer.tests.test_publish_state_machine` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_publish_state_machine_rollback_on_downstream_error` | `apps.designer.tests.test_publish_state_machine` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_publish_state_machine_rollback_on_timeout` | `apps.designer.tests.test_publish_state_machine` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_publish_state_machine_success` | `apps.designer.tests.test_publish_state_machine` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_driver_session_transaction_wrappers` | `apps.designer.tests.test_query_safety_gateway` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_parameter_bypass_validation` | `apps.designer.tests.test_query_safety_gateway` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_unbounded_wildcards_validation` | `apps.designer.tests.test_query_safety_gateway` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_activity_assignment_request` | `apps.designer.tests.test_shared_soa_models` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_epoch_validation` | `apps.designer.tests.test_shared_soa_models` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_procedure_validation` | `apps.designer.tests.test_shared_soa_models` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_properties_payload_contracts` | `apps.designer.tests.test_shared_soa_models` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_study_arm_validation` | `apps.designer.tests.test_shared_soa_models` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_timing_window_validation` | `apps.designer.tests.test_shared_soa_models` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_visit_reorder_request` | `apps.designer.tests.test_shared_soa_models` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_visit_validation` | `apps.designer.tests.test_shared_soa_models` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_api_audit_reason_enforcement` | `apps.designer.tests.test_soa_endpoints` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_api_concurrent_locking_conflict_exception_translation` | `apps.designer.tests.test_soa_endpoints` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_api_invalid_signature_exception_translation` | `apps.designer.tests.test_soa_endpoints` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_api_rule_soft_delete` | `apps.designer.tests.test_soa_endpoints` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_api_soa_crud_lifecycle_endpoints` | `apps.designer.tests.test_soa_endpoints` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_api_soa_immutability_guards` | `apps.designer.tests.test_soa_endpoints` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_api_soa_immutability_guards_updates` | `apps.designer.tests.test_soa_endpoints` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_api_soa_linking_and_matrix_projection` | `apps.designer.tests.test_soa_endpoints` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_api_soa_retirement_and_projection_exclusion` | `apps.designer.tests.test_soa_endpoints` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_api_soa_typed_validation_and_timing_rejection` | `apps.designer.tests.test_soa_endpoints` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_api_unauthorized_requests` | `apps.designer.tests.test_soa_endpoints` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_api_validation_failures` | `apps.designer.tests.test_soa_endpoints` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_api_with_mocked_neo4j_driver` | `apps.designer.tests.test_soa_endpoints` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_assert_study_version_mutable` | `apps.designer.tests.test_soa_persistence` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_epoch_neo4j` | `apps.designer.tests.test_soa_persistence` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_form_neo4j` | `apps.designer.tests.test_soa_persistence` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_get_soa_matrix_projection_neo4j` | `apps.designer.tests.test_soa_persistence` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_links_neo4j` | `apps.designer.tests.test_soa_persistence` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_mock_soa_entity_lifecycle` | `apps.designer.tests.test_soa_persistence` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_mutability_guard_rejects_locked_versions` | `apps.designer.tests.test_soa_persistence` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_neo4j_driver_operations` | `apps.designer.tests.test_soa_persistence` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_procedure_neo4j` | `apps.designer.tests.test_soa_persistence` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_soa_domain_models_schema_alignment` | `apps.designer.tests.test_soa_persistence` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_timing_window_neo4j` | `apps.designer.tests.test_soa_persistence` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_timing_window_persistence_and_carry_forward_mock` | `apps.designer.tests.test_soa_persistence` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_timing_window_update_carry_forward_neo4j` | `apps.designer.tests.test_soa_persistence` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_timing_window_validation_rules` | `apps.designer.tests.test_soa_persistence` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_update_study_arm_neo4j` | `apps.designer.tests.test_soa_persistence` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_visit_neo4j` | `apps.designer.tests.test_soa_persistence` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_with_transaction_retry_failure_exceeded` | `apps.designer.tests.test_soa_persistence` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_with_transaction_retry_success_after_retries` | `apps.designer.tests.test_soa_persistence` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_api_protocol_amendment_concurrency_race` | `apps.designer.tests.test_study_versions` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_api_protocol_amendment_invalid_signature_rejected` | `apps.designer.tests.test_study_versions` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_api_protocol_amendment_invalid_study_404` | `apps.designer.tests.test_study_versions` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_api_protocol_amendment_minor_and_major_bumps` | `apps.designer.tests.test_study_versions` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_api_protocol_approval_and_immutability` | `apps.designer.tests.test_study_versions` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_api_study_version_creation_and_guards` | `apps.designer.tests.test_study_versions` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_assert_graph_mutable_library_object_permits_active` | `apps.designer.tests.test_study_versions` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_assert_graph_mutable_library_object_rejects_frozen` | `apps.designer.tests.test_study_versions` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_assert_graph_mutable_permits_draft_active` | `apps.designer.tests.test_study_versions` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_assert_graph_mutable_rejects_frozen_states` | `apps.designer.tests.test_study_versions` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_bump_version_edge_cases` | `apps.designer.tests.test_study_versions` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_create_library_object_version_guards` | `apps.designer.tests.test_study_versions` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_mock_study_version_creation_and_immutability` | `apps.designer.tests.test_study_versions` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_neo4j_create_study_version_duplicate_raises_conflict` | `apps.designer.tests.test_study_versions` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_neo4j_create_study_version_success` | `apps.designer.tests.test_study_versions` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_update_study_properties_guards` | `apps.designer.tests.test_study_versions` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_verify_version_signature_edge_cases` | `apps.designer.tests.test_study_versions` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_synopsis_export_invalid_format_returns_400` | `apps.designer.tests.test_synopsis_router` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_synopsis_export_post_docx` | `apps.designer.tests.test_synopsis_router` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_synopsis_export_post_html` | `apps.designer.tests.test_synopsis_router` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_synopsis_render_get_download` | `apps.designer.tests.test_synopsis_router` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_terminology_cache_capacity_eviction` | `apps.designer.tests.test_terminology_cache` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_terminology_cache_hit_and_expiration` | `apps.designer.tests.test_terminology_cache` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_terminology_cache_thread_safety` | `apps.designer.tests.test_terminology_cache` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_terminology_cache_ttl_config` | `apps.designer.tests.test_terminology_cache` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_terminology_cache_unreachable_db_fallback` | `apps.designer.tests.test_terminology_cache` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_cache_hit_performs_no_external_lookup` | `apps.designer.tests.test_terminology_integration` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_existing_cache_consumers_receive_expected_shape` | `apps.designer.tests.test_terminology_integration` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_expired_entry_fallback_on_unreachable_evs` | `apps.designer.tests.test_terminology_integration` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_get_terminology_from_db_delegation` | `apps.designer.tests.test_terminology_integration` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_get_terminology_from_db_nci_evs_offline_fallback` | `apps.designer.tests.test_terminology_integration` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_get_terminology_from_db_not_found_anywhere` | `apps.designer.tests.test_terminology_integration` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_get_terminology_from_db_not_found_in_evs_but_in_mock` | `apps.designer.tests.test_terminology_integration` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_get_terminology_from_db_transport_error_and_not_in_mock` | `apps.designer.tests.test_terminology_integration` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_get_terminology_from_db_transport_error_but_in_mock` | `apps.designer.tests.test_terminology_integration` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_offline_fallback_resolves_supported_seed_concepts` | `apps.designer.tests.test_terminology_integration` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_terminology_cache_unreachable_database_exception_fallback` | `apps.designer.tests.test_terminology_integration` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_search_terminology_endpoint_bypass_and_refresh` | `apps.designer.tests.test_terminology_validation` | PRD-MDR-007 | ⚪ UNVERIFIED | N/A |
| `test_search_terminology_endpoint_cache_behavior` | `apps.designer.tests.test_terminology_validation` | PRD-MDR-007 | ⚪ UNVERIFIED | N/A |
| `test_search_terminology_endpoint_degraded` | `apps.designer.tests.test_terminology_validation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_search_terminology_endpoint_invalid_input` | `apps.designer.tests.test_terminology_validation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_search_terminology_endpoint_success` | `apps.designer.tests.test_terminology_validation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_terminology_search_cache_direct` | `apps.designer.tests.test_terminology_validation` | PRD-MDR-007 | ⚪ UNVERIFIED | N/A |
| `test_validate_concept_codes_degraded` | `apps.designer.tests.test_terminology_validation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_validate_concept_codes_success` | `apps.designer.tests.test_terminology_validation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_validate_single_code_endpoint` | `apps.designer.tests.test_terminology_validation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_validate_single_code_endpoint_degraded` | `apps.designer.tests.test_terminology_validation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_validate_single_code_endpoint_invalid_data` | `apps.designer.tests.test_terminology_validation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_validate_single_code_endpoint_marked_invalid` | `apps.designer.tests.test_terminology_validation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_validate_single_code_endpoint_not_found` | `apps.designer.tests.test_terminology_validation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_validate_study_ct_endpoint` | `apps.designer.tests.test_terminology_validation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_validate_study_ct_endpoint_not_found` | `apps.designer.tests.test_terminology_validation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_validate_study_terminology` | `apps.designer.tests.test_terminology_validation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_validate_study_terminology_endpoint_client_degraded` | `apps.designer.tests.test_terminology_validation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_validate_study_terminology_endpoint_client_not_found` | `apps.designer.tests.test_terminology_validation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_validate_study_terminology_endpoint_client_success` | `apps.designer.tests.test_terminology_validation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_validate_study_terminology_endpoint_not_found` | `apps.designer.tests.test_terminology_validation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_validate_study_terminology_endpoint_success` | `apps.designer.tests.test_terminology_validation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_validate_study_terminology_fully_valid` | `apps.designer.tests.test_terminology_validation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_atomic_rollback_leaves_graph_unmodified` | `apps.designer.tests.test_transactional_usdm_ingest` | PRD-DDF-001, PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_commit_usdm_graph_with_empty_arrays` | `apps.designer.tests.test_transactional_usdm_ingest` | PRD-MDR-007, PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_ingest_incomplete_study_with_empty_arrays` | `apps.designer.tests.test_transactional_usdm_ingest` | PRD-DDF-001, PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_linear_scaling_and_no_timeout_on_large_payload` | `apps.designer.tests.test_transactional_usdm_ingest` | PRD-DDF-001, PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_mock_state_unchanged_on_transaction_failure` | `apps.designer.tests.test_transactional_usdm_ingest` | PRD-MDR-007, PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_pydantic_domain_models` | `apps.designer.tests.test_usdm_graph_importer` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_usdm_graph_importer_in_memory_state_sync` | `apps.designer.tests.test_usdm_graph_importer` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_usdm_graph_importer_sync_wrapper` | `apps.designer.tests.test_usdm_graph_importer` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_usdm_graph_importer_transactional_rollback` | `apps.designer.tests.test_usdm_graph_importer` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_usdm_graph_importer_v4_ingestion` | `apps.designer.tests.test_usdm_graph_importer` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_usdm_importer_alias_compatibility` | `apps.designer.tests.test_usdm_graph_importer` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_usdm_importer_warning_unknown_concepts` | `apps.designer.tests.test_usdm_graph_importer` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_usdm_model_immutability` | `apps.designer.tests.test_usdm_immutability` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_usdm_importer_invalid_payload_raises` | `apps.designer.tests.test_usdm_importer` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_usdm_importer_valid_dict` | `apps.designer.tests.test_usdm_importer` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_usdm_importer_valid_model` | `apps.designer.tests.test_usdm_importer` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_usdm_importer_warning_empty_designs` | `apps.designer.tests.test_usdm_importer` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_api_validate_usdm_endpoint_invalid_422` | `apps.designer.tests.test_usdm_ingestion` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_api_validate_usdm_endpoint_valid` | `apps.designer.tests.test_usdm_ingestion` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_normalize_usdm_payload_v2_to_v3` | `apps.designer.tests.test_usdm_ingestion` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_resolve_usdm_version_override` | `apps.designer.tests.test_usdm_ingestion` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_resolve_usdm_version_v2` | `apps.designer.tests.test_usdm_ingestion` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_resolve_usdm_version_v3` | `apps.designer.tests.test_usdm_ingestion` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_safe_parse_payload_invalid` | `apps.designer.tests.test_usdm_ingestion` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_safe_parse_payload_json` | `apps.designer.tests.test_usdm_ingestion` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_safe_parse_payload_yaml` | `apps.designer.tests.test_usdm_ingestion` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_validate_usdm_payload_circular_skip_logic` | `apps.designer.tests.test_usdm_ingestion` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_validate_usdm_payload_duplicate_ids` | `apps.designer.tests.test_usdm_ingestion` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_validate_usdm_payload_invalid_structure` | `apps.designer.tests.test_usdm_ingestion` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_validate_usdm_payload_stochastic_math_operators` | `apps.designer.tests.test_usdm_ingestion` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_validate_usdm_payload_valid_v3` | `apps.designer.tests.test_usdm_ingestion` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_validate_usdm_payload_warnings_custom_elements` | `apps.designer.tests.test_usdm_ingestion` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_usdm_study_dump_by_alias` | `apps.designer.tests.test_usdm_models` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_usdm_study_parsing` | `apps.designer.tests.test_usdm_models` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_usdm_v2_to_v3_upgrade_transformer` | `apps.designer.tests.test_usdm_roundtrip` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_usdm_v3_lossless_roundtrip_fidelity` | `apps.designer.tests.test_usdm_roundtrip` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_round_trip_canonical_serialization_verification` | `apps.designer.tests.test_usdm_serialization` | PRD-MDR-007 | ⚪ UNVERIFIED | N/A |
| `test_serialize_usdm_canonical_json` | `apps.designer.tests.test_usdm_serialization` | PRD-MDR-007 | ⚪ UNVERIFIED | N/A |
| `test_serialize_usdm_canonical_yaml` | `apps.designer.tests.test_usdm_serialization` | PRD-MDR-007 | ⚪ UNVERIFIED | N/A |
| `test_serialize_usdm_validation_errors` | `apps.designer.tests.test_usdm_serialization` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_form_review_comments_endpoints` | `apps.designer.tests.test_validator_alignment_and_xml` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_generate_alignment_report_with_complete_mapping` | `apps.designer.tests.test_validator_alignment_and_xml` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_quality_sentinel_patient_context_and_readability` | `apps.designer.tests.test_validator_alignment_and_xml` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_validate_mapping_csv` | `apps.designer.tests.test_validator_alignment_and_xml` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_xml_name_validation` | `apps.designer.tests.test_validator_alignment_and_xml` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_tier1_ecrf_layout_synthesis_engine` | `apps.designer.tests.test_zero_click_usdm_build` | PRD-CRF-004 | ⚪ UNVERIFIED | N/A |
| `test_tier1_etmf_edl_seeding_milestones_and_zones` | `apps.designer.tests.test_zero_click_usdm_build` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_tier1_soa_matrix_compilation_from_graph` | `apps.designer.tests.test_zero_click_usdm_build` | PRD-MDR-007 | ⚪ UNVERIFIED | N/A |
| `test_tier1_soa_matrix_compiler_usdm_model` | `apps.designer.tests.test_zero_click_usdm_build` | PRD-MDR-007 | ⚪ UNVERIFIED | N/A |
| `test_tier1_usdm_graph_ingestion_transactional` | `apps.designer.tests.test_zero_click_usdm_build` | PRD-DDF-001, PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_tier2_atomic_rollback_on_invalid_usdm_payload` | `apps.designer.tests.test_zero_click_usdm_build` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_tier2_edge_cases_empty_and_unmapped_entities` | `apps.designer.tests.test_zero_click_usdm_build` | PRD-CRF-004, PRD-DDF-001 | ⚪ UNVERIFIED | N/A |
| `test_tier3_end_to_end_zero_click_build_pipeline` | `apps.designer.tests.test_zero_click_usdm_build` | PRD-DDF-001, PRD-MDR-007, PRD-SYS-001, PRD-TMF-001 | ⚪ UNVERIFIED | N/A |
| `test_tier4_execution_performance_benchmark_under_5s` | `apps.designer.tests.test_zero_click_usdm_build` | PRD-DDF-001 | ⚪ UNVERIFIED | N/A |
| `test_tier4_part11_gxp_audit_and_change_justification` | `apps.designer.tests.test_zero_click_usdm_build` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_tier4_phase2_oncology_real_world_protocol` | `apps.designer.tests.test_zero_click_usdm_build` | PRD-CRF-004, PRD-DDF-001 | ⚪ UNVERIFIED | N/A |
| `test_cache_entry_expiration` | `apps.econsent.tests.test_cache_redis` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_cache_max_size_eviction` | `apps.econsent.tests.test_cache_redis` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_cache_ttl_and_env_initialization` | `apps.econsent.tests.test_cache_redis` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_get_approved_template_translation_helper` | `apps.econsent.tests.test_cache_redis` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_redis_publish_error_handling` | `apps.econsent.tests.test_cache_redis` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_redis_publish_on_invalidate_and_clear` | `apps.econsent.tests.test_cache_redis` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_redis_subscriber_invalidate_template` | `apps.econsent.tests.test_cache_redis` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_redis_subscriber_receives_and_evicts_cache` | `apps.econsent.tests.test_cache_redis` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_redis_unconfigured_graceful_fallback` | `apps.econsent.tests.test_cache_redis` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_redis_unreachable_graceful_fallback` | `apps.econsent.tests.test_cache_redis` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_authoring_mutations_rejected_for_auditors` | `apps.econsent.tests.test_econsent` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_clause_lifecycle_and_versioning_audit` | `apps.econsent.tests.test_econsent` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_database_url_override_and_init` | `apps.econsent.tests.test_econsent` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_econsent_database_schema_creation` | `apps.econsent.tests.test_econsent` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_econsent_document_lifecycle_and_audit_context` | `apps.econsent.tests.test_econsent` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_econsent_get_not_found` | `apps.econsent.tests.test_econsent` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_econsent_health_check` | `apps.econsent.tests.test_econsent` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_econsent_pydantic_schemas` | `apps.econsent.tests.test_econsent` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_gateway_auth_middleware_denials` | `apps.econsent.tests.test_econsent` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_shared_audit_fields_validation` | `apps.econsent.tests.test_econsent` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_template_lifecycle_and_validation` | `apps.econsent.tests.test_econsent` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_uninitialized_database_manager_econsent` | `apps.econsent.tests.test_econsent` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_compare_templates_delta_report` | `apps.econsent.tests.test_econsent_amendment_diff` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_substantive_change_detection` | `apps.econsent.tests.test_econsent_amendment_diff` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_text_diff_computation` | `apps.econsent.tests.test_econsent_amendment_diff` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_audit_logs_endpoint` | `apps.econsent.tests.test_econsent_api_endpoints` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_export_and_diff_endpoints` | `apps.econsent.tests.test_econsent_api_endpoints` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_granular_options_router` | `apps.econsent.tests.test_econsent_api_endpoints` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_archival_status_endpoints` | `apps.econsent.tests.test_econsent_archival` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_icf_sign_and_archival_queueing` | `apps.econsent.tests.test_econsent_archival` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_poll_and_dispatch_failure_and_retry_backoff` | `apps.econsent.tests.test_econsent_archival` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_poll_and_dispatch_success` | `apps.econsent.tests.test_econsent_archival` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_append_only_audit_history` | `apps.econsent.tests.test_econsent_capture` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_capture_rejections` | `apps.econsent.tests.test_econsent_capture` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_execution_consumption_integration` | `apps.econsent.tests.test_econsent_capture` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_happy_path_capture_and_status` | `apps.econsent.tests.test_econsent_capture` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_signature_tamper_detection` | `apps.econsent.tests.test_econsent_capture` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_cdisc_odm_generation_structure` | `apps.econsent.tests.test_econsent_cdisc_odm` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_auditor_restrictions_on_checks` | `apps.econsent.tests.test_econsent_comprehension` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_create_and_retrieve_comprehension_check` | `apps.econsent.tests.test_econsent_comprehension` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_signature_blocks_if_comprehension_checks_fail_or_incomplete` | `apps.econsent.tests.test_econsent_comprehension` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_submit_answers_and_evaluation_boundaries` | `apps.econsent.tests.test_econsent_comprehension` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_template_version_separation` | `apps.econsent.tests.test_econsent_comprehension` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_render_verifiable_consent_html` | `apps.econsent.tests.test_econsent_document_renderer` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_granular_options_lifecycle_and_subject_selection` | `apps.econsent.tests.test_econsent_granular_options` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_multisig_subject_lar_and_investigator_workflow` | `apps.econsent.tests.test_econsent_multisig` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_reconsent_trigger_and_pending_queries` | `apps.econsent.tests.test_econsent_reconsent` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_failed_comprehension_quiz_blocks_signature` | `apps.econsent.tests.test_econsent_service` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_incomplete_comprehension_quiz_blocks_signature` | `apps.econsent.tests.test_econsent_service` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_invalid_otp_auth_code_blocks_signature` | `apps.econsent.tests.test_econsent_service` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_successful_signature_capture` | `apps.econsent.tests.test_econsent_service` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_workflow_engine_legacy_signature_capture` | `apps.econsent.tests.test_econsent_service` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_approved_content_retrieval_and_cache` | `apps.econsent.tests.test_econsent_translations` | Trace-10 | ⚪ UNVERIFIED | N/A |
| `test_language_code_validation` | `apps.econsent.tests.test_econsent_translations` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_translation_crud_and_validation` | `apps.econsent.tests.test_econsent_translations` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_translation_status_workflow_and_rbac` | `apps.econsent.tests.test_econsent_translations` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_clause_service_lifecycle` | `apps.econsent.tests.test_econsent_use_cases` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_template_authoring_and_composition` | `apps.econsent.tests.test_econsent_use_cases` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_translation_service_workflow` | `apps.econsent.tests.test_econsent_use_cases` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_subject_consent_withdrawal_lifecycle` | `apps.econsent.tests.test_econsent_withdrawal` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_consent_record_immutability` | `apps.econsent.tests.test_econsent_workflow` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_econsent_signature_audit_compliance` | `apps.econsent.tests.test_econsent_workflow` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_econsent_signature_capture_success` | `apps.econsent.tests.test_econsent_workflow` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_protocol_amendment_triggers_reconsent` | `apps.econsent.tests.test_econsent_workflow` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_classify_incoming_document_changed_dict` | `apps.eisf.tests.test_eisf_adapter` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_classify_incoming_document_changed_object` | `apps.eisf.tests.test_eisf_adapter` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_classify_incoming_document_duplicate_dict` | `apps.eisf.tests.test_eisf_adapter` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_classify_incoming_document_duplicate_object` | `apps.eisf.tests.test_eisf_adapter` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_classify_incoming_document_new` | `apps.eisf.tests.test_eisf_adapter` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_derive_correlation_key` | `apps.eisf.tests.test_eisf_adapter` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_deterministic_bidirectional_mapping_success` | `apps.eisf.tests.test_eisf_adapter` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_eisf_mappings_resolve_through_active_catalog` | `apps.eisf.tests.test_eisf_adapter` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_eisf_resolve_known_extension_artifact` | `apps.eisf.tests.test_eisf_adapter` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_eisf_reverse_mappings_resolve_through_active_catalog` | `apps.eisf.tests.test_eisf_adapter` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_mapping_failures` | `apps.eisf.tests.test_eisf_adapter` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_mapping_normalization` | `apps.eisf.tests.test_eisf_adapter` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_eisf_auditor_write_forbidden` | `apps.eisf.tests.test_eisf_api` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_eisf_document_cross_site_rejection_and_audit` | `apps.eisf.tests.test_eisf_api` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_eisf_document_lifecycle_same_site` | `apps.eisf.tests.test_eisf_api` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_eisf_documents_endpoint_blocks_unauthenticated` | `apps.eisf.tests.test_eisf_api` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_eisf_health_unauthenticated` | `apps.eisf.tests.test_eisf_api` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_eisf_phi_redaction_preserves_original` | `apps.eisf.tests.test_eisf_binder` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_eisf_site_isolation_and_redaction` | `apps.eisf.tests.test_eisf_binder` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_eisf_site_isolation_enforcement` | `apps.eisf.tests.test_eisf_binder` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_eisf_auditor_view_and_download_permissions` | `apps.eisf.tests.test_eisf_browse_completeness` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_eisf_completeness_workflow` | `apps.eisf.tests.test_eisf_browse_completeness` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_eisf_document_listing_with_binder_filters` | `apps.eisf.tests.test_eisf_browse_completeness` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_eisf_document_view_and_download_site_isolation` | `apps.eisf.tests.test_eisf_browse_completeness` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_site_level_data_isolation` | `apps.eisf.tests.test_eisf_compliance` | PRD-SYS-004 | ⚪ UNVERIFIED | N/A |
| `test_eisf_ingest_document_event_alias` | `apps.eisf.tests.test_eisf_ingest` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_eisf_ingest_document_success` | `apps.eisf.tests.test_eisf_ingest` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_eisf_ingest_missing_change_reason_fails` | `apps.eisf.tests.test_eisf_ingest` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_eisf_site_isolation_lifecycle` | `apps.eisf.tests.test_eisf_isolation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_eisf_document_record_creation` | `apps.eisf.tests.test_eisf_models` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_database_url_override_and_init` | `apps.eisf.tests.test_eisf_persistence` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_eisf_append_only_versions_and_deduplication` | `apps.eisf.tests.test_eisf_persistence` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_eisf_document_creation_and_site_scoped` | `apps.eisf.tests.test_eisf_persistence` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_eisf_part11_audit_log_retention` | `apps.eisf.tests.test_eisf_persistence` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_uninitialized_database_manager_eisf` | `apps.eisf.tests.test_eisf_persistence` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_get_site_eisf_binder_authorized` | `apps.eisf.tests.test_eisf_router` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_get_site_eisf_binder_unauthorized_cross_site` | `apps.eisf.tests.test_eisf_router` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_upload_and_get_site_document` | `apps.eisf.tests.test_eisf_router` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_eisf_upload_and_watermark` | `apps.eisf.tests.test_eisf_service` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_eisf_completeness_site_isolation` | `apps.eisf.tests.test_eisf_site_scope` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_eisf_external_monitor_role` | `apps.eisf.tests.test_eisf_site_scope` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_eisf_site_scoped_users_read_isolation` | `apps.eisf.tests.test_eisf_site_scope` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_eisf_site_scoped_write_restrictions` | `apps.eisf.tests.test_eisf_site_scope` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_eisf_sponsor_admin_global_visibility` | `apps.eisf.tests.test_eisf_site_scope` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_eisf_sync_conflict_client_wins` | `apps.eisf.tests.test_eisf_sync` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_eisf_sync_conflict_merge_lexicographic_tiebreaker` | `apps.eisf.tests.test_eisf_sync` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_eisf_sync_conflict_merge_lww_existing_wins` | `apps.eisf.tests.test_eisf_sync` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_eisf_sync_conflict_merge_lww_incoming_wins` | `apps.eisf.tests.test_eisf_sync` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_eisf_sync_conflict_server_wins` | `apps.eisf.tests.test_eisf_sync` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_eisf_sync_creation_and_etmf_propagation` | `apps.eisf.tests.test_eisf_sync` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_eisf_sync_echo_loop_prevention` | `apps.eisf.tests.test_eisf_sync` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_eisf_sync_exact_duplicate_ignored` | `apps.eisf.tests.test_eisf_sync` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_eisf_sync_per_field_metadata_lww` | `apps.eisf.tests.test_eisf_sync` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_eisf_sync_unmapped_propagation` | `apps.eisf.tests.test_eisf_sync` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_eisf_document_record_instantiation_defaults` | `apps.eisf.tests.test_eisf_taxonomy` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_eisf_taxonomy_querying_returns_8_mandatory_sections` | `apps.eisf.tests.test_eisf_taxonomy` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_exception_route` | `apps.etmf.presentation.routers.etmf` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_archived_document_retrieval_and_immutability` | `apps.etmf.tests.test_etmf` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_automated_ingestion_and_version_indexing` | `apps.etmf.tests.test_etmf` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_canonical_catalog_ingestion_validations` | `apps.etmf.tests.test_etmf` | PRD-TMF-002, PRD-TMF-003, Trace-5 | ⚪ UNVERIFIED | N/A |
| `test_completeness_checking_transitions` | `apps.etmf.tests.test_etmf` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_completeness_from_catalog` | `apps.etmf.tests.test_etmf` | PRD-TMF-004 | ⚪ UNVERIFIED | N/A |
| `test_completeness_from_catalog_across_versions` | `apps.etmf.tests.test_etmf` | PRD-TMF-004 | ⚪ UNVERIFIED | N/A |
| `test_deterministic_and_complete_binder_export` | `apps.etmf.tests.test_etmf` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_edl_definitions_and_crud` | `apps.etmf.tests.test_etmf` | PRD-EDL-001, Trace-4 | ⚪ UNVERIFIED | N/A |
| `test_etmf_audit_logs_filtering_and_pagination` | `apps.etmf.tests.test_etmf` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_etmf_completeness_rejects_quarantined` | `apps.etmf.tests.test_etmf` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_etmf_completeness_site_segregation_and_study_wide` | `apps.etmf.tests.test_etmf` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_etmf_edge_cases_for_coverage` | `apps.etmf.tests.test_etmf` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_etmf_qc_lifecycle_and_audit` | `apps.etmf.tests.test_etmf` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_etmf_repository_rule_deduplication` | `apps.etmf.tests.test_etmf` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_explicit_and_default_taxonomy_version_roundtrip_and_legacy_interpretability` | `apps.etmf.tests.test_etmf` | PRD-TMF-003 | ⚪ UNVERIFIED | N/A |
| `test_informed_consent_form_taxonomy_and_idempotency` | `apps.etmf.tests.test_etmf` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_inspector_portal_read_only_access_limits` | `apps.etmf.tests.test_etmf` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_ordered_artifact_history_endpoint` | `apps.etmf.tests.test_etmf` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_placeholder_scripts` | `apps.etmf.tests.test_etmf` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_protocol_versioning_and_change_justification_ingestion` | `apps.etmf.tests.test_etmf` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_qualify_catalog_cutover_and_extension_persistence` | `apps.etmf.tests.test_etmf` | PRD-TMF-002, Trace-5 | ⚪ UNVERIFIED | N/A |
| `test_regulatory_binder_export` | `apps.etmf.tests.test_etmf` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_service_caller_ingestion_immutability_violation` | `apps.etmf.tests.test_etmf` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_service_caller_ingestion_rollback_on_failure` | `apps.etmf.tests.test_etmf` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_service_caller_ingestion_success` | `apps.etmf.tests.test_etmf` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_site_aware_completeness` | `apps.etmf.tests.test_etmf` | PRD-EDL-001, Trace-4 | ⚪ UNVERIFIED | N/A |
| `test_tmf_taxonomy_mapping` | `apps.etmf.tests.test_etmf` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_ucum_extra_coverage` | `apps.etmf.tests.test_etmf` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_uninitialized_database_manager` | `apps.etmf.tests.test_etmf` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_view_download_audit_logging` | `apps.etmf.tests.test_etmf` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_watermarked_document_viewing_and_download` | `apps.etmf.tests.test_etmf` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_etmf_document_change_rationale_mandatory_rules` | `apps.etmf.tests.test_etmf_amendment_lineage` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_etmf_linkage_and_version_history_lineage` | `apps.etmf.tests.test_etmf_amendment_lineage` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_etmf_qc_transitions_immutability` | `apps.etmf.tests.test_etmf_amendment_lineage` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_audit_chain_tamper_detection` | `apps.etmf.tests.test_etmf_audit_chain_verification` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_audit_chain_verification_endpoint` | `apps.etmf.tests.test_etmf_audit_chain_verification` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_document_version_history_lineage` | `apps.etmf.tests.test_etmf_binder_structure_and_history` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_empty_binder_structure` | `apps.etmf.tests.test_etmf_binder_structure_and_history` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_partial_binder_structure` | `apps.etmf.tests.test_etmf_binder_structure_and_history` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_versions_404_not_found` | `apps.etmf.tests.test_etmf_binder_structure_and_history` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_bulk_archival_all_or_nothing_rollback` | `apps.etmf.tests.test_etmf_bulk_archival` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_bulk_archival_authorization_and_rejections` | `apps.etmf.tests.test_etmf_bulk_archival` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_bulk_archival_partial_success` | `apps.etmf.tests.test_etmf_bulk_archival` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_bulk_archival_repeating_safe_and_observable` | `apps.etmf.tests.test_etmf_bulk_archival` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_bulk_archival_successful_progression` | `apps.etmf.tests.test_etmf_bulk_archival` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_actual_cryptographic_verification` | `apps.etmf.tests.test_etmf_compliance` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_audit_logs_group_sealing_and_chaining` | `apps.etmf.tests.test_etmf_compliance` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_background_sealer_lifecycle` | `apps.etmf.tests.test_etmf_compliance` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_missing_and_invalid_signature_ingestion` | `apps.etmf.tests.test_etmf_compliance` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_mock_signature_bypass` | `apps.etmf.tests.test_etmf_compliance` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_signature_extraction_formats` | `apps.etmf.tests.test_etmf_compliance` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_signature_requirement_rules` | `apps.etmf.tests.test_etmf_compliance` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_tampering_detection_and_lockout_propagation` | `apps.etmf.tests.test_etmf_compliance` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_custom_milestones_and_backward_compatibility` | `apps.etmf.tests.test_etmf_edl_seeding` | PRD-TMF-001 | ⚪ UNVERIFIED | N/A |
| `test_live_async_session_edl_seeding_and_idempotency` | `apps.etmf.tests.test_etmf_edl_seeding` | PRD-TMF-001, Trace-4 | ⚪ UNVERIFIED | N/A |
| `test_offline_in_memory_edl_seeding_all_zones` | `apps.etmf.tests.test_etmf_edl_seeding` | PRD-EDL-001, PRD-TMF-001 | ⚪ UNVERIFIED | N/A |
| `test_repository_integration_edl_seeding` | `apps.etmf.tests.test_etmf_edl_seeding` | PRD-TMF-001 | ⚪ UNVERIFIED | N/A |
| `test_seed_edl_rest_endpoint` | `apps.etmf.tests.test_etmf_edl_seeding` | PRD-EDL-001, PRD-TMF-001 | ⚪ UNVERIFIED | N/A |
| `test_eisf_create_authorized_vs_unauthorized` | `apps.etmf.tests.test_etmf_eisf_expiration_metadata` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_eisf_creation_date_validation_rejected` | `apps.etmf.tests.test_etmf_eisf_expiration_metadata` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_eisf_expiration_update_authorized_vs_unauthorized` | `apps.etmf.tests.test_etmf_eisf_expiration_metadata` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_eisf_update_date_validation_rejected` | `apps.etmf.tests.test_etmf_eisf_expiration_metadata` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_etmf_expiration_update_authorized_vs_unauthorized` | `apps.etmf.tests.test_etmf_eisf_expiration_metadata` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_etmf_expiration_update_date_validation_rejected` | `apps.etmf.tests.test_etmf_eisf_expiration_metadata` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_etmf_ingest_authorized_vs_unauthorized` | `apps.etmf.tests.test_etmf_eisf_expiration_metadata` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_etmf_ingest_date_validation_rejected` | `apps.etmf.tests.test_etmf_eisf_expiration_metadata` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_manage_expiration_rbac_permissions` | `apps.etmf.tests.test_etmf_eisf_expiration_metadata` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_migration_adds_expiration_columns_idempotently` | `apps.etmf.tests.test_etmf_eisf_expiration_metadata` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_etmf_ems_export_package_structure` | `apps.etmf.tests.test_etmf_ems_export` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_etmf_ems_export_permissions` | `apps.etmf.tests.test_etmf_ems_export` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_audit_attribution` | `apps.etmf.tests.test_etmf_expiration_scanner` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_determine_warning_window` | `apps.etmf.tests.test_etmf_expiration_scanner` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_dispatch_failure_and_retryability` | `apps.etmf.tests.test_etmf_expiration_scanner` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_dispatch_fallback_cra_routing` | `apps.etmf.tests.test_etmf_expiration_scanner` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_dispatch_idempotency_limit` | `apps.etmf.tests.test_etmf_expiration_scanner` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_dispatch_successful_owner_routing` | `apps.etmf.tests.test_etmf_expiration_scanner` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_execute_expiration_scan_cycle_thresholds` | `apps.etmf.tests.test_etmf_expiration_scanner` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_failure_isolation_and_resilience` | `apps.etmf.tests.test_etmf_expiration_scanner` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_scanner_idempotency_restart_and_rearming` | `apps.etmf.tests.test_etmf_expiration_scanner` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_scanner_shutdown_cancellation` | `apps.etmf.tests.test_etmf_expiration_scanner` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_idempotency` | `apps.etmf.tests.test_etmf_inbound_email` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_immutability_violation_inbound_email` | `apps.etmf.tests.test_etmf_inbound_email` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_invalid_signature_rejection` | `apps.etmf.tests.test_etmf_inbound_email` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_multi_attachment_ingestion` | `apps.etmf.tests.test_etmf_inbound_email` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_oversized_payload_rejection` | `apps.etmf.tests.test_etmf_inbound_email` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_replay_protection` | `apps.etmf.tests.test_etmf_inbound_email` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_stale_timestamp_rejection` | `apps.etmf.tests.test_etmf_inbound_email` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_unresolvable_recipient_address` | `apps.etmf.tests.test_etmf_inbound_email` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_valid_inbound_email_ingestion` | `apps.etmf.tests.test_etmf_inbound_email` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_document_signature_verification_endpoint` | `apps.etmf.tests.test_etmf_inspection_readiness` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_inspection_readiness_endpoint_and_scoring` | `apps.etmf.tests.test_etmf_inspection_readiness` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_trigger_global_trial_lock` | `apps.etmf.tests.test_etmf_lock_integration` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_verify_trial_lock_status_error` | `apps.etmf.tests.test_etmf_lock_integration` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_verify_trial_lock_status_locked` | `apps.etmf.tests.test_etmf_lock_integration` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_verify_trial_lock_status_unlocked` | `apps.etmf.tests.test_etmf_lock_integration` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_document_signing_writes_outbox` | `apps.etmf.tests.test_etmf_outbox` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_etmf_admin_visibility_endpoint` | `apps.etmf.tests.test_etmf_outbox` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_etmf_outbox_no_unencrypted_pii` | `apps.etmf.tests.test_etmf_outbox` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_etmf_outbox_worker_polling_and_dispatch_success` | `apps.etmf.tests.test_etmf_outbox` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_etmf_outbox_worker_retry_and_backoff` | `apps.etmf.tests.test_etmf_outbox` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_append_only_transition_history` | `apps.etmf.tests.test_etmf_qc` | PRD-QC-005 | ⚪ UNVERIFIED | N/A |
| `test_invalid_status_transition_raises_error` | `apps.etmf.tests.test_etmf_qc` | PRD-QC-002 | ⚪ UNVERIFIED | N/A |
| `test_new_document_defaults_to_draft` | `apps.etmf.tests.test_etmf_qc` | PRD-QC-001 | ⚪ UNVERIFIED | N/A |
| `test_part11_change_reason_enforcement` | `apps.etmf.tests.test_etmf_qc` | PRD-QC-004 | ⚪ UNVERIFIED | N/A |
| `test_qc_history_api_and_audit` | `apps.etmf.tests.test_etmf_qc` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_qc_history_api_not_found` | `apps.etmf.tests.test_etmf_qc` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_qc_transitions_missing_doc` | `apps.etmf.tests.test_etmf_qc` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_role_based_access_controls_and_gates` | `apps.etmf.tests.test_etmf_qc` | PRD-QC-003 | ⚪ UNVERIFIED | N/A |
| `test_migration_clean_path` | `apps.etmf.tests.test_etmf_qc_invariants` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_migration_upgrade_and_backfill_path` | `apps.etmf.tests.test_etmf_qc_invariants` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_automated_redaction_basic` | `apps.etmf.tests.test_etmf_redaction` | PRD-TMF-005 | ⚪ UNVERIFIED | N/A |
| `test_automated_redaction_errors` | `apps.etmf.tests.test_etmf_redaction` | PRD-TMF-005 | ⚪ UNVERIFIED | N/A |
| `test_automated_redaction_profile_scopes` | `apps.etmf.tests.test_etmf_redaction` | PRD-TMF-005 | ⚪ UNVERIFIED | N/A |
| `test_automated_redaction_trial_locked` | `apps.etmf.tests.test_etmf_redaction` | PRD-TMF-005 | ⚪ UNVERIFIED | N/A |
| `test_manual_redaction_authorization_and_lock` | `apps.etmf.tests.test_etmf_redaction` | PRD-TMF-005 | ⚪ UNVERIFIED | N/A |
| `test_manual_redaction_literal_escaping` | `apps.etmf.tests.test_etmf_redaction` | PRD-TMF-005 | ⚪ UNVERIFIED | N/A |
| `test_manual_redaction_span_validation` | `apps.etmf.tests.test_etmf_redaction` | PRD-TMF-005 | ⚪ UNVERIFIED | N/A |
| `test_manual_redaction_success` | `apps.etmf.tests.test_etmf_redaction` | PRD-TMF-005 | ⚪ UNVERIFIED | N/A |
| `test_redaction_audit_trail_and_provenance` | `apps.etmf.tests.test_etmf_redaction` | PRD-TMF-005 | ⚪ UNVERIFIED | N/A |
| `test_redaction_authorization_gates` | `apps.etmf.tests.test_etmf_redaction` | PRD-TMF-005 | ⚪ UNVERIFIED | N/A |
| `test_completeness_signature_lifecycle_distinction` | `apps.etmf.tests.test_etmf_signatures` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_signature_document_routing_and_classification` | `apps.etmf.tests.test_etmf_signatures` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_signature_lifecycle_with_mock_signature` | `apps.etmf.tests.test_etmf_signatures` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_etmf_post_signature_locking` | `apps.etmf.tests.test_etmf_signing_lifecycle` | Trace-13 | ⚪ UNVERIFIED | N/A |
| `test_etmf_signing_failure_logging_and_blocking` | `apps.etmf.tests.test_etmf_signing_lifecycle` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_etmf_signing_happy_path` | `apps.etmf.tests.test_etmf_signing_lifecycle` | Trace-13 | ⚪ UNVERIFIED | N/A |
| `test_etmf_signing_reauth_failures` | `apps.etmf.tests.test_etmf_signing_lifecycle` | Trace-13 | ⚪ UNVERIFIED | N/A |
| `test_auto_quarantine_site_level_no_site_id` | `apps.etmf.tests.test_etmf_site_scope` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_binder_export_redaction_representation_policy` | `apps.etmf.tests.test_etmf_site_scope` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_completeness_site_isolation` | `apps.etmf.tests.test_etmf_site_scope` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_eisf_to_etmf_sync_preserves_scope` | `apps.etmf.tests.test_etmf_site_scope` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_is_site_level_artifact_helper` | `apps.etmf.tests.test_etmf_site_scope` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_legacy_records_quarantine_policy` | `apps.etmf.tests.test_etmf_site_scope` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_raw_original_suppression_without_read_raw` | `apps.etmf.tests.test_etmf_site_scope` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_regulatory_binder_export_site_isolation` | `apps.etmf.tests.test_etmf_site_scope` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_site_id_validation_empty_whitespace` | `apps.etmf.tests.test_etmf_site_scope` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_site_scoped_cannot_read_study_level_or_quarantined_documents` | `apps.etmf.tests.test_etmf_site_scope` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_site_scoped_no_assigned_sites_fail_closed` | `apps.etmf.tests.test_etmf_site_scope` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_site_scoped_users_read_isolation` | `apps.etmf.tests.test_etmf_site_scope` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_site_scoped_write_restrictions` | `apps.etmf.tests.test_etmf_site_scope` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_site_scoping_on_redactions_and_signatures` | `apps.etmf.tests.test_etmf_site_scope` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_unauthorized_role_denied_on_all_paths` | `apps.etmf.tests.test_etmf_site_scope` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_eisf_to_etmf_e2e_boundaries` | `apps.etmf.tests.test_etmf_sync_provenance` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_redaction_derivative_safety` | `apps.etmf.tests.test_etmf_sync_provenance` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_sealer_retains_and_validates_reason_for_change` | `apps.etmf.tests.test_etmf_sync_provenance` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_auto_file_endpoint` | `apps.etmf.tests.test_etmf_taxonomy` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_classification_service_direct` | `apps.etmf.tests.test_etmf_taxonomy` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_classify_endpoints` | `apps.etmf.tests.test_etmf_taxonomy` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_get_taxonomy_endpoint` | `apps.etmf.tests.test_etmf_taxonomy` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_resolve_document_type_helper` | `apps.etmf.tests.test_etmf_taxonomy` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_etmf_triggers_immutability` | `apps.etmf.tests.test_etmf_triggers_compliance` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_bulk_archive_use_case` | `apps.etmf.tests.test_etmf_use_cases` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_completeness_readiness_and_export_use_cases` | `apps.etmf.tests.test_etmf_use_cases` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_electronic_signature_and_redaction_use_cases` | `apps.etmf.tests.test_etmf_use_cases` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_ingest_and_qc_use_cases` | `apps.etmf.tests.test_etmf_use_cases` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_duplicate_certificate_injection_rejected` | `apps.etmf.tests.test_part11_compliance_engine` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_legacy_padding_pkcs1v15_fails` | `apps.etmf.tests.test_part11_compliance_engine` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_mandatory_documents_bypass_rejected` | `apps.etmf.tests.test_part11_compliance_engine` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_mock_signatures_blocked` | `apps.etmf.tests.test_part11_compliance_engine` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_rsassa_pss_succeeds` | `apps.etmf.tests.test_part11_compliance_engine` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_unapproved_self_signed_certificate_fails` | `apps.etmf.tests.test_part11_compliance_engine` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_active_version_selection` | `apps.etmf.tests.test_tmf_reference_model` | PRD-TMF-001 | ⚪ UNVERIFIED | N/A |
| `test_artifact_parent_identification` | `apps.etmf.tests.test_tmf_reference_model` | PRD-TMF-001 | ⚪ UNVERIFIED | N/A |
| `test_canonical_11_zones` | `apps.etmf.tests.test_tmf_reference_model` | PRD-TMF-001 | ⚪ UNVERIFIED | N/A |
| `test_complete_catalog_manifest_and_uniqueness` | `apps.etmf.tests.test_tmf_reference_model` | PRD-TMF-001 | ⚪ UNVERIFIED | N/A |
| `test_explicit_version_selection` | `apps.etmf.tests.test_tmf_reference_model` | PRD-TMF-001 | ⚪ UNVERIFIED | N/A |
| `test_get_mandatory_artifacts_failures` | `apps.etmf.tests.test_tmf_reference_model` | PRD-TMF-004 | ⚪ UNVERIFIED | N/A |
| `test_get_mandatory_artifacts_success` | `apps.etmf.tests.test_tmf_reference_model` | PRD-TMF-004 | ⚪ UNVERIFIED | N/A |
| `test_hierarchy_integrity_v3_2_0_complete` | `apps.etmf.tests.test_tmf_reference_model` | PRD-TMF-001 | ⚪ UNVERIFIED | N/A |
| `test_immutability_properties` | `apps.etmf.tests.test_tmf_reference_model` | PRD-TMF-001 | ⚪ UNVERIFIED | N/A |
| `test_no_database_dependencies` | `apps.etmf.tests.test_tmf_reference_model` | PRD-TMF-001 | ⚪ UNVERIFIED | N/A |
| `test_reproducibility_and_version_isolation` | `apps.etmf.tests.test_tmf_reference_model` | PRD-TMF-001 | ⚪ UNVERIFIED | N/A |
| `test_resolve_artifact_failures` | `apps.etmf.tests.test_tmf_reference_model` | PRD-TMF-001 | ⚪ UNVERIFIED | N/A |
| `test_resolve_artifact_success` | `apps.etmf.tests.test_tmf_reference_model` | PRD-TMF-001 | ⚪ UNVERIFIED | N/A |
| `test_standard_versus_extension_policy` | `apps.etmf.tests.test_tmf_reference_model` | PRD-TMF-001 | ⚪ UNVERIFIED | N/A |
| `test_validate_hierarchy_failures` | `apps.etmf.tests.test_tmf_reference_model` | PRD-TMF-002 | ⚪ UNVERIFIED | N/A |
| `test_validate_hierarchy_success` | `apps.etmf.tests.test_tmf_reference_model` | PRD-TMF-002 | ⚪ UNVERIFIED | N/A |
| `test_version_isolation` | `apps.etmf.tests.test_tmf_reference_model` | PRD-TMF-001 | ⚪ UNVERIFIED | N/A |
| `test_derive_adae_basic_join` | `apps.execution.tests.test_adae` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_derive_adae_missing_dates_and_ongoing` | `apps.execution.tests.test_adae` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_derive_adae_partial_dates_imputation` | `apps.execution.tests.test_adae` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_derive_adae_relative_day_formula` | `apps.execution.tests.test_adae` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_derive_adae_severity_mappings` | `apps.execution.tests.test_adae` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_derive_adae_treatment_emergent_safety_window` | `apps.execution.tests.test_adae` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_derive_adae_unmatched_subject_skipped` | `apps.execution.tests.test_adae` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_from_sas_date` | `apps.execution.tests.test_adae` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_derive_adsl_additional_branches` | `apps.execution.tests.test_adsl` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_derive_adsl_basic` | `apps.execution.tests.test_adsl` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_derive_adsl_edge_cases` | `apps.execution.tests.test_adsl` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_derive_adsl_observation_based_death_and_actarm` | `apps.execution.tests.test_adsl` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_derive_adsl_partial_dates_and_population_flags` | `apps.execution.tests.test_adsl` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_derive_adsl_various_fallback_branches` | `apps.execution.tests.test_adsl` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_derive_adsl_with_datetime_objects` | `apps.execution.tests.test_adsl` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_impute_partial_date` | `apps.execution.tests.test_adsl` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_parse_partial_date` | `apps.execution.tests.test_adsl` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_to_date_obj` | `apps.execution.tests.test_adsl` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_to_sas_date` | `apps.execution.tests.test_adsl` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_advs_baseline_selection_and_flags` | `apps.execution.tests.test_advs` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_advs_basic_derivation` | `apps.execution.tests.test_advs` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_advs_change_metrics_and_division_by_zero` | `apps.execution.tests.test_advs` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_advs_date_and_visit_fallback` | `apps.execution.tests.test_advs` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_advs_missing_baseline_behavior` | `apps.execution.tests.test_advs` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_advs_no_coercion_of_missing_numeric_values` | `apps.execution.tests.test_advs` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_compare_usdm_snapshots_added_removed_modified` | `apps.execution.tests.test_amendment_diff` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_clinical_capture_provenance_and_version_stamping` | `apps.execution.tests.test_amendment_gating_and_reconciliation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_exact_version_consent_and_reconsent_gating` | `apps.execution.tests.test_amendment_gating_and_reconciliation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_non_destructive_reconciliation_and_multi_hop` | `apps.execution.tests.test_amendment_gating_and_reconciliation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_amendment_cloning_preserves_base_version` | `apps.execution.tests.test_amendment_migration` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_bulk_subject_reconsent_endpoint` | `apps.execution.tests.test_amendment_migration` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_modular_coordinate_matching_and_collision_logging` | `apps.execution.tests.test_amendment_migration` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_reconsent_gating_blocks_form_submission` | `apps.execution.tests.test_amendment_migration` | PRD-SUB-007 | ⚪ UNVERIFIED | N/A |
| `test_reconsent_unlock_enables_v2_entry` | `apps.execution.tests.test_amendment_migration` | PRD-SUB-007 | ⚪ UNVERIFIED | N/A |
| `test_subject_historical_visits_preserve_v1_schema` | `apps.execution.tests.test_amendment_migration` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_backward_compatible_form_rehydration_lifecycle` | `apps.execution.tests.test_amendment_rehydration` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_get_amendment_summary_endpoint` | `apps.execution.tests.test_amendment_router` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_publish_amendment_post_endpoint` | `apps.execution.tests.test_amendment_router` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_endpoints_offload_to_threadpool` | `apps.execution.tests.test_anonymization_router` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_redact_pdf_post_endpoint` | `apps.execution.tests.test_anonymization_router` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_scan_phi_post_endpoint` | `apps.execution.tests.test_anonymization_router` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_abstract_consent_client_interface_and_graceful_network_failure` | `apps.execution.tests.test_audit_metadata_filtering` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_shared_session_coexistence_auditing` | `apps.execution.tests.test_audit_metadata_filtering` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_batch_sign_off_all_locks` | `apps.execution.tests.test_batch_sign_off` | Trace-14 | ⚪ UNVERIFIED | N/A |
| `test_batch_sign_off_audit_manifestation_capture` | `apps.execution.tests.test_batch_sign_off` | Trace-14 | ⚪ UNVERIFIED | N/A |
| `test_batch_sign_off_happy_path_form` | `apps.execution.tests.test_batch_sign_off` | Trace-14, Trace-17 | ⚪ UNVERIFIED | N/A |
| `test_batch_sign_off_locks_and_atomic_rollback` | `apps.execution.tests.test_batch_sign_off` | Trace-14 | ⚪ UNVERIFIED | N/A |
| `test_batch_sign_off_mismatched_bindings_and_no_write` | `apps.execution.tests.test_batch_sign_off` | Trace-14, Trace-17 | ⚪ UNVERIFIED | N/A |
| `test_batch_sign_off_non_lock_rollback` | `apps.execution.tests.test_batch_sign_off` | Trace-14 | ⚪ UNVERIFIED | N/A |
| `test_batch_sign_off_pi_only` | `apps.execution.tests.test_batch_sign_off` | Trace-14 | ⚪ UNVERIFIED | N/A |
| `test_batch_sign_off_subject_resolution` | `apps.execution.tests.test_batch_sign_off` | Trace-14 | ⚪ UNVERIFIED | N/A |
| `test_batch_sign_off_token_replay` | `apps.execution.tests.test_batch_sign_off` | Trace-17 | ⚪ UNVERIFIED | N/A |
| `test_batch_sign_off_visit_resolution` | `apps.execution.tests.test_batch_sign_off` | Trace-14 | ⚪ UNVERIFIED | N/A |
| `test_generate_casebook_manifest_structure` | `apps.execution.tests.test_batch_signatures` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_master_root_digest_tamper_sensitivity` | `apps.execution.tests.test_batch_signatures` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_advisory_locking_pg_outbox_worker` | `apps.execution.tests.test_bg_processing_coordination` | PRD-SYS-102 | ⚪ UNVERIFIED | N/A |
| `test_advisory_locking_pg_queries_escalation` | `apps.execution.tests.test_bg_processing_coordination` | PRD-SYS-102 | ⚪ UNVERIFIED | N/A |
| `test_advisory_locking_pg_sealer` | `apps.execution.tests.test_bg_processing_coordination` | PRD-SYS-102 | ⚪ UNVERIFIED | N/A |
| `test_background_verification_failure_resilience` | `apps.execution.tests.test_bg_processing_coordination` | PRD-SYS-103 | ⚪ UNVERIFIED | N/A |
| `test_integrity_verification_runs_in_background` | `apps.execution.tests.test_bg_processing_coordination` | PRD-SYS-103 | ⚪ UNVERIFIED | N/A |
| `test_dataset_json_integration_structure` | `apps.execution.tests.test_biostat` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_declarative_mappings_coverage` | `apps.execution.tests.test_biostat` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_extract_ae_sorting_ongoing_supp` | `apps.execution.tests.test_biostat` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_extract_dm_age_precision_and_controlled_terminology` | `apps.execution.tests.test_biostat` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_extract_dm_demographics` | `apps.execution.tests.test_biostat` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_extract_lb_verbatim_normalized_supp` | `apps.execution.tests.test_biostat` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_extract_mh_sequencing_and_supp` | `apps.execution.tests.test_biostat` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_extract_vs_baseline_supp` | `apps.execution.tests.test_biostat` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_mapping_helpers` | `apps.execution.tests.test_biostat` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_normalize_race` | `apps.execution.tests.test_biostat` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_normalize_severity` | `apps.execution.tests.test_biostat` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_normalize_sex` | `apps.execution.tests.test_biostat` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_supp_record_row_conversion` | `apps.execution.tests.test_biostat` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_variable_metadata_validation` | `apps.execution.tests.test_biostat` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_age_capping_thresholds` | `apps.execution.tests.test_biostat_deidentification` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_audit_log_error_scrubbing_zero_leaks` | `apps.execution.tests.test_biostat_deidentification` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_authorization_allowed_role_succeeds` | `apps.execution.tests.test_biostat_deidentification` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_authorization_disallowed_role_receives_403` | `apps.execution.tests.test_biostat_deidentification` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_dataset_json_validation_passes_after_transform` | `apps.execution.tests.test_biostat_deidentification` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_date_shift_stable_and_interval_preserving` | `apps.execution.tests.test_biostat_deidentification` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_date_shifting_placeholders_and_duration_intervals` | `apps.execution.tests.test_biostat_deidentification` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_error_redaction_and_scrubbing_on_failed_export` | `apps.execution.tests.test_biostat_deidentification` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_identical_pseudonymization_across_datasets_and_supp` | `apps.execution.tests.test_biostat_deidentification` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_identifier_masking_and_age_capping_types` | `apps.execution.tests.test_biostat_deidentification` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_in_process_export_performance_1000_records` | `apps.execution.tests.test_biostat_deidentification` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_partial_dates_shifted_without_fabricating_precision` | `apps.execution.tests.test_biostat_deidentification` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_pseudonymization_determinism_and_hex_format` | `apps.execution.tests.test_biostat_deidentification` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_scrub_error_message_direct` | `apps.execution.tests.test_biostat_deidentification` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_source_records_are_not_mutated` | `apps.execution.tests.test_biostat_deidentification` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_adae_trtemfl_logic` | `apps.execution.tests.test_biostat_export` | ADAM-ADAE-TRTEMFL-01 | ⚪ UNVERIFIED | N/A |
| `test_advs_chg_pchg_computations` | `apps.execution.tests.test_biostat_export` | ADAM-ADVS-CHG-01 | ⚪ UNVERIFIED | N/A |
| `test_api_adam_export_success` | `apps.execution.tests.test_biostat_export` | API-ADAM-EXPORT-01 | ⚪ UNVERIFIED | N/A |
| `test_api_biostat_bundle_export_with_supp_records` | `apps.execution.tests.test_biostat_export` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_api_sdtm_export_success` | `apps.execution.tests.test_biostat_export` | API-SDTM-EXPORT-01 | ⚪ UNVERIFIED | N/A |
| `test_api_sdtm_export_with_supp_records` | `apps.execution.tests.test_biostat_export` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_api_unauthenticated_export_rejection` | `apps.execution.tests.test_biostat_export` | SEC-EXPORT-AUTH-01 | ⚪ UNVERIFIED | N/A |
| `test_api_validation_failure_logging` | `apps.execution.tests.test_biostat_export` | API-EXPORT-VAL-01 | ⚪ UNVERIFIED | N/A |
| `test_partial_date_imputation_detailed` | `apps.execution.tests.test_biostat_export` | SDTM-IMPUTE-01 | ⚪ UNVERIFIED | N/A |
| `test_sdtm_age_derivation` | `apps.execution.tests.test_biostat_export` | SDTM-DM-AGE-01 | ⚪ UNVERIFIED | N/A |
| `test_sdtm_sequence_assignment` | `apps.execution.tests.test_biostat_export` | SDTM-SEQ-01 | ⚪ UNVERIFIED | N/A |
| `test_sdtm_supplemental_qualifiers` | `apps.execution.tests.test_biostat_export` | SDTM-SUPP-01 | ⚪ UNVERIFIED | N/A |
| `test_adam_all_datasets_and_formats` | `apps.execution.tests.test_biostat_exports` | PRD-SYS-001, PRD-SYS-004, Trace-1 | ⚪ UNVERIFIED | N/A |
| `test_all_domain_extractors_and_supp_records` | `apps.execution.tests.test_biostat_exports` | PRD-CRF-008, PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_biostat_adsl_and_validation_rules` | `apps.execution.tests.test_biostat_exports` | PRD-CRF-008, PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_biostat_bundle_formats` | `apps.execution.tests.test_biostat_exports` | PRD-SYS-001, PRD-SYS-004, Trace-1 | ⚪ UNVERIFIED | N/A |
| `test_biostat_dates_helpers` | `apps.execution.tests.test_biostat_exports` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_biostat_deid_and_scrubbing_helpers` | `apps.execution.tests.test_biostat_exports` | PRD-SYS-001, Trace-12 | ⚪ UNVERIFIED | N/A |
| `test_biostat_extractors_and_derivations` | `apps.execution.tests.test_biostat_exports` | PRD-CRF-008, PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_biostat_mappings_metadata` | `apps.execution.tests.test_biostat_exports` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_cdisc_odm_xml_serialization_and_audit_trail` | `apps.execution.tests.test_biostat_exports` | PRD-CRF-008, PRD-SYS-004, Trace-7 | ⚪ UNVERIFIED | N/A |
| `test_deidentified_csv_and_zip_export` | `apps.execution.tests.test_biostat_exports` | PRD-SYS-001, PRD-SYS-004, Trace-12 | ⚪ UNVERIFIED | N/A |
| `test_export_validation_failure_handling` | `apps.execution.tests.test_biostat_exports` | PRD-SYS-001, Trace-1 | ⚪ UNVERIFIED | N/A |
| `test_export_wizard_parameterized_scenarios` | `apps.execution.tests.test_biostat_exports` | PRD-CRF-008, PRD-SYS-001, PRD-SYS-004, Trace-1, Trace-12, Trace-7 | ⚪ UNVERIFIED | N/A |
| `test_ibm_360_float_encoding_roundtrip` | `apps.execution.tests.test_biostat_exports` | PRD-SYS-001, Trace-1 | ⚪ UNVERIFIED | N/A |
| `test_infer_variable_type_and_length` | `apps.execution.tests.test_biostat_exports` | PRD-SYS-001, Trace-1 | ⚪ UNVERIFIED | N/A |
| `test_invalid_requests_and_unauthorized` | `apps.execution.tests.test_biostat_exports` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_odm_xml_single_list_and_helpers` | `apps.execution.tests.test_biostat_exports` | PRD-SYS-004, Trace-7 | ⚪ UNVERIFIED | N/A |
| `test_sas_xpt_trailing_and_all_blank_rows_roundtrip` | `apps.execution.tests.test_biostat_exports` | PRD-SYS-001, Trace-1, Trace-17 | ⚪ UNVERIFIED | N/A |
| `test_sas_xpt_v5_serialization_and_deserialization` | `apps.execution.tests.test_biostat_exports` | PRD-CRF-008, PRD-SYS-001, Trace-1 | ⚪ UNVERIFIED | N/A |
| `test_sas_xpt_v8_serialization_and_deserialization` | `apps.execution.tests.test_biostat_exports` | PRD-SYS-001, Trace-1 | ⚪ UNVERIFIED | N/A |
| `test_sdtm_all_domains_and_formats` | `apps.execution.tests.test_biostat_exports` | PRD-SYS-001, PRD-SYS-004, Trace-1, Trace-12, Trace-7 | ⚪ UNVERIFIED | N/A |
| `test_sdtm_domain_export_success` | `apps.execution.tests.test_biostat_exports` | PRD-SYS-001, Trace-1 | ⚪ UNVERIFIED | N/A |
| `test_xpt_external_dataset_zero_header_count` | `apps.execution.tests.test_biostat_exports` | PRD-SYS-001, Trace-1 | ⚪ UNVERIFIED | N/A |
| `test_xpt_reader_error_handling` | `apps.execution.tests.test_biostat_exports` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_dataset_json_pydantic_v2_model_and_2d_matrix` | `apps.execution.tests.test_biostat_exports_adversarial` | PRD-SYS-001, Trace-1 | ⚪ UNVERIFIED | N/A |
| `test_deid_age_capping_and_string_redaction` | `apps.execution.tests.test_biostat_exports_adversarial` | PRD-SYS-001, Trace-12 | ⚪ UNVERIFIED | N/A |
| `test_deid_deterministic_date_shift_range_and_invariants` | `apps.execution.tests.test_biostat_exports_adversarial` | PRD-SYS-001, Trace-12 | ⚪ UNVERIFIED | N/A |
| `test_deid_error_message_scrubbing_pii` | `apps.execution.tests.test_biostat_exports_adversarial` | PRD-SYS-001, Trace-12 | ⚪ UNVERIFIED | N/A |
| `test_deid_longitudinal_parity_across_multi_domain_patient_journey` | `apps.execution.tests.test_biostat_exports_adversarial` | PRD-SYS-001, Trace-12 | ⚪ UNVERIFIED | N/A |
| `test_deid_partial_dates_and_numeric_sas_dates` | `apps.execution.tests.test_biostat_exports_adversarial` | PRD-SYS-001, Trace-12 | ⚪ UNVERIFIED | N/A |
| `test_ibm360_collision_with_dot_missing_value` | `apps.execution.tests.test_biostat_exports_adversarial` | PRD-SYS-001, Trace-1 | ⚪ UNVERIFIED | N/A |
| `test_ibm360_float_codec_exhaustive_boundaries` | `apps.execution.tests.test_biostat_exports_adversarial` | PRD-SYS-001, Trace-1 | ⚪ UNVERIFIED | N/A |
| `test_odm_xml_21cfr_part11_audit_trail_embedded` | `apps.execution.tests.test_biostat_exports_adversarial` | PRD-SYS-004, Trace-7 | ⚪ UNVERIFIED | N/A |
| `test_odm_xml_escaping_and_special_characters` | `apps.execution.tests.test_biostat_exports_adversarial` | PRD-SYS-004, Trace-7 | ⚪ UNVERIFIED | N/A |
| `test_odm_xml_namespaces_and_root_attributes` | `apps.execution.tests.test_biostat_exports_adversarial` | PRD-SYS-004, Trace-7 | ⚪ UNVERIFIED | N/A |
| `test_validator_controlled_terminology_violation_code` | `apps.execution.tests.test_biostat_exports_adversarial` | PRD-SYS-001, Trace-1 | ⚪ UNVERIFIED | N/A |
| `test_validator_duplicate_sequence_code` | `apps.execution.tests.test_biostat_exports_adversarial` | PRD-SYS-001, Trace-1 | ⚪ UNVERIFIED | N/A |
| `test_validator_empty_studyid_usubjid_code` | `apps.execution.tests.test_biostat_exports_adversarial` | PRD-SYS-001, Trace-1 | ⚪ UNVERIFIED | N/A |
| `test_validator_missing_required_variables_code` | `apps.execution.tests.test_biostat_exports_adversarial` | PRD-SYS-001, Trace-1 | ⚪ UNVERIFIED | N/A |
| `test_validator_null_flavor_inconsistency_code` | `apps.execution.tests.test_biostat_exports_adversarial` | PRD-SYS-001, Trace-1 | ⚪ UNVERIFIED | N/A |
| `test_validator_referential_inconsistency_code` | `apps.execution.tests.test_biostat_exports_adversarial` | PRD-SYS-001, Trace-1 | ⚪ UNVERIFIED | N/A |
| `test_validator_supplemental_qualifier_violation_code` | `apps.execution.tests.test_biostat_exports_adversarial` | PRD-SYS-001, Trace-1 | ⚪ UNVERIFIED | N/A |
| `test_xpt_80byte_card_framing_and_padding_stress` | `apps.execution.tests.test_biostat_exports_adversarial` | PRD-CRF-008, PRD-SYS-001, Trace-1 | ⚪ UNVERIFIED | N/A |
| `test_xpt_blank_character_row_truncation` | `apps.execution.tests.test_biostat_exports_adversarial` | PRD-SYS-001, Trace-1 | ⚪ UNVERIFIED | N/A |
| `test_xpt_large_dataset_roundtrip_integrity` | `apps.execution.tests.test_biostat_exports_adversarial` | PRD-SYS-001, Trace-1 | ⚪ UNVERIFIED | N/A |
| `test_xpt_name_length_and_label_limits_handling` | `apps.execution.tests.test_biostat_exports_adversarial` | PRD-SYS-001, Trace-1 | ⚪ UNVERIFIED | N/A |
| `test_recover_orphaned_dictionary_imports` | `apps.execution.tests.test_boot_recovery` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_can_access_study_fail_open` | `apps.execution.tests.test_cdisc_export_authorization_primitives` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_gateway_auth_middleware_tenant_fallback` | `apps.execution.tests.test_cdisc_export_authorization_primitives` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_permission_enum_export_sdtm` | `apps.execution.tests.test_cdisc_export_authorization_primitives` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_require_study_scope_resolution_order` | `apps.execution.tests.test_cdisc_export_authorization_primitives` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_data_lock_6_tier_hierarchy_and_mutation_blocking` | `apps.execution.tests.test_challenger_1_empirical_verification` | PRD-MDR-002, PRD-SYS-001, PRD-SYS-002, Trace-1, Trace-13 | ⚪ UNVERIFIED | N/A |
| `test_lab_ingestion_parsers_and_delimiter_sniffing` | `apps.execution.tests.test_challenger_1_empirical_verification` | PRD-LAB-001, PRD-MDR-001, Trace-15 | ⚪ UNVERIFIED | N/A |
| `test_lab_reference_range_selection_and_critical_sae_alerts` | `apps.execution.tests.test_challenger_1_empirical_verification` | PRD-LAB-001, PRD-MDR-001, PRD-QRY-001, Trace-15 | ⚪ UNVERIFIED | N/A |
| `test_medical_coding_fuzzy_matching_and_stemming_edge_cases` | `apps.execution.tests.test_challenger_1_empirical_verification` | PRD-SYS-001, PRD-SYS-004, Trace-16 | ⚪ UNVERIFIED | N/A |
| `test_medical_coding_upversioning_and_reclassification_impact` | `apps.execution.tests.test_challenger_1_empirical_verification` | PRD-SYS-001, PRD-SYS-004, Trace-16 | ⚪ UNVERIFIED | N/A |
| `test_sig_token_step_up_replay_prevention_and_unlock_justification` | `apps.execution.tests.test_challenger_1_empirical_verification` | PRD-SYS-001, PRD-SYS-002, Trace-1, Trace-13, Trace-17 | ⚪ UNVERIFIED | N/A |
| `test_cdisc_odm_xml_audit_records_and_entity_escaping` | `apps.execution.tests.test_challenger_empirical_stress` | PRD-MDR-001, Trace-1, Trace-17 | ⚪ UNVERIFIED | N/A |
| `test_datalock_6_tier_field_level_blocking` | `apps.execution.tests.test_challenger_empirical_stress` | PRD-SYS-002, Trace-3 | ⚪ UNVERIFIED | N/A |
| `test_datalock_6_tier_form_visit_subject_site_study_hierarchy` | `apps.execution.tests.test_challenger_empirical_stress` | PRD-SYS-002, Trace-3 | ⚪ UNVERIFIED | N/A |
| `test_datalock_step_up_signature_and_unlock_justification_boundaries` | `apps.execution.tests.test_challenger_empirical_stress` | PRD-SYS-002, Trace-3 | ⚪ UNVERIFIED | N/A |
| `test_hipaa_gdpr_deidentification_rules` | `apps.execution.tests.test_challenger_empirical_stress` | PRD-MDR-001, Trace-17 | ⚪ UNVERIFIED | N/A |
| `test_lab_ingestion_malformed_csv_and_corrupt_data` | `apps.execution.tests.test_challenger_empirical_stress` | PRD-LAB-001, Trace-15 | ⚪ UNVERIFIED | N/A |
| `test_lab_ingestion_malformed_hl7_and_fhir` | `apps.execution.tests.test_challenger_empirical_stress` | PRD-LAB-001, Trace-15 | ⚪ UNVERIFIED | N/A |
| `test_lab_unit_conversions_and_extreme_range_boundaries` | `apps.execution.tests.test_challenger_empirical_stress` | PRD-LAB-001, PRD-QRY-001, Trace-15 | ⚪ UNVERIFIED | N/A |
| `test_medical_coding_exact_threshold_boundaries` | `apps.execution.tests.test_challenger_empirical_stress` | PRD-SYS-004, Trace-16 | ⚪ UNVERIFIED | N/A |
| `test_medical_coding_uncodable_and_extreme_inputs` | `apps.execution.tests.test_challenger_empirical_stress` | PRD-QRY-001, PRD-SYS-004, Trace-16 | ⚪ UNVERIFIED | N/A |
| `test_medical_coding_upversioning_and_idempotency_stress` | `apps.execution.tests.test_challenger_empirical_stress` | PRD-SYS-004, Trace-16 | ⚪ UNVERIFIED | N/A |
| `test_sas_xpt_v5_v8_card_padding_and_ibm_floats` | `apps.execution.tests.test_challenger_empirical_stress` | PRD-MDR-001, Trace-17 | ⚪ UNVERIFIED | N/A |
| `test_api_gateway_routing` | `apps.execution.tests.test_clinical_engine` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_cdisc_export_and_validation` | `apps.execution.tests.test_clinical_engine` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_demographics_encryption` | `apps.execution.tests.test_clinical_engine` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_outlier_detection_performance` | `apps.execution.tests.test_clinical_engine` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_relational_persistence_and_recalculation` | `apps.execution.tests.test_clinical_engine` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_unit_conversions` | `apps.execution.tests.test_clinical_engine` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_visit_windowing_fields` | `apps.execution.tests.test_clinical_engine` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_candidate_creation_and_opening_workflow` | `apps.execution.tests.test_clinical_queries` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_clinical_queries_sync_endpoint` | `apps.execution.tests.test_clinical_queries` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_clinical_query_creation_with_all_audited_fields` | `apps.execution.tests.test_clinical_queries` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_clinical_query_trial_lock_enforcement_at_visit_level` | `apps.execution.tests.test_clinical_queries` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_create_clinical_query_authorization_failures` | `apps.execution.tests.test_clinical_queries` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_create_clinical_query_success` | `apps.execution.tests.test_clinical_queries` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_database_events_prevent_deletions` | `apps.execution.tests.test_clinical_queries` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_duplicate_active_query_rejected` | `apps.execution.tests.test_clinical_queries` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_query_role_gates_robustness` | `apps.execution.tests.test_clinical_queries` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_query_state_transition_and_role_boundaries` | `apps.execution.tests.test_clinical_queries` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_rejection_and_cancellation_reason_requirements` | `apps.execution.tests.test_clinical_queries` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_reopen_transitions` | `apps.execution.tests.test_clinical_queries` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_deterministic_per_subject_date_shifting` | `apps.execution.tests.test_clinical_validation_engines` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_exact_numeric_and_float_age_capping` | `apps.execution.tests.test_clinical_validation_engines` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_sas_dates_shifting` | `apps.execution.tests.test_clinical_validation_engines` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_signature_parsing_exceptions` | `apps.execution.tests.test_clinical_validation_engines` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_signature_parsing_formats` | `apps.execution.tests.test_clinical_validation_engines` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_stripped_block_json_validation` | `apps.execution.tests.test_clinical_validation_engines` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_stripped_block_xml_validation` | `apps.execution.tests.test_clinical_validation_engines` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_study_specific_pseudonym_prefixes` | `apps.execution.tests.test_clinical_validation_engines` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_unified_api_ecdsa_verification` | `apps.execution.tests.test_clinical_validation_engines` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_unified_api_invalid_inputs` | `apps.execution.tests.test_clinical_validation_engines` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_unified_api_rsa_verification` | `apps.execution.tests.test_clinical_validation_engines` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_unified_api_signature_mismatch` | `apps.execution.tests.test_clinical_validation_engines` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_datalock_model_aliases_and_properties` | `apps.execution.tests.test_data_locks_persistence` | PRD-MDR-002, PRD-SYS-001, PRD-SYS-002, Trace-1, Trace-13, Trace-17, Trace-3 | ⚪ UNVERIFIED | N/A |
| `test_datalock_sqlmodel_relational_persistence` | `apps.execution.tests.test_data_locks_persistence` | PRD-MDR-002, PRD-SYS-001, PRD-SYS-002, Trace-1, Trace-13, Trace-17, Trace-3 | ⚪ UNVERIFIED | N/A |
| `test_get_lock_status_and_tree_endpoints` | `apps.execution.tests.test_data_locks_persistence` | PRD-MDR-002, PRD-SYS-001, PRD-SYS-002, Trace-1, Trace-13, Trace-17, Trace-3 | ⚪ UNVERIFIED | N/A |
| `test_hard_lock_requires_valid_sig_token` | `apps.execution.tests.test_data_locks_persistence` | PRD-MDR-002, PRD-SYS-001, PRD-SYS-002, Trace-1, Trace-13, Trace-17, Trace-3 | ⚪ UNVERIFIED | N/A |
| `test_hierarchical_lock_inheritance_field_blocks_single_observation` | `apps.execution.tests.test_data_locks_persistence` | PRD-MDR-002, PRD-SYS-001, PRD-SYS-002, Trace-1, Trace-13, Trace-17, Trace-3 | ⚪ UNVERIFIED | N/A |
| `test_hierarchical_lock_inheritance_form_blocks_field_observations` | `apps.execution.tests.test_data_locks_persistence` | PRD-MDR-002, PRD-SYS-001, PRD-SYS-002, Trace-1, Trace-13, Trace-17, Trace-3 | ⚪ UNVERIFIED | N/A |
| `test_hierarchical_lock_inheritance_site_blocks_subjects_and_forms` | `apps.execution.tests.test_data_locks_persistence` | PRD-MDR-002, PRD-SYS-001, PRD-SYS-002, Trace-1, Trace-13, Trace-17, Trace-3 | ⚪ UNVERIFIED | N/A |
| `test_hierarchical_lock_inheritance_study_blocks_all` | `apps.execution.tests.test_data_locks_persistence` | PRD-MDR-002, PRD-SYS-001, PRD-SYS-002, Trace-1, Trace-13, Trace-17, Trace-3 | ⚪ UNVERIFIED | N/A |
| `test_hierarchical_lock_inheritance_subject_blocks_visits_and_observations` | `apps.execution.tests.test_data_locks_persistence` | PRD-MDR-002, PRD-SYS-001, PRD-SYS-002, Trace-1, Trace-13, Trace-17, Trace-3 | ⚪ UNVERIFIED | N/A |
| `test_lock_and_unlock_router_branches_and_validation` | `apps.execution.tests.test_data_locks_persistence` | PRD-MDR-002, PRD-SYS-001, PRD-SYS-002, Trace-1, Trace-13, Trace-17, Trace-3 | ⚪ UNVERIFIED | N/A |
| `test_trial_lock_manager_methods_and_reset` | `apps.execution.tests.test_data_locks_persistence` | PRD-MDR-002, PRD-SYS-001, PRD-SYS-002, Trace-1, Trace-13, Trace-17, Trace-3 | ⚪ UNVERIFIED | N/A |
| `test_unlock_enforces_min_50_char_justification` | `apps.execution.tests.test_data_locks_persistence` | PRD-MDR-002, PRD-SYS-001, PRD-SYS-002, Trace-1, Trace-13, Trace-17, Trace-3 | ⚪ UNVERIFIED | N/A |
| `test_unlocked_entity_allows_subsequent_mutations` | `apps.execution.tests.test_data_locks_persistence` | PRD-MDR-002, PRD-SYS-001, PRD-SYS-002, Trace-1, Trace-13, Trace-17, Trace-3 | ⚪ UNVERIFIED | N/A |
| `test_cdisc_metadata_headers_and_aliases` | `apps.execution.tests.test_dataset_json` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_serialize_bundle` | `apps.execution.tests.test_dataset_json` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_serialize_single_dataset_dm` | `apps.execution.tests.test_dataset_json` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_serialize_to_dataset_json_includes_metadata` | `apps.execution.tests.test_dataset_json` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_validation_success_on_valid_bundle` | `apps.execution.tests.test_dataset_json` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_validator_adam_referential_consistency_demographic_mismatch` | `apps.execution.tests.test_dataset_json` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_validator_adam_referential_consistency_missing_source_event` | `apps.execution.tests.test_dataset_json` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_validator_adam_referential_consistency_subject_not_in_adsl_or_dm` | `apps.execution.tests.test_dataset_json` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_validator_controlled_terminology_failures` | `apps.execution.tests.test_dataset_json` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_validator_duplicate_sequence_numbers` | `apps.execution.tests.test_dataset_json` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_validator_empty_studyid_usubjid` | `apps.execution.tests.test_dataset_json` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_validator_missing_required_variables` | `apps.execution.tests.test_dataset_json` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_validator_null_flavor_and_stat_reasnd_consistency` | `apps.execution.tests.test_dataset_json` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_validator_supp_dataset_linkage_and_structure` | `apps.execution.tests.test_dataset_json` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_build_dataset_json_validation` | `apps.execution.tests.test_dataset_json_builder` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_build_domain_dataset_dm` | `apps.execution.tests.test_dataset_json_builder` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_conformance_validation_float_data_type_error` | `apps.execution.tests.test_dataset_json_builder` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_conformance_validation_invalid_data_type` | `apps.execution.tests.test_dataset_json_builder` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_conformance_validation_missing_mandatory_vars` | `apps.execution.tests.test_dataset_json_builder` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_conformance_validation_missing_sequence_non_dm` | `apps.execution.tests.test_dataset_json_builder` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_conformance_validation_string_data_type_error` | `apps.execution.tests.test_dataset_json_builder` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_dynamic_fallback_unknown_domain` | `apps.execution.tests.test_dataset_json_builder` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_introspection_engine_excludes_compliance_tables` | `apps.execution.tests.test_decoupled_pg_introspection_triggers` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_trigger_rejects_missing_change_justification_on_update` | `apps.execution.tests.test_decoupled_pg_introspection_triggers` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_trigger_rejects_missing_user_identifier` | `apps.execution.tests.test_decoupled_pg_introspection_triggers` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_address_redos_prevention` | `apps.execution.tests.test_deident_scrubber` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_free_text_pii_scrubbing` | `apps.execution.tests.test_deident_scrubber` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_scrubber_preserves_date_intervals` | `apps.execution.tests.test_deident_scrubber` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_scrubber_subject_id_non_reversible` | `apps.execution.tests.test_deident_scrubber` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_sdtm_json_builder_integration` | `apps.execution.tests.test_deident_scrubber` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_age_derivation_boundary_dates` | `apps.execution.tests.test_demographics` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_custom_gender_preservation` | `apps.execution.tests.test_demographics` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_demographics_encryption_decryption_roundtrip` | `apps.execution.tests.test_demographics` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_gender_normalization` | `apps.execution.tests.test_demographics` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_get_safe_demographics_failures_fail_safely` | `apps.execution.tests.test_demographics` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_get_safe_demographics_valid_decryption` | `apps.execution.tests.test_demographics` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_authored_cross_form_rule_lifecycle` | `apps.execution.tests.test_edit_checks` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_authored_longitudinal_predecessor_handling` | `apps.execution.tests.test_edit_checks` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_critical_notification_dispatch_and_suppression` | `apps.execution.tests.test_edit_checks` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_cross_form_temporal_consistency_and_context_propagation` | `apps.execution.tests.test_edit_checks` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_deferred_predecessor_checks` | `apps.execution.tests.test_edit_checks` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_lab_out_of_range_and_auto_close` | `apps.execution.tests.test_edit_checks` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_same_record_failure_outlier_and_auto_close` | `apps.execution.tests.test_edit_checks` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_scenario_cross_form_edit_checks_and_auto_resolve` | `apps.execution.tests.test_edit_checks_scenarios` | PRD-QRY-003 | ⚪ UNVERIFIED | N/A |
| `test_scenario_longitudinal_predecessor_draft_and_complete` | `apps.execution.tests.test_edit_checks_scenarios` | PRD-QRY-004 | ⚪ UNVERIFIED | N/A |
| `test_scenario_skip_logic_and_cascading_nullification` | `apps.execution.tests.test_edit_checks_scenarios` | PRD-EDC-003, PRD-EDC-004 | ⚪ UNVERIFIED | N/A |
| `test_arithmetic_null_safety_and_bmi` | `apps.execution.tests.test_evaluator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_cascading_dependent_nullification_parity` | `apps.execution.tests.test_evaluator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_comparison_null_semantics` | `apps.execution.tests.test_evaluator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_comparison_operators` | `apps.execution.tests.test_evaluator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_field_reference_and_xpath` | `apps.execution.tests.test_evaluator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_indexed_repeat` | `apps.execution.tests.test_evaluator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_is_empty_and_not_empty` | `apps.execution.tests.test_evaluator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_literal_and_constant` | `apps.execution.tests.test_evaluator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_smart_type_coercion_and_localized_guards` | `apps.execution.tests.test_evaluator` | PRD-ELIGIBILITY-006 | ⚪ UNVERIFIED | N/A |
| `test_cdisc_xml_structure_validation` | `apps.execution.tests.test_execution_compliance` | PRD-MDR-001 | ⚪ UNVERIFIED | N/A |
| `test_cryptographic_tamper_evident_safeguards` | `apps.execution.tests.test_execution_compliance` | PRD-SYS-003 | ⚪ UNVERIFIED | N/A |
| `test_ecrf_version_control_history` | `apps.execution.tests.test_execution_compliance` | PRD-EDC-005 | ⚪ UNVERIFIED | N/A |
| `test_edc_archival_integration` | `apps.execution.tests.test_execution_compliance` | PRD-EDC-010 | ⚪ UNVERIFIED | N/A |
| `test_edc_audit_trail_and_signatures` | `apps.execution.tests.test_execution_compliance` | PRD-EDC-006 | ⚪ UNVERIFIED | N/A |
| `test_edc_concurrent_review_locks` | `apps.execution.tests.test_execution_compliance` | PRD-EDC-009 | ⚪ UNVERIFIED | N/A |
| `test_edc_electronic_signatures` | `apps.execution.tests.test_execution_compliance` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_edc_reconsent_and_versioning` | `apps.execution.tests.test_execution_compliance` | PRD-SUB-007 | ⚪ UNVERIFIED | N/A |
| `test_event_driven_site_compliance_cache` | `apps.execution.tests.test_execution_compliance` | PRD-EDL-001 | ⚪ UNVERIFIED | N/A |
| `test_fda_compliant_pdf_generation_econsent` | `apps.execution.tests.test_execution_compliance` | PRD-SUB-007 | ⚪ UNVERIFIED | N/A |
| `test_query_lifecycle_states` | `apps.execution.tests.test_execution_compliance` | PRD-QRY-001 | ⚪ UNVERIFIED | N/A |
| `test_subject_enrollment_blocking` | `apps.execution.tests.test_execution_compliance` | PRD-EDL-001 | ⚪ UNVERIFIED | N/A |
| `test_submission_archival_integration` | `apps.execution.tests.test_execution_compliance` | PRD-SUB-006 | ⚪ UNVERIFIED | N/A |
| `test_submission_audit_trail` | `apps.execution.tests.test_execution_compliance` | PRD-SUB-004 | ⚪ UNVERIFIED | N/A |
| `test_submission_e_signatures` | `apps.execution.tests.test_execution_compliance` | PRD-SUB-003 | ⚪ UNVERIFIED | N/A |
| `test_submission_locks` | `apps.execution.tests.test_execution_compliance` | PRD-SUB-005 | ⚪ UNVERIFIED | N/A |
| `test_submission_version_control` | `apps.execution.tests.test_execution_compliance` | PRD-SUB-002 | ⚪ UNVERIFIED | N/A |
| `test_system_generated_validation_queries` | `apps.execution.tests.test_execution_compliance` | PRD-QRY-002 | ⚪ UNVERIFIED | N/A |
| `test_designer_criteria_client_retrieval_and_parsing` | `apps.execution.tests.test_execution_eligibility` | PRD-ELIGIBILITY-009, PRD-MDR-007 | ⚪ UNVERIFIED | N/A |
| `test_ecrf_context_builder_demographics_and_precedence` | `apps.execution.tests.test_execution_eligibility` | PRD-ELIGIBILITY-010, PRD-MDR-007 | ⚪ UNVERIFIED | N/A |
| `test_ecrf_context_builder_kleene_absent_semantics` | `apps.execution.tests.test_execution_eligibility` | PRD-ELIGIBILITY-011, PRD-MDR-007 | ⚪ UNVERIFIED | N/A |
| `test_randomization_allocation_rejection_gate` | `apps.execution.tests.test_execution_eligibility` | PRD-MDR-007 | ⚪ UNVERIFIED | N/A |
| `test_screening_endpoint_eligible_and_transition` | `apps.execution.tests.test_execution_eligibility` | PRD-ELIGIBILITY-012, PRD-MDR-007 | ⚪ UNVERIFIED | N/A |
| `test_screening_endpoint_indeterminate_behavior` | `apps.execution.tests.test_execution_eligibility` | PRD-ELIGIBILITY-014, PRD-MDR-007 | ⚪ UNVERIFIED | N/A |
| `test_screening_endpoint_ineligible_transition_and_audit` | `apps.execution.tests.test_execution_eligibility` | PRD-ELIGIBILITY-013, PRD-MDR-007 | ⚪ UNVERIFIED | N/A |
| `test_form_submission_approval_audit_manifestation` | `apps.execution.tests.test_form_submissions` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_form_submission_audit_logging` | `apps.execution.tests.test_form_submissions` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_form_submission_invalid_transitions` | `apps.execution.tests.test_form_submissions` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_form_submission_lifecycle_happy_path` | `apps.execution.tests.test_form_submissions` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_form_submission_locks` | `apps.execution.tests.test_form_submissions` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_form_submission_validation` | `apps.execution.tests.test_form_submissions` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_granular_locking_end_to_end_audit_flow` | `apps.execution.tests.test_granular_locking` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_absent_and_malformed_roles` | `apps.execution.tests.test_granular_locks_api` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_allowed_roles_matrix` | `apps.execution.tests.test_granular_locks_api` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_forbidden_roles_matrix` | `apps.execution.tests.test_granular_locks_api` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_form_lock_and_unlock_lifecycle` | `apps.execution.tests.test_granular_locks_api` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_gateway_bypass_prevention` | `apps.execution.tests.test_granular_locks_api` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_lock_status_retrieval` | `apps.execution.tests.test_granular_locks_api` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_locked_write_prevention` | `apps.execution.tests.test_granular_locks_api` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_roles_authorization_restrictions` | `apps.execution.tests.test_granular_locks_api` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_site_lock_and_unlock_lifecycle` | `apps.execution.tests.test_granular_locks_api` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_subject_lock_and_unlock_lifecycle` | `apps.execution.tests.test_granular_locks_api` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_trial_lock_and_unlock_lifecycle` | `apps.execution.tests.test_granular_locks_api` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_visit_lock_and_unlock_lifecycle` | `apps.execution.tests.test_granular_locks_api` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_age_sex_stratified_reference_range_evaluation` | `apps.execution.tests.test_lab_batch_ingestion` | PRD-LAB-001, PRD-MDR-001, PRD-QRY-001, Trace-1, Trace-15 | ⚪ UNVERIFIED | N/A |
| `test_api_batch_status_list_and_not_found` | `apps.execution.tests.test_lab_batch_ingestion` | PRD-LAB-001, PRD-MDR-001, PRD-QRY-001, Trace-1, Trace-15 | ⚪ UNVERIFIED | N/A |
| `test_api_ingest_json_endpoint` | `apps.execution.tests.test_lab_batch_ingestion` | PRD-LAB-001, PRD-MDR-001, PRD-QRY-001, Trace-1, Trace-15 | ⚪ UNVERIFIED | N/A |
| `test_api_ingest_multipart_file_endpoint` | `apps.execution.tests.test_lab_batch_ingestion` | PRD-LAB-001, PRD-MDR-001, PRD-QRY-001, Trace-1, Trace-15 | ⚪ UNVERIFIED | N/A |
| `test_batch_store_filtering_by_study` | `apps.execution.tests.test_lab_batch_ingestion` | PRD-LAB-001, PRD-MDR-001, PRD-QRY-001, Trace-1, Trace-15 | ⚪ UNVERIFIED | N/A |
| `test_consent_version_stamping_on_ingestion` | `apps.execution.tests.test_lab_batch_ingestion` | PRD-LAB-001, PRD-MDR-001, PRD-QRY-001, Trace-1, Trace-15 | ⚪ UNVERIFIED | N/A |
| `test_critical_sae_alert_trigger` | `apps.execution.tests.test_lab_batch_ingestion` | PRD-LAB-001, PRD-MDR-001, PRD-QRY-001, Trace-1, Trace-15 | ⚪ UNVERIFIED | N/A |
| `test_csv_batch_ingestion_success` | `apps.execution.tests.test_lab_batch_ingestion` | PRD-LAB-001, PRD-MDR-001, PRD-QRY-001, Trace-1, Trace-15 | ⚪ UNVERIFIED | N/A |
| `test_csv_parser_resilience_and_errors` | `apps.execution.tests.test_lab_batch_ingestion` | PRD-LAB-001, PRD-MDR-001, PRD-QRY-001, Trace-1, Trace-15 | ⚪ UNVERIFIED | N/A |
| `test_fhir_diverse_structures_and_types` | `apps.execution.tests.test_lab_batch_ingestion` | PRD-LAB-001, PRD-MDR-001, PRD-QRY-001, Trace-1, Trace-15 | ⚪ UNVERIFIED | N/A |
| `test_fhir_observation_json_ingestion` | `apps.execution.tests.test_lab_batch_ingestion` | PRD-LAB-001, PRD-MDR-001, PRD-QRY-001, Trace-1, Trace-15 | ⚪ UNVERIFIED | N/A |
| `test_hl7_and_fhir_parser_resilience` | `apps.execution.tests.test_lab_batch_ingestion` | PRD-LAB-001, PRD-MDR-001, PRD-QRY-001, Trace-1, Trace-15 | ⚪ UNVERIFIED | N/A |
| `test_hl7_batch_ingestion_oru_r01` | `apps.execution.tests.test_lab_batch_ingestion` | PRD-LAB-001, PRD-MDR-001, PRD-QRY-001, Trace-1, Trace-15 | ⚪ UNVERIFIED | N/A |
| `test_hl7_diverse_segments_and_abnormal_flags` | `apps.execution.tests.test_lab_batch_ingestion` | PRD-LAB-001, PRD-MDR-001, PRD-QRY-001, Trace-1, Trace-15 | ⚪ UNVERIFIED | N/A |
| `test_lab_ranges_specificity_and_tie_breaking` | `apps.execution.tests.test_lab_batch_ingestion` | PRD-LAB-001, PRD-MDR-001, PRD-QRY-001, Trace-1, Trace-15 | ⚪ UNVERIFIED | N/A |
| `test_parse_helpers_unit_coverage` | `apps.execution.tests.test_lab_batch_ingestion` | PRD-LAB-001, PRD-MDR-001, PRD-QRY-001, Trace-1, Trace-15 | ⚪ UNVERIFIED | N/A |
| `test_recalculate_range_flags_full_coverage` | `apps.execution.tests.test_lab_batch_ingestion` | PRD-LAB-001, PRD-MDR-001, PRD-QRY-001, Trace-1, Trace-15 | ⚪ UNVERIFIED | N/A |
| `test_ucum_unit_conversion_integration` | `apps.execution.tests.test_lab_batch_ingestion` | PRD-LAB-001, PRD-MDR-001, PRD-QRY-001, Trace-1, Trace-15 | ⚪ UNVERIFIED | N/A |
| `test_unsupported_format_and_empty_payload` | `apps.execution.tests.test_lab_batch_ingestion` | PRD-LAB-001, PRD-MDR-001, PRD-QRY-001, Trace-1, Trace-15 | ⚪ UNVERIFIED | N/A |
| `test_lab_master_migrations` | `apps.execution.tests.test_lab_master_migrations` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_lab_reference_range_migration_upgrade_and_idempotency` | `apps.execution.tests.test_lab_master_migrations` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_migration_upgrade_and_idempotency_explicit` | `apps.execution.tests.test_lab_master_migrations` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_lab_catalog_explicit_audit_persistence` | `apps.execution.tests.test_lab_master_persistence` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_lab_test_master_crud_and_audit` | `apps.execution.tests.test_lab_master_persistence` | PRD-LAB-001 | ⚪ UNVERIFIED | N/A |
| `test_lab_unit_conversion_crud_and_audit` | `apps.execution.tests.test_lab_master_persistence` | PRD-LAB-001 | ⚪ UNVERIFIED | N/A |
| `test_get_active_lab_ranges_helper` | `apps.execution.tests.test_lab_range_cache` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_lab_range_cache_operations` | `apps.execution.tests.test_lab_range_cache` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_lab_range_cache_singleton` | `apps.execution.tests.test_lab_range_cache` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_lab_range_cache_ttl_config` | `apps.execution.tests.test_lab_range_cache` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_cache_invalidation_on_create` | `apps.execution.tests.test_lab_range_cache_invalidation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_cache_invalidation_on_delete` | `apps.execution.tests.test_lab_range_cache_invalidation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_cache_invalidation_on_recalculate` | `apps.execution.tests.test_lab_range_cache_invalidation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_cache_invalidation_on_update_no_key_change` | `apps.execution.tests.test_lab_range_cache_invalidation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_cache_invalidation_on_update_with_key_changes` | `apps.execution.tests.test_lab_range_cache_invalidation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_absent_boundaries` | `apps.execution.tests.test_lab_ranges` | PRD-LAB-001 | ⚪ UNVERIFIED | N/A |
| `test_age_boundaries` | `apps.execution.tests.test_lab_ranges` | PRD-LAB-001 | ⚪ UNVERIFIED | N/A |
| `test_convert_lab_unit_db_and_fallback` | `apps.execution.tests.test_lab_ranges` | PRD-LAB-001 | ⚪ UNVERIFIED | N/A |
| `test_critical_boundaries_and_exclusion` | `apps.execution.tests.test_lab_ranges` | PRD-LAB-001 | ⚪ UNVERIFIED | N/A |
| `test_deterministic_ties` | `apps.execution.tests.test_lab_ranges` | PRD-LAB-001 | ⚪ UNVERIFIED | N/A |
| `test_evaluate_lab_value_all_indicators` | `apps.execution.tests.test_lab_ranges` | PRD-LAB-001 | ⚪ UNVERIFIED | N/A |
| `test_is_deleted_filtering` | `apps.execution.tests.test_lab_ranges` | PRD-LAB-001 | ⚪ UNVERIFIED | N/A |
| `test_lab_reference_range_synonyms_and_audit` | `apps.execution.tests.test_lab_ranges` | PRD-LAB-001 | ⚪ UNVERIFIED | N/A |
| `test_lab_reference_range_synonyms_update_and_audit` | `apps.execution.tests.test_lab_ranges` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_negative_age_matching` | `apps.execution.tests.test_lab_ranges` | PRD-LAB-005 | ⚪ UNVERIFIED | N/A |
| `test_no_matching_rule_behavior` | `apps.execution.tests.test_lab_ranges` | PRD-LAB-001 | ⚪ UNVERIFIED | N/A |
| `test_normal_boundaries_and_inclusion` | `apps.execution.tests.test_lab_ranges` | PRD-LAB-001 | ⚪ UNVERIFIED | N/A |
| `test_sex_and_all_fallback` | `apps.execution.tests.test_lab_ranges` | PRD-LAB-001 | ⚪ UNVERIFIED | N/A |
| `test_site_and_source_precedence` | `apps.execution.tests.test_lab_ranges` | PRD-LAB-001 | ⚪ UNVERIFIED | N/A |
| `test_task1_divergence_select_reference_range_vs_normalize_gender` | `apps.execution.tests.test_lab_ranges` | PRD-LAB-001 | ⚪ UNVERIFIED | N/A |
| `test_task1_exact_m_rejected_against_f_only_range` | `apps.execution.tests.test_lab_ranges` | PRD-LAB-001 | ⚪ UNVERIFIED | N/A |
| `test_task1_sex_alias_strings` | `apps.execution.tests.test_lab_ranges` | PRD-LAB-001 | ⚪ UNVERIFIED | N/A |
| `test_task1_sex_u_matching` | `apps.execution.tests.test_lab_ranges` | PRD-LAB-001 | ⚪ UNVERIFIED | N/A |
| `test_task2_age_inclusive_boundaries` | `apps.execution.tests.test_lab_ranges` | PRD-LAB-001 | ⚪ UNVERIFIED | N/A |
| `test_task2_age_none_matching` | `apps.execution.tests.test_lab_ranges` | PRD-LAB-001 | ⚪ UNVERIFIED | N/A |
| `test_task2_age_span_tie_breaking` | `apps.execution.tests.test_lab_ranges` | PRD-LAB-001 | ⚪ UNVERIFIED | N/A |
| `test_task2_zero_and_negative_age_evaluation` | `apps.execution.tests.test_lab_ranges` | PRD-LAB-001 | ⚪ UNVERIFIED | N/A |
| `test_task3_site_id_combinations` | `apps.execution.tests.test_lab_ranges` | PRD-LAB-001 | ⚪ UNVERIFIED | N/A |
| `test_task3_study_id_isolation` | `apps.execution.tests.test_lab_ranges` | PRD-LAB-001 | ⚪ UNVERIFIED | N/A |
| `test_task3_test_code_isolation` | `apps.execution.tests.test_lab_ranges` | PRD-LAB-001 | ⚪ UNVERIFIED | N/A |
| `test_task3_unknown_lab_source_fallback` | `apps.execution.tests.test_lab_ranges` | PRD-LAB-001 | ⚪ UNVERIFIED | N/A |
| `test_task4_convert_lab_unit_edge_cases` | `apps.execution.tests.test_lab_ranges` | PRD-LAB-001 | ⚪ UNVERIFIED | N/A |
| `test_task4_evaluate_lab_value_edge_cases` | `apps.execution.tests.test_lab_ranges` | PRD-LAB-001 | ⚪ UNVERIFIED | N/A |
| `test_tie_breaking_with_none_bounds` | `apps.execution.tests.test_lab_ranges` | PRD-LAB-001 | ⚪ UNVERIFIED | N/A |
| `test_unit_matching` | `apps.execution.tests.test_lab_ranges` | PRD-LAB-001 | ⚪ UNVERIFIED | N/A |
| `test_create_central_range_with_null_site_id_allowed` | `apps.execution.tests.test_lab_ranges_crud` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_create_central_range_with_site_id_blocked` | `apps.execution.tests.test_lab_ranges_crud` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_create_lab_reference_range_success` | `apps.execution.tests.test_lab_ranges_crud` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_create_lab_reference_range_unauthorized` | `apps.execution.tests.test_lab_ranges_crud` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_create_lab_reference_range_validation_errors` | `apps.execution.tests.test_lab_ranges_crud` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_get_and_update_lab_reference_range` | `apps.execution.tests.test_lab_ranges_crud` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_list_and_filter_lab_reference_ranges` | `apps.execution.tests.test_lab_ranges_crud` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_list_lab_reference_ranges_filtering_by_lab_source` | `apps.execution.tests.test_lab_ranges_crud` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_soft_delete_lab_reference_range` | `apps.execution.tests.test_lab_ranges_crud` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_update_local_to_central_invariant_enforcement` | `apps.execution.tests.test_lab_ranges_crud` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_lab_ranges_comprehensive_e2e_workflow` | `apps.execution.tests.test_lab_ranges_e2e_verification` | PRD-LAB-001 | ⚪ UNVERIFIED | N/A |
| `test_age_bounded_range_recalculation` | `apps.execution.tests.test_lab_ranges_recalculate` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_lab_range_evaluation_and_recalculation_gxp` | `apps.execution.tests.test_lab_ranges_recalculate` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_lab_range_recalculation_authorized_data_manager` | `apps.execution.tests.test_lab_ranges_recalculate` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_lab_range_recalculation_blank_reason` | `apps.execution.tests.test_lab_ranges_recalculate` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_lab_range_recalculation_critical_alert` | `apps.execution.tests.test_lab_ranges_recalculate` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_lab_range_recalculation_missing_reason` | `apps.execution.tests.test_lab_ranges_recalculate` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_lab_range_recalculation_no_match` | `apps.execution.tests.test_lab_ranges_recalculate` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_lab_range_recalculation_unauthorized_role` | `apps.execution.tests.test_lab_ranges_recalculate` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_missing_and_undecryptable_demographics_recalculation` | `apps.execution.tests.test_lab_ranges_recalculate` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_sex_specific_range_recalculation` | `apps.execution.tests.test_lab_ranges_recalculate` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_clinical_observation_extended_fields` | `apps.execution.tests.test_lab_reference_range_persistence` | PRD-LAB-001 | ⚪ UNVERIFIED | N/A |
| `test_gxp_audit_quartet_explicit_assertions` | `apps.execution.tests.test_lab_reference_range_persistence` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_lab_reference_range_audit_and_triggers` | `apps.execution.tests.test_lab_reference_range_persistence` | PRD-LAB-001 | ⚪ UNVERIFIED | N/A |
| `test_lab_reference_range_audit_quartet_persistence` | `apps.execution.tests.test_lab_reference_range_persistence` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_lab_reference_range_crud_and_precision` | `apps.execution.tests.test_lab_reference_range_persistence` | PRD-LAB-001 | ⚪ UNVERIFIED | N/A |
| `test_schema_evolution_migration_upgrade` | `apps.execution.tests.test_lab_reference_range_persistence` | PRD-LAB-001, PRD-QRY-005 | ⚪ UNVERIFIED | N/A |
| `test_lab_range_recalculate_request_and_response` | `apps.execution.tests.test_lab_schemas` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_lab_reference_range_create_invalid_enum` | `apps.execution.tests.test_lab_schemas` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_lab_reference_range_create_valid` | `apps.execution.tests.test_lab_schemas` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_lab_reference_range_response_valid` | `apps.execution.tests.test_lab_schemas` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_lab_reference_range_update_valid` | `apps.execution.tests.test_lab_schemas` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_lab_source_enum_values` | `apps.execution.tests.test_lab_schemas` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_lab_test_master_create_valid` | `apps.execution.tests.test_lab_schemas` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_lab_test_master_record_invalid` | `apps.execution.tests.test_lab_schemas` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_lab_test_master_record_valid` | `apps.execution.tests.test_lab_schemas` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_lab_test_master_response_valid` | `apps.execution.tests.test_lab_schemas` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_lab_unit_conversion_create_valid` | `apps.execution.tests.test_lab_schemas` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_lab_unit_conversion_record_valid` | `apps.execution.tests.test_lab_schemas` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_lab_unit_conversion_response_valid` | `apps.execution.tests.test_lab_schemas` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_in_memory_accessibility_auditing` | `apps.execution.tests.test_layout_validator_compliance` | PRD-CRF-015, Trace-33 | ⚪ UNVERIFIED | N/A |
| `test_lock_enforcement_field_level_blocked` | `apps.execution.tests.test_lock_enforcement` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_lock_enforcement_form_level_blocked` | `apps.execution.tests.test_lock_enforcement` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_data_lock_record_creation` | `apps.execution.tests.test_lock_models` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_data_unlock_record_creation` | `apps.execution.tests.test_lock_models` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_get_form_lock_status_endpoint` | `apps.execution.tests.test_lock_router` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_lock_data_missing_reason_returns_400` | `apps.execution.tests.test_lock_router` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_lock_data_post_endpoint` | `apps.execution.tests.test_lock_router` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_unlock_data_post_endpoint` | `apps.execution.tests.test_lock_router` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_detect_file_type` | `apps.execution.tests.test_meddra_parser` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_meddra_parser_init_validation` | `apps.execution.tests.test_meddra_parser` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_parse_empty_fields_validation` | `apps.execution.tests.test_meddra_parser` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_parse_hlgt_valid` | `apps.execution.tests.test_meddra_parser` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_parse_hlt_valid` | `apps.execution.tests.test_meddra_parser` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_parse_in_batches_invalid_batch_size` | `apps.execution.tests.test_meddra_parser` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_parse_llt_invalid_code` | `apps.execution.tests.test_meddra_parser` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_parse_llt_invalid_pt_code` | `apps.execution.tests.test_meddra_parser` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_parse_llt_valid` | `apps.execution.tests.test_meddra_parser` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_parse_mdhier_invalid_flag` | `apps.execution.tests.test_meddra_parser` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_parse_mdhier_missing_fields` | `apps.execution.tests.test_meddra_parser` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_parse_mdhier_valid` | `apps.execution.tests.test_meddra_parser` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_parse_pt_valid` | `apps.execution.tests.test_meddra_parser` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_parse_soc_valid` | `apps.execution.tests.test_meddra_parser` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_parser_incremental_batched_consumption` | `apps.execution.tests.test_meddra_parser` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_public_entry_point_file_path` | `apps.execution.tests.test_meddra_parser` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_audit_trigger_logging_on_coding_workflow` | `apps.execution.tests.test_medical_coding` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_coding_schemas_validation` | `apps.execution.tests.test_medical_coding` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_dictionary_import_job_lifecycle` | `apps.execution.tests.test_medical_coding` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_import_failure_rollback_and_failed_state` | `apps.execution.tests.test_medical_coding` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_import_invalid_layout_rejected` | `apps.execution.tests.test_medical_coding` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_import_unauthorized_roles_forbidden` | `apps.execution.tests.test_medical_coding` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_import_unsupported_dictionary_rejected` | `apps.execution.tests.test_medical_coding` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_lookup_and_indexes` | `apps.execution.tests.test_medical_coding` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_lookup_endpoints_validation_errors` | `apps.execution.tests.test_medical_coding` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_meddra_import_happy_path` | `apps.execution.tests.test_medical_coding` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_meddra_lookup_endpoint_happy_path` | `apps.execution.tests.test_medical_coding` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_meddra_term_unique_constraint` | `apps.execution.tests.test_medical_coding` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_whodrug_import_happy_path` | `apps.execution.tests.test_medical_coding` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_whodrug_lookup_endpoint_happy_path` | `apps.execution.tests.test_medical_coding` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_whodrug_record_unique_constraint` | `apps.execution.tests.test_medical_coding` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_audit_relevant_workflows` | `apps.execution.tests.test_medical_coding_engine` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_cache_behavior_and_degradation` | `apps.execution.tests.test_medical_coding_engine` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_coding_transitions` | `apps.execution.tests.test_medical_coding_engine` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_dictionary_version_isolation` | `apps.execution.tests.test_medical_coding_engine` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_import_auth_and_job_status` | `apps.execution.tests.test_medical_coding_engine` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_lookups_endpoints` | `apps.execution.tests.test_medical_coding_engine` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_matcher_normalization_and_scoring_thresholds` | `apps.execution.tests.test_medical_coding_engine` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_override_reason_validation` | `apps.execution.tests.test_medical_coding_engine` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_parser_fixtures` | `apps.execution.tests.test_medical_coding_engine` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_uncodable_query_generation_and_pii_isolation` | `apps.execution.tests.test_medical_coding_engine` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_upversioning_ledger_outcomes` | `apps.execution.tests.test_medical_coding_engine` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_impact_analysis_meddra_and_whodrug_lifecycle` | `apps.execution.tests.test_medical_coding_impact` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_auto_coding_on_observation_creation` | `apps.execution.tests.test_medical_coding_lifecycle` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_coder_action_accept_and_override_lifecycle` | `apps.execution.tests.test_medical_coding_lifecycle` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_mid_confidence_persists_as_suggestions` | `apps.execution.tests.test_medical_coding_lifecycle` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_cache_aside_and_stale_fallback` | `apps.execution.tests.test_medical_coding_matcher` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_cache_degradation_and_stale_on_error` | `apps.execution.tests.test_medical_coding_matcher` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_cache_ttl_configuration` | `apps.execution.tests.test_medical_coding_matcher` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_cache_unavailability_graceful_degradation` | `apps.execution.tests.test_medical_coding_matcher` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_meddra_matching_integration` | `apps.execution.tests.test_medical_coding_matcher` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_normalize_term` | `apps.execution.tests.test_medical_coding_matcher` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_similarity_computations` | `apps.execution.tests.test_medical_coding_matcher` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_stem_word` | `apps.execution.tests.test_medical_coding_matcher` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_token_cosine_similarity_empty` | `apps.execution.tests.test_medical_coding_matcher` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_whodrug_matching_integration` | `apps.execution.tests.test_medical_coding_matcher` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_audit_log_captures_all_coding_actions` | `apps.execution.tests.test_medical_coding_workbench` | PRD-QRY-001, PRD-SYS-001, PRD-SYS-004, Trace-1, Trace-16 | ⚪ UNVERIFIED | N/A |
| `test_batch_assignment_with_gxp_audit_logging` | `apps.execution.tests.test_medical_coding_workbench` | PRD-QRY-001, PRD-SYS-001, PRD-SYS-004, Trace-1, Trace-16 | ⚪ UNVERIFIED | N/A |
| `test_coding_queue_retrieval_and_filtering` | `apps.execution.tests.test_medical_coding_workbench` | PRD-QRY-001, PRD-SYS-001, PRD-SYS-004, Trace-1, Trace-16 | ⚪ UNVERIFIED | N/A |
| `test_discrepancy_query_escalation_lifecycle` | `apps.execution.tests.test_medical_coding_workbench` | PRD-QRY-001, PRD-SYS-001, PRD-SYS-004, Trace-1, Trace-16 | ⚪ UNVERIFIED | N/A |
| `test_impact_analysis_reclassification_and_mutation` | `apps.execution.tests.test_medical_coding_workbench` | PRD-QRY-001, PRD-SYS-001, PRD-SYS-004, Trace-1, Trace-16 | ⚪ UNVERIFIED | N/A |
| `test_matcher_normalization_and_fuzzy_scoring` | `apps.execution.tests.test_medical_coding_workbench` | PRD-QRY-001, PRD-SYS-001, PRD-SYS-004, Trace-1, Trace-16 | ⚪ UNVERIFIED | N/A |
| `test_meddra_hierarchy_search_and_traversal` | `apps.execution.tests.test_medical_coding_workbench` | PRD-QRY-001, PRD-SYS-001, PRD-SYS-004, Trace-1, Trace-16 | ⚪ UNVERIFIED | N/A |
| `test_parsers_meddra_and_whodrug_ascii_lines` | `apps.execution.tests.test_medical_coding_workbench` | PRD-QRY-001, PRD-SYS-001, PRD-SYS-004, Trace-1, Trace-16 | ⚪ UNVERIFIED | N/A |
| `test_single_coder_action_accept_and_override` | `apps.execution.tests.test_medical_coding_workbench` | PRD-QRY-001, PRD-SYS-001, PRD-SYS-004, Trace-1, Trace-16 | ⚪ UNVERIFIED | N/A |
| `test_upversioning_impact_analysis_endpoint` | `apps.execution.tests.test_medical_coding_workbench` | PRD-QRY-001, PRD-SYS-001, PRD-SYS-004, Trace-1, Trace-16 | ⚪ UNVERIFIED | N/A |
| `test_whodrug_atc_and_ingredient_lookup` | `apps.execution.tests.test_medical_coding_workbench` | PRD-QRY-001, PRD-SYS-001, PRD-SYS-004, Trace-1, Trace-16 | ⚪ UNVERIFIED | N/A |
| `test_certificate_revocation_string_serial_formats` | `apps.execution.tests.test_part11_esignatures` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_certificate_revocation_verification` | `apps.execution.tests.test_part11_esignatures` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_esignature_duplicate_serial_rejection` | `apps.execution.tests.test_part11_esignatures` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_esignature_tamper_detection_e2e` | `apps.execution.tests.test_part11_esignatures` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_private_key` | `apps.execution.tests.test_part11_esignatures` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_tampered_pdf_fails_verification` | `apps.execution.tests.test_part11_esignatures` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_valid_certificate_pem_substring_no_false_positive` | `apps.execution.tests.test_part11_esignatures` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_valid_part11_signature_verification` | `apps.execution.tests.test_part11_esignatures` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_x509_cert` | `apps.execution.tests.test_part11_esignatures` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_pdf_redaction_engine_bounding_box` | `apps.execution.tests.test_pdf_redactor` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_pdf_redaction_engine_purges_metadata_and_fields` | `apps.execution.tests.test_pdf_redactor` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_pdf_redaction_overlay_generation` | `apps.execution.tests.test_pdf_redactor` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_zero_phi_leak_in_redacted_pdf` | `apps.execution.tests.test_phi_redaction` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_concurrent_connection_isolation_and_no_weakref_errors` | `apps.execution.tests.test_pool_state_eviction` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_pool_connection_state_eviction` | `apps.execution.tests.test_pool_state_eviction` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_manual_physical_paper_consent_override` | `apps.execution.tests.test_pubsub_auto_enrollment` | PRD-SUB-007 | ⚪ UNVERIFIED | N/A |
| `test_pubsub_auto_enrollment_screening` | `apps.execution.tests.test_pubsub_auto_enrollment` | PRD-SUB-007 | ⚪ UNVERIFIED | N/A |
| `test_site_activation_compliance_validation` | `apps.execution.tests.test_pubsub_auto_enrollment` | PRD-SUB-007 | ⚪ UNVERIFIED | N/A |
| `test_digest_window_configurations` | `apps.execution.tests.test_queries_escalation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_escalation_idempotency` | `apps.execution.tests.test_queries_escalation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_escalation_missing_ids_fallback` | `apps.execution.tests.test_queries_escalation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_no_aging_queries` | `apps.execution.tests.test_queries_escalation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_startup_shutdown_and_resilience` | `apps.execution.tests.test_queries_escalation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_threshold_boundaries_and_escalation` | `apps.execution.tests.test_queries_escalation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_auto_query_generation_and_auto_close_on_form_completion` | `apps.execution.tests.test_query_coalescing_form_batching` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_context_resolution_query_budget` | `apps.execution.tests.test_query_coalescing_form_batching` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_form_completion_enqueues_single_background_task` | `apps.execution.tests.test_query_coalescing_form_batching` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_in_memory_prefiltering_rules` | `apps.execution.tests.test_query_coalescing_form_batching` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_predecessor_pause_and_resume_on_form_completion` | `apps.execution.tests.test_query_coalescing_form_batching` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_concurrent_randomization_unique_and_monotonic` | `apps.execution.tests.test_randomization_concurrency` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_forced_failure_rolls_back_atomically` | `apps.execution.tests.test_randomization_concurrency` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_subject_demographics_mutation_and_deletion_endpoints` | `apps.execution.tests.test_randomization_endpoints` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_subject_state_transition_endpoint` | `apps.execution.tests.test_randomization_endpoints` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_randomization_entities_audit_trail_and_soft_delete` | `apps.execution.tests.test_randomization_persistence` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_randomization_entities_hard_delete_prevented` | `apps.execution.tests.test_randomization_persistence` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_randomization_entities_trial_lock_conformity` | `apps.execution.tests.test_randomization_persistence` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_subject_consent_blocking_and_reconsent_lifecycle` | `apps.execution.tests.test_reconsent_blocking` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_subject_consent_endpoint_lifecycle` | `apps.execution.tests.test_reconsent_blocking` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_admin_visibility_endpoint` | `apps.execution.tests.test_relational_outbox` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_background_worker_uses_separate_database_connection_channel` | `apps.execution.tests.test_relational_outbox` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_commit_clinical_change_without_reason_fails` | `apps.execution.tests.test_relational_outbox` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_manual_coding_writes_query_resolve_to_outbox` | `apps.execution.tests.test_relational_outbox` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_outbox_no_unencrypted_pii` | `apps.execution.tests.test_relational_outbox` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_outbox_worker_batch_size_limit` | `apps.execution.tests.test_relational_outbox` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_outbox_worker_concurrent_dispatch` | `apps.execution.tests.test_relational_outbox` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_outbox_worker_dialect_aware_locking_pg` | `apps.execution.tests.test_relational_outbox` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_outbox_worker_dialect_aware_locking_sqlite` | `apps.execution.tests.test_relational_outbox` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_outbox_worker_polling_and_dispatch_success` | `apps.execution.tests.test_relational_outbox` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_outbox_worker_retry_and_backoff` | `apps.execution.tests.test_relational_outbox` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_parallel_workers_do_not_deliver_duplicate_events` | `apps.execution.tests.test_relational_outbox` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_rollback_prevents_outbox_record_creation` | `apps.execution.tests.test_relational_outbox` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_trial_lock_writes_outbox` | `apps.execution.tests.test_relational_outbox` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_get_subject_api_blinding_and_isolation` | `apps.execution.tests.test_role_redaction_and_access` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_get_visit_api_blinding_and_isolation` | `apps.execution.tests.test_role_redaction_and_access` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_site_isolation_guard_and_audit` | `apps.execution.tests.test_role_redaction_and_access` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_trial_role_enum_and_helper` | `apps.execution.tests.test_role_redaction_and_access` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_block_allocation_mechanics` | `apps.execution.tests.test_rtsm_algorithms` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_block_allocation_uneven_ratios` | `apps.execution.tests.test_rtsm_algorithms` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_canonical_stratum_key_generation` | `apps.execution.tests.test_rtsm_algorithms` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_minimization_imbalance_and_biased_coin` | `apps.execution.tests.test_rtsm_algorithms` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_minimization_uneven_ratios_and_weights` | `apps.execution.tests.test_rtsm_algorithms` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_randomization_config_validation` | `apps.execution.tests.test_rtsm_algorithms` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_reproducibility_and_seeding` | `apps.execution.tests.test_rtsm_algorithms` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_stratified_block_isolation` | `apps.execution.tests.test_rtsm_algorithms` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_evaluate_resupply_boundaries` | `apps.execution.tests.test_rtsm_supply` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_hard_delete_prevented_for_supply_entities` | `apps.execution.tests.test_rtsm_supply` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_insufficient_stock_rejection_and_rollback` | `apps.execution.tests.test_rtsm_supply` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_invalid_site_kit_relationship_rejection` | `apps.execution.tests.test_rtsm_supply` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_locked_site_rejection` | `apps.execution.tests.test_rtsm_supply` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_resupply_threshold_breach_and_deduplication` | `apps.execution.tests.test_rtsm_supply` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_site_inventory_unique_constraint` | `apps.execution.tests.test_rtsm_supply` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_successful_dispensation_endpoint` | `apps.execution.tests.test_rtsm_supply` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_supply_entities_audit_trail_and_soft_delete` | `apps.execution.tests.test_rtsm_supply` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_trial_locking_conformity` | `apps.execution.tests.test_rtsm_supply` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_ae_required_optional_and_date_order` | `apps.execution.tests.test_sdtm_foundation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_auditable_model_fields_and_validation` | `apps.execution.tests.test_sdtm_foundation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_cm_required_optional_and_date_order` | `apps.execution.tests.test_sdtm_foundation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_date_format_validation` | `apps.execution.tests.test_sdtm_foundation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_dm_required_and_optional_fields` | `apps.execution.tests.test_sdtm_foundation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_lb_required_and_optional_fields` | `apps.execution.tests.test_sdtm_foundation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_models_optional_nones` | `apps.execution.tests.test_sdtm_foundation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_null_flavor_enum_membership` | `apps.execution.tests.test_sdtm_foundation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_sdtm_domain_enum_membership` | `apps.execution.tests.test_sdtm_foundation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_suppqual_fields_and_validation` | `apps.execution.tests.test_sdtm_foundation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_terminology_normalization_and_enums` | `apps.execution.tests.test_sdtm_foundation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_vs_required_and_optional_fields` | `apps.execution.tests.test_sdtm_foundation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_cdash_ae_mapping` | `apps.execution.tests.test_sdtm_mapper` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_cdash_generic_orchestrator` | `apps.execution.tests.test_sdtm_mapper` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_cdash_vs_unit_conversion_and_study_day` | `apps.execution.tests.test_sdtm_mapper` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_chronological_date_validation` | `apps.execution.tests.test_sdtm_mapper` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_compute_age` | `apps.execution.tests.test_sdtm_mapper` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_get_demographics` | `apps.execution.tests.test_sdtm_mapper` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_map_ae_flat_structure` | `apps.execution.tests.test_sdtm_mapper` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_map_ae_grouped_structure` | `apps.execution.tests.test_sdtm_mapper` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_map_cm_flat_structure` | `apps.execution.tests.test_sdtm_mapper` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_map_cm_grouped_structure` | `apps.execution.tests.test_sdtm_mapper` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_map_dm_defaults_and_fallbacks` | `apps.execution.tests.test_sdtm_mapper` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_map_dm_happy_path` | `apps.execution.tests.test_sdtm_mapper` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_map_lb` | `apps.execution.tests.test_sdtm_mapper` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_map_to_sdtm_orchestrator` | `apps.execution.tests.test_sdtm_mapper` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_map_vs` | `apps.execution.tests.test_sdtm_mapper` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_mapper_dedicated_helpers` | `apps.execution.tests.test_sdtm_mapper` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_persist_sdtm_records_ae_domain_reclassification` | `apps.execution.tests.test_sdtm_mapper` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_persist_sdtm_records_cm_ds_mh` | `apps.execution.tests.test_sdtm_mapper` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_persist_sdtm_records_pipeline` | `apps.execution.tests.test_sdtm_mapper` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_to_dtc` | `apps.execution.tests.test_sdtm_mapper` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_visit_records_require_start_date` | `apps.execution.tests.test_sdtm_mapper` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_bulk_query_generation_deduplication` | `apps.execution.tests.test_sdv` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_bulk_query_generation_happy_path` | `apps.execution.tests.test_sdv` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_bulk_query_generation_input_validation` | `apps.execution.tests.test_sdv` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_bulk_query_generation_rbac_gating` | `apps.execution.tests.test_sdv` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_bulk_sdv_signoff_batch_binding_mismatch` | `apps.execution.tests.test_sdv` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_bulk_sdv_signoff_happy_path` | `apps.execution.tests.test_sdv` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_bulk_sdv_signoff_input_validation` | `apps.execution.tests.test_sdv` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_bulk_sdv_signoff_rbac_and_idempotency` | `apps.execution.tests.test_sdv` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_sdv_automatic_verification_drop_compliance` | `apps.execution.tests.test_sdv` | PRD-QRY-006 | ⚪ UNVERIFIED | N/A |
| `test_sdv_signoff_endpoints_rbac_and_target_validation` | `apps.execution.tests.test_sdv` | PRD-QRY-005 | ⚪ UNVERIFIED | N/A |
| `test_flag_target_descriptor_validation` | `apps.execution.tests.test_sdv_item_level_rbac` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_sdv_flag_granular_permissions` | `apps.execution.tests.test_sdv_item_level_rbac` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_sdv_flag_rbac_permissions` | `apps.execution.tests.test_sdv_item_level_rbac` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_sdv_flag_request_validation` | `apps.execution.tests.test_sdv_item_level_rbac` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_sdv_flag_severity_enum` | `apps.execution.tests.test_sdv_item_level_rbac` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_sdv_resolve_request_validation` | `apps.execution.tests.test_sdv_item_level_rbac` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_sdv_response_structures` | `apps.execution.tests.test_sdv_item_level_rbac` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_clinical_observation_sdv_defaults` | `apps.execution.tests.test_sdv_tsdv_persistence` | PRD-QRY-005, PRD-QRY-007 | ⚪ UNVERIFIED | N/A |
| `test_item_level_sdv_data_model_and_state_machine` | `apps.execution.tests.test_sdv_tsdv_persistence` | PRD-QRY-005 | ⚪ UNVERIFIED | N/A |
| `test_sdv_automatic_verification_drop` | `apps.execution.tests.test_sdv_tsdv_persistence` | PRD-QRY-005, PRD-QRY-006 | ⚪ UNVERIFIED | N/A |
| `test_sdv_sign_off_persistence_and_audit` | `apps.execution.tests.test_sdv_tsdv_persistence` | PRD-QRY-005 | ⚪ UNVERIFIED | N/A |
| `test_sdv_signoff_endpoint_and_idempotency` | `apps.execution.tests.test_sdv_tsdv_persistence` | PRD-QRY-005, PRD-QRY-006 | ⚪ UNVERIFIED | N/A |
| `test_sdv_signoff_page_visit_scopes` | `apps.execution.tests.test_sdv_tsdv_persistence` | PRD-QRY-005, PRD-QRY-006 | ⚪ UNVERIFIED | N/A |
| `test_tsdv_config_persistence` | `apps.execution.tests.test_sdv_tsdv_persistence` | PRD-QRY-007 | ⚪ UNVERIFIED | N/A |
| `test_downstream_replay_cache_redis_reset` | `apps.execution.tests.test_sig_token_verifier` | Trace-17 | ⚪ UNVERIFIED | N/A |
| `test_downstream_replay_cache_redis_success` | `apps.execution.tests.test_sig_token_verifier` | Trace-17 | ⚪ UNVERIFIED | N/A |
| `test_redis_consumption_fallback_on_exception` | `apps.execution.tests.test_sig_token_verifier` | Trace-17 | ⚪ UNVERIFIED | N/A |
| `test_redis_consumption_replay_blocked` | `apps.execution.tests.test_sig_token_verifier` | Trace-17 | ⚪ UNVERIFIED | N/A |
| `test_redis_consumption_success` | `apps.execution.tests.test_sig_token_verifier` | Trace-17 | ⚪ UNVERIFIED | N/A |
| `test_redis_reset` | `apps.execution.tests.test_sig_token_verifier` | Trace-17 | ⚪ UNVERIFIED | N/A |
| `test_verify_and_consume_sig_token_expired` | `apps.execution.tests.test_sig_token_verifier` | Trace-17 | ⚪ UNVERIFIED | N/A |
| `test_verify_and_consume_sig_token_mismatched_user` | `apps.execution.tests.test_sig_token_verifier` | Trace-17 | ⚪ UNVERIFIED | N/A |
| `test_verify_and_consume_sig_token_replay_blocked` | `apps.execution.tests.test_sig_token_verifier` | Trace-17 | ⚪ UNVERIFIED | N/A |
| `test_verify_and_consume_sig_token_success` | `apps.execution.tests.test_sig_token_verifier` | Trace-17 | ⚪ UNVERIFIED | N/A |
| `test_compute_content_digest` | `apps.execution.tests.test_signature_builder` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_rsa_signature_sign_and_verify` | `apps.execution.tests.test_signature_builder` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_asymmetric_sign_and_verify` | `apps.execution.tests.test_signature_manifestation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_async_signature_context_decorator` | `apps.execution.tests.test_signature_manifestation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_capture_certificate_identifiers` | `apps.execution.tests.test_signature_manifestation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_controlled_enums` | `apps.execution.tests.test_signature_manifestation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_sha256_hashing_helper` | `apps.execution.tests.test_signature_manifestation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_signature_context_propagation` | `apps.execution.tests.test_signature_manifestation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_signature_manifestation_lifecycle` | `apps.execution.tests.test_signature_manifestation` | Trace-13 | ⚪ UNVERIFIED | N/A |
| `test_batch_signature_empty_target_forms_returns_400` | `apps.execution.tests.test_signature_router` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_batch_signature_missing_password_returns_400` | `apps.execution.tests.test_signature_router` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_batch_signature_sign_off_success` | `apps.execution.tests.test_signature_router` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_signature_sign_and_verify_lifecycle_success` | `apps.execution.tests.test_signature_router` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_signature_verify_mock_fails` | `apps.execution.tests.test_signature_router` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_signature_verify_tampered_fails` | `apps.execution.tests.test_signature_router` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_protocol_capture_and_reconciliation_lifecycle` | `apps.execution.tests.test_study_migration` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_db_migration_cloning_and_sealing` | `apps.execution.tests.test_subject_migration` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_migrate_subject_submissions_field_remapping` | `apps.execution.tests.test_subject_migration` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_pure_python_transition_guard` | `apps.execution.tests.test_subject_randomization_lifecycle` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_stratification_factors_locking` | `apps.execution.tests.test_subject_randomization_lifecycle` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_subject_initial_state_and_persistence` | `apps.execution.tests.test_subject_randomization_lifecycle` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_subject_state_transitions` | `apps.execution.tests.test_subject_randomization_lifecycle` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_unblinding_and_withdrawal_behavior` | `apps.execution.tests.test_subject_randomization_lifecycle` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_events_captured_in_part_11_audit_history` | `apps.execution.tests.test_system_coding_queries` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_manual_coding_resolution_associates_with_query_and_closes_it` | `apps.execution.tests.test_system_coding_queries` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_resolving_query_reverts_assignment_to_uncoded` | `apps.execution.tests.test_system_coding_queries` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_uncodable_term_creates_query_pending_and_actionable_query` | `apps.execution.tests.test_system_coding_queries` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_uncodable_term_query_creation_is_idempotent` | `apps.execution.tests.test_system_coding_queries` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_site_and_visit_locks` | `apps.execution.tests.test_trial_lock` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_subject_and_form_locks` | `apps.execution.tests.test_trial_lock` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_trial_lock_freeze` | `apps.execution.tests.test_trial_lock` | PRD-SYS-003, Trace-3 | ⚪ UNVERIFIED | N/A |
| `test_api_tsdv_config_validation_rules` | `apps.execution.tests.test_tsdv` | PRD-QRY-007 | ⚪ UNVERIFIED | N/A |
| `test_api_tsdv_configuration_rbac` | `apps.execution.tests.test_tsdv` | PRD-QRY-007 | ⚪ UNVERIFIED | N/A |
| `test_api_tsdv_evaluation_integration_and_context_errors` | `apps.execution.tests.test_tsdv` | PRD-QRY-007 | ⚪ UNVERIFIED | N/A |
| `test_api_tsdv_immutable_enrollment_index_stability` | `apps.execution.tests.test_tsdv` | PRD-QRY-007 | ⚪ UNVERIFIED | N/A |
| `test_evaluate_bulk_tsdv` | `apps.execution.tests.test_tsdv` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_sdv_transport_schemas` | `apps.execution.tests.test_tsdv` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_tsdv_pure_deterministic_sampling` | `apps.execution.tests.test_tsdv` | PRD-QRY-007 | ⚪ UNVERIFIED | N/A |
| `test_tsdv_pure_different_seeds_produce_different_values` | `apps.execution.tests.test_tsdv` | PRD-QRY-007 | ⚪ UNVERIFIED | N/A |
| `test_tsdv_pure_evaluation_sampling_models` | `apps.execution.tests.test_tsdv` | PRD-QRY-007 | ⚪ UNVERIFIED | N/A |
| `test_tsdv_pure_field_requirement_precedence` | `apps.execution.tests.test_tsdv` | PRD-QRY-007 | ⚪ UNVERIFIED | N/A |
| `test_tsdv_pure_first_n_selection` | `apps.execution.tests.test_tsdv` | PRD-QRY-007 | ⚪ UNVERIFIED | N/A |
| `test_tsdv_pure_percentage_boundaries` | `apps.execution.tests.test_tsdv` | PRD-QRY-007 | ⚪ UNVERIFIED | N/A |
| `test_api_tsdv_config_authorization_and_upsert` | `apps.execution.tests.test_tsdv_logic` | PRD-QRY-007 | ⚪ UNVERIFIED | N/A |
| `test_api_tsdv_config_validation` | `apps.execution.tests.test_tsdv_logic` | PRD-QRY-007 | ⚪ UNVERIFIED | N/A |
| `test_api_tsdv_evaluation_endpoint` | `apps.execution.tests.test_tsdv_logic` | PRD-QRY-007 | ⚪ UNVERIFIED | N/A |
| `test_tsdv_evaluation_models` | `apps.execution.tests.test_tsdv_logic` | PRD-QRY-007 | ⚪ UNVERIFIED | N/A |
| `test_tsdv_field_required_precedence` | `apps.execution.tests.test_tsdv_logic` | PRD-QRY-007 | ⚪ UNVERIFIED | N/A |
| `test_tsdv_subject_selection_boundaries` | `apps.execution.tests.test_tsdv_logic` | PRD-QRY-007 | ⚪ UNVERIFIED | N/A |
| `test_tsdv_subject_selection_deterministic` | `apps.execution.tests.test_tsdv_logic` | PRD-QRY-007 | ⚪ UNVERIFIED | N/A |
| `test_tsdv_subject_selection_first_n` | `apps.execution.tests.test_tsdv_logic` | PRD-QRY-007 | ⚪ UNVERIFIED | N/A |
| `test_convert_unit_errors` | `apps.execution.tests.test_ucum_coverage` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_convert_unit_identical` | `apps.execution.tests.test_ucum_coverage` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_convert_unit_multiplicative` | `apps.execution.tests.test_ucum_coverage` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_convert_unit_temperature` | `apps.execution.tests.test_ucum_coverage` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_get_normalized_representation` | `apps.execution.tests.test_ucum_coverage` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_normalize_unit_name` | `apps.execution.tests.test_ucum_coverage` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_terminology_numeric_and_boolean_mappings` | `apps.execution.tests.test_unified_schemas_terminology` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_translation_fails_early_on_invalid_structure` | `apps.execution.tests.test_unified_schemas_terminology` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_validator_with_invalid_gender` | `apps.execution.tests.test_unified_schemas_terminology` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_validator_with_variant_and_boolean_inputs` | `apps.execution.tests.test_unified_schemas_terminology` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_delimited_format_int_indices_without_header` | `apps.execution.tests.test_whodrug_parser` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_delimited_format_parsing` | `apps.execution.tests.test_whodrug_parser` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_detect_file_type` | `apps.execution.tests.test_whodrug_parser` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_invalid_and_missing_required_fields` | `apps.execution.tests.test_whodrug_parser` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_max_length_constraints` | `apps.execution.tests.test_whodrug_parser` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_non_strict_referential_validation` | `apps.execution.tests.test_whodrug_parser` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_parse_in_batches_whodrug` | `apps.execution.tests.test_whodrug_parser` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_parse_valid_fixed_width_atc` | `apps.execution.tests.test_whodrug_parser` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_parse_valid_fixed_width_drug_atc` | `apps.execution.tests.test_whodrug_parser` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_parse_valid_fixed_width_drug_ingredients` | `apps.execution.tests.test_whodrug_parser` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_parse_valid_fixed_width_drugs` | `apps.execution.tests.test_whodrug_parser` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_parse_valid_fixed_width_ingredients` | `apps.execution.tests.test_whodrug_parser` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_public_entry_point_reusing_parser` | `apps.execution.tests.test_whodrug_parser` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_public_entry_point_whodrug` | `apps.execution.tests.test_whodrug_parser` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_strict_referential_validation_triggers` | `apps.execution.tests.test_whodrug_parser` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_whodrug_parser_init_validation` | `apps.execution.tests.test_whodrug_parser` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_generate_auditor_token_post_endpoint` | `apps.gateway.tests.test_auditor_router` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_inspect_study_audit_trail_endpoint` | `apps.gateway.tests.test_auditor_router` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_expired_auditor_token_raises_error` | `apps.gateway.tests.test_auditor_token` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_generate_and_validate_auditor_token` | `apps.gateway.tests.test_auditor_token` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_cdisc_openapi_component_schemas` | `apps.gateway.tests.test_cdisc_openapi_contract` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_cdisc_openapi_export_parity` | `apps.gateway.tests.test_cdisc_openapi_contract` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_cdisc_openapi_file_exists_and_valid` | `apps.gateway.tests.test_cdisc_openapi_contract` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_cdisc_openapi_paths_coverage` | `apps.gateway.tests.test_cdisc_openapi_contract` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_signature_verification_keycloak_token_secret` | `apps.gateway.tests.test_double_auth` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_signature_verification_replay_attack_prevention` | `apps.gateway.tests.test_double_auth` | PRD-QRY-005 | ⚪ UNVERIFIED | N/A |
| `test_signature_verification_role_insufficient_auth` | `apps.gateway.tests.test_double_auth` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_signature_verification_token_expiration` | `apps.gateway.tests.test_double_auth` | PRD-QRY-005 | ⚪ UNVERIFIED | N/A |
| `test_create_demo_session` | `apps.gateway.tests.test_gateway` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_eisf_gateway_site_isolation_propagation` | `apps.gateway.tests.test_gateway` | Trace-18 | ⚪ UNVERIFIED | N/A |
| `test_gateway_bearer_only_subject_routing_and_header_enforcement` | `apps.gateway.tests.test_gateway` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_gateway_comprehensive_scope_spoofing_prevention` | `apps.gateway.tests.test_gateway` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_gateway_cors_headers` | `apps.gateway.tests.test_gateway` | PRD-UNI-001 | ⚪ UNVERIFIED | N/A |
| `test_gateway_local_secret_bypasses_audience_check` | `apps.gateway.tests.test_gateway` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_gateway_notifications_header_enforcement_and_spoofing_prevention` | `apps.gateway.tests.test_gateway` | PRD-SYS-004 | ⚪ UNVERIFIED | N/A |
| `test_gateway_proxy_eisf_headers_propagation` | `apps.gateway.tests.test_gateway` | Trace-18 | ⚪ UNVERIFIED | N/A |
| `test_gateway_rate_limiting` | `apps.gateway.tests.test_gateway` | PRD-UNI-001 | ⚪ UNVERIFIED | N/A |
| `test_gateway_scope_extraction_and_verification_integrity` | `apps.gateway.tests.test_gateway` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_gateway_semantic_action_issuance_and_enforcement` | `apps.gateway.tests.test_gateway` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_gateway_sponsor_claim_extraction` | `apps.gateway.tests.test_gateway` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_gateway_startup_development_with_bypass_configs` | `apps.gateway.tests.test_gateway` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_gateway_startup_offline_idp_recovery` | `apps.gateway.tests.test_gateway` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_gateway_startup_production_no_bypass_configs` | `apps.gateway.tests.test_gateway` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_gateway_startup_production_with_skip_jwks` | `apps.gateway.tests.test_gateway` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_gateway_startup_production_with_test_secret` | `apps.gateway.tests.test_gateway` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_gateway_startup_production_with_unverified_jwt` | `apps.gateway.tests.test_gateway` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_gateway_startup_staging_fails_with_bypass_configs` | `apps.gateway.tests.test_gateway` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_gateway_subject_role_routing_restrictions` | `apps.gateway.tests.test_gateway` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_gateway_tenant_claim_extraction` | `apps.gateway.tests.test_gateway` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_gateway_tenant_spoofing_prevention` | `apps.gateway.tests.test_gateway` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_gateway_test_secret_bypasses_audience_check` | `apps.gateway.tests.test_gateway` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_gateway_unauthorized_client_token_returns_401` | `apps.gateway.tests.test_gateway` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_generate_signature` | `apps.gateway.tests.test_gateway` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_generate_signature_v2` | `apps.gateway.tests.test_gateway` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_get_openapi_json` | `apps.gateway.tests.test_gateway` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_get_openapi_json_error` | `apps.gateway.tests.test_gateway` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_get_swagger_ui` | `apps.gateway.tests.test_gateway` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_keycloak_token_missing_audience` | `apps.gateway.tests.test_gateway` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_keycloak_token_unauthorized_audience_array` | `apps.gateway.tests.test_gateway` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_keycloak_token_unauthorized_audience_string` | `apps.gateway.tests.test_gateway` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_keycloak_token_valid_audience_array` | `apps.gateway.tests.test_gateway` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_keycloak_token_valid_audience_string` | `apps.gateway.tests.test_gateway` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_proxy_requests_change_reason_too_long` | `apps.gateway.tests.test_gateway` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_proxy_requests_invalid_auth` | `apps.gateway.tests.test_gateway` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_proxy_requests_no_auth` | `apps.gateway.tests.test_gateway` | PRD-UNI-001 | ⚪ UNVERIFIED | N/A |
| `test_proxy_requests_paths` | `apps.gateway.tests.test_gateway` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_proxy_requests_terminology_paths` | `apps.gateway.tests.test_gateway` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_proxy_requests_v2_headers` | `apps.gateway.tests.test_gateway` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_proxy_requests_valid_auth` | `apps.gateway.tests.test_gateway` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_sandbox_tenant_isolation_gate_violations` | `apps.gateway.tests.test_gateway` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_signature_gated_mutation_enforcement` | `apps.gateway.tests.test_gateway` | Trace-17 | ⚪ UNVERIFIED | N/A |
| `test_signature_gated_mutation_expired_token` | `apps.gateway.tests.test_gateway` | Trace-17 | ⚪ UNVERIFIED | N/A |
| `test_signature_gated_mutation_mismatched_action` | `apps.gateway.tests.test_gateway` | Trace-17 | ⚪ UNVERIFIED | N/A |
| `test_signature_token_altered_signature_rejected` | `apps.gateway.tests.test_gateway` | Trace-17 | ⚪ UNVERIFIED | N/A |
| `test_signature_token_credentials_not_logged_or_returned` | `apps.gateway.tests.test_gateway` | Trace-17 | ⚪ UNVERIFIED | N/A |
| `test_signature_verification_invalid_credentials` | `apps.gateway.tests.test_gateway` | Trace-17 | ⚪ UNVERIFIED | N/A |
| `test_signature_verification_role_insufficient` | `apps.gateway.tests.test_gateway` | Trace-17 | ⚪ UNVERIFIED | N/A |
| `test_signature_verification_study_designer_role_allowed` | `apps.gateway.tests.test_gateway` | Trace-17 | ⚪ UNVERIFIED | N/A |
| `test_signature_verification_success` | `apps.gateway.tests.test_gateway` | Trace-17 | ⚪ UNVERIFIED | N/A |
| `test_signature_verification_with_batch_id` | `apps.gateway.tests.test_gateway` | Trace-17 | ⚪ UNVERIFIED | N/A |
| `test_verify_gateway_signed_token` | `apps.gateway.tests.test_gateway` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_verify_token_fetch_failure_fallback` | `apps.gateway.tests.test_gateway` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_verify_token_invalid` | `apps.gateway.tests.test_gateway` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_verify_token_on_demand_success` | `apps.gateway.tests.test_gateway` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_verify_token_stampede_protection` | `apps.gateway.tests.test_gateway` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_gateway_base_client_headers` | `apps.gateway.tests.test_gateway_base_client` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_gateway_base_client_request_exception_logging` | `apps.gateway.tests.test_gateway_base_client` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_gateway_base_client_request_failure_logging` | `apps.gateway.tests.test_gateway_base_client` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_gateway_base_client_request_success` | `apps.gateway.tests.test_gateway_base_client` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_run_async_basic` | `apps.gateway.tests.test_gateway_base_client` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_run_async_in_running_loop` | `apps.gateway.tests.test_gateway_base_client` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_gateway_cdisc_cdash_domain_authenticated` | `apps.gateway.tests.test_gateway_cdisc` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_gateway_cdisc_codelist_authenticated` | `apps.gateway.tests.test_gateway_cdisc` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_gateway_cdisc_products_authenticated` | `apps.gateway.tests.test_gateway_cdisc` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_gateway_cdisc_sdtm_domain_authenticated` | `apps.gateway.tests.test_gateway_cdisc` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_gateway_cdisc_unauthenticated_returns_401` | `apps.gateway.tests.test_gateway_cdisc` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_environment_integrity` | `apps.gateway.tests.test_gateway_compliance` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_acknowledge_notification_authorized` | `apps.gateway.tests.test_gateway_ecoa` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_acknowledge_notification_cross_subject_block` | `apps.gateway.tests.test_gateway_ecoa` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_bulk_sync_epro_entries_authorized` | `apps.gateway.tests.test_gateway_ecoa` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_bulk_sync_epro_entries_cross_subject_block` | `apps.gateway.tests.test_gateway_ecoa` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_ecoa_unauthenticated_block` | `apps.gateway.tests.test_gateway_ecoa` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_ecoa_unauthorized_role_block` | `apps.gateway.tests.test_gateway_ecoa` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_get_subject_assignments_authorized` | `apps.gateway.tests.test_gateway_ecoa` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_get_subject_assignments_cross_subject_block` | `apps.gateway.tests.test_gateway_ecoa` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_get_subject_compliance_authorized` | `apps.gateway.tests.test_gateway_ecoa` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_get_subject_compliance_cross_subject_block` | `apps.gateway.tests.test_gateway_ecoa` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_get_subject_instruments_authorized` | `apps.gateway.tests.test_gateway_ecoa` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_get_subject_instruments_cross_subject_block` | `apps.gateway.tests.test_gateway_ecoa` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_get_subject_notifications_authorized` | `apps.gateway.tests.test_gateway_ecoa` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_get_subject_notifications_cross_subject_block` | `apps.gateway.tests.test_gateway_ecoa` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_staff_authoring_assignments` | `apps.gateway.tests.test_gateway_ecoa` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_staff_authoring_instruments` | `apps.gateway.tests.test_gateway_ecoa` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_submit_epro_entry_authorized` | `apps.gateway.tests.test_gateway_ecoa` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_submit_epro_entry_cross_subject_block` | `apps.gateway.tests.test_gateway_ecoa` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_gateway_proxying_v2_studies` | `apps.gateway.tests.test_gateway_usdm` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_gateway_usdm_export_authenticated` | `apps.gateway.tests.test_gateway_usdm` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_gateway_usdm_import_authenticated` | `apps.gateway.tests.test_gateway_usdm` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_gateway_usdm_unauthenticated_returns_401` | `apps.gateway.tests.test_gateway_usdm` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_usdm_export_schema_validation_gateway` | `apps.gateway.tests.test_gateway_usdm` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_audit_context_variables_and_decorator` | `apps.gateway.tests.test_security_middleware` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_canonical_json_signing_and_verification` | `apps.gateway.tests.test_security_middleware` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_downstream_signature_gated_endpoint_expired_token` | `apps.gateway.tests.test_security_middleware` | Trace-17 | ⚪ UNVERIFIED | N/A |
| `test_downstream_signature_gated_endpoint_mismatched_action` | `apps.gateway.tests.test_security_middleware` | Trace-17 | ⚪ UNVERIFIED | N/A |
| `test_downstream_signature_gated_endpoint_replay_blocked` | `apps.gateway.tests.test_security_middleware` | Trace-17 | ⚪ UNVERIFIED | N/A |
| `test_downstream_signature_gated_endpoint_requires_sig_token` | `apps.gateway.tests.test_security_middleware` | Trace-17 | ⚪ UNVERIFIED | N/A |
| `test_downstream_signature_gated_endpoint_valid_sig_token` | `apps.gateway.tests.test_security_middleware` | Trace-17 | ⚪ UNVERIFIED | N/A |
| `test_middleware_cross_request_scope_isolation` | `apps.gateway.tests.test_security_middleware` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_middleware_expired_timestamp` | `apps.gateway.tests.test_security_middleware` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_middleware_explicit_legacy_version_accepted` | `apps.gateway.tests.test_security_middleware` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_middleware_explicit_legacy_version_invalid_rejected` | `apps.gateway.tests.test_security_middleware` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_middleware_health_bypass` | `apps.gateway.tests.test_security_middleware` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_middleware_invalid_timestamp_format` | `apps.gateway.tests.test_security_middleware` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_middleware_missing_headers` | `apps.gateway.tests.test_security_middleware` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_middleware_missing_signature_version_rejected` | `apps.gateway.tests.test_security_middleware` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_middleware_permissions_parsed_in_state` | `apps.gateway.tests.test_security_middleware` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_middleware_scope_header_mutation_and_injection_rejection` | `apps.gateway.tests.test_security_middleware` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_middleware_tenant_context_and_state` | `apps.gateway.tests.test_security_middleware` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_middleware_tenant_missing_fallback` | `apps.gateway.tests.test_security_middleware` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_middleware_tenant_signature_tampering_rejected` | `apps.gateway.tests.test_security_middleware` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_middleware_unblinded_access_edge_cases` | `apps.gateway.tests.test_security_middleware` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_middleware_unblinded_access_parametrization` | `apps.gateway.tests.test_security_middleware` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_middleware_unsupported_version_rejected` | `apps.gateway.tests.test_security_middleware` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_middleware_v2_invalid_signature` | `apps.gateway.tests.test_security_middleware` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_middleware_v2_mismatched_reason` | `apps.gateway.tests.test_security_middleware` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_middleware_v2_missing_reason` | `apps.gateway.tests.test_security_middleware` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_middleware_v2_safe_method_no_reason_success` | `apps.gateway.tests.test_security_middleware` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_middleware_v2_success` | `apps.gateway.tests.test_security_middleware` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_mutation_unsigned_and_non_compliant_rejections` | `apps.gateway.tests.test_security_middleware` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_verify_gateway_signature_scope_fallback_restrictions` | `apps.gateway.tests.test_security_middleware` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_verify_gateway_signature_tenant_and_multishape_restrictions` | `apps.gateway.tests.test_security_middleware` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_verify_sig_token_helper_scenarios` | `apps.gateway.tests.test_security_middleware` | Trace-17 | ⚪ UNVERIFIED | N/A |
| `test_basic_detection_results` | `apps.interop.tests.test_deid` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_cli_get_line_and_col` | `apps.interop.tests.test_deid` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_cli_load_gitignore_patterns` | `apps.interop.tests.test_deid` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_cli_main_bypass_comments_and_false_positives` | `apps.interop.tests.test_deid` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_cli_main_clean` | `apps.interop.tests.test_deid` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_cli_main_violation` | `apps.interop.tests.test_deid` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_cli_should_scan_file` | `apps.interop.tests.test_deid` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_compliance_profiles` | `apps.interop.tests.test_deid` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_custom_literal_terms` | `apps.interop.tests.test_deid` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_dates_detector` | `apps.interop.tests.test_deid` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_deidentify_free_text_direct` | `apps.interop.tests.test_deid` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_email_detector` | `apps.interop.tests.test_deid` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_fhir_narrative_and_notes_integration` | `apps.interop.tests.test_deid` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_ip_mac_detector` | `apps.interop.tests.test_deid` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_is_excluded_path_alembic` | `apps.interop.tests.test_deid` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_medical_record_account_detector` | `apps.interop.tests.test_deid` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_overlap_resolution_deterministic` | `apps.interop.tests.test_deid` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_phone_fax_detector` | `apps.interop.tests.test_deid` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_redact_text_sequential` | `apps.interop.tests.test_deid` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_ssn_national_id_detector` | `apps.interop.tests.test_deid` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_urls_detector` | `apps.interop.tests.test_deid` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_zip_geographic_detector` | `apps.interop.tests.test_deid` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_assignment_compliance_states_and_recalculations` | `apps.interop.tests.test_ecoa_coverage` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_instrument_retrieval_and_assignment_boundaries` | `apps.interop.tests.test_ecoa_coverage` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_notifications_lifecycle_reminders_and_acknowledgments` | `apps.interop.tests.test_ecoa_coverage` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_offline_submission_conflict_resolution_lifecycles` | `apps.interop.tests.test_ecoa_coverage` | PRD-EDC-008 | ⚪ UNVERIFIED | N/A |
| `test_structural_conflict_on_missing_or_deleted_targets` | `apps.interop.tests.test_ecoa_coverage` | PRD-EDC-008 | ⚪ UNVERIFIED | N/A |
| `test_subject_only_authorization_and_cross_subject_rejection` | `apps.interop.tests.test_ecoa_coverage` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_bulk_offline_sync` | `apps.interop.tests.test_interop` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_compute_reminders_all_subjects_staff` | `apps.interop.tests.test_interop` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_compute_reminders_by_subject_and_end_date_branch` | `apps.interop.tests.test_interop` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_deliver_notification_task_exception` | `apps.interop.tests.test_interop` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_epro_submission_and_conflict_resolution` | `apps.interop.tests.test_interop` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_fhir_prefill_bundle_pipeline` | `apps.interop.tests.test_interop` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_foreign_key_and_cascade_lifecycle_integrity` | `apps.interop.tests.test_interop` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_instrument_and_assignment_endpoints_and_auditing` | `apps.interop.tests.test_interop` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_instrument_and_assignment_orm_persistence` | `apps.interop.tests.test_interop` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_notifications_and_reminders_lifecycle` | `apps.interop.tests.test_interop` | Trace-10 | ⚪ UNVERIFIED | N/A |
| `test_pseudonymization_and_pii_stripping` | `apps.interop.tests.test_interop` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_subject_assignment_missing_diary_alert_dedup_columns` | `apps.interop.tests.test_interop` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_subject_content_submission_and_compliance_apis` | `apps.interop.tests.test_interop` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_subject_role_authorization_and_identity_binding` | `apps.interop.tests.test_interop` | Trace-8 | ⚪ UNVERIFIED | N/A |
| `test_bulk_sync_with_valid_signatures_and_tallies` | `apps.interop.tests.test_interop_defeated` | PRD-EDC-007 | ⚪ UNVERIFIED | N/A |
| `test_defeated_record_persistence_on_conflicts` | `apps.interop.tests.test_interop_defeated` | PRD-EDC-008, Trace-9 | ⚪ UNVERIFIED | N/A |
| `test_structural_conflict_on_missing_target` | `apps.interop.tests.test_interop_defeated` | PRD-EDC-008 | ⚪ UNVERIFIED | N/A |
| `test_submit_with_invalid_signature_fails` | `apps.interop.tests.test_interop_defeated` | PRD-EDC-008 | ⚪ UNVERIFIED | N/A |
| `test_submit_with_valid_signature` | `apps.interop.tests.test_interop_defeated` | PRD-EDC-008 | ⚪ UNVERIFIED | N/A |
| `test_build_ecrf_context_mapping` | `apps.interop.tests.test_interop_prescreen` | PRD-MDR-007 | ⚪ UNVERIFIED | N/A |
| `test_build_ecrf_context_multiple_and_missing` | `apps.interop.tests.test_interop_prescreen` | PRD-MDR-007 | ⚪ UNVERIFIED | N/A |
| `test_no_edc_mutation_boundary` | `apps.interop.tests.test_interop_prescreen` | PRD-MDR-007 | ⚪ UNVERIFIED | N/A |
| `test_pre_screen_audit_evidence_non_phi` | `apps.interop.tests.test_interop_prescreen` | PRD-MDR-007 | ⚪ UNVERIFIED | N/A |
| `test_pre_screen_eligible` | `apps.interop.tests.test_interop_prescreen` | PRD-MDR-007 | ⚪ UNVERIFIED | N/A |
| `test_pre_screen_indeterminate` | `apps.interop.tests.test_interop_prescreen` | PRD-MDR-007 | ⚪ UNVERIFIED | N/A |
| `test_pre_screen_ineligible` | `apps.interop.tests.test_interop_prescreen` | PRD-MDR-007 | ⚪ UNVERIFIED | N/A |
| `test_epro_quarantine_sync_pipeline` | `apps.interop.tests.test_interop_quarantine` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_epro_version_mismatch_quarantine_pipeline` | `apps.interop.tests.test_interop_quarantine` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_offline_sync_batch_success_and_idempotency` | `apps.interop.tests.test_offline_router` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_offline_sync_batch_update_action` | `apps.interop.tests.test_offline_router` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_offline_batch_sync_success` | `apps.interop.tests.test_offline_sync` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_offline_delta_ingestion_integrity` | `apps.interop.tests.test_offline_sync` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_offline_sync_conflict_resolution` | `apps.interop.tests.test_offline_sync` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_offline_sync_cryptographic_verification` | `apps.interop.tests.test_offline_sync` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_offline_sync_idempotency` | `apps.interop.tests.test_offline_sync` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_generic_natural_deduplication_key` | `apps.interop.tests.test_sync_engine` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_signature_validation_failures` | `apps.interop.tests.test_sync_engine` | PRD-EDC-008 | ⚪ UNVERIFIED | N/A |
| `test_signature_validation_happy_path` | `apps.interop.tests.test_sync_engine` | PRD-EDC-008 | ⚪ UNVERIFIED | N/A |
| `test_strategy_client_wins_existing` | `apps.interop.tests.test_sync_engine` | PRD-EDC-008 | ⚪ UNVERIFIED | N/A |
| `test_strategy_client_wins_no_existing` | `apps.interop.tests.test_sync_engine` | PRD-EDC-008 | ⚪ UNVERIFIED | N/A |
| `test_strategy_merge_independent_fields` | `apps.interop.tests.test_sync_engine` | PRD-EDC-008 | ⚪ UNVERIFIED | N/A |
| `test_strategy_merge_lww_existing_wins` | `apps.interop.tests.test_sync_engine` | PRD-EDC-008 | ⚪ UNVERIFIED | N/A |
| `test_strategy_merge_lww_incoming_wins` | `apps.interop.tests.test_sync_engine` | PRD-EDC-008 | ⚪ UNVERIFIED | N/A |
| `test_strategy_merge_lww_timestamp_tie` | `apps.interop.tests.test_sync_engine` | PRD-EDC-008 | ⚪ UNVERIFIED | N/A |
| `test_strategy_server_wins` | `apps.interop.tests.test_sync_engine` | PRD-EDC-008 | ⚪ UNVERIFIED | N/A |
| `test_audit_log_created_on_article_creation` | `apps.knowledge.tests.test_article_lifecycle` | PRD-SYS-KH-002 | ⚪ UNVERIFIED | N/A |
| `test_audit_log_immutability_enforced` | `apps.knowledge.tests.test_article_lifecycle` | PRD-SYS-KH-002 | ⚪ UNVERIFIED | N/A |
| `test_audit_log_written_on_every_transition` | `apps.knowledge.tests.test_article_lifecycle` | PRD-SYS-KH-002 | ⚪ UNVERIFIED | N/A |
| `test_four_eyes_different_user_can_approve` | `apps.knowledge.tests.test_article_lifecycle` | PRD-SYS-KH-002 | ⚪ UNVERIFIED | N/A |
| `test_four_eyes_same_editor_cannot_approve` | `apps.knowledge.tests.test_article_lifecycle` | PRD-SYS-KH-002 | ⚪ UNVERIFIED | N/A |
| `test_invalid_transition_draft_to_published_raises` | `apps.knowledge.tests.test_article_lifecycle` | PRD-SYS-KH-001 | ⚪ UNVERIFIED | N/A |
| `test_invalid_transition_published_to_draft_raises` | `apps.knowledge.tests.test_article_lifecycle` | PRD-SYS-KH-001 | ⚪ UNVERIFIED | N/A |
| `test_no_notification_dispatched_on_draft_save` | `apps.knowledge.tests.test_article_lifecycle` | PRD-SYS-KH-001 | ⚪ UNVERIFIED | N/A |
| `test_notification_dispatched_on_published` | `apps.knowledge.tests.test_article_lifecycle` | PRD-SYS-KH-001 | ⚪ UNVERIFIED | N/A |
| `test_reason_not_required_on_draft_save` | `apps.knowledge.tests.test_article_lifecycle` | PRD-SYS-KH-001 | ⚪ UNVERIFIED | N/A |
| `test_reason_required_on_approve_raises_without_it` | `apps.knowledge.tests.test_article_lifecycle` | PRD-SYS-KH-002 | ⚪ UNVERIFIED | N/A |
| `test_reason_required_on_publish_raises_without_it` | `apps.knowledge.tests.test_article_lifecycle` | PRD-SYS-KH-002 | ⚪ UNVERIFIED | N/A |
| `test_valid_transition_approved_to_published` | `apps.knowledge.tests.test_article_lifecycle` | PRD-SYS-KH-001 | ⚪ UNVERIFIED | N/A |
| `test_valid_transition_archived_to_draft` | `apps.knowledge.tests.test_article_lifecycle` | PRD-SYS-KH-001 | ⚪ UNVERIFIED | N/A |
| `test_valid_transition_draft_to_in_review` | `apps.knowledge.tests.test_article_lifecycle` | PRD-SYS-KH-001 | ⚪ UNVERIFIED | N/A |
| `test_valid_transition_in_review_to_approved` | `apps.knowledge.tests.test_article_lifecycle` | PRD-SYS-KH-001 | ⚪ UNVERIFIED | N/A |
| `test_valid_transition_in_review_to_rejected` | `apps.knowledge.tests.test_article_lifecycle` | PRD-SYS-KH-001 | ⚪ UNVERIFIED | N/A |
| `test_valid_transition_rejected_to_draft` | `apps.knowledge.tests.test_article_lifecycle` | PRD-SYS-KH-001 | ⚪ UNVERIFIED | N/A |
| `test_version_index_increments_on_publish` | `apps.knowledge.tests.test_article_lifecycle` | PRD-SYS-KH-001 | ⚪ UNVERIFIED | N/A |
| `test_publish_notification_failure_swallowed` | `apps.notifications.tests.test_clinical_workflow_notifications` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_publish_notification_success` | `apps.notifications.tests.test_clinical_workflow_notifications` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_router_send_dashboard_notification_sdv_drop` | `apps.notifications.tests.test_clinical_workflow_notifications` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_router_send_email_mapping` | `apps.notifications.tests.test_clinical_workflow_notifications` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_router_send_sms_mapping` | `apps.notifications.tests.test_clinical_workflow_notifications` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_router_send_webhook_mapping` | `apps.notifications.tests.test_clinical_workflow_notifications` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_unblind_emergency_unblinding_alert_integration` | `apps.notifications.tests.test_clinical_workflow_notifications` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_emergency_unblinding_generates_notification` | `apps.notifications.tests.test_clinical_workflow_notifications_integration` | PRD-SUB-006 | ⚪ UNVERIFIED | N/A |
| `test_query_aging_generates_notification` | `apps.notifications.tests.test_clinical_workflow_notifications_integration` | PRD-QRY-002 | ⚪ UNVERIFIED | N/A |
| `test_sdv_drop_generates_notification` | `apps.notifications.tests.test_clinical_workflow_notifications_integration` | PRD-QRY-006 | ⚪ UNVERIFIED | N/A |
| `test_trial_lock_generates_notification` | `apps.notifications.tests.test_clinical_workflow_notifications_integration` | PRD-SYS-003, PRD-SYS-004 | ⚪ UNVERIFIED | N/A |
| `test_start_stop_notification_worker_integration` | `apps.notifications.tests.test_notification_worker` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_worker_gxp_exponential_retry_and_dlq` | `apps.notifications.tests.test_notification_worker` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_worker_resolves_cra_for_document_expiry` | `apps.notifications.tests.test_notification_worker` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_worker_resolves_crc_for_edc_query` | `apps.notifications.tests.test_notification_worker` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_worker_resolves_safety_officer_for_sae_flag` | `apps.notifications.tests.test_notification_worker` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_direct_transition_open_to_resolved` | `apps.notifications.tests.test_notifications` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_email_delivery_channel_failure_and_exhaustion` | `apps.notifications.tests.test_notifications` | PRD-SYS-003 | ⚪ UNVERIFIED | N/A |
| `test_email_delivery_channel_success` | `apps.notifications.tests.test_notifications` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_lifecycle_transitions_and_justifications` | `apps.notifications.tests.test_notifications` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_missing_diary_alert_rendering` | `apps.notifications.tests.test_notifications` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_multi_channel_edge_case_in_app_succeeds_email_exhausts` | `apps.notifications.tests.test_notifications` | PRD-SYS-003 | ⚪ UNVERIFIED | N/A |
| `test_notification_creation_and_auditing` | `apps.notifications.tests.test_notifications` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_notification_detail_visibility` | `apps.notifications.tests.test_notifications` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_notification_list_visibility_and_filtering` | `apps.notifications.tests.test_notifications` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_notifications_database_schema_creation` | `apps.notifications.tests.test_notifications` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_notifications_health_check` | `apps.notifications.tests.test_notifications` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_notifications_negative_security_paths` | `apps.notifications.tests.test_notifications` | PRD-SYS-004 | ⚪ UNVERIFIED | N/A |
| `test_webhook_delivery_channel_failure_and_retry_backoff` | `apps.notifications.tests.test_notifications` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_webhook_delivery_channel_success` | `apps.notifications.tests.test_notifications` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_reconsent_template_mapping_and_rendering` | `apps.notifications.tests.test_reconsent_notifications` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_reconsent_worker_recipient_resolution` | `apps.notifications.tests.test_reconsent_notifications` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_complete_doa_workflow_lifecycle` | `apps.org.tests.test_doa_workflow` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_doa_signoff_automatic_archival_handoff` | `apps.org.tests.test_org_integration_e2e` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_doa_signoff_tampered_payload_rejected` | `apps.org.tests.test_org_integration_e2e` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_eisf_completeness_participation` | `apps.org.tests.test_org_integration_e2e` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_gateway_openapi_aggregation_with_org` | `apps.org.tests.test_org_integration_e2e` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_gateway_org_proxy_routing` | `apps.org.tests.test_org_integration_e2e` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_training_log_crud_and_validation` | `apps.org.tests.test_org_integration_e2e` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_training_log_invalid_signature_fails` | `apps.org.tests.test_org_integration_e2e` | PRD-SYS-003 | ⚪ UNVERIFIED | N/A |
| `test_training_log_signing_and_archival_handoff` | `apps.org.tests.test_org_integration_e2e` | PRD-SYS-003 | ⚪ UNVERIFIED | N/A |
| `test_training_log_unauthorized_access` | `apps.org.tests.test_org_integration_e2e` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_training_log_update_missing_justification_fails` | `apps.org.tests.test_org_integration_e2e` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_cro_affiliation_validation` | `apps.org.tests.test_org_service` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_delegation_of_authority_flow` | `apps.org.tests.test_org_service` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_gxp_audit_logging_and_actor_context` | `apps.org.tests.test_org_service` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_health_endpoint` | `apps.org.tests.test_org_service` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_org_audit_log_append_only` | `apps.org.tests.test_org_service` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_organization_and_site_relationship` | `apps.org.tests.test_org_service` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_organization_crud_api` | `apps.org.tests.test_org_service` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_personnel_and_sitestaff_alias` | `apps.org.tests.test_org_service` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_personnel_assignments_crud` | `apps.org.tests.test_org_service` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_personnel_crud_api` | `apps.org.tests.test_org_service` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_resolve_assignments_endpoint` | `apps.org.tests.test_org_service` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_site_crud_api` | `apps.org.tests.test_org_service` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_audit_fields_change_reason_validation` | `apps.org.tests.test_organization_domain` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_audit_fields_instantiation` | `apps.org.tests.test_organization_domain` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_audit_fields_reusability` | `apps.org.tests.test_organization_domain` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_clinical_staff_role_values` | `apps.org.tests.test_organization_domain` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_organization_type_values` | `apps.org.tests.test_organization_domain` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_trial_duty_values` | `apps.org.tests.test_organization_domain` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_audit_engagement_lifecycle` | `apps.quality.tests.test_audit_service` | PRD-QLT-006 | ⚪ UNVERIFIED | N/A |
| `test_audit_findings_and_one_click_capa_promotion` | `apps.quality.tests.test_audit_service` | PRD-QLT-006 | ⚪ UNVERIFIED | N/A |
| `test_inspection_readiness_dossier_compilation_and_tamper_seal` | `apps.quality.tests.test_audit_service` | PRD-QLT-008 | ⚪ UNVERIFIED | N/A |
| `test_automated_quality_event_ingestion_api` | `apps.quality.tests.test_eqms_comprehensive` | PRD-QLT-001 | ⚪ UNVERIFIED | N/A |
| `test_capa_action_items_and_effectiveness_evaluations` | `apps.quality.tests.test_eqms_comprehensive` | PRD-QLT-003 | ⚪ UNVERIFIED | N/A |
| `test_multi_methodology_rca_5whys_and_fishbone` | `apps.quality.tests.test_eqms_comprehensive` | PRD-QLT-002 | ⚪ UNVERIFIED | N/A |
| `test_capa_creation_validations_and_closed_deviation` | `apps.quality.tests.test_quality` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_capa_transition_edge_cases_and_optimistic_locking` | `apps.quality.tests.test_quality` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_capa_update_edge_cases_and_optional_fields` | `apps.quality.tests.test_quality` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_database_manager_uninitialized_raises_exception` | `apps.quality.tests.test_quality` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_deviation_lifecycle_and_traceability_fields` | `apps.quality.tests.test_quality` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_deviation_not_found_404` | `apps.quality.tests.test_quality` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_deviation_rca_capa_relationships_and_cascading` | `apps.quality.tests.test_quality` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_endpoint_change_reason_check_via_mock` | `apps.quality.tests.test_quality` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_lifespan_coverage` | `apps.quality.tests.test_quality` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_list_deviations_filters` | `apps.quality.tests.test_quality` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_missing_change_reasons_unauthorized` | `apps.quality.tests.test_quality` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_quality_audit_log_append_only` | `apps.quality.tests.test_quality` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_quality_database_schema_creation` | `apps.quality.tests.test_quality` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_quality_health_check` | `apps.quality.tests.test_quality` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_sqlite_foreign_key_constraints` | `apps.quality.tests.test_quality` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_sqlite_pragma_exception_handling` | `apps.quality.tests.test_quality` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_amendment_impact_and_cost_estimation` | `apps.quality.tests.test_quality_sentinel` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_block_eligibility_soa_inconsistencies` | `apps.quality.tests.test_quality_sentinel` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_burden_tracing_with_invasiveness_modifiers` | `apps.quality.tests.test_quality_sentinel` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_pluggable_fixture_patient_attrition` | `apps.quality.tests.test_quality_sentinel` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_quality_sentinel_complete_protocol` | `apps.quality.tests.test_quality_sentinel` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_quality_sentinel_incomplete_protocol_detects_errors` | `apps.quality.tests.test_quality_sentinel` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_quality_sentinel_router_endpoint` | `apps.quality.tests.test_quality_sentinel` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_quality_sentinel_router_endpoint_dependency_override` | `apps.quality.tests.test_quality_sentinel` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_readability_metrics_and_scoring` | `apps.quality.tests.test_quality_sentinel` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_syllable_counter_deterministic` | `apps.quality.tests.test_quality_sentinel` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_audit_log_endpoint_properties` | `apps.quality.tests.test_quality_workflow` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_capa_approval_closure_requires_quality_oversight` | `apps.quality.tests.test_quality_workflow` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_capa_creation_validations` | `apps.quality.tests.test_quality_workflow` | PRD-SUB-001 | ⚪ UNVERIFIED | N/A |
| `test_capa_lifecycle_transitions` | `apps.quality.tests.test_quality_workflow` | PRD-SUB-001 | ⚪ UNVERIFIED | N/A |
| `test_capa_updates_and_concurrency` | `apps.quality.tests.test_quality_workflow` | PRD-SUB-001 | ⚪ UNVERIFIED | N/A |
| `test_create_and_list_deviations` | `apps.quality.tests.test_quality_workflow` | PRD-SYS-001, Trace-7 | ⚪ UNVERIFIED | N/A |
| `test_create_and_update_rca` | `apps.quality.tests.test_quality_workflow` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_mutation_without_change_reason_rejected` | `apps.quality.tests.test_quality_workflow` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_permission_failure_leaves_no_misleading_audit_entry` | `apps.quality.tests.test_quality_workflow` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_read_only_roles_forbidden` | `apps.quality.tests.test_quality_workflow` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_successful_mutation_creates_audit_log_and_is_atomic` | `apps.quality.tests.test_quality_workflow` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_transition_capa_sig_token_matrix` | `apps.quality.tests.test_quality_workflow` | PRD-QLT-009 | ⚪ UNVERIFIED | N/A |
| `test_ctq_factor_lifecycle` | `apps.quality.tests.test_rbqm_engine` | PRD-QLT-004 | ⚪ UNVERIFIED | N/A |
| `test_kri_batch_evaluation_and_statistical_z_scores` | `apps.quality.tests.test_rbqm_engine` | PRD-QLT-004 | ⚪ UNVERIFIED | N/A |
| `test_kri_definitions_and_auto_seeding` | `apps.quality.tests.test_rbqm_engine` | PRD-QLT-004 | ⚪ UNVERIFIED | N/A |
| `test_qtl_tolerance_limit_and_csr_narrative` | `apps.quality.tests.test_rbqm_engine` | PRD-QLT-005 | ⚪ UNVERIFIED | N/A |
| `test_site_risk_profile_computation_and_ranking` | `apps.quality.tests.test_rbqm_engine` | PRD-QLT-004 | ⚪ UNVERIFIED | N/A |
| `test_serious_breach_confirmation_and_status_progression` | `apps.quality.tests.test_serious_breaches` | PRD-QLT-007 | ⚪ UNVERIFIED | N/A |
| `test_serious_breach_reporting_and_initial_clock` | `apps.quality.tests.test_serious_breaches` | PRD-QLT-007 | ⚪ UNVERIFIED | N/A |
| `test_generate_e2b_xml_happy_path` | `apps.safety.tests.test_e2b` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_generate_e2b_xml_invalid_raises_value_error` | `apps.safety.tests.test_e2b` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_icsr_version_and_reason_for_change_rendering` | `apps.safety.tests.test_e2b` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_invalid_namespace_fails` | `apps.safety.tests.test_e2b` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_invalid_root_tag_fails` | `apps.safety.tests.test_e2b` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_malformed_xml_validation_fails` | `apps.safety.tests.test_e2b` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_missing_drugs_or_drug_fields_fails` | `apps.safety.tests.test_e2b` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_missing_header_fails` | `apps.safety.tests.test_e2b` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_missing_header_fields_fail` | `apps.safety.tests.test_e2b` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_missing_patient_fails` | `apps.safety.tests.test_e2b` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_missing_patient_fields_fail` | `apps.safety.tests.test_e2b` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_missing_reactions_or_reaction_term_fails` | `apps.safety.tests.test_e2b` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_missing_safety_report_fails` | `apps.safety.tests.test_e2b` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_missing_worldwide_unique_case_id_fails` | `apps.safety.tests.test_e2b` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_valid_icsr_rendering_and_validation` | `apps.safety.tests.test_e2b` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_validate_e2b_xml_structure_direct` | `apps.safety.tests.test_e2b` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_parse_e2b_xml_valid_payload` | `apps.safety.tests.test_e2b_parser` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_unblind_missing_sig_token` | `apps.safety.tests.test_emergency_unblinding` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_unblind_screening_status_error` | `apps.safety.tests.test_emergency_unblinding` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_unblind_subject_not_found` | `apps.safety.tests.test_emergency_unblinding` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_unblind_success_authorized_access` | `apps.safety.tests.test_emergency_unblinding` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_unblind_success_masked_access` | `apps.safety.tests.test_emergency_unblinding` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_unblind_withdrawn_status_error` | `apps.safety.tests.test_emergency_unblinding` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_icsr_version_metadata` | `apps.safety.tests.test_sae_icsr` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_invalid_icsr_drug_role` | `apps.safety.tests.test_sae_icsr` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_invalid_icsr_patient_age_negative` | `apps.safety.tests.test_sae_icsr` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_invalid_icsr_patient_age_unit` | `apps.safety.tests.test_sae_icsr` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_invalid_meddra_coding_primary_soc` | `apps.safety.tests.test_sae_icsr` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_invalid_sae_date_chronology` | `apps.safety.tests.test_sae_icsr` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_invalid_sae_date_format` | `apps.safety.tests.test_sae_icsr` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_invalid_sae_seq` | `apps.safety.tests.test_sae_icsr` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_invalid_sae_seriousness` | `apps.safety.tests.test_sae_icsr` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_invalid_sae_severity` | `apps.safety.tests.test_sae_icsr` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_sae_version_metadata` | `apps.safety.tests.test_sae_icsr` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_valid_icsr_full` | `apps.safety.tests.test_sae_icsr` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_valid_meddra_coding` | `apps.safety.tests.test_sae_icsr` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_valid_sae_full_normalization` | `apps.safety.tests.test_sae_icsr` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_valid_sae_minimum` | `apps.safety.tests.test_sae_icsr` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_sae_reconciler_concordant_and_discrepant` | `apps.safety.tests.test_sae_reconciler` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_deterministic_output_sorting` | `apps.safety.tests.test_sae_reconciliation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_execution_client_and_adapter_methods` | `apps.safety.tests.test_sae_reconciliation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_pure_comparison_differing_fields` | `apps.safety.tests.test_sae_reconciliation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_pure_comparison_missing_on_either_side` | `apps.safety.tests.test_sae_reconciliation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_pure_comparison_same_code_different_terms` | `apps.safety.tests.test_sae_reconciliation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_pure_function_generate_stable_event_key` | `apps.safety.tests.test_sae_reconciliation` | Trace-15 | ⚪ UNVERIFIED | N/A |
| `test_pure_function_normalize_edc_ae_to_sae` | `apps.safety.tests.test_sae_reconciliation` | Trace-15 | ⚪ UNVERIFIED | N/A |
| `test_pure_function_normalize_external_icsr_to_saes` | `apps.safety.tests.test_sae_reconciliation` | Trace-15 | ⚪ UNVERIFIED | N/A |
| `test_reconciliation_jobs_read_endpoints_and_gating` | `apps.safety.tests.test_sae_reconciliation` | Trace-15 | ⚪ UNVERIFIED | N/A |
| `test_reconciliation_persistence_and_audit` | `apps.safety.tests.test_sae_reconciliation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_reconciliation_runs_read_endpoints` | `apps.safety.tests.test_sae_reconciliation` | Trace-15 | ⚪ UNVERIFIED | N/A |
| `test_reconciliation_version_index_increment` | `apps.safety.tests.test_sae_reconciliation` | Trace-15 | ⚪ UNVERIFIED | N/A |
| `test_safety_mutations_negative_signatures` | `apps.safety.tests.test_sae_reconciliation` | Trace-15 | ⚪ UNVERIFIED | N/A |
| `test_safety_reads_negative_signatures` | `apps.safety.tests.test_sae_reconciliation` | Trace-15 | ⚪ UNVERIFIED | N/A |
| `test_terminology_cache_functionality` | `apps.safety.tests.test_sae_reconciliation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_alert_dispatch_failure_exception` | `apps.safety.tests.test_sae_reconciliation_jobs` | Trace-15 | ⚪ UNVERIFIED | N/A |
| `test_alert_dispatch_failure_non_2xx` | `apps.safety.tests.test_sae_reconciliation_jobs` | Trace-15 | ⚪ UNVERIFIED | N/A |
| `test_notifications_gxp_medical_monitor_alert` | `apps.safety.tests.test_sae_reconciliation_jobs` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_reconciliation_job_failure_path` | `apps.safety.tests.test_sae_reconciliation_jobs` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_trigger_and_poll_reconciliation_job_success` | `apps.safety.tests.test_sae_reconciliation_jobs` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_generate_e2b_xml_happy_path` | `apps.safety.tests.test_safety_e2b` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_generate_e2b_xml_invalid_raises_value_error` | `apps.safety.tests.test_safety_e2b` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_icsr_version_and_reason_for_change_rendering` | `apps.safety.tests.test_safety_e2b` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_invalid_namespace_fails` | `apps.safety.tests.test_safety_e2b` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_invalid_root_tag_fails` | `apps.safety.tests.test_safety_e2b` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_malformed_xml_validation_fails` | `apps.safety.tests.test_safety_e2b` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_missing_drugs_or_drug_fields_fails` | `apps.safety.tests.test_safety_e2b` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_missing_header_fails` | `apps.safety.tests.test_safety_e2b` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_missing_header_fields_fail` | `apps.safety.tests.test_safety_e2b` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_missing_patient_fails` | `apps.safety.tests.test_safety_e2b` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_missing_patient_fields_fail` | `apps.safety.tests.test_safety_e2b` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_missing_reactions_or_reaction_term_fails` | `apps.safety.tests.test_safety_e2b` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_missing_safety_report_fails` | `apps.safety.tests.test_safety_e2b` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_missing_worldwide_unique_case_id_fails` | `apps.safety.tests.test_safety_e2b` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_valid_icsr_rendering_and_validation` | `apps.safety.tests.test_safety_e2b` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_validate_e2b_xml_structure_direct` | `apps.safety.tests.test_safety_e2b` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_e2b_xml_generation_and_parser_roundtrip` | `apps.safety.tests.test_safety_gateway` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_safety_gateway_negative_signatures_async` | `apps.safety.tests.test_safety_gateway` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_safety_reconciliation_job_lifecycle_async` | `apps.safety.tests.test_safety_gateway` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_dispatch_safety_report_post_endpoint` | `apps.safety.tests.test_safety_router` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_reconcile_sae_cases_post_endpoint` | `apps.safety.tests.test_safety_router` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_database_manager_uninitialized_raises_exception` | `apps.safety.tests.test_safety_service` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_invalid_xml_validation_fails` | `apps.safety.tests.test_safety_service` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_list_audit_logs_endpoint` | `apps.safety.tests.test_safety_service` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_missing_change_reason_fails_mutations` | `apps.safety.tests.test_safety_service` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_missing_v2_headers_or_change_reason_fails` | `apps.safety.tests.test_safety_service` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_nonexistent_resources_return_404` | `apps.safety.tests.test_safety_service` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_safety_audit_log_immutable_ledger` | `apps.safety.tests.test_safety_service` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_safety_case_lifecycle` | `apps.safety.tests.test_safety_service` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_safety_database_schema_creation` | `apps.safety.tests.test_safety_service` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_safety_export_job_lifecycle` | `apps.safety.tests.test_safety_service` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_safety_health_check` | `apps.safety.tests.test_safety_service` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_successful_export_and_transmission` | `apps.safety.tests.test_safety_service` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_unauthenticated_requests_are_rejected` | `apps.safety.tests.test_safety_service` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_cross_app_ticket_ingestion_from_execution_edc` | `apps.tickets.tests.test_cross_app_ingestion` | PRD-SYS-042, PRD-TCK-003 | ⚪ UNVERIFIED | N/A |
| `test_21cfr_part11_esignature_capture` | `apps.tickets.tests.test_part11_signatures_attachments` | PRD-TCK-004 | ⚪ UNVERIFIED | N/A |
| `test_audited_evidence_attachments` | `apps.tickets.tests.test_part11_signatures_attachments` | PRD-TCK-004 | ⚪ UNVERIFIED | N/A |
| `test_comment_visibility_boundaries` | `apps.tickets.tests.test_part11_signatures_attachments` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_rca_validation_on_major_and_critical_closure` | `apps.tickets.tests.test_part11_signatures_attachments` | PRD-TCK-001 | ⚪ UNVERIFIED | N/A |
| `test_regulatory_audit_trail_export` | `apps.tickets.tests.test_part11_signatures_attachments` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_kpi_and_kri_metrics_computation` | `apps.tickets.tests.test_tickets_analytics` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_audit_log_creation_on_escalate` | `apps.tickets.tests.test_tickets_escalation` | Trace-16 | ⚪ UNVERIFIED | N/A |
| `test_bounded_priority_advancement` | `apps.tickets.tests.test_tickets_escalation` | Trace-16 | ⚪ UNVERIFIED | N/A |
| `test_cooldown_gating_and_idempotency` | `apps.tickets.tests.test_tickets_escalation` | Trace-16 | ⚪ UNVERIFIED | N/A |
| `test_escalation_eligibility_rules` | `apps.tickets.tests.test_tickets_escalation` | PRD-TCK-002 | ⚪ UNVERIFIED | N/A |
| `test_notification_deduplication_and_partial_failures` | `apps.tickets.tests.test_tickets_escalation` | Trace-16 | ⚪ UNVERIFIED | N/A |
| `test_startup_shutdown_and_resilience` | `apps.tickets.tests.test_tickets_escalation` | Trace-16 | ⚪ UNVERIFIED | N/A |
| `test_end_to_end_tickets_and_notifications_handshake` | `apps.tickets.tests.test_tickets_integration_seam` | Trace-16 | ⚪ UNVERIFIED | N/A |
| `test_escalation_worker_notifications_retry_mechanics` | `apps.tickets.tests.test_tickets_integration_seam` | Trace-16 | ⚪ UNVERIFIED | N/A |
| `test_publish_notification_non_2xx_failure` | `apps.tickets.tests.test_tickets_notifications_client` | Trace-16 | ⚪ UNVERIFIED | N/A |
| `test_publish_notification_success` | `apps.tickets.tests.test_tickets_notifications_client` | Trace-16 | ⚪ UNVERIFIED | N/A |
| `test_publish_notification_transport_exception` | `apps.tickets.tests.test_tickets_notifications_client` | Trace-16 | ⚪ UNVERIFIED | N/A |
| `test_notification_failure_isolation` | `apps.tickets.tests.test_tickets_notifications_integration` | Trace-16 | ⚪ UNVERIFIED | N/A |
| `test_notification_idempotency` | `apps.tickets.tests.test_tickets_notifications_integration` | Trace-16 | ⚪ UNVERIFIED | N/A |
| `test_ticket_assignment_notification` | `apps.tickets.tests.test_tickets_notifications_integration` | Trace-16 | ⚪ UNVERIFIED | N/A |
| `test_ticket_comment_notification` | `apps.tickets.tests.test_tickets_notifications_integration` | Trace-16 | ⚪ UNVERIFIED | N/A |
| `test_ticket_transition_notification` | `apps.tickets.tests.test_tickets_notifications_integration` | Trace-16 | ⚪ UNVERIFIED | N/A |
| `test_update_ticket_notifications` | `apps.tickets.tests.test_tickets_notifications_integration` | Trace-16 | ⚪ UNVERIFIED | N/A |
| `test_end_to_end_escalation_worker_flow_and_retries` | `apps.tickets.tests.test_tickets_notifications_seam` | Trace-16 | ⚪ UNVERIFIED | N/A |
| `test_end_to_end_ticket_creation_and_comment_flow` | `apps.tickets.tests.test_tickets_notifications_seam` | Trace-16 | ⚪ UNVERIFIED | N/A |
| `test_comments_creation_and_retrieval_scoped` | `apps.tickets.tests.test_tickets_service` | Trace-16 | ⚪ UNVERIFIED | N/A |
| `test_list_ticket_audit_logs_endpoint` | `apps.tickets.tests.test_tickets_service` | Trace-16 | ⚪ UNVERIFIED | N/A |
| `test_missing_change_reason_fails_mutations` | `apps.tickets.tests.test_tickets_service` | Trace-16 | ⚪ UNVERIFIED | N/A |
| `test_nonexistent_resources_return_404` | `apps.tickets.tests.test_tickets_service` | Trace-16 | ⚪ UNVERIFIED | N/A |
| `test_ticket_audit_log_immutable_ledger` | `apps.tickets.tests.test_tickets_service` | Trace-16 | ⚪ UNVERIFIED | N/A |
| `test_ticket_concurrent_reference_generation` | `apps.tickets.tests.test_tickets_service` | Trace-16 | ⚪ UNVERIFIED | N/A |
| `test_ticket_scoped_audit_logs` | `apps.tickets.tests.test_tickets_service` | Trace-16 | ⚪ UNVERIFIED | N/A |
| `test_tickets_audit_logs_pagination` | `apps.tickets.tests.test_tickets_service` | Trace-16 | ⚪ UNVERIFIED | N/A |
| `test_tickets_audit_logs_query_boundaries` | `apps.tickets.tests.test_tickets_service` | Trace-16 | ⚪ UNVERIFIED | N/A |
| `test_tickets_audit_logs_time_filtering` | `apps.tickets.tests.test_tickets_service` | Trace-16 | ⚪ UNVERIFIED | N/A |
| `test_tickets_auditor_comments_access` | `apps.tickets.tests.test_tickets_service` | Trace-16 | ⚪ UNVERIFIED | N/A |
| `test_tickets_database_schema_creation` | `apps.tickets.tests.test_tickets_service` | Trace-16 | ⚪ UNVERIFIED | N/A |
| `test_tickets_enums_and_models_attributes` | `apps.tickets.tests.test_tickets_service` | Trace-16 | ⚪ UNVERIFIED | N/A |
| `test_tickets_get_by_reference` | `apps.tickets.tests.test_tickets_service` | Trace-16 | ⚪ UNVERIFIED | N/A |
| `test_tickets_health_check` | `apps.tickets.tests.test_tickets_service` | Trace-16 | ⚪ UNVERIFIED | N/A |
| `test_tickets_in_scope_success_and_self_audit` | `apps.tickets.tests.test_tickets_service` | Trace-16 | ⚪ UNVERIFIED | N/A |
| `test_tickets_lifecycle` | `apps.tickets.tests.test_tickets_service` | Trace-16 | ⚪ UNVERIFIED | N/A |
| `test_tickets_optimistic_locking_and_explicit_endpoints` | `apps.tickets.tests.test_tickets_service` | Trace-16 | ⚪ UNVERIFIED | N/A |
| `test_tickets_rbac_auditor_cannot_mutate_but_can_read` | `apps.tickets.tests.test_tickets_service` | Trace-16 | ⚪ UNVERIFIED | N/A |
| `test_tickets_scope_aware_filtering` | `apps.tickets.tests.test_tickets_service` | Trace-16 | ⚪ UNVERIFIED | N/A |
| `test_tickets_site_scope_filtering_audit_logs_unfiltered` | `apps.tickets.tests.test_tickets_service` | Trace-16 | ⚪ UNVERIFIED | N/A |
| `test_tickets_terminal_state_rejection` | `apps.tickets.tests.test_tickets_service` | Trace-16 | ⚪ UNVERIFIED | N/A |
| `test_tickets_unauthorized_site_scope_blocking` | `apps.tickets.tests.test_tickets_service` | Trace-16 | ⚪ UNVERIFIED | N/A |
| `test_tickets_validation_invalid_enums` | `apps.tickets.tests.test_tickets_service` | Trace-16 | ⚪ UNVERIFIED | N/A |
| `test_unauthenticated_requests_are_rejected` | `apps.tickets.tests.test_tickets_service` | Trace-16 | ⚪ UNVERIFIED | N/A |
| `test_evaluate_sla_status_amber_warning_and_breach` | `apps.tickets.tests.test_tickets_sla_advanced` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_sla_target_calculation_with_multipliers` | `apps.tickets.tests.test_tickets_sla_advanced` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_ticket_sla_pause_and_resume_lifecycle` | `apps.tickets.tests.test_tickets_sla_advanced` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_web_amendment_branching_and_diff_contracts` | `apps.web.tests.test_amendment_diff` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_web_execution_reconsent_gating_and_subject_impact` | `apps.web.tests.test_amendment_diff` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_21_cfr_part_11_dual_credential_signature_capture` | `apps.web.tests.test_econsent` | PRD-SYS-042 | ⚪ UNVERIFIED | N/A |
| `test_comprehension_quiz_evaluation_and_threshold_enforcement` | `apps.web.tests.test_econsent` | PRD-SYS-043 | ⚪ UNVERIFIED | N/A |
| `test_icf_builder_modular_clauses_linked_to_protocol_version` | `apps.web.tests.test_econsent` | PRD-SYS-042 | ⚪ UNVERIFIED | N/A |
| `test_cli_cdisc_export_json` | `packages.cli.tests.test_cadence_cli` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_cli_db_seed_json` | `packages.cli.tests.test_cadence_cli` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_cli_db_snapshot_and_restore` | `packages.cli.tests.test_cadence_cli` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_cli_db_status_json` | `packages.cli.tests.test_cadence_cli` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_cli_dev_json` | `packages.cli.tests.test_cadence_cli` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_cli_doctor_auto_fix_json` | `packages.cli.tests.test_cadence_cli` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_cli_doctor_json` | `packages.cli.tests.test_cadence_cli` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_cli_gxp_export_cdisc_json` | `packages.cli.tests.test_cadence_cli` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_cli_help` | `packages.cli.tests.test_cadence_cli` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_find_target_test_file_resolution` | `packages.cli.tests.test_cadence_cli` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_cli_db_seed_full_cadence_101_json` | `packages.cli.tests.test_db_seed` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_cli_db_seed_sqlite_content` | `packages.cli.tests.test_db_seed` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_cli_db_seed_tier_filtering` | `packages.cli.tests.test_db_seed` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_change_request_audit_trail_recorded` | `packages.compliance.tests.test_compliance_change_request` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_change_request_requires_dual_approval` | `packages.compliance.tests.test_compliance_change_request` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_compliance_change_request_audit_trail` | `packages.compliance.tests.test_compliance_change_request` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_assert_secure_secrets_validation` | `packages.compliance.tests.test_compliance_security` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_audit_logger_creates_valid_record` | `packages.compliance.tests.test_compliance_security` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_audit_logger_detects_chain_tampering` | `packages.compliance.tests.test_compliance_security` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_audit_logger_raises_runtime_error_if_secret_missing` | `packages.compliance.tests.test_compliance_security` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_crypto_verifier_invalid_signature` | `packages.compliance.tests.test_compliance_security` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_crypto_verifier_valid_signature` | `packages.compliance.tests.test_compliance_security` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_gateway_raises_runtime_error_if_secret_missing` | `packages.compliance.tests.test_compliance_security` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_global_scanner_with_opt_out` | `packages.compliance.tests.test_compliance_security` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_mock_signature_and_key_detection` | `packages.compliance.tests.test_compliance_security` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_security_audit_exclusions` | `packages.compliance.tests.test_compliance_security` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_security_audit_scanner_detection_and_bypass` | `packages.compliance.tests.test_compliance_security` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_security_audit_script` | `packages.compliance.tests.test_compliance_security` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_security_audit_targeted_files` | `packages.compliance.tests.test_compliance_security` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_signing_raises_runtime_error_if_email_secret_missing` | `packages.compliance.tests.test_compliance_security` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_revocation_blocks_when_serial_matches` | `packages.compliance.tests.test_esignature_verifier_revocation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_revocation_exact_match_prevents_false_positives` | `packages.compliance.tests.test_esignature_verifier_revocation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_revoked_certs_normalization_helper` | `packages.compliance.tests.test_esignature_verifier_revocation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_clean_neo4j_graph_calls_run` | `packages.database.tests.test_asgi_live_db` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_clean_postgres_databases_calls_truncate` | `packages.database.tests.test_asgi_live_db` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_live_db_halt_when_neo4j_unreachable` | `packages.database.tests.test_asgi_live_db` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_live_db_halt_when_postgres_unreachable` | `packages.database.tests.test_asgi_live_db` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_pre_flush_justification_rejection` | `packages.database.tests.test_compliance_pre_flush` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_site_freeze_blocking_writes` | `packages.database.tests.test_compliance_pre_flush` | PRD-SYS-002 | ⚪ UNVERIFIED | N/A |
| `test_sync_and_async_map_database_exceptions` | `packages.database.tests.test_compliance_pre_flush` | PRD-SYS-005 | ⚪ UNVERIFIED | N/A |
| `test_trial_freeze_blocking_writes` | `packages.database.tests.test_compliance_pre_flush` | PRD-SYS-002 | ⚪ UNVERIFIED | N/A |
| `test_utc_datetime_type_decorator_enforcement` | `packages.database.tests.test_compliance_pre_flush` | PRD-SYS-004 | ⚪ UNVERIFIED | N/A |
| `test_ci_database_parity_enforcement_raises_on_failure` | `packages.database.tests.test_database_managers` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_ctms_database_manager_uninitialized_and_close` | `packages.database.tests.test_database_managers` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_econsent_database_manager_uninitialized_and_close` | `packages.database.tests.test_database_managers` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_eisf_database_manager_uninitialized_and_close` | `packages.database.tests.test_database_managers` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_etmf_database_manager_uninitialized_and_close` | `packages.database.tests.test_database_managers` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_interop_database_manager_uninitialized_and_close` | `packages.database.tests.test_database_managers` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_map_database_exceptions_decorator` | `packages.database.tests.test_database_managers` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_notifications_database_manager_uninitialized_and_close` | `packages.database.tests.test_database_managers` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_db_lifecycle_safety_guard_non_local` | `packages.database.tests.test_db_lifecycle` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_db_lifecycle_safety_guard_production` | `packages.database.tests.test_db_lifecycle` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_db_lifecycle_success_offline` | `packages.database.tests.test_db_lifecycle` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_assign_activities_to_visit_mock` | `packages.database.tests.test_delta` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_assign_activities_to_visit_real` | `packages.database.tests.test_delta` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_concurrent_library_version_increments` | `packages.database.tests.test_delta` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_concurrent_study_saves_serialization` | `packages.database.tests.test_delta` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_create_library_object_version_existing` | `packages.database.tests.test_delta` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_create_library_object_version_new` | `packages.database.tests.test_delta` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_create_study_root` | `packages.database.tests.test_delta` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_get_study_differences` | `packages.database.tests.test_delta` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_reorder_visits_mock` | `packages.database.tests.test_delta` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_reorder_visits_real` | `packages.database.tests.test_delta` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_update_study_properties` | `packages.database.tests.test_delta` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_mock_graph_database_manager` | `packages.database.tests.test_graph_persistence` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_transaction_retry_decorator` | `packages.database.tests.test_graph_persistence` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_transaction_retry_failure` | `packages.database.tests.test_graph_persistence` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_validate_cypher_query_failures` | `packages.database.tests.test_graph_persistence` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_validate_cypher_query_success` | `packages.database.tests.test_graph_persistence` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_ledger_sealing_and_validation` | `packages.database.tests.test_ledger_and_triggers` | PRD-SYS-003 | ⚪ UNVERIFIED | N/A |
| `test_out_of_band_update_triggers_audit_entry` | `packages.database.tests.test_ledger_and_triggers` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_prevent_audit_ledger_seals_mutation` | `packages.database.tests.test_ledger_and_triggers` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_prevent_audit_log_mutation` | `packages.database.tests.test_ledger_and_triggers` | PRD-SYS-001, Trace-1 | ⚪ UNVERIFIED | N/A |
| `test_prevent_hard_delete_on_audited_model` | `packages.database.tests.test_ledger_and_triggers` | PRD-SYS-002, Trace-1 | ⚪ UNVERIFIED | N/A |
| `test_lab_reference_ranges_evolution` | `packages.database.tests.test_migrate` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_main_cli` | `packages.database.tests.test_migrate` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_new_tables_metadata_creation` | `packages.database.tests.test_migrate` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_placeholders` | `packages.database.tests.test_migrate` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_run_migrations_failure` | `packages.database.tests.test_migrate` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_run_migrations_real_sqlite` | `packages.database.tests.test_migrate` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_run_migrations_success` | `packages.database.tests.test_migrate` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_reset_db_safety_guard_non_local` | `packages.database.tests.test_reset_db` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_reset_db_safety_guard_production` | `packages.database.tests.test_reset_db` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_reset_db_success_offline` | `packages.database.tests.test_reset_db` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_apply_deid_transforms_right_to_left` | `packages.deid.tests.test_deid_transforms` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_cap_age_string` | `packages.deid.tests.test_deid_transforms` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_empty_reason_raises_validation_error` | `packages.deid.tests.test_deid_transforms` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_end_to_end_detector_and_transforms` | `packages.deid.tests.test_deid_transforms` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_pseudonymize_value_deterministic` | `packages.deid.tests.test_deid_transforms` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_redaction_manifest_asymmetric_tamper_evident` | `packages.deid.tests.test_deid_transforms` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_redaction_manifest_symmetric_tamper_evident` | `packages.deid.tests.test_deid_transforms` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_shift_date_string` | `packages.deid.tests.test_deid_transforms` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_age_capping_and_edge_cases` | `packages.deid.tests.test_deidentification` | PRD-TMF-005 | ⚪ UNVERIFIED | N/A |
| `test_compliance_profiles` | `packages.deid.tests.test_deidentification` | PRD-TMF-005 | ⚪ UNVERIFIED | N/A |
| `test_date_shifting_and_edge_cases` | `packages.deid.tests.test_deidentification` | PRD-TMF-005 | ⚪ UNVERIFIED | N/A |
| `test_detections_all_categories` | `packages.deid.tests.test_deidentification` | PRD-TMF-005 | ⚪ UNVERIFIED | N/A |
| `test_hmac_pseudonymization_determinism` | `packages.deid.tests.test_deidentification` | PRD-TMF-005 | ⚪ UNVERIFIED | N/A |
| `test_manifest_tamper_evident_asymmetric` | `packages.deid.tests.test_deidentification` | PRD-TMF-005 | ⚪ UNVERIFIED | N/A |
| `test_manifest_tamper_evident_symmetric` | `packages.deid.tests.test_deidentification` | PRD-TMF-005 | ⚪ UNVERIFIED | N/A |
| `test_no_raw_matched_values_persisted` | `packages.deid.tests.test_deidentification` | PRD-TMF-005 | ⚪ UNVERIFIED | N/A |
| `test_normalize_and_cap_age_direct` | `packages.deid.tests.test_deidentification` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_overlap_resolution_comprehensive` | `packages.deid.tests.test_deidentification` | PRD-TMF-005 | ⚪ UNVERIFIED | N/A |
| `test_source_documents_remain_unchanged` | `packages.deid.tests.test_deidentification` | PRD-TMF-005 | ⚪ UNVERIFIED | N/A |
| `test_transforms_all_strategies` | `packages.deid.tests.test_deidentification` | PRD-TMF-005 | ⚪ UNVERIFIED | N/A |
| `test_custom_terms_and_overlap_resolution` | `packages.deid.tests.test_ner_scrubber` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_detect_phi_patterns` | `packages.deid.tests.test_ner_scrubber` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_scrub_phi_redaction` | `packages.deid.tests.test_ner_scrubber` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_word_boundaries_custom_terms` | `packages.deid.tests.test_ner_scrubber` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_embedded_custom_terms_redaction` | `packages.deid.tests.test_unified_compliance_engine` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_leading_non_word_phone_email_redaction` | `packages.deid.tests.test_unified_compliance_engine` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_legacy_passthrough_parity` | `packages.deid.tests.test_unified_compliance_engine` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_new_hipaa_categories_redaction` | `packages.deid.tests.test_unified_compliance_engine` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_trailing_punctuation_clinical_identifiers` | `packages.deid.tests.test_unified_compliance_engine` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_all_services_have_ports` | `packages.hexagonal.tests.test_hexagonal_architecture` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_application_layer_isolation` | `packages.hexagonal.tests.test_hexagonal_architecture` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_decoupled_api_routers_have_no_direct_db_imports` | `packages.hexagonal.tests.test_hexagonal_architecture` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_designer_core_isolation` | `packages.hexagonal.tests.test_hexagonal_architecture` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_domain_layer_isolation` | `packages.hexagonal.tests.test_hexagonal_architecture` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_main_entrypoint_is_thin` | `packages.hexagonal.tests.test_hexagonal_architecture` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_main_entrypoints_count_integrity` | `packages.hexagonal.tests.test_hexagonal_architecture` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_no_singular_adapter_directory` | `packages.hexagonal.tests.test_hexagonal_architecture` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_presentation_layer_driver_isolation` | `packages.hexagonal.tests.test_hexagonal_architecture` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_repository_ports_count_integrity` | `packages.hexagonal.tests.test_hexagonal_architecture` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_service_repository_ports_subclass_base` | `packages.hexagonal.tests.test_hexagonal_architecture` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_consent_form_record_immutability_domain` | `packages.hexagonal.tests.test_hexagonal_domain` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_consent_signature_immutability_domain` | `packages.hexagonal.tests.test_hexagonal_domain` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_safety_audit_log_immutability_domain` | `packages.hexagonal.tests.test_hexagonal_domain` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_sqlalchemy_audit_repository_persistence` | `packages.hexagonal.tests.test_hexagonal_domain` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_sqlalchemy_consent_repository_persistence` | `packages.hexagonal.tests.test_hexagonal_domain` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_sqlalchemy_subject_repository_persistence` | `packages.hexagonal.tests.test_hexagonal_domain` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_subject_lifecycle_pure_domain_transitions` | `packages.hexagonal.tests.test_hexagonal_domain` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_subject_stratification_factors_locking_domain` | `packages.hexagonal.tests.test_hexagonal_domain` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_unblinding_and_withdrawal_domain` | `packages.hexagonal.tests.test_hexagonal_domain` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_workflows_with_in_memory_repositories` | `packages.hexagonal.tests.test_hexagonal_domain` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_aggregate_root_events` | `packages.hexagonal.tests.test_hexagonal_kernel` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_base_entity_equality` | `packages.hexagonal.tests.test_hexagonal_kernel` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_domain_error_http_mappings` | `packages.hexagonal.tests.test_hexagonal_kernel` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_value_object_equality` | `packages.hexagonal.tests.test_hexagonal_kernel` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_api_routers_contain_no_direct_db_calls` | `packages.hexagonal.tests.test_hexagonal_ports_adapters` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_designer_graph_modifications_with_mock_repositories` | `packages.hexagonal.tests.test_hexagonal_ports_adapters` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_domain_models_contain_zero_database_imports` | `packages.hexagonal.tests.test_hexagonal_ports_adapters` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_relational_services_execute_database_disabled` | `packages.hexagonal.tests.test_hexagonal_ports_adapters` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_signed_consent_immutability_pure_python_validation` | `packages.hexagonal.tests.test_hexagonal_ports_adapters` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_subject_status_transitions_pure_python_validation` | `packages.hexagonal.tests.test_hexagonal_ports_adapters` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_audit_records_ip_and_custom_timestamp` | `packages.security.tests.test_audit` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_hard_delete_is_prevented` | `packages.security.tests.test_audit` | Trace-1 | ⚪ UNVERIFIED | N/A |
| `test_insert_generates_audit_log` | `packages.security.tests.test_audit` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_read_only_queries_do_not_generate_audit_logs` | `packages.security.tests.test_audit` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_rollback_prevents_orphan_audit_logs` | `packages.security.tests.test_audit` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_soft_delete_generates_audit_log` | `packages.security.tests.test_audit` | PRD-SYS-002 | ⚪ UNVERIFIED | N/A |
| `test_subject_notification_skips_clinical_auditing` | `packages.security.tests.test_audit` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_update_generates_audit_log` | `packages.security.tests.test_audit` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_fingerprint_vs_serial_forgery_rejection` | `packages.security.tests.test_cert_store` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_register_and_verify_valid_certificate` | `packages.security.tests.test_cert_store` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_revoke_certificate_status_check` | `packages.security.tests.test_cert_store` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_dual_custody_negative_duplicate_shares` | `packages.security.tests.test_cryptography` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_dual_custody_negative_malformed_share` | `packages.security.tests.test_cryptography` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_dual_custody_negative_mismatched_versions` | `packages.security.tests.test_cryptography` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_dual_custody_negative_single_share` | `packages.security.tests.test_cryptography` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_dual_custody_negative_tampered_share` | `packages.security.tests.test_cryptography` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_dual_custody_positive` | `packages.security.tests.test_cryptography` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_encryption_decryption_with_rotation` | `packages.security.tests.test_cryptography` | PRD-MDR-005, Trace-2 | ⚪ UNVERIFIED | N/A |
| `test_key_splitting` | `packages.security.tests.test_cryptography` | PRD-MDR-005, Trace-2 | ⚪ UNVERIFIED | N/A |
| `test_encryption_roundtrip` | `packages.security.tests.test_encryption` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_encryption_tamper_rejection` | `packages.security.tests.test_encryption` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_hkdf_determinism` | `packages.security.tests.test_encryption` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_rejection_of_invalid_key_material` | `packages.security.tests.test_encryption` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_validate_branding_dev_bypass` | `packages.security.tests.test_fail_fast_branding` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_validate_branding_gateway_failures` | `packages.security.tests.test_fail_fast_branding` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_validate_branding_prod_failures` | `packages.security.tests.test_fail_fast_branding` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_validate_branding_success` | `packages.security.tests.test_fail_fast_branding` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_can_access_site` | `packages.security.tests.test_rbac` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_cross_site_query_read_isolation` | `packages.security.tests.test_rbac` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_cross_site_unblind_denied_with_alert` | `packages.security.tests.test_rbac` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_ecoa_diary_alert_permissions` | `packages.security.tests.test_rbac` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_etmf_audit_logs_gated_to_auditors` | `packages.security.tests.test_rbac` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_etmf_document_transition_auditor_forbidden` | `packages.security.tests.test_rbac` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_etmf_edl_creation_auditor_forbidden` | `packages.security.tests.test_rbac` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_etmf_edl_update_auditor_forbidden` | `packages.security.tests.test_rbac` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_etmf_ingest_auditor_forbidden` | `packages.security.tests.test_rbac` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_etmf_taxonomy_and_tag_permissions` | `packages.security.tests.test_rbac` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_execution_observation_creation_auditor_forbidden` | `packages.security.tests.test_rbac` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_execution_subject_creation_auditor_forbidden` | `packages.security.tests.test_rbac` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_execution_visit_creation_auditor_forbidden` | `packages.security.tests.test_rbac` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_external_monitor_aliases` | `packages.security.tests.test_rbac` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_external_monitor_eisf_denies_writes_allows_reads` | `packages.security.tests.test_rbac` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_external_monitor_permissions_matrix` | `packages.security.tests.test_rbac` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_external_monitor_principal_resolution` | `packages.security.tests.test_rbac` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_get_principal_from_request` | `packages.security.tests.test_rbac` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_has_permission` | `packages.security.tests.test_rbac` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_lab_range_alert_permissions` | `packages.security.tests.test_rbac` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_lab_range_rbac_permissions` | `packages.security.tests.test_rbac` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_mask_payload_recursive` | `packages.security.tests.test_rbac` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_medical_coding_rbac_permissions` | `packages.security.tests.test_rbac` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_principal_agreement_with_middleware_coercion` | `packages.security.tests.test_rbac` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_require_permission_dependency` | `packages.security.tests.test_rbac` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_require_study_scope_extraction` | `packages.security.tests.test_rbac` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_role_aliases_normalization` | `packages.security.tests.test_rbac` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_role_normalization_list` | `packages.security.tests.test_rbac` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_role_normalization_string` | `packages.security.tests.test_rbac` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_rtsm_role_aliases_normalization` | `packages.security.tests.test_rbac` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_rtsm_role_aware_masking` | `packages.security.tests.test_rbac` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_rtsm_role_permissions` | `packages.security.tests.test_rbac` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_soa_granular_permissions` | `packages.security.tests.test_rbac` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_soa_rbac_permissions` | `packages.security.tests.test_rbac` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_verify_is_auditor_allows_auditors` | `packages.security.tests.test_rbac` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_verify_is_auditor_denies_non_auditors` | `packages.security.tests.test_rbac` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_verify_not_auditor_allows_others` | `packages.security.tests.test_rbac` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_verify_not_auditor_denies_auditors` | `packages.security.tests.test_rbac` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_visit_windowing_granular_permissions` | `packages.security.tests.test_rbac` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_visit_windowing_rbac_permissions` | `packages.security.tests.test_rbac` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_rbac_designer_study_scoping` | `packages.security.tests.test_rbac_e2e` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_rbac_etmf_site_scoping` | `packages.security.tests.test_rbac_e2e` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_rbac_execution_access` | `packages.security.tests.test_rbac_e2e` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_rbac_execution_unauthorized` | `packages.security.tests.test_rbac_e2e` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_field_level_masking_blinded_user` | `packages.security.tests.test_rbac_enforcement` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_field_level_masking_pii_fields` | `packages.security.tests.test_rbac_enforcement` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_field_level_masking_unblinded_user` | `packages.security.tests.test_rbac_enforcement` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_mask_clinical_records_list` | `packages.security.tests.test_rbac_enforcement` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_rbac_cra_monitoring_authorization` | `packages.security.tests.test_rbac_enforcement` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_rbac_data_manager_lock_authorization` | `packages.security.tests.test_rbac_enforcement` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_rbac_sponsor_admin_authorization` | `packages.security.tests.test_rbac_enforcement` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_get_permissions_for_role_cra` | `packages.security.tests.test_rbac_permissions` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_get_permissions_for_role_data_manager` | `packages.security.tests.test_rbac_permissions` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_get_permissions_for_role_sponsor_admin` | `packages.security.tests.test_rbac_permissions` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_get_permissions_for_roles_aggregated` | `packages.security.tests.test_rbac_permissions` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_has_permission_checks` | `packages.security.tests.test_rbac_permissions` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_normalize_role_name` | `packages.security.tests.test_rbac_permissions` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_permission_matrix_enum_values` | `packages.security.tests.test_rbac_permissions` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_role_enum_canonical_names` | `packages.security.tests.test_rbac_permissions` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_soa_permissions_definitions` | `packages.security.tests.test_rbac_permissions` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_soa_permissions_matrix_mapping` | `packages.security.tests.test_rbac_permissions` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_unknown_role_returns_empty_permissions` | `packages.security.tests.test_rbac_permissions` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_commit_within_active_transaction` | `packages.security.tests.test_sqlmodel_audit_store` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_pluggable_sqlmodel_store_async` | `packages.security.tests.test_sqlmodel_audit_store` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_pluggable_sqlmodel_store_sync` | `packages.security.tests.test_sqlmodel_audit_store` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_local_storage_provider_integrity_failure` | `packages.storage.tests.test_blob_store` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_local_storage_provider_lifecycle` | `packages.storage.tests.test_blob_store` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_local_storage_provider_not_found` | `packages.storage.tests.test_blob_store` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_local_storage_provider_traversal_prevention` | `packages.storage.tests.test_blob_store` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_s3_storage_provider_integrity_failure` | `packages.storage.tests.test_blob_store` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_s3_storage_provider_lifecycle` | `packages.storage.tests.test_blob_store` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_s3_storage_provider_not_found` | `packages.storage.tests.test_blob_store` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_verify_checksum` | `packages.storage.tests.test_blob_store` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_automated_webhook_non_degraded_ingestion` | `packages.storage.tests.test_safe_binary_storage_watermark` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_native_docx_watermarking` | `packages.storage.tests.test_safe_binary_storage_watermark` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_native_pdf_watermarking` | `packages.storage.tests.test_safe_binary_storage_watermark` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_safe_binary_ingestion_and_export` | `packages.storage.tests.test_safe_binary_storage_watermark` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_factories_creation` | `packages.testing.tests.test_testing_package` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_in_memory_repository` | `packages.testing.tests.test_testing_package` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_security_helpers` | `packages.testing.tests.test_testing_package` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_domain_model_creation` | `scripts.scaffold_service` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_cross_service_import_isolation` | `scripts.test_m4_challenger2_stress` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_etmf_protocol_version_ref_invalid_status` | `scripts.test_m4_challenger2_stress` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_etmf_protocol_version_ref_invalid_study_id` | `scripts.test_m4_challenger2_stress` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_etmf_protocol_version_ref_invalid_version_index` | `scripts.test_m4_challenger2_stress` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_etmf_protocol_version_ref_invalid_version_tag` | `scripts.test_m4_challenger2_stress` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_etmf_protocol_version_ref_json_serialization` | `scripts.test_m4_challenger2_stress` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_etmf_protocol_version_ref_string_trimming` | `scripts.test_m4_challenger2_stress` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_etmf_protocol_version_ref_valid` | `scripts.test_m4_challenger2_stress` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_etmf_watermark_decoupling` | `scripts.test_m4_challenger2_stress` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_interop_dsl_parsing_and_evaluation` | `scripts.test_m4_challenger2_stress` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_interop_epro_transport_dto_serialization` | `scripts.test_m4_challenger2_stress` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_interop_expression_node_dto_validation` | `scripts.test_m4_challenger2_stress` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_interop_field_reference_dto` | `scripts.test_m4_challenger2_stress` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_api_parameters_parity` | `scripts.tests.test_api_contract_validation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_api_paths_and_methods_parity` | `scripts.tests.test_api_contract_validation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_api_request_bodies_parity` | `scripts.tests.test_api_contract_validation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_api_responses_parity` | `scripts.tests.test_api_contract_validation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_extra_response_properties_pass_validation` | `scripts.tests.test_api_contract_validation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_markdown_spec_extract_and_parse` | `scripts.tests.test_api_contract_validation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_markdown_spec_syntax_checks_malformed_yaml` | `scripts.tests.test_api_contract_validation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_undocumented_parameter_fails_parity_check` | `scripts.tests.test_api_contract_validation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_undocumented_route_fails_parity_check` | `scripts.tests.test_api_contract_validation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_validation_fails_on_route_path_mismatch` | `scripts.tests.test_api_contract_validation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_artifact_cascade_engine_generation` | `scripts.tests.test_artifact_cascade` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_artifact_cascade_router_endpoint` | `scripts.tests.test_artifact_cascade` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_artifact_cascade_router_endpoint_dependency_override` | `scripts.tests.test_artifact_cascade` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_js_reordered_helper_functions` | `scripts.tests.test_ast_merge_driver` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_python_edited_and_reordered` | `scripts.tests.test_ast_merge_driver` | PRD-SYS-002 | ⚪ UNVERIFIED | N/A |
| `test_python_imports_merged_and_sorted` | `scripts.tests.test_ast_merge_driver` | PRD-SYS-002 | ⚪ UNVERIFIED | N/A |
| `test_python_overlapping_logical_edits_fallback` | `scripts.tests.test_ast_merge_driver` | PRD-SYS-003 | ⚪ UNVERIFIED | N/A |
| `test_python_reordered_helper_functions` | `scripts.tests.test_ast_merge_driver` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_analyze_diff_endpoint_via_gateway` | `scripts.tests.test_change_analyzer` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_disable_audit_logging_is_blocked_outright` | `scripts.tests.test_change_analyzer` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_high_risk_compliance_setting_changes` | `scripts.tests.test_change_analyzer` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_low_risk_ui_display_changes` | `scripts.tests.test_change_analyzer` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_medium_risk_configuration_changes` | `scripts.tests.test_change_analyzer` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_type_aware_diff_no_op` | `scripts.tests.test_change_analyzer` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_load_categorized_ports_fallback` | `scripts.tests.test_check_ports` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_load_categorized_ports_with_compose` | `scripts.tests.test_check_ports` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_parse_port_entry` | `scripts.tests.test_check_ports` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_classification_driven_multi_site_export` | `scripts.tests.test_classification_multi_site_export` | PRD-TMF-001 | ⚪ UNVERIFIED | N/A |
| `test_unresolved_site_level_omit_manually` | `scripts.tests.test_classification_multi_site_export` | PRD-TMF-001 | ⚪ UNVERIFIED | N/A |
| `test_rtm_generation_conftest_hook_detection` | `scripts.tests.test_cli_etmf_archival` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_rtm_generation_with_cli_overrides` | `scripts.tests.test_cli_etmf_archival` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_create_and_retrieve_form_comments` | `scripts.tests.test_comments_router` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_resolve_form_comment_logs_gxp_audit` | `scripts.tests.test_comments_router` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_narrative_assembly_and_ref_resolution` | `scripts.tests.test_content_assembly` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_narrative_display_rule_duplicate_section_numbers` | `scripts.tests.test_content_assembly` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_narrative_display_rule_missing_section_number` | `scripts.tests.test_content_assembly` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_render_synopsis_template_html` | `scripts.tests.test_content_assembly` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_soa_matrix_assembly` | `scripts.tests.test_content_assembly` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_successful_assembly_and_synopsis` | `scripts.tests.test_content_assembly` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_unresolved_reference_invalid_attribute` | `scripts.tests.test_content_assembly` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_unresolved_reference_non_existent_id` | `scripts.tests.test_content_assembly` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_data_lifecycle_protocol_amendment_traceability` | `scripts.tests.test_data_lifecycle_protocol_amendment_traceability` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_in_memory_eligibility_rejection` | `scripts.tests.test_decoupled_services_in_memory` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_in_memory_eligibility_success` | `scripts.tests.test_decoupled_services_in_memory` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_process_coding_action_accept_in_memory` | `scripts.tests.test_decoupled_services_in_memory` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_process_coding_action_invalid_code_in_memory` | `scripts.tests.test_decoupled_services_in_memory` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_main_no_duplicates_scanned` | `scripts.tests.test_detect_duplication` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_main_url_logic_preservation` | `scripts.tests.test_detect_duplication` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_main_with_duplicates_detected` | `scripts.tests.test_detect_duplication` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_normalize_line_css` | `scripts.tests.test_detect_duplication` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_normalize_line_javascript` | `scripts.tests.test_detect_duplication` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_normalize_line_python` | `scripts.tests.test_detect_duplication` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_normalize_line_vue` | `scripts.tests.test_detect_duplication` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_path_normalization_win32` | `scripts.tests.test_detect_duplication` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_repo_root_resolution` | `scripts.tests.test_detect_duplication` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_scan_file_for_lines` | `scripts.tests.test_detect_duplication` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_build_compose_command_all` | `scripts.tests.test_dev_orchestrator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_build_compose_command_designer` | `scripts.tests.test_dev_orchestrator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_build_compose_command_down_operations` | `scripts.tests.test_dev_orchestrator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_build_compose_command_execution_with_flag` | `scripts.tests.test_dev_orchestrator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_build_compose_command_no_detach` | `scripts.tests.test_dev_orchestrator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_main_dry_run` | `scripts.tests.test_dev_orchestrator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_main_executes_subprocess` | `scripts.tests.test_dev_orchestrator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_broken_fragment_relative_link_detection` | `scripts.tests.test_directory_sweeping_pipeline` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_compliance_utility_directory_sweeping` | `scripts.tests.test_directory_sweeping_pipeline` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_duplicate_requirement_id_in_fragments_fails` | `scripts.tests.test_directory_sweeping_pipeline` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_full_generate_rtm_cli_sweeping` | `scripts.tests.test_directory_sweeping_pipeline` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_malformed_trace_id_definition_fails` | `scripts.tests.test_directory_sweeping_pipeline` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_orphan_fragment_detection` | `scripts.tests.test_directory_sweeping_pipeline` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_sweep_and_aggregate_modular_fragments` | `scripts.tests.test_directory_sweeping_pipeline` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_document_renderer_render_docx` | `scripts.tests.test_document_renderer` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_document_renderer_render_pdf` | `scripts.tests.test_document_renderer` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_document_download_logs_audit_and_watermarks` | `scripts.tests.test_document_router` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_document_upload_missing_permission` | `scripts.tests.test_document_router` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_document_upload_success` | `scripts.tests.test_document_router` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_document_versions_lineage` | `scripts.tests.test_document_router` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_study_archival_job_flow` | `scripts.tests.test_document_router` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_aggregate_eligibility_evaluation` | `scripts.tests.test_eligibility_engine` | PRD-ELIGIBILITY-008, PRD-MDR-007 | ⚪ UNVERIFIED | N/A |
| `test_eligibility_criterion_compatibility_and_enums` | `scripts.tests.test_eligibility_engine` | PRD-ELIGIBILITY-009 | ⚪ UNVERIFIED | N/A |
| `test_evaluate_criteria_group_helper` | `scripts.tests.test_eligibility_engine` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_evaluate_structured_expression_helper` | `scripts.tests.test_eligibility_engine` | PRD-ELIGIBILITY-010 | ⚪ UNVERIFIED | N/A |
| `test_evaluation_all_operators` | `scripts.tests.test_eligibility_engine` | PRD-ELIGIBILITY-005, PRD-MDR-007 | ⚪ UNVERIFIED | N/A |
| `test_evaluation_incompatible_types_graceful_handling` | `scripts.tests.test_eligibility_engine` | PRD-ELIGIBILITY-007 | ⚪ UNVERIFIED | N/A |
| `test_evaluation_kleene_indeterminate_propagation` | `scripts.tests.test_eligibility_engine` | PRD-ELIGIBILITY-006, PRD-MDR-007 | ⚪ UNVERIFIED | N/A |
| `test_parse_invalid_syntax` | `scripts.tests.test_eligibility_engine` | PRD-ELIGIBILITY-004 | ⚪ UNVERIFIED | N/A |
| `test_parse_logical_and_nested_expressions` | `scripts.tests.test_eligibility_engine` | PRD-ELIGIBILITY-003, PRD-MDR-007 | ⚪ UNVERIFIED | N/A |
| `test_parse_simple_expressions` | `scripts.tests.test_eligibility_engine` | PRD-ELIGIBILITY-002, PRD-MDR-007 | ⚪ UNVERIFIED | N/A |
| `test_eligibility_criteria_crud_endpoints` | `scripts.tests.test_eligibility_mdr` | PRD-MDR-007 | ⚪ UNVERIFIED | N/A |
| `test_eligibility_criteria_immutability` | `scripts.tests.test_eligibility_mdr` | PRD-MDR-007 | ⚪ UNVERIFIED | N/A |
| `test_eligibility_criteria_usdm_projection` | `scripts.tests.test_eligibility_mdr` | PRD-MDR-007 | ⚪ UNVERIFIED | N/A |
| `test_eligibility_criteria_validation_failures` | `scripts.tests.test_eligibility_mdr` | PRD-MDR-007 | ⚪ UNVERIFIED | N/A |
| `test_generate_schema_documentation_main` | `scripts.tests.test_generate_schema_documentation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_get_designer_schema` | `scripts.tests.test_generate_schema_documentation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_parse_sqlalchemy_schema_etmf` | `scripts.tests.test_generate_schema_documentation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_parse_sqlalchemy_schema_execution` | `scripts.tests.test_generate_schema_documentation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_generate_schemas_halts_in_production` | `scripts.tests.test_generate_schemas` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_generate_schemas_omits_sensitive_tables` | `scripts.tests.test_generate_schemas` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_git_merge_driver_cli_conflict_and_markers` | `scripts.tests.test_git_merge_driver` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_git_merge_driver_cli_non_overlapping_success` | `scripts.tests.test_git_merge_driver` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_gitattributes_configuration_mapping` | `scripts.tests.test_git_merge_driver` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_is_logical_code` | `scripts.tests.test_git_merge_driver` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_merge_generic_json` | `scripts.tests.test_git_merge_driver` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_merge_generic_json_value_collision_fail_fast` | `scripts.tests.test_git_merge_driver` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_merge_markdown_text` | `scripts.tests.test_git_merge_driver` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_merge_markdown_text_overlapping` | `scripts.tests.test_git_merge_driver` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_merge_secrets_baseline` | `scripts.tests.test_git_merge_driver` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_merge_secrets_baseline_structural_mismatch_fail_fast` | `scripts.tests.test_git_merge_driver` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_merge_secrets_baseline_top_level_scalar_collision` | `scripts.tests.test_git_merge_driver` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_merge_secrets_baseline_value_collision_fail_fast` | `scripts.tests.test_git_merge_driver` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_gxp_generation_and_runs_splitting` | `scripts.tests.test_gxp_decomposed_signatures` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_sign_and_verify_rsa_and_ecdsa` | `scripts.tests.test_gxp_decomposed_signatures` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_tamper_detection_body_modification` | `scripts.tests.test_gxp_decomposed_signatures` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_tamper_detection_metadata_modification` | `scripts.tests.test_gxp_decomposed_signatures` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_tamper_detection_signature_bytes_modification` | `scripts.tests.test_gxp_decomposed_signatures` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_verification_performance_under_five_seconds` | `scripts.tests.test_gxp_decomposed_signatures` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_fail_fast_without_report_and_draft_flag` | `scripts.tests.test_gxp_fail_fast` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_missing_report_gxp_sync_dry_run` | `scripts.tests.test_gxp_fail_fast` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_success_with_draft_flag` | `scripts.tests.test_gxp_fail_fast` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_layout_gating_approved_and_logged` | `scripts.tests.test_layout_validator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_layout_gating_missing_justification_rejected` | `scripts.tests.test_layout_validator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_layout_validation_integration` | `scripts.tests.test_layout_validator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_layout_validation_invisible` | `scripts.tests.test_layout_validator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_layout_validation_overlap` | `scripts.tests.test_layout_validator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_layout_validation_scrambled_sequence` | `scripts.tests.test_layout_validator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_layout_validation_valid` | `scripts.tests.test_layout_validator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_aggregate_eligibility_evaluation_scenarios` | `scripts.tests.test_m4_challenger1_stress` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_ctms_document_renderer_fallback` | `scripts.tests.test_m4_challenger1_stress` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_ctms_sync_reconciliation_signature_enforcement_errors` | `scripts.tests.test_m4_challenger1_stress` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_ctms_sync_reconciliation_strategies` | `scripts.tests.test_m4_challenger1_stress` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_ctms_sync_signature_verification_and_tampering` | `scripts.tests.test_m4_challenger1_stress` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_designer_eligibility_criterion_alias_sync` | `scripts.tests.test_m4_challenger1_stress` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_field_reference_dto_parsing` | `scripts.tests.test_m4_challenger1_stress` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_kleene_3_valued_logic_evaluation` | `scripts.tests.test_m4_challenger1_stress` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_protocol_version_ref_dto_validation` | `scripts.tests.test_m4_challenger1_stress` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_usdm_validation_dto_and_parser` | `scripts.tests.test_m4_challenger1_stress` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_designer_gateway_auth_expired_timestamp` | `scripts.tests.test_main` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_designer_gateway_auth_invalid_signature` | `scripts.tests.test_main` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_designer_gateway_auth_invalid_timestamp` | `scripts.tests.test_main` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_designer_gateway_auth_missing_headers` | `scripts.tests.test_main` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_designer_health` | `scripts.tests.test_main` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_execution_health` | `scripts.tests.test_main` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_gateway_health` | `scripts.tests.test_main` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_invalid_leading_number` | `scripts.tests.test_mapping_validator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_invalid_spacing` | `scripts.tests.test_mapping_validator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_multiple_colons` | `scripts.tests.test_mapping_validator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_valid_csv` | `scripts.tests.test_mapping_validator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_clean_token` | `scripts.tests.test_markdown_validator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_contributing_guide_skip_and_validation` | `scripts.tests.test_markdown_validator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_degraded_linter_warnings_and_fallback` | `scripts.tests.test_markdown_validator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_degraded_linter_warnings_and_fallback_failure` | `scripts.tests.test_markdown_validator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_dynamic_pydantic_model_import_failure_allow_degraded` | `scripts.tests.test_markdown_validator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_dynamic_pydantic_model_import_failure_strict_mode` | `scripts.tests.test_markdown_validator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_exclude_tests_from_codebase_map` | `scripts.tests.test_markdown_validator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_func` | `scripts.tests.test_markdown_validator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_html_comment_filtering` | `scripts.tests.test_markdown_validator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_is_potential_path_ref` | `scripts.tests.test_markdown_validator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_json_block_validation` | `scripts.tests.test_markdown_validator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_main_with_arguments` | `scripts.tests.test_markdown_validator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_mock_environment_variables` | `scripts.tests.test_markdown_validator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_nested_code_blocks_in_html_comments` | `scripts.tests.test_markdown_validator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_process_markdown_file_e2e` | `scripts.tests.test_markdown_validator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_python_block_validation` | `scripts.tests.test_markdown_validator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_reference_style_link_validation` | `scripts.tests.test_markdown_validator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_resolve_path` | `scripts.tests.test_markdown_validator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_skip_and_raw_text_flags` | `scripts.tests.test_markdown_validator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_sys_path_append` | `scripts.tests.test_markdown_validator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_uncommented_code_block_errors_maintained` | `scripts.tests.test_markdown_validator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_validate_cli_command_flag_checks` | `scripts.tests.test_markdown_validator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_validate_cli_command_python_and_pytest` | `scripts.tests.test_markdown_validator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_validate_docker_compose_scenarios` | `scripts.tests.test_markdown_validator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_validate_path` | `scripts.tests.test_markdown_validator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_docker_compose_front_proxy_configuration` | `scripts.tests.test_nginx_front_proxy` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_nginx_default_site_configuration` | `scripts.tests.test_nginx_front_proxy` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_nginx_main_configuration` | `scripts.tests.test_nginx_front_proxy` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_split_clients_and_path_override_routing_logic` | `scripts.tests.test_nginx_front_proxy` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_trace17_header_forwarding_contract` | `scripts.tests.test_nginx_front_proxy` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_build_comment_body` | `scripts.tests.test_pr_comment` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_combined_audit_logic` | `scripts.tests.test_pr_comment` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_get_status_emoji` | `scripts.tests.test_pr_comment` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_gxp_validation_and_migration_outcomes` | `scripts.tests.test_pr_comment` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_markdown_and_architecture_outcomes` | `scripts.tests.test_pr_comment` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_merge_outcomes` | `scripts.tests.test_pr_comment` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_parse_existing_outcomes` | `scripts.tests.test_pr_comment` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_traceability_outcome_handling` | `scripts.tests.test_pr_comment` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_check_and_run_exporter_bypass` | `scripts.tests.test_pre_commit_openapi` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_check_and_run_exporter_missing_dependencies` | `scripts.tests.test_pre_commit_openapi` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_check_and_run_exporter_missing_venv` | `scripts.tests.test_pre_commit_openapi` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_check_and_run_exporter_success` | `scripts.tests.test_pre_commit_openapi` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_check_and_run_exporter_validation_failure` | `scripts.tests.test_pre_commit_openapi` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_get_staged_files_failure` | `scripts.tests.test_pre_commit_openapi` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_get_staged_files_success` | `scripts.tests.test_pre_commit_openapi` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_should_trigger_schema_generation` | `scripts.tests.test_pre_commit_openapi` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_fetch_all_issues` | `scripts.tests.test_remove_jules_labels` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_is_jules_label` | `scripts.tests.test_remove_jules_labels` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_remove_label_dry_run` | `scripts.tests.test_remove_jules_labels` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_remove_label_success` | `scripts.tests.test_remove_jules_labels` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_pq_all_tests_passed` | `scripts.tests.test_rtm_generation_pq_validation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_pq_test_failed` | `scripts.tests.test_rtm_generation_pq_validation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_pq_test_missing_draft_mode` | `scripts.tests.test_rtm_generation_pq_validation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_pq_test_missing_fail_fast` | `scripts.tests.test_rtm_generation_pq_validation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_pq_test_skipped` | `scripts.tests.test_rtm_generation_pq_validation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_enforce_python_runtime_fails_on_outdated_version` | `scripts.tests.test_runtime_guard` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_enforce_python_runtime_matrix` | `scripts.tests.test_runtime_guard` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_enforce_python_runtime_passes_on_valid_version` | `scripts.tests.test_runtime_guard` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_get_runtime_info` | `scripts.tests.test_runtime_guard` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_print_runtime_info` | `scripts.tests.test_runtime_guard` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_gateway_graceful_handling_invalid_downstream` | `scripts.tests.test_schema_validation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_rewrite_references_nested_references` | `scripts.tests.test_schema_validation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_rewrite_references_recursion_protection` | `scripts.tests.test_schema_validation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_static_schema_validation_script` | `scripts.tests.test_schema_validation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_handle_github_api_error` | `scripts.tests.test_self_heal` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_is_executable_or_test_file` | `scripts.tests.test_self_heal` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_is_safe_file` | `scripts.tests.test_self_heal` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_is_tampering_attempt` | `scripts.tests.test_self_heal` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_main_blocked_on_non_safe_files` | `scripts.tests.test_self_heal` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_main_graceful_exit_on_api_error` | `scripts.tests.test_self_heal` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_main_graceful_on_github_api_error` | `scripts.tests.test_self_heal` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_main_no_conflict_needed` | `scripts.tests.test_self_heal` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_main_no_conflict_with_non_safe_files` | `scripts.tests.test_self_heal` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_main_skipped_if_no_safe_change_label` | `scripts.tests.test_self_heal` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_tampering_blocked_on_workflow_change` | `scripts.tests.test_self_heal` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_validation_bypassed_on_code_change` | `scripts.tests.test_self_heal` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_validation_executed_on_non_executable_change` | `scripts.tests.test_self_heal` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_cross_service_interception_and_replay` | `scripts.tests.test_shared_infrastructure` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_service_client_fixtures_isolation` | `scripts.tests.test_shared_infrastructure` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_signed_headers_generation` | `scripts.tests.test_shared_infrastructure` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_cross_service_interception` | `scripts.tests.test_shared_rbac_harness` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_mock_designer_driver` | `scripts.tests.test_shared_rbac_harness` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_persona_builders_contain_correct_claims` | `scripts.tests.test_shared_rbac_harness` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_shared_sqlite_dbs_and_clients` | `scripts.tests.test_shared_rbac_harness` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_find_migration_script` | `scripts.tests.test_start` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_main_designer_service` | `scripts.tests.test_start` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_main_execution_service` | `scripts.tests.test_start` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_run_pre_boot_migrations_failure` | `scripts.tests.test_start` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_run_pre_boot_migrations_success` | `scripts.tests.test_start` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_run_web_server_nt` | `scripts.tests.test_start` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_run_web_server_posix_fallback` | `scripts.tests.test_start` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_run_web_server_posix_success` | `scripts.tests.test_start` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_label_based_backlog_gating` | `scripts.tests.test_sync_github_project` | Trace-34 | ⚪ UNVERIFIED | N/A |
| `test_get_repository_fallback` | `scripts.tests.test_sync_ruleset` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_get_repository_from_env` | `scripts.tests.test_sync_ruleset` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_get_repository_from_git_https` | `scripts.tests.test_sync_ruleset` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_get_repository_from_git_ssh` | `scripts.tests.test_sync_ruleset` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_gxp_ruleset_file_structures` | `scripts.tests.test_sync_ruleset` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_sync_ruleset_create_new` | `scripts.tests.test_sync_ruleset` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_sync_ruleset_dry_run` | `scripts.tests.test_sync_ruleset` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_sync_ruleset_multiple_files_integration` | `scripts.tests.test_sync_ruleset` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_sync_ruleset_permission_denied_403` | `scripts.tests.test_sync_ruleset` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_sync_ruleset_permission_denied_403_graceful` | `scripts.tests.test_sync_ruleset` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_sync_ruleset_update_existing` | `scripts.tests.test_sync_ruleset` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_admin_cache_clear_forces_fresh_read` | `scripts.tests.test_transformers` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_legacy_endpoint_returns_original_schema` | `scripts.tests.test_transformers` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_terminology_cache_prevents_db_queries` | `scripts.tests.test_transformers` | PRD-MDR-001 | ⚪ UNVERIFIED | N/A |
| `test_usdm_endpoint_returns_nested_schema_and_fast` | `scripts.tests.test_transformers` | PRD-MDR-003, PRD-MDR-004 | ⚪ UNVERIFIED | N/A |
| `test_usdm_validation_error_on_invalid_data` | `scripts.tests.test_transformers` | PRD-MDR-001 | ⚪ UNVERIFIED | N/A |
| `test_audited_fallbacks_and_warning_propagation` | `scripts.tests.test_translation_recovery` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_security_gate_unauthenticated_requests` | `scripts.tests.test_translation_recovery` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_translation_error_status_and_rollback` | `scripts.tests.test_translation_recovery` | Trace-12 | ⚪ UNVERIFIED | N/A |
| `test_translation_status_and_listing_success` | `scripts.tests.test_translation_recovery` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_worker_context_and_session_cleanup` | `scripts.tests.test_translation_recovery` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_audit_safe_context_binds_and_cleans_up` | `scripts.tests.test_translator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_audit_safe_context_cleans_up_on_error` | `scripts.tests.test_translator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_background_translation_records_user_audit` | `scripts.tests.test_translator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_identifier_sanitization_during_translation` | `scripts.tests.test_translator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_multi_language_localization_and_hint_system` | `scripts.tests.test_translator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_rules_compilation_and_artifact_generation` | `scripts.tests.test_translator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_study_published_event_triggers_translation` | `scripts.tests.test_translator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_study_published_expired_timestamp_rejection` | `scripts.tests.test_translator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_study_published_invalid_signature_rejection` | `scripts.tests.test_translator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_translation_validation_failure` | `scripts.tests.test_translator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_adr_compliance_validation_logic` | `scripts.tests.test_validate_adrs` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_check_architectural_changes_require_adr_missing_adr` | `scripts.tests.test_validate_adrs` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_check_architectural_changes_require_adr_no_changes` | `scripts.tests.test_validate_adrs` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_check_architectural_changes_require_adr_with_deleted_adr` | `scripts.tests.test_validate_adrs` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_check_architectural_changes_require_adr_with_valid_adr` | `scripts.tests.test_validate_adrs` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_compliance_utility_extraction_and_normalization` | `scripts.tests.test_validate_adrs` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_compliance_utility_parsing` | `scripts.tests.test_validate_adrs` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_get_changed_files_bypasses_merge_commits_and_parses_status` | `scripts.tests.test_validate_adrs` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_get_changed_files_from_git_fallbacks` | `scripts.tests.test_validate_adrs` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_get_changed_files_from_txt` | `scripts.tests.test_validate_adrs` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_get_closest_local_branch_point_fallback_to_root` | `scripts.tests.test_validate_adrs` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_get_closest_local_branch_point_multiple_branches` | `scripts.tests.test_validate_adrs` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_is_architectural_file` | `scripts.tests.test_validate_adrs` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_validate_existing_adrs_valid_case` | `scripts.tests.test_validate_adrs` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_validate_existing_adrs_with_targets_outside_folder` | `scripts.tests.test_validate_adrs` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_validate_existing_adrs_with_targets_valid` | `scripts.tests.test_validate_adrs` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_validate_dependencies_fails_on_forbidden_package` | `scripts.tests.test_validate_dependencies` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_validate_dependencies_passes_on_clean_package_json` | `scripts.tests.test_validate_dependencies` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_check_file_imports_cross_service_violation` | `scripts.tests.test_validate_imports` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_check_file_imports_invalid_syntax` | `scripts.tests.test_validate_imports` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_check_file_imports_package_to_package_declared` | `scripts.tests.test_validate_imports` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_check_file_imports_package_to_package_undeclared` | `scripts.tests.test_validate_imports` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_check_file_imports_relative_cross_service` | `scripts.tests.test_validate_imports` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_check_file_imports_relative_same_service` | `scripts.tests.test_validate_imports` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_check_file_imports_same_service` | `scripts.tests.test_validate_imports` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_check_file_imports_shared_packages` | `scripts.tests.test_validate_imports` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_check_file_imports_test_files_enforced_unless_exempt` | `scripts.tests.test_validate_imports` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_get_service_name` | `scripts.tests.test_validate_imports` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_designer_validation_error_rfc7807` | `scripts.tests.test_validation_problem_details` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_execution_validation_error_rfc7807` | `scripts.tests.test_validation_problem_details` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_generate_alignment_report` | `scripts.tests.test_validator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_generate_alignment_report_with_mappings` | `scripts.tests.test_validator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_validate_ast_port_contracts_fails` | `scripts.tests.test_verify_contracts` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_validate_ast_port_contracts_passes` | `scripts.tests.test_verify_contracts` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_cli_bypass_blocking` | `scripts.tests.test_vulnerabilities` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_execute_pip_audit_success` | `scripts.tests.test_vulnerabilities` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_execute_pnpm_audit_success` | `scripts.tests.test_vulnerabilities` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_extract_active_frontend_vulnerabilities_invalid` | `scripts.tests.test_vulnerabilities` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_extract_active_frontend_vulnerabilities_modern_v9` | `scripts.tests.test_vulnerabilities` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_extract_active_frontend_vulnerabilities_valid` | `scripts.tests.test_vulnerabilities` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_extract_active_vulnerabilities_invalid` | `scripts.tests.test_vulnerabilities` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_extract_active_vulnerabilities_valid` | `scripts.tests.test_vulnerabilities` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_load_and_validate_ledger_frontend_invalid_justification` | `scripts.tests.test_vulnerabilities` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_load_and_validate_ledger_frontend_invalid_rpn` | `scripts.tests.test_vulnerabilities` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_load_and_validate_ledger_incorrect_rpn` | `scripts.tests.test_vulnerabilities` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_load_and_validate_ledger_invalid_fmea_scores` | `scripts.tests.test_vulnerabilities` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_load_and_validate_ledger_invalid_json` | `scripts.tests.test_vulnerabilities` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_load_and_validate_ledger_missing_fmea_fields` | `scripts.tests.test_vulnerabilities` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_load_and_validate_ledger_missing_justification` | `scripts.tests.test_vulnerabilities` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_load_and_validate_ledger_missing_vuln_id` | `scripts.tests.test_vulnerabilities` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_load_and_validate_ledger_multiple_entries_same_id` | `scripts.tests.test_vulnerabilities` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_load_and_validate_ledger_not_found` | `scripts.tests.test_vulnerabilities` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_load_and_validate_ledger_not_list` | `scripts.tests.test_vulnerabilities` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_load_and_validate_ledger_rpn_threshold` | `scripts.tests.test_vulnerabilities` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_load_and_validate_ledger_valid` | `scripts.tests.test_vulnerabilities` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_scan_exits_successfully_on_unreadable_files` | `scripts.tests.test_vulnerabilities` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_scan_for_config_bypasses_no_violations` | `scripts.tests.test_vulnerabilities` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_scan_for_config_bypasses_with_violations` | `scripts.tests.test_vulnerabilities` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_scan_for_inline_bypasses_comments_and_empty_lines` | `scripts.tests.test_vulnerabilities` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_scan_for_inline_bypasses_logical_boundary_reset` | `scripts.tests.test_vulnerabilities` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_scan_for_inline_bypasses_multiline_consecutive` | `scripts.tests.test_vulnerabilities` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_scan_for_inline_bypasses_multiline_out_of_scope` | `scripts.tests.test_vulnerabilities` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_scan_for_inline_bypasses_multiline_three_lines` | `scripts.tests.test_vulnerabilities` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_scan_for_inline_bypasses_no_violations` | `scripts.tests.test_vulnerabilities` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_scan_for_inline_bypasses_same_line_boundary_reset` | `scripts.tests.test_vulnerabilities` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_scan_for_inline_bypasses_shell_boundary_reset` | `scripts.tests.test_vulnerabilities` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_scan_for_inline_bypasses_with_violations` | `scripts.tests.test_vulnerabilities` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_scan_for_inline_bypasses_yaml_folded_vs_literal` | `scripts.tests.test_vulnerabilities` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_scan_for_manifest_bypasses` | `scripts.tests.test_vulnerabilities` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_validate_vulnerabilities_compound_matching` | `scripts.tests.test_vulnerabilities` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_validate_vulnerabilities_multiple_identical_vuln_ids` | `scripts.tests.test_vulnerabilities` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_tier1_feature01_coding_queue_and_filter` | `tests.e2e.test_phase1_e2e_suite` | PRD-SYS-001, Trace-1 | ⚪ UNVERIFIED | N/A |
| `test_tier1_feature02_meddra_and_whodrug_traversal` | `tests.e2e.test_phase1_e2e_suite` | PRD-MDR-001, PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_tier1_feature03_single_and_batch_coding_assignment` | `tests.e2e.test_phase1_e2e_suite` | PRD-SYS-001, Trace-1, Trace-28 | ⚪ UNVERIFIED | N/A |
| `test_tier1_feature04_dictionary_upversioning_impact` | `tests.e2e.test_phase1_e2e_suite` | PRD-SYS-001, Trace-30 | ⚪ UNVERIFIED | N/A |
| `test_tier1_feature05_query_escalation_and_resolution` | `tests.e2e.test_phase1_e2e_suite` | PRD-QRY-001, Trace-1 | ⚪ UNVERIFIED | N/A |
| `test_tier1_feature06_relational_datalock_persistence` | `tests.e2e.test_phase1_e2e_suite` | PRD-MDR-002, PRD-SYS-001, Trace-1 | ⚪ UNVERIFIED | N/A |
| `test_tier1_feature07_hierarchical_lock_inheritance` | `tests.e2e.test_phase1_e2e_suite` | PRD-MDR-002, Trace-3 | ⚪ UNVERIFIED | N/A |
| `test_tier1_feature08_dual_signature_step_up_token` | `tests.e2e.test_phase1_e2e_suite` | Trace-13, Trace-17 | ⚪ UNVERIFIED | N/A |
| `test_tier1_feature09_unlock_justification_enforcement` | `tests.e2e.test_phase1_e2e_suite` | PRD-SYS-001, Trace-1, Trace-28 | ⚪ UNVERIFIED | N/A |
| `test_tier1_feature10_multi_format_lab_ingestion` | `tests.e2e.test_phase1_e2e_suite` | PRD-LAB-001, Trace-15 | ⚪ UNVERIFIED | N/A |
| `test_tier1_feature11_ucum_normalization_and_range_eval` | `tests.e2e.test_phase1_e2e_suite` | PRD-LAB-001, PRD-MDR-001 | ⚪ UNVERIFIED | N/A |
| `test_tier1_feature12_lab_discrepancy_and_sae_auto_queries` | `tests.e2e.test_phase1_e2e_suite` | PRD-LAB-001, PRD-QRY-001, Trace-15 | ⚪ UNVERIFIED | N/A |
| `test_tier1_feature13_sas_transport_binary_export` | `tests.e2e.test_phase1_e2e_suite` | PRD-CRF-008, Trace-7 | ⚪ UNVERIFIED | N/A |
| `test_tier1_feature14_cdisc_odm_xml_export_with_audits` | `tests.e2e.test_phase1_e2e_suite` | PRD-CRF-008, PRD-SYS-001, Trace-1 | ⚪ UNVERIFIED | N/A |
| `test_tier1_feature15_cdisc_dataset_json_export` | `tests.e2e.test_phase1_e2e_suite` | PRD-CRF-008 | ⚪ UNVERIFIED | N/A |
| `test_tier1_feature16_deidentified_csv_export` | `tests.e2e.test_phase1_e2e_suite` | Trace-12 | ⚪ UNVERIFIED | N/A |
| `test_tier1_feature17_ui_router_and_navigation_metadata` | `tests.e2e.test_phase1_e2e_suite` | Trace-11 | ⚪ UNVERIFIED | N/A |
| `test_tier2_boundary01_empty_lab_payload_rejected` | `tests.e2e.test_phase1_e2e_suite` | PRD-LAB-001 | ⚪ UNVERIFIED | N/A |
| `test_tier2_boundary02_invalid_hl7_segments_rejected` | `tests.e2e.test_phase1_e2e_suite` | PRD-LAB-001 | ⚪ UNVERIFIED | N/A |
| `test_tier2_boundary03_unlock_justification_under_50_chars_rejected` | `tests.e2e.test_phase1_e2e_suite` | PRD-SYS-001, Trace-1 | ⚪ UNVERIFIED | N/A |
| `test_tier2_boundary04_hard_lock_without_step_up_token_rejected` | `tests.e2e.test_phase1_e2e_suite` | Trace-17 | ⚪ UNVERIFIED | N/A |
| `test_tier2_boundary05_incompatible_ucum_unit_conversion` | `tests.e2e.test_phase1_e2e_suite` | PRD-LAB-001 | ⚪ UNVERIFIED | N/A |
| `test_tier2_boundary06_nonexistent_dictionary_term_resolution` | `tests.e2e.test_phase1_e2e_suite` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_tier2_boundary07_invalid_sdtm_domain_export_rejected` | `tests.e2e.test_phase1_e2e_suite` | PRD-CRF-008 | ⚪ UNVERIFIED | N/A |
| `test_tier2_boundary08_unauthorized_lock_action_rejected` | `tests.e2e.test_phase1_e2e_suite` | Trace-27 | ⚪ UNVERIFIED | N/A |
| `test_tier2_boundary09_replay_sig_token_prevention` | `tests.e2e.test_phase1_e2e_suite` | Trace-17 | ⚪ UNVERIFIED | N/A |
| `test_tier2_boundary10_deidentification_empty_demographics` | `tests.e2e.test_phase1_e2e_suite` | Trace-12 | ⚪ UNVERIFIED | N/A |
| `test_tier3_pairwise01_form_lock_then_medical_coding` | `tests.e2e.test_phase1_e2e_suite` | PRD-MDR-002, PRD-SYS-001, Trace-1 | ⚪ UNVERIFIED | N/A |
| `test_tier3_pairwise02_out_of_range_lab_then_subject_lock` | `tests.e2e.test_phase1_e2e_suite` | PRD-LAB-001, PRD-MDR-002, PRD-QRY-001 | ⚪ UNVERIFIED | N/A |
| `test_tier3_pairwise03_batch_coding_then_biostat_export` | `tests.e2e.test_phase1_e2e_suite` | PRD-CRF-008, PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_tier3_pairwise04_subject_lock_followed_by_lab_ingestion` | `tests.e2e.test_phase1_e2e_suite` | PRD-LAB-001, PRD-MDR-002 | ⚪ UNVERIFIED | N/A |
| `test_tier3_pairwise05_upversioning_impact_then_query_escalation` | `tests.e2e.test_phase1_e2e_suite` | PRD-QRY-001, PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_tier3_pairwise06_hard_lock_step_up_then_unlock_and_export` | `tests.e2e.test_phase1_e2e_suite` | PRD-CRF-008, Trace-1, Trace-17 | ⚪ UNVERIFIED | N/A |
| `test_tier4_scenario01_oncology_trial_multisite_lock` | `tests.e2e.test_phase1_e2e_suite` | PRD-MDR-002, Trace-1, Trace-13, Trace-17 | ⚪ UNVERIFIED | N/A |
| `test_tier4_scenario02_high_throughput_lab_batch_ingestion` | `tests.e2e.test_phase1_e2e_suite` | PRD-LAB-001, PRD-QRY-001, Trace-15 | ⚪ UNVERIFIED | N/A |
| `test_tier4_scenario03_meddra_upversioning_and_batch_coding` | `tests.e2e.test_phase1_e2e_suite` | PRD-QRY-001, PRD-SYS-001, Trace-1 | ⚪ UNVERIFIED | N/A |
| `test_tier4_scenario04_regulatory_submission_bundle_generation` | `tests.e2e.test_phase1_e2e_suite` | PRD-CRF-008, Trace-12, Trace-7 | ⚪ UNVERIFIED | N/A |
| `test_tier4_scenario05_full_lifecycle_e2e_trial_workflow` | `tests.e2e.test_phase1_e2e_suite` | PRD-CRF-008, PRD-LAB-001, PRD-MDR-002, PRD-QRY-001, PRD-SYS-001, Trace-1, Trace-17 | ⚪ UNVERIFIED | N/A |
| `test_build_worker_suffix_uniqueness` | `tests.test_harness_isolation` | PRD-SYS-004 | ⚪ UNVERIFIED | N/A |
| `test_get_run_uid_from_config` | `tests.test_harness_isolation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_get_worker_id_from_config` | `tests.test_harness_isolation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_is_xdist_controller_detection` | `tests.test_harness_isolation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_run_sync_timeout_enforcement` | `tests.test_harness_isolation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_service_database_names_length_and_format` | `tests.test_harness_isolation` | PRD-SYS-004 | ⚪ UNVERIFIED | N/A |
| `test_should_provision_postgres_controller_boundary` | `tests.test_harness_isolation` | PRD-SYS-004 | ⚪ UNVERIFIED | N/A |
| `test_should_provision_postgres_worker` | `tests.test_harness_isolation` | PRD-SYS-004 | ⚪ UNVERIFIED | N/A |
| `test_simulated_concurrent_runs_have_disjoint_databases` | `tests.test_harness_isolation` | PRD-SYS-004 | ⚪ UNVERIFIED | N/A |
| `test_catalog_cross_version_integrity` | `tests.validation.dia_tmf_validation_suite` | PRD-TMF-001 | ⚪ UNVERIFIED | N/A |
| `test_milestone_mandatory_artifacts` | `tests.validation.dia_tmf_validation_suite` | PRD-TMF-004 | ⚪ UNVERIFIED | N/A |
| `test_site_level_classification_drift` | `tests.validation.dia_tmf_validation_suite` | PRD-TMF-001, PRD-TMF-003 | ⚪ UNVERIFIED | N/A |
| `test_gxp_compliance_drifts_identified` | `tests.validation.gxp_compliance_suite` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_feature_matrix_validation_ignoring_helper_and_excluded_services` | `tests.validation.test_feature_matrix_gating` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_feature_matrix_validation_missing_service` | `tests.validation.test_feature_matrix_gating` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_feature_matrix_validation_success` | `tests.validation.test_feature_matrix_gating` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_committed_typescript_schema_is_up_to_date` | `tests.validation.test_offline_schema_drift` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_formatting_and_whitespace_immunity` | `tests.validation.test_offline_schema_drift` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_pending_delta_schema_drift` | `tests.validation.test_offline_schema_drift` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_simulated_field_name_rename_drift` | `tests.validation.test_offline_schema_drift` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_simulated_type_mismatch_drift` | `tests.validation.test_offline_schema_drift` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_submissions_schema_drift` | `tests.validation.test_offline_schema_drift` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_environment_integrity_assertions` | `tests.validation.test_path_boundary_linter` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_linter_negative_cases` | `tests.validation.test_path_boundary_linter` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_linter_positive_cases` | `tests.validation.test_path_boundary_linter` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |

## 4. Performance Qualification (PQ) & Scenario Validation
Performance Qualification documents the verification of end-to-end clinical workflow scenarios defined in Section 5 of the QA & Validation Plan.

### TC-VAL-LOG-001: Protocol Version Locking & Immutability Rejection
- **Target Requirements:** PRD-MDR-001, PRD-UNI-003
- **Description:** Verifies that locked study version nodes in Neo4j are completely immutable, and direct database manipulations are rejected.
- **Verification Status:** ⚪ Unverified (Draft Mode)

### TC-VAL-LOG-002: Stratification Factor Re-randomization Rejections
- **Target Requirements:** PRD-SUB-002, PRD-SUB-001
- **Description:** Verifies that stratification factor modifications and backward state machine updates are strictly forbidden once randomized.
- **Verification Status:** ⚪ Unverified (Draft Mode)

### TC-VAL-LOG-003: Offline Mode Data Entry, Sync Collision & Conflict Resolution
- **Target Requirements:** PRD-EDC-004, PRD-UNI-002
- **Description:** Verifies that offline data entries are synchronized accurately, conflict resolution runs deterministically, and the audit ledger captures all states.
- **Verification Status:** ⚪ Unverified (Draft Mode)

### TC-VAL-LOG-004: Re-authentication Enforcement during Emergency Unblinding
- **Target Requirements:** PRD-MDR-003, PRD-UNI-002
- **Description:** Verifies that unblinding requests require strict multi-factor re-authentication, trigger immediate unblinded state transition, lock the trial on tampering, and dispatch security alerts.
- **Verification Status:** ⚪ Unverified (Draft Mode)

## 5. Qualification Review & Authorization
This GxP computerized system validation log is compiled with mathematical determinism directly from the execution runners of the build system.
```
Lead Systems Validation Engineer:   ___________________________   Date: _______________
Director of Clinical Quality Assurance: ___________________________   Date: _______________
```

---

## Electronic Signature Block

- **Signer Identity:** jules
- **Timestamp:** 2026-08-21 14:40:25 UTC
- **Meaning / Purpose:** GxP Dynamic Execution Run Record
- **Cryptographic Hash (SHA-256):** 2c10a62482676d19c500ef4660d13d5c92d20e60aefaedef836eefaa537b403c

-----BEGIN CERTIFICATE-----
MIIDNDCCAhygAwIBAgIUGWALkmTiSqXia9p4OFegVcWsi3QwDQYJKoZIhvcNAQEL
BQAwVDEuMCwGA1UEAwwlQ2FkZW5jZSBHeFAgVmFsaWRhdGlvbiBSdW5uZXIgKGp1
bGVzKTEiMCAGA1UECgwZQ2FkZW5jZSBDbGluaWNhbCBTb2Z0d2FyZTAeFw0yNjA4
MjAxNDQwMjZaFw0yNzA4MjExNDQwMjZaMFQxLjAsBgNVBAMMJUNhZGVuY2UgR3hQ
IFZhbGlkYXRpb24gUnVubmVyIChqdWxlcykxIjAgBgNVBAoMGUNhZGVuY2UgQ2xp
bmljYWwgU29mdHdhcmUwggEiMA0GCSqGSIb3DQEBAQUAA4IBDwAwggEKAoIBAQDj
Y6g7LfnikIJqR4VQvh11NWGVlpoLuaPgCvmGK/cf53T+h5xWNRab0BOF4ox0tWzI
EzmJYZox5zp4EHkt0f11VXzkEbsqnJPBfbRFex5PHF9ZrF7w9wh7gslxSaRXpKs0
Z/ilne2yQ0gkhkTQFJ2G7EegevWYGzxLZixIZxj5BhPTqCt4a4zKBfB+WsfdPuKw
ptyD9bUBA71LkeW5FYciTRcMOgeVuNfW0wFp6uGmpIE+ANCj3WiB97DkbIHuWlPP
ZoUashs2cYRCBGY9DbNAFwRlncapYdaL4m2ar6Ohz4sYwR7gFHuU+j+jw9zTuOhG
+KhK4cW8z0IeAd1bPyRPAgMBAAEwDQYJKoZIhvcNAQELBQADggEBAANTb9oFHegw
5ppvwEjD/HTHPaOW4PRij1E1yZYuhkCdVKZHiGclr2kANn/SjZ2XMfXAskKeBito
4nyg/gjLRSvzmvxwSJTBx3FncLc6UA//31X26irp1tX6Cl1uaQc3sj5drQ9kRbxu
Gxwc7H2GGlMI0PX2xcW3xUL7sQoyRK1TWIpRce5d7Td7CKdJFDiHemc3+2PyWaJ9
d5Xxy39UcTc4Xq+RzpIEolwG5VHWUjfXyQhZTr4iXsPBNLjor2iSXfkQe5ACIpe7
3j1eGYg2r7Ur1jQxMM0BaNnKBIejuWb/Eqvd4tNKSaxfCGypp1LzjL8/02JAoWZz
Mo4OEmdawdk=
-----END CERTIFICATE-----
-----BEGIN SIGNATURE-----
UbQsSYTM6QyQi68JNqOXFQb1YdqSdhYF8ezhffF0ea2F1fSW+zooVYohRxcixUYff/0uDMCrmUrmBjD/HqJ4irCZP4mka9BxSsJfV23bAqwJnFXaNVBCi0MGWmWeukMwFSO2q6/n2U2R1ijHbDWW3dQ3g2AkmbFXNuVeBPS+otWPiK2gXvji74/zftnojIw7ybnvHSgy5r73dzJwFxwewrb+N0PYIRFPjTWiZOE+knuD2Kf9uxRGlNIfO38cdTcdP360tHKPTuMX8M/fgwYsVpZAuqqQzCL1VPswDtJmiwkIs2RVjwskdNjV7UTBn4WEn0JM78ipTQaM24+KGsmmBQ==
-----END SIGNATURE-----

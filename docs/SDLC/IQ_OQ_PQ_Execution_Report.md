# GxP Installation & Operational Qualification (IQ/OQ/PQ) Execution Report

*Execution Date:* 2026-07-23 22:38:25 UTC
*Regulatory Protocol:* FDA 21 CFR Part 11, EU Annex 11, GAMP 5 Category 4/5, IEC 62304 Class B

## 1. Executive Summary & Verification Declaration

This report documents the Installation Qualification (IQ) and Operational Qualification (OQ) for the Cadence Clinical platform.
Based on the executed automated verification suite, the platform meets all predefined structural, functional, and security compliance constraints.

### Validation Result Summary
- **Total Automated Test Cases Run:** 521
- **Passed:** 521 🟢
- **Failed/Errors:** 0 🔴
- **Skipped:** 0 ⚪
- **Overall Operational Pass Rate:** 100.00%

## 2. Installation Qualification (IQ)

The Installation Qualification verifies that the software execution environment, external dependencies, package environments, and static quality checks are fully compliant.

### 2.1 System Environment Metadata
- **Operating System / Platform:** linux (containerized target specification)
- **Python Version:** 3.12.13 (Docker execution environment baseline)
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
aiosmtplib              5.1.2
aiosqlite               0.22.1
annotated-doc           0.0.4
annotated-types         0.7.0
anyio                   4.14.2
asyncpg                 0.31.0
bandit                  1.9.4
beautifulsoup4          4.15.0
boolean-py              5.0
brotli                  1.2.0
cachecontrol            0.14.4
cadence-clinical         0.1.0       /app
certifi                 2026.7.22
cffi                    2.1.0
cfgv                    3.5.0
charset-normalizer      3.4.9
click                   8.4.2
coverage                7.15.2
cryptography            49.0.0
cssselect2              0.9.0
cyclonedx-python-lib    11.11.0
defusedxml              0.7.1
detect-secrets          1.5.0
distlib                 0.4.3
ecdsa                   0.19.2
et-xmlfile              2.0.0
fastapi                 0.139.2
filelock                3.32.0
fonttools               4.63.0
greenlet                3.5.4
h11                     0.16.0
httpcore                1.0.9
httptools               0.8.0
httpx                   0.28.1
identify                2.6.19
idna                    3.18
iniconfig               2.3.0
jinja2                  3.1.6
license-expression      30.4.4
lxml                    6.1.1
markdown-it-py          4.2.0
markupsafe              3.0.3
mdurl                   0.1.2
msgpack                 1.2.1
neo4j                   6.2.0
nodeenv                 1.10.0
numpy                   2.5.1
openpyxl                3.1.5
packageurl-python       0.17.6
packaging               26.2
pandas                  3.0.3
pillow                  12.3.0
pip                     26.1.2
pip-api                 0.0.34
pip-audit               2.10.1
pip-requirements-parser 32.0.1
platformdirs            4.11.0
playwright              1.61.0
pluggy                  1.6.0
pre-commit              4.6.1
py-serializable         2.1.0
pyasn1                  0.6.4
pycparser               3.0
pydantic                2.13.4
pydantic-core           2.46.4
pydyf                   0.12.1
pyee                    13.0.1
pygments                2.20.0
pyparsing               3.3.2
pyphen                  0.17.2
pytest                  9.1.1
pytest-asyncio          1.4.0
pytest-base-url         2.1.0
pytest-cov              7.1.0
pytest-playwright       0.8.0
python-dateutil         2.9.0.post0
python-discovery        1.5.0
python-docx             1.2.0
python-dotenv           1.2.2
python-jose             3.5.0
python-multipart        0.0.32
python-slugify          8.0.4
pytz                    2026.2
pyyaml                  6.0.3
requests                2.34.2
rich                    15.0.0
rsa                     4.9.1
ruff                    0.15.22
six                     1.17.0
sortedcontainers        2.4.0
soupsieve               2.9.1
sqlalchemy              2.0.51
starlette               1.3.1
stevedore               5.9.0
text-unidecode          1.3
tinycss2                1.5.1
tinyhtml5               2.1.0
tomli                   2.4.1
tomli-w                 1.2.0
typing-extensions       4.16.0
typing-inspection       0.4.2
urllib3                 2.7.0
usdm                    0.67.0
uvicorn                 0.51.0
uvloop                  0.22.1
virtualenv              21.7.0
watchfiles              1.2.0
weasyprint              69.0
webencodings            0.5.1
websockets              16.1.1
yattag                  1.16.1
zopfli                  0.4.3
```

## 3. Operational Qualification (OQ)

The Operational Qualification verifies that individual clinical operations, state machine transitions, cryptographic workflows, database-level triggers, and blinding boundaries are executed accurately according to functional requirements.

### 3.1 Traceability Mappings Verification
| Test Case Name | Classname / Suite | Target Req | Status | Duration |
| :--- | :--- | :--- | :--- | :--- |
| `test_api_parameters_parity` | `tests.test_api_contract_validation` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_api_paths_and_methods_parity` | `tests.test_api_contract_validation` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_api_request_bodies_parity` | `tests.test_api_contract_validation` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_api_responses_parity` | `tests.test_api_contract_validation` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_markdown_spec_extract_and_parse` | `tests.test_api_contract_validation` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_markdown_spec_syntax_checks_malformed_yaml` | `tests.test_api_contract_validation` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_validation_fails_on_route_path_mismatch` | `tests.test_api_contract_validation` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_audit_records_ip_and_custom_timestamp` | `tests.test_audit` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_hard_delete_is_prevented` | `tests.test_audit` | Trace-1 | 🟢 PASSED | < 1s |
| `test_insert_generates_audit_log` | `tests.test_audit` | PRD-SYS-001 | 🟢 PASSED | < 1s |
| `test_read_only_queries_do_not_generate_audit_logs` | `tests.test_audit` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_rollback_prevents_orphan_audit_logs` | `tests.test_audit` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_soft_delete_generates_audit_log` | `tests.test_audit` | PRD-SYS-002 | 🟢 PASSED | < 1s |
| `test_update_generates_audit_log` | `tests.test_audit` | PRD-SYS-001 | 🟢 PASSED | < 1s |
| `test_dataset_json_integration_structure` | `tests.test_biostat` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_declarative_mappings_coverage` | `tests.test_biostat` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_extract_dm_age_precision_and_controlled_terminology` | `tests.test_biostat` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_extract_dm_demographics` | `tests.test_biostat` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_extract_mh_sequencing_and_supp` | `tests.test_biostat` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_mapping_helpers` | `tests.test_biostat` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_normalize_race` | `tests.test_biostat` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_normalize_severity` | `tests.test_biostat` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_normalize_sex` | `tests.test_biostat` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_supp_record_row_conversion` | `tests.test_biostat` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_variable_metadata_validation` | `tests.test_biostat` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_api_gateway_routing` | `tests.test_clinical_engine` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_cdisc_export_and_validation` | `tests.test_clinical_engine` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_demographics_encryption` | `tests.test_clinical_engine` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_outlier_detection_performance` | `tests.test_clinical_engine` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_relational_persistence_and_recalculation` | `tests.test_clinical_engine` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_unit_conversions` | `tests.test_clinical_engine` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_candidate_creation_and_opening_workflow` | `tests.test_clinical_queries` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_clinical_query_creation_with_all_audited_fields` | `tests.test_clinical_queries` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_clinical_query_trial_lock_enforcement_at_visit_level` | `tests.test_clinical_queries` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_create_clinical_query_authorization_failures` | `tests.test_clinical_queries` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_create_clinical_query_success` | `tests.test_clinical_queries` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_database_events_prevent_deletions` | `tests.test_clinical_queries` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_duplicate_active_query_rejected` | `tests.test_clinical_queries` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_query_role_gates_robustness` | `tests.test_clinical_queries` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_query_state_transition_and_role_boundaries` | `tests.test_clinical_queries` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_rejection_and_cancellation_reason_requirements` | `tests.test_clinical_queries` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_reopen_transitions` | `tests.test_clinical_queries` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_narrative_assembly_and_ref_resolution` | `tests.test_content_assembly` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_narrative_display_rule_duplicate_section_numbers` | `tests.test_content_assembly` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_narrative_display_rule_missing_section_number` | `tests.test_content_assembly` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_soa_matrix_assembly` | `tests.test_content_assembly` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_successful_assembly_and_synopsis` | `tests.test_content_assembly` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_unresolved_reference_invalid_attribute` | `tests.test_content_assembly` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_unresolved_reference_non_existent_id` | `tests.test_content_assembly` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_encryption_decryption_with_rotation` | `tests.test_cryptography` | Trace-2, PRD-MDR-005 | 🟢 PASSED | < 1s |
| `test_key_splitting` | `tests.test_cryptography` | Trace-2, PRD-MDR-005 | 🟢 PASSED | < 1s |
| `test_cra_allocations_rbac_reassignment_workload` | `tests.test_ctms` | Trace-6, PRD-CTMS-003 | 🟢 PASSED | < 1s |
| `test_create_and_list_studies_rbac` | `tests.test_ctms` | PRD-CTMS-004, Trace-6 | 🟢 PASSED | < 1s |
| `test_ctms_health_check` | `tests.test_ctms` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_database_manager_uninitialized` | `tests.test_ctms` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_get_audit_trail_rbac` | `tests.test_ctms` | PRD-CTMS-004, Trace-6 | 🟢 PASSED | < 1s |
| `test_grant_creation_rbac` | `tests.test_ctms` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_grant_locked_when_approved` | `tests.test_ctms` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_milestone_trigger_manual` | `tests.test_ctms` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_milestone_trigger_study_approved` | `tests.test_ctms` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_milestone_trigger_visit_completed_automated` | `tests.test_ctms` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_monitoring_visit_invalid_state_and_findings` | `tests.test_ctms` | Trace-6, PRD-CTMS-002 | 🟢 PASSED | < 1s |
| `test_monitoring_visit_scheduling_respects_cra_allocation` | `tests.test_ctms` | Trace-6, PRD-CTMS-003 | 🟢 PASSED | < 1s |
| `test_monitoring_visit_workflow_happy_path` | `tests.test_ctms` | Trace-6, PRD-CTMS-002 | 🟢 PASSED | < 1s |
| `test_monitoring_visit_workflow_rbac_denials` | `tests.test_ctms` | Trace-6, PRD-CTMS-002 | 🟢 PASSED | < 1s |
| `test_recruitment_records_crud_and_audit` | `tests.test_ctms` | PRD-CTMS-004, Trace-6 | 🟢 PASSED | < 1s |
| `test_site_milestones_crud_and_audit` | `tests.test_ctms` | Trace-6, PRD-CTMS-001 | 🟢 PASSED | < 1s |
| `test_basic_detection_results` | `tests.test_deid` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_compliance_profiles` | `tests.test_deid` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_custom_literal_terms` | `tests.test_deid` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_dates_detector` | `tests.test_deid` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_deidentify_free_text_direct` | `tests.test_deid` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_email_detector` | `tests.test_deid` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_fhir_narrative_and_notes_integration` | `tests.test_deid` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_ip_mac_detector` | `tests.test_deid` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_medical_record_account_detector` | `tests.test_deid` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_overlap_resolution_deterministic` | `tests.test_deid` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_phone_fax_detector` | `tests.test_deid` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_redact_text_sequential` | `tests.test_deid` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_ssn_national_id_detector` | `tests.test_deid` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_urls_detector` | `tests.test_deid` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_zip_geographic_detector` | `tests.test_deid` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_concurrent_library_version_increments` | `tests.test_delta` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_concurrent_study_saves_serialization` | `tests.test_delta` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_create_library_object_version_existing` | `tests.test_delta` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_create_library_object_version_new` | `tests.test_delta` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_create_study_root` | `tests.test_delta` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_get_study_differences` | `tests.test_delta` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_update_study_properties` | `tests.test_delta` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_age_derivation_boundary_dates` | `tests.test_demographics` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_demographics_encryption_decryption_roundtrip` | `tests.test_demographics` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_gender_normalization` | `tests.test_demographics` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_get_safe_demographics_failures_fail_safely` | `tests.test_demographics` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_get_safe_demographics_valid_decryption` | `tests.test_demographics` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_study_differences_missing_version` | `tests.test_designer_differences` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_study_differences_registry_404` | `tests.test_designer_differences` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_study_differences_registry_error` | `tests.test_designer_differences` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_study_differences_registry_offline` | `tests.test_designer_differences` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_study_differences_registry_timeout` | `tests.test_designer_differences` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_study_differences_success` | `tests.test_designer_differences` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_detect_circular_dependencies` | `tests.test_designer_rules` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_detect_unknown_fields` | `tests.test_designer_rules` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_invalid_comparison_arity` | `tests.test_designer_rules` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_invalid_logical_not_arity` | `tests.test_designer_rules` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_invalid_skip_logic_schema_missing_fields` | `tests.test_designer_rules` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_map_study_to_usdm_with_rules` | `tests.test_designer_rules` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_neo4j_create_rule` | `tests.test_designer_rules` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_neo4j_delete_rule` | `tests.test_designer_rules` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_neo4j_get_rules` | `tests.test_designer_rules` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_neo4j_update_rule` | `tests.test_designer_rules` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_rules_auth_gateways` | `tests.test_designer_rules` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_rules_crud_endpoints` | `tests.test_designer_rules` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_valid_skip_logic_schema` | `tests.test_designer_rules` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_xpath_compile_logical_and_functions` | `tests.test_designer_rules` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_xpath_compile_simple` | `tests.test_designer_rules` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_database_url_override_and_init` | `tests.test_econsent` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_econsent_database_schema_creation` | `tests.test_econsent` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_econsent_document_lifecycle_and_audit_context` | `tests.test_econsent` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_econsent_get_not_found` | `tests.test_econsent` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_econsent_health_check` | `tests.test_econsent` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_econsent_pydantic_schemas` | `tests.test_econsent` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_gateway_auth_middleware_denials` | `tests.test_econsent` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_shared_audit_fields_validation` | `tests.test_econsent` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_uninitialized_database_manager_econsent` | `tests.test_econsent` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_database_url_override_and_init` | `tests.test_eisf_persistence` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_eisf_append_only_versions_and_deduplication` | `tests.test_eisf_persistence` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_eisf_document_creation_and_site_scoped` | `tests.test_eisf_persistence` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_eisf_part11_audit_log_retention` | `tests.test_eisf_persistence` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_uninitialized_database_manager_eisf` | `tests.test_eisf_persistence` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_aggregate_eligibility_evaluation` | `tests.test_eligibility_engine` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_evaluation_all_operators` | `tests.test_eligibility_engine` | PRD-ELIGIBILITY-005 | 🟢 PASSED | < 1s |
| `test_evaluation_incompatible_types_graceful_handling` | `tests.test_eligibility_engine` | PRD-ELIGIBILITY-007 | 🟢 PASSED | < 1s |
| `test_evaluation_kleene_indeterminate_propagation` | `tests.test_eligibility_engine` | PRD-ELIGIBILITY-006 | 🟢 PASSED | < 1s |
| `test_parse_invalid_syntax` | `tests.test_eligibility_engine` | PRD-ELIGIBILITY-004 | 🟢 PASSED | < 1s |
| `test_parse_logical_and_nested_expressions` | `tests.test_eligibility_engine` | PRD-ELIGIBILITY-003 | 🟢 PASSED | < 1s |
| `test_parse_simple_expressions` | `tests.test_eligibility_engine` | PRD-ELIGIBILITY-002 | 🟢 PASSED | < 1s |
| `test_automated_ingestion_and_version_indexing` | `tests.test_etmf` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_canonical_catalog_ingestion_validations` | `tests.test_etmf` | PRD-TMF-002, PRD-TMF-003, Trace-5 | 🟢 PASSED | < 1s |
| `test_completeness_checking_transitions` | `tests.test_etmf` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_completeness_from_catalog` | `tests.test_etmf` | PRD-TMF-004 | 🟢 PASSED | < 1s |
| `test_edl_definitions_and_crud` | `tests.test_etmf` | PRD-EDL-001, Trace-4 | 🟢 PASSED | < 1s |
| `test_etmf_edge_cases_for_coverage` | `tests.test_etmf` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_etmf_qc_lifecycle_and_audit` | `tests.test_etmf` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_inspector_portal_read_only_access_limits` | `tests.test_etmf` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_placeholder_scripts` | `tests.test_etmf` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_site_aware_completeness` | `tests.test_etmf` | PRD-EDL-001, Trace-4 | 🟢 PASSED | < 1s |
| `test_tmf_taxonomy_mapping` | `tests.test_etmf` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_ucum_extra_coverage` | `tests.test_etmf` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_uninitialized_database_manager` | `tests.test_etmf` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_view_download_audit_logging` | `tests.test_etmf` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_actual_cryptographic_verification` | `tests.test_etmf_compliance` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_audit_logs_group_sealing_and_chaining` | `tests.test_etmf_compliance` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_background_sealer_lifecycle` | `tests.test_etmf_compliance` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_missing_and_invalid_signature_ingestion` | `tests.test_etmf_compliance` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_mock_signature_bypass` | `tests.test_etmf_compliance` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_signature_extraction_formats` | `tests.test_etmf_compliance` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_signature_requirement_rules` | `tests.test_etmf_compliance` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_tampering_detection_and_lockout_propagation` | `tests.test_etmf_compliance` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_append_only_transition_history` | `tests.test_etmf_qc` | PRD-QC-005 | 🟢 PASSED | < 1s |
| `test_invalid_status_transition_raises_error` | `tests.test_etmf_qc` | PRD-QC-002 | 🟢 PASSED | < 1s |
| `test_new_document_defaults_to_draft` | `tests.test_etmf_qc` | PRD-QC-001 | 🟢 PASSED | < 1s |
| `test_part11_change_reason_enforcement` | `tests.test_etmf_qc` | PRD-QC-004 | 🟢 PASSED | < 1s |
| `test_qc_history_api_and_audit` | `tests.test_etmf_qc` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_qc_history_api_not_found` | `tests.test_etmf_qc` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_qc_transitions_missing_doc` | `tests.test_etmf_qc` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_role_based_access_controls_and_gates` | `tests.test_etmf_qc` | PRD-QC-003 | 🟢 PASSED | < 1s |
| `test_client_configuration_env_vars` | `tests.test_evs_client` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_client_configuration_overrides` | `tests.test_evs_client` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_get_concept_http_status_error_404` | `tests.test_evs_client` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_get_concept_invalid_via_400` | `tests.test_evs_client` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_get_concept_not_found` | `tests.test_evs_client` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_get_concept_server_error_500` | `tests.test_evs_client` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_get_concept_success` | `tests.test_evs_client` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_get_concept_timeout` | `tests.test_evs_client` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_get_concept_transport_error` | `tests.test_evs_client` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_import_does_not_make_network_calls` | `tests.test_evs_client` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_search_concepts_list_shape` | `tests.test_evs_client` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_search_concepts_success` | `tests.test_evs_client` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_search_concepts_timeout` | `tests.test_evs_client` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_search_concepts_transport_error` | `tests.test_evs_client` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_form_submission_audit_logging` | `tests.test_form_submissions` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_form_submission_invalid_transitions` | `tests.test_form_submissions` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_form_submission_lifecycle_happy_path` | `tests.test_form_submissions` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_form_submission_locks` | `tests.test_form_submissions` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_form_submission_validation` | `tests.test_form_submissions` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_gateway_cors_headers` | `tests.test_gateway` | PRD-UNI-001 | 🟢 PASSED | < 1s |
| `test_gateway_rate_limiting` | `tests.test_gateway` | PRD-UNI-001 | 🟢 PASSED | < 1s |
| `test_gateway_subject_role_routing_restrictions` | `tests.test_gateway` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_generate_signature` | `tests.test_gateway` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_generate_signature_v2` | `tests.test_gateway` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_get_openapi_json` | `tests.test_gateway` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_get_openapi_json_error` | `tests.test_gateway` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_get_swagger_ui` | `tests.test_gateway` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_proxy_requests_change_reason_too_long` | `tests.test_gateway` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_proxy_requests_invalid_auth` | `tests.test_gateway` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_proxy_requests_no_auth` | `tests.test_gateway` | PRD-UNI-001 | 🟢 PASSED | < 1s |
| `test_proxy_requests_paths` | `tests.test_gateway` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_proxy_requests_v2_headers` | `tests.test_gateway` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_proxy_requests_valid_auth` | `tests.test_gateway` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_signature_gated_mutation_enforcement` | `tests.test_gateway` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_signature_gated_mutation_expired_token` | `tests.test_gateway` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_signature_gated_mutation_mismatched_action` | `tests.test_gateway` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_signature_verification_invalid_credentials` | `tests.test_gateway` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_signature_verification_role_insufficient` | `tests.test_gateway` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_signature_verification_success` | `tests.test_gateway` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_verify_token_invalid` | `tests.test_gateway` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_invalid_data_element_default_unit_fails` | `tests.test_global_library` | PRD-MDR-001 | 🟢 PASSED | < 1s |
| `test_invalid_mismatched_type_payload_fails` | `tests.test_global_library` | PRD-MDR-001 | 🟢 PASSED | < 1s |
| `test_mutation_creation_requires_non_empty_change_reason` | `tests.test_global_library` | PRD-MDR-001 | 🟢 PASSED | < 1s |
| `test_mutation_update_requires_non_empty_reason_for_change` | `tests.test_global_library` | PRD-MDR-001 | 🟢 PASSED | < 1s |
| `test_valid_arm_detail_validation` | `tests.test_global_library` | PRD-MDR-001 | 🟢 PASSED | < 1s |
| `test_valid_data_element_detail_validation` | `tests.test_global_library` | PRD-MDR-001 | 🟢 PASSED | < 1s |
| `test_valid_form_detail_validation` | `tests.test_global_library` | PRD-MDR-001 | 🟢 PASSED | < 1s |
| `test_valid_visit_detail_validation` | `tests.test_global_library` | PRD-MDR-001 | 🟢 PASSED | < 1s |
| `test_bulk_offline_sync` | `tests.test_interop` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_epro_submission_and_conflict_resolution` | `tests.test_interop` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_fhir_prefill_bundle_pipeline` | `tests.test_interop` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_foreign_key_and_cascade_lifecycle_integrity` | `tests.test_interop` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_instrument_and_assignment_endpoints_and_auditing` | `tests.test_interop` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_instrument_and_assignment_orm_persistence` | `tests.test_interop` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_pseudonymization_and_pii_stripping` | `tests.test_interop` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_subject_role_authorization_and_identity_binding` | `tests.test_interop` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_clinical_observation_extended_fields` | `tests.test_lab_reference_range_persistence` | PRD-LAB-001 | 🟢 PASSED | < 1s |
| `test_lab_reference_range_audit_and_triggers` | `tests.test_lab_reference_range_persistence` | PRD-LAB-001 | 🟢 PASSED | < 1s |
| `test_lab_reference_range_crud_and_precision` | `tests.test_lab_reference_range_persistence` | PRD-LAB-001 | 🟢 PASSED | < 1s |
| `test_schema_evolution_migration_upgrade` | `tests.test_lab_reference_range_persistence` | PRD-LAB-001 | 🟢 PASSED | < 1s |
| `test_layout_validation_integration` | `tests.test_layout_validator` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_layout_validation_invisible` | `tests.test_layout_validator` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_layout_validation_overlap` | `tests.test_layout_validator` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_layout_validation_scrambled_sequence` | `tests.test_layout_validator` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_layout_validation_valid` | `tests.test_layout_validator` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_ledger_sealing_and_validation` | `tests.test_ledger_and_triggers` | PRD-SYS-003 | 🟢 PASSED | < 1s |
| `test_out_of_band_update_triggers_audit_entry` | `tests.test_ledger_and_triggers` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_prevent_audit_ledger_seals_mutation` | `tests.test_ledger_and_triggers` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_prevent_audit_log_mutation` | `tests.test_ledger_and_triggers` | Trace-1, PRD-SYS-001 | 🟢 PASSED | < 1s |
| `test_prevent_hard_delete_on_audited_model` | `tests.test_ledger_and_triggers` | Trace-1, PRD-SYS-002 | 🟢 PASSED | < 1s |
| `test_designer_gateway_auth_expired_timestamp` | `tests.test_main` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_designer_gateway_auth_invalid_signature` | `tests.test_main` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_designer_gateway_auth_invalid_timestamp` | `tests.test_main` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_designer_gateway_auth_missing_headers` | `tests.test_main` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_designer_health` | `tests.test_main` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_execution_health` | `tests.test_main` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_gateway_health` | `tests.test_main` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_invalid_leading_number` | `tests.test_mapping_validator` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_invalid_spacing` | `tests.test_mapping_validator` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_multiple_colons` | `tests.test_mapping_validator` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_valid_csv` | `tests.test_mapping_validator` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_clean_token` | `tests.test_markdown_validator` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_is_potential_path_ref` | `tests.test_markdown_validator` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_process_markdown_file_e2e` | `tests.test_markdown_validator` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_resolve_path` | `tests.test_markdown_validator` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_validate_cli_command_flag_checks` | `tests.test_markdown_validator` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_validate_cli_command_python_and_pytest` | `tests.test_markdown_validator` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_validate_docker_compose_scenarios` | `tests.test_markdown_validator` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_validate_path` | `tests.test_markdown_validator` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_detect_file_type` | `tests.test_meddra_parser` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_meddra_parser_init_validation` | `tests.test_meddra_parser` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_parse_empty_fields_validation` | `tests.test_meddra_parser` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_parse_hlgt_valid` | `tests.test_meddra_parser` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_parse_hlt_valid` | `tests.test_meddra_parser` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_parse_in_batches_invalid_batch_size` | `tests.test_meddra_parser` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_parse_llt_invalid_code` | `tests.test_meddra_parser` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_parse_llt_invalid_pt_code` | `tests.test_meddra_parser` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_parse_llt_valid` | `tests.test_meddra_parser` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_parse_mdhier_invalid_flag` | `tests.test_meddra_parser` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_parse_mdhier_missing_fields` | `tests.test_meddra_parser` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_parse_mdhier_valid` | `tests.test_meddra_parser` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_parse_pt_valid` | `tests.test_meddra_parser` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_parse_soc_valid` | `tests.test_meddra_parser` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_parser_incremental_batched_consumption` | `tests.test_meddra_parser` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_public_entry_point_file_path` | `tests.test_meddra_parser` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_audit_trigger_logging_on_coding_workflow` | `tests.test_medical_coding` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_dictionary_import_job_lifecycle` | `tests.test_medical_coding` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_lookup_and_indexes` | `tests.test_medical_coding` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_meddra_term_unique_constraint` | `tests.test_medical_coding` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_whodrug_record_unique_constraint` | `tests.test_medical_coding` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_main_cli` | `tests.test_migrate` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_placeholders` | `tests.test_migrate` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_run_migrations_failure` | `tests.test_migrate` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_run_migrations_real_sqlite` | `tests.test_migrate` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_run_migrations_success` | `tests.test_migrate` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_direct_transition_open_to_resolved` | `tests.test_notifications` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_email_delivery_channel_success` | `tests.test_notifications` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_lifecycle_transitions_and_justifications` | `tests.test_notifications` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_notification_creation_and_auditing` | `tests.test_notifications` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_notification_detail_visibility` | `tests.test_notifications` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_notification_list_visibility_and_filtering` | `tests.test_notifications` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_notifications_database_schema_creation` | `tests.test_notifications` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_notifications_health_check` | `tests.test_notifications` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_webhook_delivery_channel_failure_and_retry_backoff` | `tests.test_notifications` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_webhook_delivery_channel_success` | `tests.test_notifications` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_audit_fields_change_reason_validation` | `tests.test_organization_domain` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_audit_fields_instantiation` | `tests.test_organization_domain` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_audit_fields_reusability` | `tests.test_organization_domain` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_clinical_staff_role_values` | `tests.test_organization_domain` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_organization_type_values` | `tests.test_organization_domain` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_trial_duty_values` | `tests.test_organization_domain` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_build_comment_body` | `tests.test_pr_comment` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_combined_audit_logic` | `tests.test_pr_comment` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_get_status_emoji` | `tests.test_pr_comment` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_merge_outcomes` | `tests.test_pr_comment` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_parse_existing_outcomes` | `tests.test_pr_comment` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_export_metadata_invalid_version` | `tests.test_protocol_render` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_export_metadata_missing_change_reason_on_version_bump` | `tests.test_protocol_render` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_export_metadata_valid_initial` | `tests.test_protocol_render` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_export_metadata_valid_version_bump` | `tests.test_protocol_render` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_narrative_item_and_section_views` | `tests.test_protocol_render` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_rendered_protocol_document_with_usdm_study` | `tests.test_protocol_render` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_soa_matrix_view` | `tests.test_protocol_render` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_synopsis_view_parsing` | `tests.test_protocol_render` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_database_manager_uninitialized_raises_exception` | `tests.test_quality` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_deviation_lifecycle_and_traceability_fields` | `tests.test_quality` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_deviation_rca_capa_relationships_and_cascading` | `tests.test_quality` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_quality_audit_log_append_only` | `tests.test_quality` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_quality_database_schema_creation` | `tests.test_quality` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_quality_health_check` | `tests.test_quality` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_sqlite_foreign_key_constraints` | `tests.test_quality` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_audit_log_endpoint_properties` | `tests.test_quality_workflow` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_capa_approval_closure_requires_quality_oversight` | `tests.test_quality_workflow` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_capa_creation_validations` | `tests.test_quality_workflow` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `test_capa_lifecycle_transitions` | `tests.test_quality_workflow` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `test_capa_updates_and_concurrency` | `tests.test_quality_workflow` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `test_create_and_list_deviations` | `tests.test_quality_workflow` | PRD-SYS-001 | 🟢 PASSED | < 1s |
| `test_create_and_update_rca` | `tests.test_quality_workflow` | PRD-SYS-001 | 🟢 PASSED | < 1s |
| `test_mutation_without_change_reason_rejected` | `tests.test_quality_workflow` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_permission_failure_leaves_no_misleading_audit_entry` | `tests.test_quality_workflow` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_read_only_roles_forbidden` | `tests.test_quality_workflow` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_successful_mutation_creates_audit_log_and_is_atomic` | `tests.test_quality_workflow` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_randomization_entities_audit_trail_and_soft_delete` | `tests.test_randomization_persistence` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_randomization_entities_hard_delete_prevented` | `tests.test_randomization_persistence` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_randomization_entities_trial_lock_conformity` | `tests.test_randomization_persistence` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_etmf_audit_logs_gated_to_auditors` | `tests.test_rbac` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_etmf_document_transition_auditor_forbidden` | `tests.test_rbac` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_etmf_edl_creation_auditor_forbidden` | `tests.test_rbac` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_etmf_edl_update_auditor_forbidden` | `tests.test_rbac` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_etmf_ingest_auditor_forbidden` | `tests.test_rbac` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_execution_observation_creation_auditor_forbidden` | `tests.test_rbac` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_execution_subject_creation_auditor_forbidden` | `tests.test_rbac` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_execution_visit_creation_auditor_forbidden` | `tests.test_rbac` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_role_normalization_list` | `tests.test_rbac` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_role_normalization_string` | `tests.test_rbac` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_verify_is_auditor_allows_auditors` | `tests.test_rbac` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_verify_is_auditor_denies_non_auditors` | `tests.test_rbac` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_verify_not_auditor_allows_others` | `tests.test_rbac` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_verify_not_auditor_denies_auditors` | `tests.test_rbac` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_icsr_version_metadata` | `tests.test_sae_icsr` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_invalid_icsr_drug_role` | `tests.test_sae_icsr` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_invalid_icsr_patient_age_negative` | `tests.test_sae_icsr` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_invalid_icsr_patient_age_unit` | `tests.test_sae_icsr` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_invalid_meddra_coding_primary_soc` | `tests.test_sae_icsr` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_invalid_sae_date_chronology` | `tests.test_sae_icsr` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_invalid_sae_date_format` | `tests.test_sae_icsr` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_invalid_sae_seq` | `tests.test_sae_icsr` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_invalid_sae_seriousness` | `tests.test_sae_icsr` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_invalid_sae_severity` | `tests.test_sae_icsr` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_sae_version_metadata` | `tests.test_sae_icsr` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_valid_icsr_full` | `tests.test_sae_icsr` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_valid_meddra_coding` | `tests.test_sae_icsr` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_valid_sae_full_normalization` | `tests.test_sae_icsr` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_valid_sae_minimum` | `tests.test_sae_icsr` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_ae_required_optional_and_date_order` | `tests.test_sdtm_foundation` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_auditable_model_fields_and_validation` | `tests.test_sdtm_foundation` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_cm_required_optional_and_date_order` | `tests.test_sdtm_foundation` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_date_format_validation` | `tests.test_sdtm_foundation` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_dm_required_and_optional_fields` | `tests.test_sdtm_foundation` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_lb_required_and_optional_fields` | `tests.test_sdtm_foundation` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_models_optional_nones` | `tests.test_sdtm_foundation` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_null_flavor_enum_membership` | `tests.test_sdtm_foundation` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_sdtm_domain_enum_membership` | `tests.test_sdtm_foundation` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_suppqual_fields_and_validation` | `tests.test_sdtm_foundation` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_terminology_normalization_and_enums` | `tests.test_sdtm_foundation` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_vs_required_and_optional_fields` | `tests.test_sdtm_foundation` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_clinical_observation_sdv_defaults` | `tests.test_sdv_tsdv_persistence` | PRD-QRY-005, PRD-QRY-007 | 🟢 PASSED | < 1s |
| `test_sdv_sign_off_persistence_and_audit` | `tests.test_sdv_tsdv_persistence` | PRD-QRY-005 | 🟢 PASSED | < 1s |
| `test_tsdv_config_persistence` | `tests.test_sdv_tsdv_persistence` | PRD-QRY-007 | 🟢 PASSED | < 1s |
| `test_audit_context_variables_and_decorator` | `tests.test_security_middleware` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_canonical_json_signing_and_verification` | `tests.test_security_middleware` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_downstream_signature_gated_endpoint_expired_token` | `tests.test_security_middleware` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_downstream_signature_gated_endpoint_mismatched_action` | `tests.test_security_middleware` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_downstream_signature_gated_endpoint_replay_blocked` | `tests.test_security_middleware` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_downstream_signature_gated_endpoint_requires_sig_token` | `tests.test_security_middleware` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_downstream_signature_gated_endpoint_valid_sig_token` | `tests.test_security_middleware` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_middleware_expired_timestamp` | `tests.test_security_middleware` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_middleware_explicit_legacy_version_rejected` | `tests.test_security_middleware` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_middleware_health_bypass` | `tests.test_security_middleware` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_middleware_invalid_timestamp_format` | `tests.test_security_middleware` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_middleware_missing_headers` | `tests.test_security_middleware` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_middleware_missing_signature_version_rejected` | `tests.test_security_middleware` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_middleware_unsupported_version_rejected` | `tests.test_security_middleware` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_middleware_v2_invalid_signature` | `tests.test_security_middleware` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_middleware_v2_mismatched_reason` | `tests.test_security_middleware` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_middleware_v2_missing_reason` | `tests.test_security_middleware` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_middleware_v2_safe_method_no_reason_success` | `tests.test_security_middleware` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_middleware_v2_success` | `tests.test_security_middleware` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_mutation_unsigned_and_non_compliant_rejections` | `tests.test_security_middleware` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_asymmetric_sign_and_verify` | `tests.test_signature_manifestation` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_async_signature_context_decorator` | `tests.test_signature_manifestation` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_capture_certificate_identifiers` | `tests.test_signature_manifestation` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_controlled_enums` | `tests.test_signature_manifestation` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_sha256_hashing_helper` | `tests.test_signature_manifestation` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_signature_context_propagation` | `tests.test_signature_manifestation` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_signature_manifestation_lifecycle` | `tests.test_signature_manifestation` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_api_protocol_amendment_concurrency_race` | `tests.test_study_versions` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_api_protocol_amendment_invalid_signature_rejected` | `tests.test_study_versions` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_api_protocol_amendment_invalid_study_404` | `tests.test_study_versions` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_api_protocol_amendment_minor_and_major_bumps` | `tests.test_study_versions` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_api_study_version_creation_and_guards` | `tests.test_study_versions` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_assert_graph_mutable_library_object_permits_active` | `tests.test_study_versions` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_assert_graph_mutable_library_object_rejects_frozen` | `tests.test_study_versions` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_assert_graph_mutable_permits_draft_active` | `tests.test_study_versions` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_assert_graph_mutable_rejects_frozen_states` | `tests.test_study_versions` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_bump_version_edge_cases` | `tests.test_study_versions` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_create_library_object_version_guards` | `tests.test_study_versions` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_mock_study_version_creation_and_immutability` | `tests.test_study_versions` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_neo4j_create_study_version_duplicate_raises_conflict` | `tests.test_study_versions` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_neo4j_create_study_version_success` | `tests.test_study_versions` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_update_study_properties_guards` | `tests.test_study_versions` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_verify_version_signature_edge_cases` | `tests.test_study_versions` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_pure_python_transition_guard` | `tests.test_subject_randomization_lifecycle` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_stratification_factors_locking` | `tests.test_subject_randomization_lifecycle` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_subject_initial_state_and_persistence` | `tests.test_subject_randomization_lifecycle` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_subject_state_transitions` | `tests.test_subject_randomization_lifecycle` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_unblinding_and_withdrawal_behavior` | `tests.test_subject_randomization_lifecycle` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_generic_natural_deduplication_key` | `tests.test_sync_engine` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_signature_validation_failures` | `tests.test_sync_engine` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_signature_validation_happy_path` | `tests.test_sync_engine` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_strategy_client_wins_existing` | `tests.test_sync_engine` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_strategy_client_wins_no_existing` | `tests.test_sync_engine` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_strategy_merge_independent_fields` | `tests.test_sync_engine` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_strategy_merge_lww_existing_wins` | `tests.test_sync_engine` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_strategy_merge_lww_incoming_wins` | `tests.test_sync_engine` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_strategy_merge_lww_timestamp_tie` | `tests.test_sync_engine` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_strategy_server_wins` | `tests.test_sync_engine` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_get_repository_fallback` | `tests.test_sync_ruleset` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_get_repository_from_env` | `tests.test_sync_ruleset` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_get_repository_from_git_https` | `tests.test_sync_ruleset` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_get_repository_from_git_ssh` | `tests.test_sync_ruleset` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_sync_ruleset_create_new` | `tests.test_sync_ruleset` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_sync_ruleset_dry_run` | `tests.test_sync_ruleset` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_sync_ruleset_permission_denied_403` | `tests.test_sync_ruleset` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_sync_ruleset_update_existing` | `tests.test_sync_ruleset` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_terminology_cache_capacity_eviction` | `tests.test_terminology_cache` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_terminology_cache_hit_and_expiration` | `tests.test_terminology_cache` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_terminology_cache_thread_safety` | `tests.test_terminology_cache` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_terminology_cache_ttl_config` | `tests.test_terminology_cache` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_terminology_cache_unreachable_db_fallback` | `tests.test_terminology_cache` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_cache_hit_performs_no_external_lookup` | `tests.test_terminology_integration` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_existing_cache_consumers_receive_expected_shape` | `tests.test_terminology_integration` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_expired_entry_fallback_on_unreachable_evs` | `tests.test_terminology_integration` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_offline_fallback_resolves_supported_seed_concepts` | `tests.test_terminology_integration` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_active_version_selection` | `tests.test_tmf_reference_model` | PRD-TMF-001 | 🟢 PASSED | < 1s |
| `test_artifact_parent_identification` | `tests.test_tmf_reference_model` | PRD-TMF-001 | 🟢 PASSED | < 1s |
| `test_canonical_11_zones` | `tests.test_tmf_reference_model` | PRD-TMF-001 | 🟢 PASSED | < 1s |
| `test_explicit_version_selection` | `tests.test_tmf_reference_model` | PRD-TMF-001 | 🟢 PASSED | < 1s |
| `test_get_mandatory_artifacts_failures` | `tests.test_tmf_reference_model` | PRD-TMF-004 | 🟢 PASSED | < 1s |
| `test_get_mandatory_artifacts_success` | `tests.test_tmf_reference_model` | PRD-TMF-004 | 🟢 PASSED | < 1s |
| `test_immutability_properties` | `tests.test_tmf_reference_model` | PRD-TMF-001 | 🟢 PASSED | < 1s |
| `test_no_database_dependencies` | `tests.test_tmf_reference_model` | PRD-TMF-001 | 🟢 PASSED | < 1s |
| `test_resolve_artifact_failures` | `tests.test_tmf_reference_model` | PRD-TMF-001 | 🟢 PASSED | < 1s |
| `test_resolve_artifact_success` | `tests.test_tmf_reference_model` | PRD-TMF-001 | 🟢 PASSED | < 1s |
| `test_validate_hierarchy_failures` | `tests.test_tmf_reference_model` | PRD-TMF-002 | 🟢 PASSED | < 1s |
| `test_validate_hierarchy_success` | `tests.test_tmf_reference_model` | PRD-TMF-002 | 🟢 PASSED | < 1s |
| `test_version_isolation` | `tests.test_tmf_reference_model` | PRD-TMF-001 | 🟢 PASSED | < 1s |
| `test_admin_cache_clear_forces_fresh_read` | `tests.test_transformers` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_legacy_endpoint_returns_original_schema` | `tests.test_transformers` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_terminology_cache_prevents_db_queries` | `tests.test_transformers` | PRD-MDR-001 | 🟢 PASSED | < 1s |
| `test_usdm_endpoint_returns_nested_schema_and_fast` | `tests.test_transformers` | PRD-MDR-003, PRD-MDR-004 | 🟢 PASSED | < 1s |
| `test_usdm_validation_error_on_invalid_data` | `tests.test_transformers` | PRD-MDR-001 | 🟢 PASSED | < 1s |
| `test_security_gate_unauthenticated_requests` | `tests.test_translation_recovery` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_translation_error_status_and_rollback` | `tests.test_translation_recovery` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_translation_status_and_listing_success` | `tests.test_translation_recovery` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_worker_context_and_session_cleanup` | `tests.test_translation_recovery` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_audit_safe_context_binds_and_cleans_up` | `tests.test_translator` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_audit_safe_context_cleans_up_on_error` | `tests.test_translator` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_background_translation_records_user_audit` | `tests.test_translator` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_identifier_sanitization_during_translation` | `tests.test_translator` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_study_published_event_triggers_translation` | `tests.test_translator` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_study_published_expired_timestamp_rejection` | `tests.test_translator` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_study_published_invalid_signature_rejection` | `tests.test_translator` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_translation_validation_failure` | `tests.test_translator` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_site_and_visit_locks` | `tests.test_trial_lock` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_subject_and_form_locks` | `tests.test_trial_lock` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_trial_lock_freeze` | `tests.test_trial_lock` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_check_architectural_changes_require_adr_missing_adr` | `tests.test_validate_adrs` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_check_architectural_changes_require_adr_no_changes` | `tests.test_validate_adrs` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_check_architectural_changes_require_adr_with_deleted_adr` | `tests.test_validate_adrs` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_check_architectural_changes_require_adr_with_valid_adr` | `tests.test_validate_adrs` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_get_changed_files_from_git_fallbacks` | `tests.test_validate_adrs` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_get_changed_files_from_txt` | `tests.test_validate_adrs` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_is_architectural_file` | `tests.test_validate_adrs` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_validate_existing_adrs_valid_case` | `tests.test_validate_adrs` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_generate_alignment_report` | `tests.test_validator` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_extract_active_vulnerabilities_invalid` | `tests.test_vulnerabilities` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_extract_active_vulnerabilities_valid` | `tests.test_vulnerabilities` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_load_and_validate_ledger_incorrect_rpn` | `tests.test_vulnerabilities` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_load_and_validate_ledger_invalid_fmea_scores` | `tests.test_vulnerabilities` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_load_and_validate_ledger_invalid_json` | `tests.test_vulnerabilities` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_load_and_validate_ledger_missing_fmea_fields` | `tests.test_vulnerabilities` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_load_and_validate_ledger_missing_justification` | `tests.test_vulnerabilities` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_load_and_validate_ledger_missing_vuln_id` | `tests.test_vulnerabilities` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_load_and_validate_ledger_not_found` | `tests.test_vulnerabilities` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_load_and_validate_ledger_not_list` | `tests.test_vulnerabilities` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_load_and_validate_ledger_valid` | `tests.test_vulnerabilities` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_scan_for_inline_bypasses_no_violations` | `tests.test_vulnerabilities` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_scan_for_inline_bypasses_with_violations` | `tests.test_vulnerabilities` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_delimited_format_int_indices_without_header` | `tests.test_whodrug_parser` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_delimited_format_parsing` | `tests.test_whodrug_parser` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_detect_file_type` | `tests.test_whodrug_parser` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_invalid_and_missing_required_fields` | `tests.test_whodrug_parser` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_max_length_constraints` | `tests.test_whodrug_parser` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_non_strict_referential_validation` | `tests.test_whodrug_parser` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_parse_in_batches_whodrug` | `tests.test_whodrug_parser` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_parse_valid_fixed_width_atc` | `tests.test_whodrug_parser` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_parse_valid_fixed_width_drug_atc` | `tests.test_whodrug_parser` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_parse_valid_fixed_width_drug_ingredients` | `tests.test_whodrug_parser` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_parse_valid_fixed_width_drugs` | `tests.test_whodrug_parser` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_parse_valid_fixed_width_ingredients` | `tests.test_whodrug_parser` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_public_entry_point_reusing_parser` | `tests.test_whodrug_parser` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_public_entry_point_whodrug` | `tests.test_whodrug_parser` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_strict_referential_validation_triggers` | `tests.test_whodrug_parser` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_whodrug_parser_init_validation` | `tests.test_whodrug_parser` | *Regression/Helper* | 🟢 PASSED | < 1s |

## 4. Performance Qualification (PQ) & Scenario Validation

Performance Qualification documents the verification of end-to-end clinical workflow scenarios defined in Section 5 of the QA & Validation Plan.

### TC-VAL-LOG-001: Protocol Version Locking & Immutability Rejection
- **Target Requirements:** PRD-MDR-001, PRD-UNI-003
- **Description:** Verifies that locked study version nodes in Neo4j are completely immutable, and direct database manipulations are rejected.
- **Verification Status:** ✅ Verified Compliant via Automated Integration Suite

### TC-VAL-LOG-002: Stratification Factor Re-randomization Rejections
- **Target Requirements:** PRD-SUB-002, PRD-SUB-001
- **Description:** Verifies that stratification factor modifications and backward state machine updates are strictly forbidden once randomized.
- **Verification Status:** ✅ Verified Compliant via Automated Integration Suite

### TC-VAL-LOG-003: Offline Mode Data Entry, Sync Collision & Conflict Resolution
- **Target Requirements:** PRD-EDC-004, PRD-UNI-002
- **Description:** Verifies that offline data entries are synchronized accurately, conflict resolution runs deterministically, and the audit ledger captures all states.
- **Verification Status:** ✅ Verified Compliant via Automated Integration Suite

### TC-VAL-LOG-004: Re-authentication Enforcement during Emergency Unblinding
- **Target Requirements:** PRD-MDR-003, PRD-UNI-002
- **Description:** Verifies that unblinding requests require strict multi-factor re-authentication, trigger immediate unblinded state transition, lock the trial on tampering, and dispatch security alerts.
- **Verification Status:** ✅ Verified Compliant via Automated Integration Suite

## 5. Qualification Review & Authorization

This GxP computerized system validation log is compiled with mathematical determinism directly from the execution runners of the build system.

```
Lead Systems Validation Engineer:   ___________________________   Date: _______________
Director of Clinical Quality Assurance: ___________________________   Date: _______________
```

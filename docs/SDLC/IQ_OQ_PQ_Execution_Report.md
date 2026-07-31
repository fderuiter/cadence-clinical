# GxP Installation & Operational Qualification (IQ/OQ/PQ) Execution Report

*Execution Date:* 2026-07-23 22:38:25 UTC
*Regulatory Protocol:* FDA 21 CFR Part 11, EU Annex 11, GAMP 5 Category 4/5, IEC 62304 Class B

## 1. Executive Summary & Verification Declaration

This report documents the Installation Qualification (IQ) and Operational Qualification (OQ) for the Cadence Clinical platform.
Based on the executed automated verification suite, the platform meets all predefined structural, functional, and security compliance constraints.

### Validation Result Summary
- **Total Automated Test Cases Run:** 1428
- **Passed:** 1428 🟢
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
aiosmtplib               5.1.2
aiosqlite                0.22.1
annotated-doc            0.0.4
annotated-types          0.7.0
anyio                    4.14.2
asyncpg                  0.31.0
babel                    2.18.0
bandit                   1.9.4
beautifulsoup4           4.15.0
boolean-py               5.0
brotli                   1.2.0
brotlicffi               1.2.0.1
cachecontrol             0.14.4
cadence-clinical         0.1.0       /app
certifi                  2026.7.22
cffi                     2.1.0
cfgv                     3.5.0
charset-normalizer       3.4.9
click                    8.4.2
colorama                 0.4.6
coverage                 7.15.2
cryptography             49.0.0
cssselect2               0.9.0
cyclonedx-python-lib     11.11.0
defusedxml               0.7.1
detect-secrets           1.5.0
distlib                  0.4.3
docraptor                3.1.0
docxcompose              2.2.0
docxtpl                  0.20.2
ecdsa                    0.19.2
et-xmlfile               2.0.0
execnet                  2.1.2
fastapi                  0.139.2
fhir-core                1.1.9
fhir-resources           8.3.0
filelock                 3.32.0
fonttools                4.63.0
greenlet                 3.5.4
h11                      0.16.0
httpcore                 1.0.9
httptools                0.8.0
httpx                    0.28.1
identify                 2.6.19
idna                     3.18
iniconfig                2.3.0
jinja2                   3.1.6
license-expression       30.4.4
lxml                     6.1.1
markdown-it-py           4.2.0
markupsafe               3.0.3
mdurl                    0.1.2
msgpack                  1.2.1
neo4j                    6.2.0
nodeenv                  1.10.0
numpy                    2.4.6
numpy                    2.5.1
openpyxl                 3.1.5
packageurl-python        0.17.6
packaging                26.2
pandas                   3.0.3
pillow                   12.3.0
pip                      26.1.2
pip-api                  0.0.34
pip-audit                2.10.1
pip-requirements-parser  32.0.1
platformdirs             4.11.0
playwright               1.61.0
pluggy                   1.6.0
pre-commit               4.6.1
py-serializable          2.1.0
pyasn1                   0.6.4
pycparser                3.0
pydantic                 2.13.4
pydantic-core            2.46.4
pydyf                    0.12.1
pyee                     13.0.1
pygments                 2.20.0
pyparsing                3.3.2
pyphen                   0.17.2
pytest                   9.1.1
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
requests                 2.34.2
rich                     15.0.0
rsa                      4.9.1
ruff                     0.15.22
six                      1.17.0
sortedcontainers         2.4.0
soupsieve                2.9.1
sqlalchemy               2.0.51
starlette                1.3.1
stevedore                5.9.0
stringcase               1.2.0
text-unidecode           1.3
tinycss2                 1.5.1
tinyhtml5                2.1.0
tomli                    2.4.1
tomli-w                  1.2.0
typing-extensions        4.16.0
typing-inspection        0.4.2
tzdata                   2026.3
urllib3                  2.7.0
usdm                     0.66.0
usdm                     0.67.0
uvicorn                  0.51.0
uvloop                   0.22.1
virtualenv               21.7.0
watchfiles               1.2.0
weasyprint               69.0
webencodings             0.5.1
websockets               16.1.1
yattag                   1.16.1
zopfli                   0.4.3
```

## 3. Operational Qualification (OQ)

The Operational Qualification verifies that individual clinical operations, state machine transitions, cryptographic workflows, database-level triggers, and blinding boundaries are executed accurately according to functional requirements.

### 3.1 Traceability Mappings Verification
| Test Case Name | Classname / Suite | Target Req | Status | Duration |
| :--- | :--- | :--- | :--- | :--- |
| `test_derive_adae_basic_join` | `tests.test_adae` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_derive_adae_missing_dates_and_ongoing` | `tests.test_adae` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_derive_adae_partial_dates_imputation` | `tests.test_adae` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_derive_adae_relative_day_formula` | `tests.test_adae` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_derive_adae_severity_mappings` | `tests.test_adae` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_derive_adae_treatment_emergent_safety_window` | `tests.test_adae` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_derive_adae_unmatched_subject_skipped` | `tests.test_adae` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_from_sas_date` | `tests.test_adae` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_derive_adsl_additional_branches` | `tests.test_adsl` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_derive_adsl_basic` | `tests.test_adsl` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_derive_adsl_edge_cases` | `tests.test_adsl` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_derive_adsl_observation_based_death_and_actarm` | `tests.test_adsl` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_derive_adsl_partial_dates_and_population_flags` | `tests.test_adsl` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_derive_adsl_various_fallback_branches` | `tests.test_adsl` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_derive_adsl_with_datetime_objects` | `tests.test_adsl` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_impute_partial_date` | `tests.test_adsl` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_parse_partial_date` | `tests.test_adsl` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_to_date_obj` | `tests.test_adsl` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_to_sas_date` | `tests.test_adsl` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_advs_baseline_selection_and_flags` | `tests.test_advs` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_advs_basic_derivation` | `tests.test_advs` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_advs_change_metrics_and_division_by_zero` | `tests.test_advs` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_advs_date_and_visit_fallback` | `tests.test_advs` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_advs_missing_baseline_behavior` | `tests.test_advs` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_advs_no_coercion_of_missing_numeric_values` | `tests.test_advs` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_api_parameters_parity` | `tests.test_api_contract_validation` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_api_paths_and_methods_parity` | `tests.test_api_contract_validation` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_api_request_bodies_parity` | `tests.test_api_contract_validation` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_api_responses_parity` | `tests.test_api_contract_validation` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_extra_response_properties_pass_validation` | `tests.test_api_contract_validation` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_markdown_spec_extract_and_parse` | `tests.test_api_contract_validation` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_markdown_spec_syntax_checks_malformed_yaml` | `tests.test_api_contract_validation` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_undocumented_parameter_fails_parity_check` | `tests.test_api_contract_validation` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_undocumented_route_fails_parity_check` | `tests.test_api_contract_validation` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_validation_fails_on_route_path_mismatch` | `tests.test_api_contract_validation` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_audit_records_ip_and_custom_timestamp` | `tests.test_audit` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_hard_delete_is_prevented` | `tests.test_audit` | Trace-1 | 🟢 PASSED | < 1s |
| `test_insert_generates_audit_log` | `tests.test_audit` | PRD-SYS-001 | 🟢 PASSED | < 1s |
| `test_read_only_queries_do_not_generate_audit_logs` | `tests.test_audit` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_rollback_prevents_orphan_audit_logs` | `tests.test_audit` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_soft_delete_generates_audit_log` | `tests.test_audit` | PRD-SYS-002 | 🟢 PASSED | < 1s |
| `test_subject_notification_skips_clinical_auditing` | `tests.test_audit` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_update_generates_audit_log` | `tests.test_audit` | PRD-SYS-001 | 🟢 PASSED | < 1s |
| `test_batch_sign_off_all_locks` | `tests.test_batch_sign_off` | Trace-14 | 🟢 PASSED | < 1s |
| `test_batch_sign_off_audit_manifestation_capture` | `tests.test_batch_sign_off` | Trace-14 | 🟢 PASSED | < 1s |
| `test_batch_sign_off_happy_path_form` | `tests.test_batch_sign_off` | Trace-14, Trace-15 | 🟢 PASSED | < 1s |
| `test_batch_sign_off_locks_and_atomic_rollback` | `tests.test_batch_sign_off` | Trace-14 | 🟢 PASSED | < 1s |
| `test_batch_sign_off_mismatched_bindings_and_no_write` | `tests.test_batch_sign_off` | Trace-14, Trace-15 | 🟢 PASSED | < 1s |
| `test_batch_sign_off_non_lock_rollback` | `tests.test_batch_sign_off` | Trace-14 | 🟢 PASSED | < 1s |
| `test_batch_sign_off_pi_only` | `tests.test_batch_sign_off` | Trace-14 | 🟢 PASSED | < 1s |
| `test_batch_sign_off_subject_resolution` | `tests.test_batch_sign_off` | Trace-14 | 🟢 PASSED | < 1s |
| `test_batch_sign_off_token_replay` | `tests.test_batch_sign_off` | Trace-15 | 🟢 PASSED | < 1s |
| `test_batch_sign_off_visit_resolution` | `tests.test_batch_sign_off` | Trace-14 | 🟢 PASSED | < 1s |
| `test_dataset_json_integration_structure` | `tests.test_biostat` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_declarative_mappings_coverage` | `tests.test_biostat` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_extract_ae_sorting_ongoing_supp` | `tests.test_biostat` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_extract_dm_age_precision_and_controlled_terminology` | `tests.test_biostat` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_extract_dm_demographics` | `tests.test_biostat` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_extract_lb_verbatim_normalized_supp` | `tests.test_biostat` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_extract_mh_sequencing_and_supp` | `tests.test_biostat` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_extract_vs_baseline_supp` | `tests.test_biostat` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_mapping_helpers` | `tests.test_biostat` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_normalize_race` | `tests.test_biostat` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_normalize_severity` | `tests.test_biostat` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_normalize_sex` | `tests.test_biostat` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_supp_record_row_conversion` | `tests.test_biostat` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_variable_metadata_validation` | `tests.test_biostat` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_age_capping_thresholds` | `tests.test_biostat_deidentification` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_authorization_allowed_role_succeeds` | `tests.test_biostat_deidentification` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_authorization_disallowed_role_receives_403` | `tests.test_biostat_deidentification` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_dataset_json_validation_passes_after_transform` | `tests.test_biostat_deidentification` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_date_shift_stable_and_interval_preserving` | `tests.test_biostat_deidentification` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_error_redaction_and_scrubbing_on_failed_export` | `tests.test_biostat_deidentification` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_identical_pseudonymization_across_datasets_and_supp` | `tests.test_biostat_deidentification` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_partial_dates_shifted_without_fabricating_precision` | `tests.test_biostat_deidentification` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_pseudonymization_determinism_and_hex_format` | `tests.test_biostat_deidentification` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_scrub_error_message_direct` | `tests.test_biostat_deidentification` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_source_records_are_not_mutated` | `tests.test_biostat_deidentification` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_adae_trtemfl_logic` | `tests.test_biostat_export` | ADAM-ADAE-TRTEMFL-01 | 🟢 PASSED | < 1s |
| `test_advs_chg_pchg_computations` | `tests.test_biostat_export` | ADAM-ADVS-CHG-01 | 🟢 PASSED | < 1s |
| `test_api_adam_export_success` | `tests.test_biostat_export` | API-ADAM-EXPORT-01 | 🟢 PASSED | < 1s |
| `test_api_biostat_bundle_export_with_supp_records` | `tests.test_biostat_export` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_api_sdtm_export_success` | `tests.test_biostat_export` | API-SDTM-EXPORT-01 | 🟢 PASSED | < 1s |
| `test_api_sdtm_export_with_supp_records` | `tests.test_biostat_export` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_api_unauthenticated_export_rejection` | `tests.test_biostat_export` | SEC-EXPORT-AUTH-01 | 🟢 PASSED | < 1s |
| `test_api_validation_failure_logging` | `tests.test_biostat_export` | API-EXPORT-VAL-01 | 🟢 PASSED | < 1s |
| `test_partial_date_imputation_detailed` | `tests.test_biostat_export` | SDTM-IMPUTE-01 | 🟢 PASSED | < 1s |
| `test_sdtm_age_derivation` | `tests.test_biostat_export` | SDTM-DM-AGE-01 | 🟢 PASSED | < 1s |
| `test_sdtm_sequence_assignment` | `tests.test_biostat_export` | SDTM-SEQ-01 | 🟢 PASSED | < 1s |
| `test_sdtm_supplemental_qualifiers` | `tests.test_biostat_export` | SDTM-SUPP-01 | 🟢 PASSED | < 1s |
| `test_adam_dataset_export_success` | `tests.test_biostat_exports` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_biostat_bundle_export_success` | `tests.test_biostat_exports` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_export_validation_failure_handling` | `tests.test_biostat_exports` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_invalid_adam_dataset_rejection` | `tests.test_biostat_exports` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_invalid_sdtm_domain_rejection` | `tests.test_biostat_exports` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_sdtm_domain_export_success` | `tests.test_biostat_exports` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_unauthenticated_access_rejection` | `tests.test_biostat_exports` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_rtm_generation_conftest_hook_detection` | `tests.test_cli_etmf_archival` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_rtm_generation_with_cli_overrides` | `tests.test_cli_etmf_archival` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_api_gateway_routing` | `tests.test_clinical_engine` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_cdisc_export_and_validation` | `tests.test_clinical_engine` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_demographics_encryption` | `tests.test_clinical_engine` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_outlier_detection_performance` | `tests.test_clinical_engine` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_relational_persistence_and_recalculation` | `tests.test_clinical_engine` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_unit_conversions` | `tests.test_clinical_engine` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_candidate_creation_and_opening_workflow` | `tests.test_clinical_queries` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_clinical_queries_sync_endpoint` | `tests.test_clinical_queries` | *Regression/Helper* | 🟢 PASSED | < 1s |
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
| `test_publish_notification_failure_swallowed` | `tests.test_clinical_workflow_notifications` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_publish_notification_success` | `tests.test_clinical_workflow_notifications` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_router_send_dashboard_notification_sdv_drop` | `tests.test_clinical_workflow_notifications` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_router_send_email_mapping` | `tests.test_clinical_workflow_notifications` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_router_send_sms_mapping` | `tests.test_clinical_workflow_notifications` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_router_send_webhook_mapping` | `tests.test_clinical_workflow_notifications` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_unblind_emergency_unblinding_alert_integration` | `tests.test_clinical_workflow_notifications` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_check_dict_for_value` | `tests.test_concept_locks` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_concept_mutations_locked_active_recruiting` | `tests.test_concept_locks` | PRD-MDR-002 | 🟢 PASSED | < 1s |
| `test_concept_mutations_unreferenced` | `tests.test_concept_locks` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_is_concept_referenced_by_active_recruiting_study` | `tests.test_concept_locks` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_narrative_assembly_and_ref_resolution` | `tests.test_content_assembly` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_narrative_display_rule_duplicate_section_numbers` | `tests.test_content_assembly` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_narrative_display_rule_missing_section_number` | `tests.test_content_assembly` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_soa_matrix_assembly` | `tests.test_content_assembly` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_successful_assembly_and_synopsis` | `tests.test_content_assembly` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_unresolved_reference_invalid_attribute` | `tests.test_content_assembly` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_unresolved_reference_non_existent_id` | `tests.test_content_assembly` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_candidate_item_review_transitions` | `tests.test_crf_ingestion` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_docx_ingestion_success` | `tests.test_crf_ingestion` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_low_confidence_classification` | `tests.test_crf_ingestion` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_malformed_or_unsupported_document` | `tests.test_crf_ingestion` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_pdf_ingestion_success` | `tests.test_crf_ingestion` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_promotion_gates_and_draft_creation` | `tests.test_crf_ingestion` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_unauthorized_upload` | `tests.test_crf_ingestion` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_dual_custody_negative_duplicate_shares` | `tests.test_cryptography` | PRD-SYS-001 | 🟢 PASSED | < 1s |
| `test_dual_custody_negative_malformed_share` | `tests.test_cryptography` | PRD-SYS-001 | 🟢 PASSED | < 1s |
| `test_dual_custody_negative_mismatched_versions` | `tests.test_cryptography` | PRD-SYS-001 | 🟢 PASSED | < 1s |
| `test_dual_custody_negative_single_share` | `tests.test_cryptography` | PRD-SYS-001 | 🟢 PASSED | < 1s |
| `test_dual_custody_negative_tampered_share` | `tests.test_cryptography` | PRD-SYS-001 | 🟢 PASSED | < 1s |
| `test_dual_custody_positive` | `tests.test_cryptography` | PRD-SYS-001 | 🟢 PASSED | < 1s |
| `test_encryption_decryption_with_rotation` | `tests.test_cryptography` | PRD-MDR-005, Trace-2 | 🟢 PASSED | < 1s |
| `test_key_splitting` | `tests.test_cryptography` | PRD-MDR-005, Trace-2 | 🟢 PASSED | < 1s |
| `test_cra_allocations_rbac_reassignment_workload` | `tests.test_ctms` | PRD-CTMS-003, Trace-6 | 🟢 PASSED | < 1s |
| `test_create_and_list_studies_rbac` | `tests.test_ctms` | PRD-CTMS-004, Trace-6 | 🟢 PASSED | < 1s |
| `test_ctms_health_check` | `tests.test_ctms` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_ctms_sync_conflict_merge` | `tests.test_ctms` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_ctms_sync_conflict_server_wins` | `tests.test_ctms` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_ctms_sync_happy_path_and_reloads` | `tests.test_ctms` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_ctms_sync_rbac_denial` | `tests.test_ctms` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_ctms_sync_structural_conflict` | `tests.test_ctms` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_database_manager_uninitialized` | `tests.test_ctms` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_get_audit_trail_rbac` | `tests.test_ctms` | PRD-CTMS-004, Trace-6 | 🟢 PASSED | < 1s |
| `test_grant_approve_sig_token_matrix` | `tests.test_ctms` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_grant_creation_rbac` | `tests.test_ctms` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_grant_locked_when_approved` | `tests.test_ctms` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_milestone_trigger_manual` | `tests.test_ctms` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_milestone_trigger_study_approved` | `tests.test_ctms` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_milestone_trigger_visit_completed_automated` | `tests.test_ctms` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_monitoring_visit_invalid_state_and_findings` | `tests.test_ctms` | PRD-CTMS-002, Trace-6 | 🟢 PASSED | < 1s |
| `test_monitoring_visit_scheduling_respects_cra_allocation` | `tests.test_ctms` | PRD-CTMS-003, Trace-6 | 🟢 PASSED | < 1s |
| `test_monitoring_visit_workflow_happy_path` | `tests.test_ctms` | PRD-CTMS-002, Trace-6 | 🟢 PASSED | < 1s |
| `test_monitoring_visit_workflow_rbac_denials` | `tests.test_ctms` | PRD-CTMS-002, Trace-6 | 🟢 PASSED | < 1s |
| `test_recruitment_records_crud_and_audit` | `tests.test_ctms` | PRD-CTMS-004, Trace-6 | 🟢 PASSED | < 1s |
| `test_site_milestones_crud_and_audit` | `tests.test_ctms` | PRD-CTMS-001, Trace-6 | 🟢 PASSED | < 1s |
| `test_data_lifecycle_protocol_amendment_traceability` | `tests.test_data_lifecycle_protocol_amendment_traceability` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_ctms_database_manager_uninitialized_and_close` | `tests.test_database_managers` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_econsent_database_manager_uninitialized_and_close` | `tests.test_database_managers` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_eisf_database_manager_uninitialized_and_close` | `tests.test_database_managers` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_etmf_database_manager_uninitialized_and_close` | `tests.test_database_managers` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_interop_database_manager_uninitialized_and_close` | `tests.test_database_managers` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_notifications_database_manager_uninitialized_and_close` | `tests.test_database_managers` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_serialize_bundle` | `tests.test_dataset_json` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_serialize_single_dataset_dm` | `tests.test_dataset_json` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_validation_success_on_valid_bundle` | `tests.test_dataset_json` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_validator_adam_referential_consistency_demographic_mismatch` | `tests.test_dataset_json` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_validator_adam_referential_consistency_missing_source_event` | `tests.test_dataset_json` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_validator_adam_referential_consistency_subject_not_in_adsl_or_dm` | `tests.test_dataset_json` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_validator_controlled_terminology_failures` | `tests.test_dataset_json` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_validator_duplicate_sequence_numbers` | `tests.test_dataset_json` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_validator_empty_studyid_usubjid` | `tests.test_dataset_json` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_validator_missing_required_variables` | `tests.test_dataset_json` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_validator_null_flavor_and_stat_reasnd_consistency` | `tests.test_dataset_json` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_validator_supp_dataset_linkage_and_structure` | `tests.test_dataset_json` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_basic_detection_results` | `tests.test_deid` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_cli_get_line_and_col` | `tests.test_deid` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_cli_load_gitignore_patterns` | `tests.test_deid` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_cli_main_bypass_comments_and_false_positives` | `tests.test_deid` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_cli_main_clean` | `tests.test_deid` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_cli_main_violation` | `tests.test_deid` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_cli_should_scan_file` | `tests.test_deid` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_compliance_profiles` | `tests.test_deid` | PRD-TMF-005 | 🟢 PASSED | < 1s |
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
| `test_apply_deid_transforms_right_to_left` | `tests.test_deid_transforms` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_cap_age_string` | `tests.test_deid_transforms` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_empty_reason_raises_validation_error` | `tests.test_deid_transforms` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_end_to_end_detector_and_transforms` | `tests.test_deid_transforms` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_pseudonymize_value_deterministic` | `tests.test_deid_transforms` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_redaction_manifest_asymmetric_tamper_evident` | `tests.test_deid_transforms` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_redaction_manifest_symmetric_tamper_evident` | `tests.test_deid_transforms` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_shift_date_string` | `tests.test_deid_transforms` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_age_capping_and_edge_cases` | `tests.test_deidentification` | PRD-TMF-005 | 🟢 PASSED | < 1s |
| `test_compliance_profiles` | `tests.test_deidentification` | PRD-TMF-005 | 🟢 PASSED | < 1s |
| `test_date_shifting_and_edge_cases` | `tests.test_deidentification` | PRD-TMF-005 | 🟢 PASSED | < 1s |
| `test_detections_all_categories` | `tests.test_deidentification` | PRD-TMF-005 | 🟢 PASSED | < 1s |
| `test_hmac_pseudonymization_determinism` | `tests.test_deidentification` | PRD-TMF-005 | 🟢 PASSED | < 1s |
| `test_manifest_tamper_evident_asymmetric` | `tests.test_deidentification` | PRD-TMF-005 | 🟢 PASSED | < 1s |
| `test_manifest_tamper_evident_symmetric` | `tests.test_deidentification` | PRD-TMF-005 | 🟢 PASSED | < 1s |
| `test_no_raw_matched_values_persisted` | `tests.test_deidentification` | PRD-TMF-005 | 🟢 PASSED | < 1s |
| `test_overlap_resolution_comprehensive` | `tests.test_deidentification` | PRD-TMF-005 | 🟢 PASSED | < 1s |
| `test_source_documents_remain_unchanged` | `tests.test_deidentification` | PRD-TMF-005 | 🟢 PASSED | < 1s |
| `test_transforms_all_strategies` | `tests.test_deidentification` | PRD-TMF-005 | 🟢 PASSED | < 1s |
| `test_delegation_allowed_non_pi_when_not_enforced` | `tests.test_delegation` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_delegation_denied_site_mismatch` | `tests.test_delegation` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_delegation_denied_sponsor_mismatch` | `tests.test_delegation` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_delegation_denied_when_not_pi` | `tests.test_delegation` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_delegation_malformed_role` | `tests.test_delegation` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_delegation_missing_delegator_context` | `tests.test_delegation` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_delegation_missing_target_context` | `tests.test_delegation` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_delegation_successful_pi_matching_scope` | `tests.test_delegation` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_delegation_target_from_body` | `tests.test_delegation` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_external_monitor_delegation_exclusion` | `tests.test_delegation` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_normalize_and_validate_staff_role_invalid` | `tests.test_delegation` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_normalize_and_validate_staff_role_valid` | `tests.test_delegation` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_validate_request_staff_roles_empty` | `tests.test_delegation` | *Regression/Helper* | 🟢 PASSED | < 1s |
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
| `test_api_round_trip_endpoint_internal_success` | `tests.test_designer_roundtrip` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_compare_payloads_lossless_equivalence` | `tests.test_designer_roundtrip` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_compare_payloads_lossy_mismatch` | `tests.test_designer_roundtrip` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_flatten_dict_complex` | `tests.test_designer_roundtrip` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_orchestrate_circular_skip_logic_lossy` | `tests.test_designer_roundtrip` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_orchestrate_internal_to_usdm_to_internal_lossless` | `tests.test_designer_roundtrip` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_orchestrate_stochastic_operator_lossy` | `tests.test_designer_roundtrip` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_orchestrate_usdm_to_internal_to_usdm_lossless` | `tests.test_designer_roundtrip` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_compiler_agreement_all_functions` | `tests.test_designer_rules` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_detect_circular_dependencies` | `tests.test_designer_rules` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_detect_unknown_fields` | `tests.test_designer_rules` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_invalid_comparison_arity` | `tests.test_designer_rules` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_invalid_indexed_repeat_arity_rejection` | `tests.test_designer_rules` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_invalid_is_empty_arity_rejection` | `tests.test_designer_rules` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_invalid_logical_not_arity` | `tests.test_designer_rules` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_invalid_skip_logic_schema_missing_fields` | `tests.test_designer_rules` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_map_study_to_usdm_with_rules` | `tests.test_designer_rules` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_neo4j_create_rule` | `tests.test_designer_rules` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_neo4j_delete_rule` | `tests.test_designer_rules` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_neo4j_get_rules` | `tests.test_designer_rules` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_neo4j_update_rule` | `tests.test_designer_rules` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_python_evaluator_indexed_repeat_and_arity_mismatch` | `tests.test_designer_rules` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_rules_auth_gateways` | `tests.test_designer_rules` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_rules_crud_endpoints` | `tests.test_designer_rules` | Trace-11 | 🟢 PASSED | < 1s |
| `test_valid_indexed_repeat_schema_and_compile` | `tests.test_designer_rules` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_valid_skip_logic_schema` | `tests.test_designer_rules` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_xpath_compile_logical_and_functions` | `tests.test_designer_rules` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_xpath_compile_simple` | `tests.test_designer_rules` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_version_diff_success` | `tests.test_designer_version_diff` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_version_diff_unrelated_or_nonexistent` | `tests.test_designer_version_diff` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_complete_doa_workflow_lifecycle` | `tests.test_doa_workflow` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_assignment_compliance_states_and_recalculations` | `tests.test_ecoa_coverage` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_instrument_retrieval_and_assignment_boundaries` | `tests.test_ecoa_coverage` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_notifications_lifecycle_reminders_and_acknowledgments` | `tests.test_ecoa_coverage` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_offline_submission_conflict_resolution_lifecycles` | `tests.test_ecoa_coverage` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_structural_conflict_on_missing_or_deleted_targets` | `tests.test_ecoa_coverage` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_subject_only_authorization_and_cross_subject_rejection` | `tests.test_ecoa_coverage` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_authoring_mutations_rejected_for_auditors` | `tests.test_econsent` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_clause_lifecycle_and_versioning_audit` | `tests.test_econsent` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_database_url_override_and_init` | `tests.test_econsent` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_econsent_database_schema_creation` | `tests.test_econsent` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_econsent_document_lifecycle_and_audit_context` | `tests.test_econsent` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_econsent_get_not_found` | `tests.test_econsent` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_econsent_health_check` | `tests.test_econsent` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_econsent_pydantic_schemas` | `tests.test_econsent` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_gateway_auth_middleware_denials` | `tests.test_econsent` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_shared_audit_fields_validation` | `tests.test_econsent` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_template_lifecycle_and_validation` | `tests.test_econsent` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_uninitialized_database_manager_econsent` | `tests.test_econsent` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_archival_status_endpoints` | `tests.test_econsent_archival` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_icf_sign_and_archival_queueing` | `tests.test_econsent_archival` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_poll_and_dispatch_failure_and_retry_backoff` | `tests.test_econsent_archival` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_poll_and_dispatch_success` | `tests.test_econsent_archival` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_append_only_audit_history` | `tests.test_econsent_capture` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_capture_rejections` | `tests.test_econsent_capture` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_execution_consumption_integration` | `tests.test_econsent_capture` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_happy_path_capture_and_status` | `tests.test_econsent_capture` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_signature_tamper_detection` | `tests.test_econsent_capture` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_auditor_restrictions_on_checks` | `tests.test_econsent_comprehension` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_create_and_retrieve_comprehension_check` | `tests.test_econsent_comprehension` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_signature_blocks_if_comprehension_checks_fail_or_incomplete` | `tests.test_econsent_comprehension` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_submit_answers_and_evaluation_boundaries` | `tests.test_econsent_comprehension` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_template_version_separation` | `tests.test_econsent_comprehension` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_approved_content_retrieval_and_cache` | `tests.test_econsent_translations` | Trace-10 | 🟢 PASSED | < 1s |
| `test_language_code_validation` | `tests.test_econsent_translations` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_translation_crud_and_validation` | `tests.test_econsent_translations` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_translation_status_workflow_and_rbac` | `tests.test_econsent_translations` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_authored_cross_form_rule_lifecycle` | `tests.test_edit_checks` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_authored_longitudinal_predecessor_handling` | `tests.test_edit_checks` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_cross_form_temporal_consistency_and_context_propagation` | `tests.test_edit_checks` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_deferred_predecessor_checks` | `tests.test_edit_checks` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_same_record_failure_outlier_and_auto_close` | `tests.test_edit_checks` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_scenario_cross_form_edit_checks_and_auto_resolve` | `tests.test_edit_checks_scenarios` | PRD-QRY-003 | 🟢 PASSED | < 1s |
| `test_scenario_longitudinal_predecessor_draft_and_complete` | `tests.test_edit_checks_scenarios` | PRD-QRY-004 | 🟢 PASSED | < 1s |
| `test_scenario_skip_logic_and_cascading_nullification` | `tests.test_edit_checks_scenarios` | PRD-EDC-003, PRD-EDC-004 | 🟢 PASSED | < 1s |
| `test_classify_incoming_document_changed_dict` | `tests.test_eisf_adapter` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_classify_incoming_document_changed_object` | `tests.test_eisf_adapter` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_classify_incoming_document_duplicate_dict` | `tests.test_eisf_adapter` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_classify_incoming_document_duplicate_object` | `tests.test_eisf_adapter` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_classify_incoming_document_new` | `tests.test_eisf_adapter` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_derive_correlation_key` | `tests.test_eisf_adapter` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_deterministic_bidirectional_mapping_success` | `tests.test_eisf_adapter` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_eisf_mappings_resolve_through_active_catalog` | `tests.test_eisf_adapter` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_eisf_resolve_known_extension_artifact` | `tests.test_eisf_adapter` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_eisf_reverse_mappings_resolve_through_active_catalog` | `tests.test_eisf_adapter` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_mapping_failures` | `tests.test_eisf_adapter` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_mapping_normalization` | `tests.test_eisf_adapter` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_eisf_auditor_write_forbidden` | `tests.test_eisf_api` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_eisf_document_cross_site_rejection_and_audit` | `tests.test_eisf_api` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_eisf_document_lifecycle_same_site` | `tests.test_eisf_api` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_eisf_documents_endpoint_blocks_unauthenticated` | `tests.test_eisf_api` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_eisf_health_unauthenticated` | `tests.test_eisf_api` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_eisf_auditor_view_and_download_permissions` | `tests.test_eisf_browse_completeness` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_eisf_completeness_workflow` | `tests.test_eisf_browse_completeness` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_eisf_document_listing_with_binder_filters` | `tests.test_eisf_browse_completeness` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_eisf_document_view_and_download_site_isolation` | `tests.test_eisf_browse_completeness` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_eisf_ingest_document_event_alias` | `tests.test_eisf_ingest` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_eisf_ingest_document_success` | `tests.test_eisf_ingest` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_eisf_ingest_missing_change_reason_fails` | `tests.test_eisf_ingest` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_database_url_override_and_init` | `tests.test_eisf_persistence` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_eisf_append_only_versions_and_deduplication` | `tests.test_eisf_persistence` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_eisf_document_creation_and_site_scoped` | `tests.test_eisf_persistence` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_eisf_part11_audit_log_retention` | `tests.test_eisf_persistence` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_uninitialized_database_manager_eisf` | `tests.test_eisf_persistence` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_eisf_completeness_site_isolation` | `tests.test_eisf_site_scope` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_eisf_external_monitor_role` | `tests.test_eisf_site_scope` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_eisf_site_scoped_users_read_isolation` | `tests.test_eisf_site_scope` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_eisf_site_scoped_write_restrictions` | `tests.test_eisf_site_scope` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_eisf_sponsor_admin_global_visibility` | `tests.test_eisf_site_scope` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_eisf_sync_conflict_client_wins` | `tests.test_eisf_sync` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_eisf_sync_conflict_merge_lexicographic_tiebreaker` | `tests.test_eisf_sync` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_eisf_sync_conflict_merge_lww_existing_wins` | `tests.test_eisf_sync` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_eisf_sync_conflict_merge_lww_incoming_wins` | `tests.test_eisf_sync` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_eisf_sync_conflict_server_wins` | `tests.test_eisf_sync` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_eisf_sync_creation_and_etmf_propagation` | `tests.test_eisf_sync` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_eisf_sync_echo_loop_prevention` | `tests.test_eisf_sync` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_eisf_sync_exact_duplicate_ignored` | `tests.test_eisf_sync` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_eisf_sync_per_field_metadata_lww` | `tests.test_eisf_sync` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_eisf_sync_unmapped_propagation` | `tests.test_eisf_sync` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_aggregate_eligibility_evaluation` | `tests.test_eligibility_engine` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_evaluation_all_operators` | `tests.test_eligibility_engine` | PRD-ELIGIBILITY-005 | 🟢 PASSED | < 1s |
| `test_evaluation_incompatible_types_graceful_handling` | `tests.test_eligibility_engine` | PRD-ELIGIBILITY-007 | 🟢 PASSED | < 1s |
| `test_evaluation_kleene_indeterminate_propagation` | `tests.test_eligibility_engine` | PRD-ELIGIBILITY-006 | 🟢 PASSED | < 1s |
| `test_parse_invalid_syntax` | `tests.test_eligibility_engine` | PRD-ELIGIBILITY-004 | 🟢 PASSED | < 1s |
| `test_parse_logical_and_nested_expressions` | `tests.test_eligibility_engine` | PRD-ELIGIBILITY-003 | 🟢 PASSED | < 1s |
| `test_parse_simple_expressions` | `tests.test_eligibility_engine` | PRD-ELIGIBILITY-002 | 🟢 PASSED | < 1s |
| `test_eligibility_criteria_crud_endpoints` | `tests.test_eligibility_mdr` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_eligibility_criteria_immutability` | `tests.test_eligibility_mdr` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_eligibility_criteria_usdm_projection` | `tests.test_eligibility_mdr` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_eligibility_criteria_validation_failures` | `tests.test_eligibility_mdr` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_unblind_missing_sig_token` | `tests.test_emergency_unblinding` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_unblind_screening_status_error` | `tests.test_emergency_unblinding` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_unblind_subject_not_found` | `tests.test_emergency_unblinding` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_unblind_success_authorized_access` | `tests.test_emergency_unblinding` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_unblind_success_masked_access` | `tests.test_emergency_unblinding` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_unblind_withdrawn_status_error` | `tests.test_emergency_unblinding` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_encryption_roundtrip` | `tests.test_encryption` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_encryption_tamper_rejection` | `tests.test_encryption` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_hkdf_determinism` | `tests.test_encryption` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_rejection_of_invalid_key_material` | `tests.test_encryption` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_archived_document_retrieval_and_immutability` | `tests.test_etmf` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_automated_ingestion_and_version_indexing` | `tests.test_etmf` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_canonical_catalog_ingestion_validations` | `tests.test_etmf` | PRD-TMF-002, PRD-TMF-003, Trace-5 | 🟢 PASSED | < 1s |
| `test_completeness_checking_transitions` | `tests.test_etmf` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_completeness_from_catalog` | `tests.test_etmf` | PRD-TMF-004 | 🟢 PASSED | < 1s |
| `test_completeness_from_catalog_across_versions` | `tests.test_etmf` | PRD-TMF-004 | 🟢 PASSED | < 1s |
| `test_deterministic_and_complete_binder_export` | `tests.test_etmf` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_edl_definitions_and_crud` | `tests.test_etmf` | PRD-EDL-001, Trace-4 | 🟢 PASSED | < 1s |
| `test_etmf_audit_logs_filtering_and_pagination` | `tests.test_etmf` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_etmf_edge_cases_for_coverage` | `tests.test_etmf` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_etmf_qc_lifecycle_and_audit` | `tests.test_etmf` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_explicit_and_default_taxonomy_version_roundtrip_and_legacy_interpretability` | `tests.test_etmf` | PRD-TMF-003 | 🟢 PASSED | < 1s |
| `test_informed_consent_form_taxonomy_and_idempotency` | `tests.test_etmf` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_inspector_portal_read_only_access_limits` | `tests.test_etmf` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_ordered_artifact_history_endpoint` | `tests.test_etmf` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_placeholder_scripts` | `tests.test_etmf` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_protocol_versioning_and_change_justification_ingestion` | `tests.test_etmf` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_qualify_catalog_cutover_and_extension_persistence` | `tests.test_etmf` | PRD-TMF-002, Trace-5 | 🟢 PASSED | < 1s |
| `test_regulatory_binder_export` | `tests.test_etmf` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_service_caller_ingestion_immutability_violation` | `tests.test_etmf` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_service_caller_ingestion_rollback_on_failure` | `tests.test_etmf` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_service_caller_ingestion_success` | `tests.test_etmf` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_site_aware_completeness` | `tests.test_etmf` | PRD-EDL-001, Trace-4 | 🟢 PASSED | < 1s |
| `test_tmf_taxonomy_mapping` | `tests.test_etmf` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_ucum_extra_coverage` | `tests.test_etmf` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_uninitialized_database_manager` | `tests.test_etmf` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_view_download_audit_logging` | `tests.test_etmf` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_watermarked_document_viewing_and_download` | `tests.test_etmf` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_document_version_history_lineage` | `tests.test_etmf_binder_structure_and_history` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_empty_binder_structure` | `tests.test_etmf_binder_structure_and_history` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_partial_binder_structure` | `tests.test_etmf_binder_structure_and_history` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_versions_404_not_found` | `tests.test_etmf_binder_structure_and_history` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_bulk_archival_all_or_nothing_rollback` | `tests.test_etmf_bulk_archival` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_bulk_archival_authorization_and_rejections` | `tests.test_etmf_bulk_archival` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_bulk_archival_partial_success` | `tests.test_etmf_bulk_archival` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_bulk_archival_repeating_safe_and_observable` | `tests.test_etmf_bulk_archival` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_bulk_archival_successful_progression` | `tests.test_etmf_bulk_archival` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_actual_cryptographic_verification` | `tests.test_etmf_compliance` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_audit_logs_group_sealing_and_chaining` | `tests.test_etmf_compliance` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_background_sealer_lifecycle` | `tests.test_etmf_compliance` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_missing_and_invalid_signature_ingestion` | `tests.test_etmf_compliance` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_mock_signature_bypass` | `tests.test_etmf_compliance` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_signature_extraction_formats` | `tests.test_etmf_compliance` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_signature_requirement_rules` | `tests.test_etmf_compliance` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_tampering_detection_and_lockout_propagation` | `tests.test_etmf_compliance` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_eisf_create_authorized_vs_unauthorized` | `tests.test_etmf_eisf_expiration_metadata` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_eisf_creation_date_validation_rejected` | `tests.test_etmf_eisf_expiration_metadata` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_eisf_expiration_update_authorized_vs_unauthorized` | `tests.test_etmf_eisf_expiration_metadata` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_eisf_update_date_validation_rejected` | `tests.test_etmf_eisf_expiration_metadata` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_etmf_expiration_update_authorized_vs_unauthorized` | `tests.test_etmf_eisf_expiration_metadata` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_etmf_expiration_update_date_validation_rejected` | `tests.test_etmf_eisf_expiration_metadata` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_etmf_ingest_authorized_vs_unauthorized` | `tests.test_etmf_eisf_expiration_metadata` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_etmf_ingest_date_validation_rejected` | `tests.test_etmf_eisf_expiration_metadata` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_manage_expiration_rbac_permissions` | `tests.test_etmf_eisf_expiration_metadata` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_migration_adds_expiration_columns_idempotently` | `tests.test_etmf_eisf_expiration_metadata` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_audit_attribution` | `tests.test_etmf_expiration_scanner` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_determine_warning_window` | `tests.test_etmf_expiration_scanner` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_execute_expiration_scan_cycle_thresholds` | `tests.test_etmf_expiration_scanner` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_failure_isolation_and_resilience` | `tests.test_etmf_expiration_scanner` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_scanner_idempotency_restart_and_rearming` | `tests.test_etmf_expiration_scanner` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_scanner_shutdown_cancellation` | `tests.test_etmf_expiration_scanner` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_idempotency` | `tests.test_etmf_inbound_email` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_immutability_violation_inbound_email` | `tests.test_etmf_inbound_email` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_invalid_signature_rejection` | `tests.test_etmf_inbound_email` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_multi_attachment_ingestion` | `tests.test_etmf_inbound_email` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_oversized_payload_rejection` | `tests.test_etmf_inbound_email` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_replay_protection` | `tests.test_etmf_inbound_email` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_stale_timestamp_rejection` | `tests.test_etmf_inbound_email` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_unresolvable_recipient_address` | `tests.test_etmf_inbound_email` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_valid_inbound_email_ingestion` | `tests.test_etmf_inbound_email` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_trigger_global_trial_lock` | `tests.test_etmf_lock_integration` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_verify_trial_lock_status_error` | `tests.test_etmf_lock_integration` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_verify_trial_lock_status_locked` | `tests.test_etmf_lock_integration` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_verify_trial_lock_status_unlocked` | `tests.test_etmf_lock_integration` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_append_only_transition_history` | `tests.test_etmf_qc` | PRD-QC-005 | 🟢 PASSED | < 1s |
| `test_invalid_status_transition_raises_error` | `tests.test_etmf_qc` | PRD-QC-002 | 🟢 PASSED | < 1s |
| `test_new_document_defaults_to_draft` | `tests.test_etmf_qc` | PRD-QC-001 | 🟢 PASSED | < 1s |
| `test_part11_change_reason_enforcement` | `tests.test_etmf_qc` | PRD-QC-004 | 🟢 PASSED | < 1s |
| `test_qc_history_api_and_audit` | `tests.test_etmf_qc` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_qc_history_api_not_found` | `tests.test_etmf_qc` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_qc_transitions_missing_doc` | `tests.test_etmf_qc` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_role_based_access_controls_and_gates` | `tests.test_etmf_qc` | PRD-QC-003 | 🟢 PASSED | < 1s |
| `test_migration_clean_path` | `tests.test_etmf_qc_invariants` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_migration_upgrade_and_backfill_path` | `tests.test_etmf_qc_invariants` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_automated_redaction_basic` | `tests.test_etmf_redaction` | PRD-TMF-005 | 🟢 PASSED | < 1s |
| `test_automated_redaction_errors` | `tests.test_etmf_redaction` | PRD-TMF-005 | 🟢 PASSED | < 1s |
| `test_automated_redaction_profile_scopes` | `tests.test_etmf_redaction` | PRD-TMF-005 | 🟢 PASSED | < 1s |
| `test_automated_redaction_trial_locked` | `tests.test_etmf_redaction` | PRD-TMF-005 | 🟢 PASSED | < 1s |
| `test_manual_redaction_authorization_and_lock` | `tests.test_etmf_redaction` | PRD-TMF-005 | 🟢 PASSED | < 1s |
| `test_manual_redaction_literal_escaping` | `tests.test_etmf_redaction` | PRD-TMF-005 | 🟢 PASSED | < 1s |
| `test_manual_redaction_span_validation` | `tests.test_etmf_redaction` | PRD-TMF-005 | 🟢 PASSED | < 1s |
| `test_manual_redaction_success` | `tests.test_etmf_redaction` | PRD-TMF-005 | 🟢 PASSED | < 1s |
| `test_redaction_audit_trail_and_provenance` | `tests.test_etmf_redaction` | PRD-TMF-005 | 🟢 PASSED | < 1s |
| `test_redaction_authorization_gates` | `tests.test_etmf_redaction` | PRD-TMF-005 | 🟢 PASSED | < 1s |
| `test_completeness_signature_lifecycle_distinction` | `tests.test_etmf_signatures` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_signature_document_routing_and_classification` | `tests.test_etmf_signatures` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_signature_lifecycle_with_mock_signature` | `tests.test_etmf_signatures` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_etmf_post_signature_locking` | `tests.test_etmf_signing_lifecycle` | Trace-13 | 🟢 PASSED | < 1s |
| `test_etmf_signing_happy_path` | `tests.test_etmf_signing_lifecycle` | Trace-13 | 🟢 PASSED | < 1s |
| `test_etmf_signing_reauth_failures` | `tests.test_etmf_signing_lifecycle` | Trace-13 | 🟢 PASSED | < 1s |
| `test_auto_quarantine_site_level_no_site_id` | `tests.test_etmf_site_scope` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_binder_export_redaction_representation_policy` | `tests.test_etmf_site_scope` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_completeness_site_isolation` | `tests.test_etmf_site_scope` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_eisf_to_etmf_sync_preserves_scope` | `tests.test_etmf_site_scope` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_is_site_level_artifact_helper` | `tests.test_etmf_site_scope` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_legacy_records_quarantine_policy` | `tests.test_etmf_site_scope` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_raw_original_suppression_without_read_raw` | `tests.test_etmf_site_scope` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_regulatory_binder_export_site_isolation` | `tests.test_etmf_site_scope` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_site_id_validation_empty_whitespace` | `tests.test_etmf_site_scope` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_site_scoped_cannot_read_study_level_or_quarantined_documents` | `tests.test_etmf_site_scope` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_site_scoped_no_assigned_sites_fail_closed` | `tests.test_etmf_site_scope` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_site_scoped_users_read_isolation` | `tests.test_etmf_site_scope` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_site_scoped_write_restrictions` | `tests.test_etmf_site_scope` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_site_scoping_on_redactions_and_signatures` | `tests.test_etmf_site_scope` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_unauthorized_role_denied_on_all_paths` | `tests.test_etmf_site_scope` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_eisf_to_etmf_e2e_boundaries` | `tests.test_etmf_sync_provenance` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_redaction_derivative_safety` | `tests.test_etmf_sync_provenance` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_sealer_retains_and_validates_reason_for_change` | `tests.test_etmf_sync_provenance` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_arithmetic_null_safety_and_bmi` | `tests.test_evaluator` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_comparison_null_semantics` | `tests.test_evaluator` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_comparison_operators` | `tests.test_evaluator` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_field_reference_and_xpath` | `tests.test_evaluator` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_indexed_repeat` | `tests.test_evaluator` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_is_empty_and_not_empty` | `tests.test_evaluator` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_literal_and_constant` | `tests.test_evaluator` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_client_configuration_env_vars` | `tests.test_evs_client` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_client_configuration_overrides` | `tests.test_evs_client` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_get_concept_http_status_error_404` | `tests.test_evs_client` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_get_concept_invalid_json` | `tests.test_evs_client` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_get_concept_invalid_via_400` | `tests.test_evs_client` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_get_concept_invalid_via_422_not_found` | `tests.test_evs_client` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_get_concept_not_found` | `tests.test_evs_client` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_get_concept_server_error_500` | `tests.test_evs_client` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_get_concept_success` | `tests.test_evs_client` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_get_concept_timeout` | `tests.test_evs_client` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_get_concept_transport_error` | `tests.test_evs_client` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_import_does_not_make_network_calls` | `tests.test_evs_client` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_normalize_concept_edge_cases` | `tests.test_evs_client` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_search_concepts_invalid_json` | `tests.test_evs_client` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_search_concepts_list_shape` | `tests.test_evs_client` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_search_concepts_success` | `tests.test_evs_client` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_search_concepts_timeout` | `tests.test_evs_client` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_search_concepts_transport_error` | `tests.test_evs_client` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_designer_criteria_client_retrieval_and_parsing` | `tests.test_execution_eligibility` | PRD-ELIGIBILITY-009 | 🟢 PASSED | < 1s |
| `test_ecrf_context_builder_demographics_and_precedence` | `tests.test_execution_eligibility` | PRD-ELIGIBILITY-010 | 🟢 PASSED | < 1s |
| `test_ecrf_context_builder_kleene_absent_semantics` | `tests.test_execution_eligibility` | PRD-ELIGIBILITY-011 | 🟢 PASSED | < 1s |
| `test_randomization_allocation_rejection_gate` | `tests.test_execution_eligibility` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_screening_endpoint_eligible_and_transition` | `tests.test_execution_eligibility` | PRD-ELIGIBILITY-012 | 🟢 PASSED | < 1s |
| `test_screening_endpoint_indeterminate_behavior` | `tests.test_execution_eligibility` | PRD-ELIGIBILITY-014 | 🟢 PASSED | < 1s |
| `test_screening_endpoint_ineligible_transition_and_audit` | `tests.test_execution_eligibility` | PRD-ELIGIBILITY-013 | 🟢 PASSED | < 1s |
| `test_form_submission_approval_audit_manifestation` | `tests.test_form_submissions` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_form_submission_audit_logging` | `tests.test_form_submissions` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_form_submission_invalid_transitions` | `tests.test_form_submissions` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_form_submission_lifecycle_happy_path` | `tests.test_form_submissions` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_form_submission_locks` | `tests.test_form_submissions` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_form_submission_validation` | `tests.test_form_submissions` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_eisf_gateway_site_isolation_propagation` | `tests.test_gateway` | Trace-16 | 🟢 PASSED | < 1s |
| `test_gateway_bearer_only_subject_routing_and_header_enforcement` | `tests.test_gateway` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_gateway_cors_headers` | `tests.test_gateway` | PRD-UNI-001 | 🟢 PASSED | < 1s |
| `test_gateway_proxy_eisf_headers_propagation` | `tests.test_gateway` | Trace-16 | 🟢 PASSED | < 1s |
| `test_gateway_rate_limiting` | `tests.test_gateway` | PRD-UNI-001 | 🟢 PASSED | < 1s |
| `test_gateway_scope_extraction_and_verification_integrity` | `tests.test_gateway` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_gateway_semantic_action_issuance_and_enforcement` | `tests.test_gateway` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_gateway_sponsor_claim_extraction` | `tests.test_gateway` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_gateway_startup_development_with_bypass_configs` | `tests.test_gateway` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_gateway_startup_production_no_bypass_configs` | `tests.test_gateway` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_gateway_startup_production_with_skip_jwks` | `tests.test_gateway` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_gateway_startup_production_with_test_secret` | `tests.test_gateway` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_gateway_startup_production_with_unverified_jwt` | `tests.test_gateway` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_gateway_subject_role_routing_restrictions` | `tests.test_gateway` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_gateway_tenant_claim_extraction` | `tests.test_gateway` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_gateway_tenant_spoofing_prevention` | `tests.test_gateway` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_generate_signature` | `tests.test_gateway` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_generate_signature_v2` | `tests.test_gateway` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_get_openapi_json` | `tests.test_gateway` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_get_openapi_json_error` | `tests.test_gateway` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_get_swagger_ui` | `tests.test_gateway` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_proxy_requests_change_reason_too_long` | `tests.test_gateway` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_proxy_requests_invalid_auth` | `tests.test_gateway` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_proxy_requests_no_auth` | `tests.test_gateway` | PRD-UNI-001 | 🟢 PASSED | < 1s |
| `test_proxy_requests_paths` | `tests.test_gateway` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_proxy_requests_terminology_paths` | `tests.test_gateway` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_proxy_requests_v2_headers` | `tests.test_gateway` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_proxy_requests_valid_auth` | `tests.test_gateway` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_signature_gated_mutation_enforcement` | `tests.test_gateway` | Trace-15 | 🟢 PASSED | < 1s |
| `test_signature_gated_mutation_expired_token` | `tests.test_gateway` | Trace-15 | 🟢 PASSED | < 1s |
| `test_signature_gated_mutation_mismatched_action` | `tests.test_gateway` | Trace-15 | 🟢 PASSED | < 1s |
| `test_signature_token_altered_signature_rejected` | `tests.test_gateway` | Trace-15 | 🟢 PASSED | < 1s |
| `test_signature_token_credentials_not_logged_or_returned` | `tests.test_gateway` | Trace-15 | 🟢 PASSED | < 1s |
| `test_signature_verification_invalid_credentials` | `tests.test_gateway` | Trace-15 | 🟢 PASSED | < 1s |
| `test_signature_verification_role_insufficient` | `tests.test_gateway` | Trace-15 | 🟢 PASSED | < 1s |
| `test_signature_verification_study_designer_role_allowed` | `tests.test_gateway` | Trace-15 | 🟢 PASSED | < 1s |
| `test_signature_verification_success` | `tests.test_gateway` | Trace-15 | 🟢 PASSED | < 1s |
| `test_signature_verification_with_batch_id` | `tests.test_gateway` | Trace-15 | 🟢 PASSED | < 1s |
| `test_verify_token_invalid` | `tests.test_gateway` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_invalid_data_element_default_unit_fails` | `tests.test_global_library` | PRD-MDR-001 | 🟢 PASSED | < 1s |
| `test_invalid_mismatched_type_payload_fails` | `tests.test_global_library` | PRD-MDR-001 | 🟢 PASSED | < 1s |
| `test_mutation_creation_requires_non_empty_change_reason` | `tests.test_global_library` | PRD-MDR-001 | 🟢 PASSED | < 1s |
| `test_mutation_update_requires_non_empty_reason_for_change` | `tests.test_global_library` | PRD-MDR-001 | 🟢 PASSED | < 1s |
| `test_valid_arm_detail_validation` | `tests.test_global_library` | PRD-MDR-001 | 🟢 PASSED | < 1s |
| `test_valid_data_element_detail_validation` | `tests.test_global_library` | PRD-MDR-001 | 🟢 PASSED | < 1s |
| `test_valid_form_detail_validation` | `tests.test_global_library` | PRD-MDR-001 | 🟢 PASSED | < 1s |
| `test_valid_visit_detail_validation` | `tests.test_global_library` | PRD-MDR-001 | 🟢 PASSED | < 1s |
| `test_auth_and_malformed_requests` | `tests.test_global_library_api` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_create_and_retrieve_library_objects` | `tests.test_global_library_api` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_global_library_governance_lifecycle_transitions` | `tests.test_global_library_api` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_instantiate_library_object_cross_sponsor_rejected` | `tests.test_global_library_api` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_instantiate_library_object_inaccessible_study` | `tests.test_global_library_api` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_instantiate_library_object_success` | `tests.test_global_library_api` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_library_instance_updates_and_inheritance_diffs` | `tests.test_global_library_api` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_library_object_in_use_and_amendments` | `tests.test_global_library_api` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_sponsor_security_boundaries` | `tests.test_global_library_api` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_stripe_style_pagination_and_filtering` | `tests.test_global_library_api` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_update_and_history_versioning` | `tests.test_global_library_api` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_mock_flow_library_version_chain_and_immutability` | `tests.test_global_library_neo4j` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_mock_list_filtering_and_pagination` | `tests.test_global_library_neo4j` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_neo4j_library_object_version_chain_queries` | `tests.test_global_library_neo4j` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_absent_and_malformed_roles` | `tests.test_granular_locks_api` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_allowed_roles_matrix` | `tests.test_granular_locks_api` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_forbidden_roles_matrix` | `tests.test_granular_locks_api` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_form_lock_and_unlock_lifecycle` | `tests.test_granular_locks_api` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_gateway_bypass_prevention` | `tests.test_granular_locks_api` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_lock_status_retrieval` | `tests.test_granular_locks_api` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_locked_write_prevention` | `tests.test_granular_locks_api` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_roles_authorization_restrictions` | `tests.test_granular_locks_api` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_site_lock_and_unlock_lifecycle` | `tests.test_granular_locks_api` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_subject_lock_and_unlock_lifecycle` | `tests.test_granular_locks_api` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_trial_lock_and_unlock_lifecycle` | `tests.test_granular_locks_api` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_visit_lock_and_unlock_lifecycle` | `tests.test_granular_locks_api` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_bulk_offline_sync` | `tests.test_interop` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_compute_reminders_all_subjects_staff` | `tests.test_interop` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_compute_reminders_by_subject_and_end_date_branch` | `tests.test_interop` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_deliver_notification_task_exception` | `tests.test_interop` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_epro_submission_and_conflict_resolution` | `tests.test_interop` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_fhir_prefill_bundle_pipeline` | `tests.test_interop` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_foreign_key_and_cascade_lifecycle_integrity` | `tests.test_interop` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_instrument_and_assignment_endpoints_and_auditing` | `tests.test_interop` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_instrument_and_assignment_orm_persistence` | `tests.test_interop` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_notifications_and_reminders_lifecycle` | `tests.test_interop` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_pseudonymization_and_pii_stripping` | `tests.test_interop` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_subject_content_submission_and_compliance_apis` | `tests.test_interop` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_subject_role_authorization_and_identity_binding` | `tests.test_interop` | Trace-8 | 🟢 PASSED | < 1s |
| `test_bulk_sync_with_valid_signatures_and_tallies` | `tests.test_interop_defeated` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_defeated_record_persistence_on_conflicts` | `tests.test_interop_defeated` | Trace-9 | 🟢 PASSED | < 1s |
| `test_structural_conflict_on_missing_target` | `tests.test_interop_defeated` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_submit_with_invalid_signature_fails` | `tests.test_interop_defeated` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_submit_with_valid_signature` | `tests.test_interop_defeated` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_build_ecrf_context_mapping` | `tests.test_interop_prescreen` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_build_ecrf_context_multiple_and_missing` | `tests.test_interop_prescreen` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_no_edc_mutation_boundary` | `tests.test_interop_prescreen` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_pre_screen_audit_evidence_non_phi` | `tests.test_interop_prescreen` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_pre_screen_eligible` | `tests.test_interop_prescreen` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_pre_screen_indeterminate` | `tests.test_interop_prescreen` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_pre_screen_ineligible` | `tests.test_interop_prescreen` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_circular_skip_logic_rules_raises_value_error` | `tests.test_inverse_mapper` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_inverse_mapping_valid_round_trip` | `tests.test_inverse_mapper` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_missing_required_fields_raises_value_error` | `tests.test_inverse_mapper` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_resolve_concept_id` | `tests.test_inverse_mapper` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_unmapped_fields_preservation` | `tests.test_inverse_mapper` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_unsupported_rule_expression_raises_value_error` | `tests.test_inverse_mapper` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_absent_boundaries` | `tests.test_lab_ranges` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_age_boundaries` | `tests.test_lab_ranges` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_critical_boundaries_and_exclusion` | `tests.test_lab_ranges` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_deterministic_ties` | `tests.test_lab_ranges` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_is_deleted_filtering` | `tests.test_lab_ranges` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_no_matching_rule_behavior` | `tests.test_lab_ranges` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_normal_boundaries_and_inclusion` | `tests.test_lab_ranges` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_sex_and_all_fallback` | `tests.test_lab_ranges` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_site_and_source_precedence` | `tests.test_lab_ranges` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_tie_breaking_with_none_bounds` | `tests.test_lab_ranges` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_unit_matching` | `tests.test_lab_ranges` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_create_central_range_with_null_site_id_allowed` | `tests.test_lab_ranges_crud` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_create_central_range_with_site_id_blocked` | `tests.test_lab_ranges_crud` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_create_lab_reference_range_success` | `tests.test_lab_ranges_crud` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_create_lab_reference_range_unauthorized` | `tests.test_lab_ranges_crud` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_create_lab_reference_range_validation_errors` | `tests.test_lab_ranges_crud` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_get_and_update_lab_reference_range` | `tests.test_lab_ranges_crud` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_list_and_filter_lab_reference_ranges` | `tests.test_lab_ranges_crud` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_soft_delete_lab_reference_range` | `tests.test_lab_ranges_crud` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_update_local_to_central_invariant_enforcement` | `tests.test_lab_ranges_crud` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_lab_ranges_comprehensive_e2e_workflow` | `tests.test_lab_ranges_e2e_verification` | PRD-LAB-001 | 🟢 PASSED | < 1s |
| `test_lab_range_evaluation_and_recalculation_gxp` | `tests.test_lab_ranges_recalculate` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_lab_range_recalculation_authorized_data_manager` | `tests.test_lab_ranges_recalculate` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_lab_range_recalculation_blank_reason` | `tests.test_lab_ranges_recalculate` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_lab_range_recalculation_missing_reason` | `tests.test_lab_ranges_recalculate` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_lab_range_recalculation_no_match` | `tests.test_lab_ranges_recalculate` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_lab_range_recalculation_unauthorized_role` | `tests.test_lab_ranges_recalculate` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_clinical_observation_extended_fields` | `tests.test_lab_reference_range_persistence` | PRD-LAB-001 | 🟢 PASSED | < 1s |
| `test_lab_reference_range_audit_and_triggers` | `tests.test_lab_reference_range_persistence` | PRD-LAB-001 | 🟢 PASSED | < 1s |
| `test_lab_reference_range_crud_and_precision` | `tests.test_lab_reference_range_persistence` | PRD-LAB-001 | 🟢 PASSED | < 1s |
| `test_schema_evolution_migration_upgrade` | `tests.test_lab_reference_range_persistence` | PRD-LAB-001, PRD-QRY-005 | 🟢 PASSED | < 1s |
| `test_layout_validation_integration` | `tests.test_layout_validator` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_layout_validation_invisible` | `tests.test_layout_validator` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_layout_validation_overlap` | `tests.test_layout_validator` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_layout_validation_scrambled_sequence` | `tests.test_layout_validator` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_layout_validation_valid` | `tests.test_layout_validator` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_ledger_sealing_and_validation` | `tests.test_ledger_and_triggers` | PRD-SYS-003 | 🟢 PASSED | < 1s |
| `test_out_of_band_update_triggers_audit_entry` | `tests.test_ledger_and_triggers` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_prevent_audit_ledger_seals_mutation` | `tests.test_ledger_and_triggers` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_prevent_audit_log_mutation` | `tests.test_ledger_and_triggers` | PRD-SYS-001, Trace-1 | 🟢 PASSED | < 1s |
| `test_prevent_hard_delete_on_audited_model` | `tests.test_ledger_and_triggers` | PRD-SYS-002, Trace-1 | 🟢 PASSED | < 1s |
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
| `test_func` | `tests.test_markdown_validator` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_html_comment_filtering` | `tests.test_markdown_validator` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_is_potential_path_ref` | `tests.test_markdown_validator` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_json_block_validation` | `tests.test_markdown_validator` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_main_with_arguments` | `tests.test_markdown_validator` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_process_markdown_file_e2e` | `tests.test_markdown_validator` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_python_block_validation` | `tests.test_markdown_validator` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_reference_style_link_validation` | `tests.test_markdown_validator` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_resolve_path` | `tests.test_markdown_validator` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_skip_and_raw_text_flags` | `tests.test_markdown_validator` | *Regression/Helper* | 🟢 PASSED | < 1s |
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
| `test_import_failure_rollback_and_failed_state` | `tests.test_medical_coding` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_import_invalid_layout_rejected` | `tests.test_medical_coding` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_import_unauthorized_roles_forbidden` | `tests.test_medical_coding` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_import_unsupported_dictionary_rejected` | `tests.test_medical_coding` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_lookup_and_indexes` | `tests.test_medical_coding` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_lookup_endpoints_validation_errors` | `tests.test_medical_coding` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_meddra_import_happy_path` | `tests.test_medical_coding` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_meddra_lookup_endpoint_happy_path` | `tests.test_medical_coding` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_meddra_term_unique_constraint` | `tests.test_medical_coding` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_whodrug_import_happy_path` | `tests.test_medical_coding` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_whodrug_lookup_endpoint_happy_path` | `tests.test_medical_coding` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_whodrug_record_unique_constraint` | `tests.test_medical_coding` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_audit_relevant_workflows` | `tests.test_medical_coding_engine` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_cache_behavior_and_degradation` | `tests.test_medical_coding_engine` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_coding_transitions` | `tests.test_medical_coding_engine` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_dictionary_version_isolation` | `tests.test_medical_coding_engine` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_import_auth_and_job_status` | `tests.test_medical_coding_engine` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_lookups_endpoints` | `tests.test_medical_coding_engine` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_matcher_normalization_and_scoring_thresholds` | `tests.test_medical_coding_engine` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_override_reason_validation` | `tests.test_medical_coding_engine` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_parser_fixtures` | `tests.test_medical_coding_engine` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_uncodable_query_generation_and_pii_isolation` | `tests.test_medical_coding_engine` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_upversioning_ledger_outcomes` | `tests.test_medical_coding_engine` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_impact_analysis_meddra_and_whodrug_lifecycle` | `tests.test_medical_coding_impact` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_auto_coding_on_observation_creation` | `tests.test_medical_coding_lifecycle` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_coder_action_accept_and_override_lifecycle` | `tests.test_medical_coding_lifecycle` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_mid_confidence_persists_as_suggestions` | `tests.test_medical_coding_lifecycle` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_cache_aside_and_stale_fallback` | `tests.test_medical_coding_matcher` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_cache_degradation_and_stale_on_error` | `tests.test_medical_coding_matcher` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_cache_ttl_configuration` | `tests.test_medical_coding_matcher` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_cache_unavailability_graceful_degradation` | `tests.test_medical_coding_matcher` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_meddra_matching_integration` | `tests.test_medical_coding_matcher` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_normalize_term` | `tests.test_medical_coding_matcher` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_similarity_computations` | `tests.test_medical_coding_matcher` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_stem_word` | `tests.test_medical_coding_matcher` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_token_cosine_similarity_empty` | `tests.test_medical_coding_matcher` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_whodrug_matching_integration` | `tests.test_medical_coding_matcher` | *Regression/Helper* | 🟢 PASSED | < 1s |
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
| `test_doa_signoff_automatic_archival_handoff` | `tests.test_org_integration_e2e` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_doa_signoff_tampered_payload_rejected` | `tests.test_org_integration_e2e` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_eisf_completeness_participation` | `tests.test_org_integration_e2e` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_gateway_openapi_aggregation_with_org` | `tests.test_org_integration_e2e` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_gateway_org_proxy_routing` | `tests.test_org_integration_e2e` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_cro_affiliation_validation` | `tests.test_org_service` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_delegation_of_authority_flow` | `tests.test_org_service` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_gxp_audit_logging_and_actor_context` | `tests.test_org_service` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_health_endpoint` | `tests.test_org_service` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_org_audit_log_append_only` | `tests.test_org_service` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_organization_and_site_relationship` | `tests.test_org_service` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_organization_crud_api` | `tests.test_org_service` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_personnel_and_sitestaff_alias` | `tests.test_org_service` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_personnel_assignments_crud` | `tests.test_org_service` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_personnel_crud_api` | `tests.test_org_service` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_resolve_assignments_endpoint` | `tests.test_org_service` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_site_crud_api` | `tests.test_org_service` | *Regression/Helper* | 🟢 PASSED | < 1s |
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
| `test_traceability_outcome_handling` | `tests.test_pr_comment` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_clinical_capture_provenance_and_version_stamping` | `tests.test_protocol_amendments_validation_suite` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_designer_amendment_immutability_and_race_safety` | `tests.test_protocol_amendments_validation_suite` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_designer_amendment_signature_validation` | `tests.test_protocol_amendments_validation_suite` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_etmf_document_change_rationale_mandatory_rules` | `tests.test_protocol_amendments_validation_suite` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_etmf_linkage_and_version_history_lineage` | `tests.test_protocol_amendments_validation_suite` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_etmf_qc_transitions_immutability` | `tests.test_protocol_amendments_validation_suite` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_exact_version_consent_and_reconsent_gating` | `tests.test_protocol_amendments_validation_suite` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_non_destructive_reconciliation_and_multi_hop` | `tests.test_protocol_amendments_validation_suite` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_api_block_crud_with_rbac` | `tests.test_protocol_blocks` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_arm_aware_soa_matrix_projection` | `tests.test_protocol_blocks` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_block_persistence_lifecycle` | `tests.test_protocol_blocks` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_canonical_ich_skeleton` | `tests.test_protocol_blocks` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_immutability_guard_rejects_locked_block_writes` | `tests.test_protocol_blocks` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_protocol_block_parenting` | `tests.test_protocol_blocks` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_protocol_block_validation` | `tests.test_protocol_blocks` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_reorder_blocks` | `tests.test_protocol_blocks` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_selective_lineage_propagation` | `tests.test_protocol_blocks` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_usdm_block_round_trip` | `tests.test_protocol_blocks` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_api_section_collaboration_gates` | `tests.test_protocol_collaboration` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_block_mutation_locks_enforcement` | `tests.test_protocol_collaboration` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_comments_and_threads_lifecycle` | `tests.test_protocol_collaboration` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_section_review_transitions_lifecycle` | `tests.test_protocol_collaboration` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_suggestions_decision_and_stale_rejection` | `tests.test_protocol_collaboration` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_build_docx_template` | `tests.test_protocol_export` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_export_protocol_as_docx_success` | `tests.test_protocol_export` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_export_protocol_as_pdf_success` | `tests.test_protocol_export` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_export_protocol_etmf_forwarding_best_effort` | `tests.test_protocol_export` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_export_protocol_etmf_forwarding_strict_failure` | `tests.test_protocol_export` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_export_protocol_generation_auditing` | `tests.test_protocol_export` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_export_protocol_invalid_output` | `tests.test_protocol_export` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_export_protocol_not_found` | `tests.test_protocol_export` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_export_protocol_outputs_rendering` | `tests.test_protocol_export` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_export_protocol_template_unavailable_integration` | `tests.test_protocol_export` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_export_protocol_unauthenticated` | `tests.test_protocol_export` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_export_protocol_unauthorized_empty_roles` | `tests.test_protocol_export` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_export_protocol_unsupported_format` | `tests.test_protocol_export` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_get_safe_filename` | `tests.test_protocol_export` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_load_template_invalid` | `tests.test_protocol_export` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_load_template_missing` | `tests.test_protocol_export` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_production_template_immutability_integration` | `tests.test_protocol_export` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_render_protocol_to_docx_combined_structure` | `tests.test_protocol_export` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_render_protocol_to_docx_gated_narrative_only` | `tests.test_protocol_export` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_render_protocol_to_docx_gated_soa_only` | `tests.test_protocol_export` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_render_protocol_to_docx_gated_synopsis_only` | `tests.test_protocol_export` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_sanitize_filename` | `tests.test_protocol_export` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_template_immutability` | `tests.test_protocol_export` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_export_metadata_invalid_version` | `tests.test_protocol_render` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_export_metadata_missing_change_reason_on_version_bump` | `tests.test_protocol_render` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_export_metadata_valid_initial` | `tests.test_protocol_render` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_export_metadata_valid_version_bump` | `tests.test_protocol_render` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_narrative_item_and_section_views` | `tests.test_protocol_render` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_render_protocol_to_html_combined` | `tests.test_protocol_render` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_render_protocol_to_html_narrative_only` | `tests.test_protocol_render` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_render_protocol_to_html_soa_only` | `tests.test_protocol_render` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_render_protocol_to_html_synopsis_only` | `tests.test_protocol_render` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_rendered_protocol_document_with_usdm_study` | `tests.test_protocol_render` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_soa_matrix_view` | `tests.test_protocol_render` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_synopsis_view_parsing` | `tests.test_protocol_render` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_protocol_version_ref_accepted_statuses` | `tests.test_protocol_version_ref` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_protocol_version_ref_serialization` | `tests.test_protocol_version_ref` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_protocol_version_ref_valid_payload` | `tests.test_protocol_version_ref` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_protocol_version_ref_validation_blank_fields` | `tests.test_protocol_version_ref` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_protocol_version_ref_validation_index` | `tests.test_protocol_version_ref` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_protocol_version_ref_validation_status` | `tests.test_protocol_version_ref` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_capa_creation_validations_and_closed_deviation` | `tests.test_quality` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_capa_transition_edge_cases_and_optimistic_locking` | `tests.test_quality` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_capa_update_edge_cases_and_optional_fields` | `tests.test_quality` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_database_manager_uninitialized_raises_exception` | `tests.test_quality` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_deviation_lifecycle_and_traceability_fields` | `tests.test_quality` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_deviation_not_found_404` | `tests.test_quality` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_deviation_rca_capa_relationships_and_cascading` | `tests.test_quality` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_endpoint_change_reason_check_via_mock` | `tests.test_quality` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_lifespan_coverage` | `tests.test_quality` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_list_deviations_filters` | `tests.test_quality` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_missing_change_reasons_unauthorized` | `tests.test_quality` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_quality_audit_log_append_only` | `tests.test_quality` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_quality_database_schema_creation` | `tests.test_quality` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_quality_health_check` | `tests.test_quality` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_sqlite_foreign_key_constraints` | `tests.test_quality` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_sqlite_pragma_exception_handling` | `tests.test_quality` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_audit_log_endpoint_properties` | `tests.test_quality_workflow` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_capa_approval_closure_requires_quality_oversight` | `tests.test_quality_workflow` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_capa_creation_validations` | `tests.test_quality_workflow` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `test_capa_lifecycle_transitions` | `tests.test_quality_workflow` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `test_capa_updates_and_concurrency` | `tests.test_quality_workflow` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `test_create_and_list_deviations` | `tests.test_quality_workflow` | PRD-SYS-001, Trace-7 | 🟢 PASSED | < 1s |
| `test_create_and_update_rca` | `tests.test_quality_workflow` | PRD-SYS-001 | 🟢 PASSED | < 1s |
| `test_mutation_without_change_reason_rejected` | `tests.test_quality_workflow` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_permission_failure_leaves_no_misleading_audit_entry` | `tests.test_quality_workflow` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_read_only_roles_forbidden` | `tests.test_quality_workflow` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_successful_mutation_creates_audit_log_and_is_atomic` | `tests.test_quality_workflow` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_transition_capa_sig_token_matrix` | `tests.test_quality_workflow` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_digest_window_configurations` | `tests.test_queries_escalation` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_escalation_idempotency` | `tests.test_queries_escalation` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_escalation_missing_ids_fallback` | `tests.test_queries_escalation` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_no_aging_queries` | `tests.test_queries_escalation` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_startup_shutdown_and_resilience` | `tests.test_queries_escalation` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_threshold_boundaries_and_escalation` | `tests.test_queries_escalation` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_concurrent_randomization_unique_and_monotonic` | `tests.test_randomization_concurrency` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_forced_failure_rolls_back_atomically` | `tests.test_randomization_concurrency` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_randomization_entities_audit_trail_and_soft_delete` | `tests.test_randomization_persistence` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_randomization_entities_hard_delete_prevented` | `tests.test_randomization_persistence` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_randomization_entities_trial_lock_conformity` | `tests.test_randomization_persistence` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_can_access_site` | `tests.test_rbac` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_cross_site_query_read_isolation` | `tests.test_rbac` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_cross_site_unblind_denied_with_alert` | `tests.test_rbac` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_etmf_audit_logs_gated_to_auditors` | `tests.test_rbac` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_etmf_document_transition_auditor_forbidden` | `tests.test_rbac` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_etmf_edl_creation_auditor_forbidden` | `tests.test_rbac` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_etmf_edl_update_auditor_forbidden` | `tests.test_rbac` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_etmf_ingest_auditor_forbidden` | `tests.test_rbac` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_execution_observation_creation_auditor_forbidden` | `tests.test_rbac` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_execution_subject_creation_auditor_forbidden` | `tests.test_rbac` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_execution_visit_creation_auditor_forbidden` | `tests.test_rbac` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_external_monitor_aliases` | `tests.test_rbac` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_external_monitor_eisf_denies_writes_allows_reads` | `tests.test_rbac` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_external_monitor_permissions_matrix` | `tests.test_rbac` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_external_monitor_principal_resolution` | `tests.test_rbac` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_get_principal_from_request` | `tests.test_rbac` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_has_permission` | `tests.test_rbac` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_mask_payload_recursive` | `tests.test_rbac` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_require_permission_dependency` | `tests.test_rbac` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_role_aliases_normalization` | `tests.test_rbac` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_role_normalization_list` | `tests.test_rbac` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_role_normalization_string` | `tests.test_rbac` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_rtsm_role_aliases_normalization` | `tests.test_rbac` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_rtsm_role_aware_masking` | `tests.test_rbac` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_rtsm_role_permissions` | `tests.test_rbac` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_verify_is_auditor_allows_auditors` | `tests.test_rbac` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_verify_is_auditor_denies_non_auditors` | `tests.test_rbac` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_verify_not_auditor_allows_others` | `tests.test_rbac` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_verify_not_auditor_denies_auditors` | `tests.test_rbac` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_subject_consent_blocking_and_reconsent_lifecycle` | `tests.test_reconsent_blocking` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_reset_db_safety_guard_non_local` | `tests.test_reset_db` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_reset_db_safety_guard_production` | `tests.test_reset_db` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_reset_db_success_offline` | `tests.test_reset_db` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_block_allocation_mechanics` | `tests.test_rtsm_algorithms` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_block_allocation_uneven_ratios` | `tests.test_rtsm_algorithms` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_canonical_stratum_key_generation` | `tests.test_rtsm_algorithms` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_minimization_imbalance_and_biased_coin` | `tests.test_rtsm_algorithms` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_minimization_uneven_ratios_and_weights` | `tests.test_rtsm_algorithms` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_randomization_config_validation` | `tests.test_rtsm_algorithms` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_reproducibility_and_seeding` | `tests.test_rtsm_algorithms` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_stratified_block_isolation` | `tests.test_rtsm_algorithms` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_evaluate_resupply_boundaries` | `tests.test_rtsm_supply` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_hard_delete_prevented_for_supply_entities` | `tests.test_rtsm_supply` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_insufficient_stock_rejection_and_rollback` | `tests.test_rtsm_supply` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_invalid_site_kit_relationship_rejection` | `tests.test_rtsm_supply` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_locked_site_rejection` | `tests.test_rtsm_supply` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_resupply_threshold_breach_and_deduplication` | `tests.test_rtsm_supply` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_site_inventory_unique_constraint` | `tests.test_rtsm_supply` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_successful_dispensation_endpoint` | `tests.test_rtsm_supply` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_supply_entities_audit_trail_and_soft_delete` | `tests.test_rtsm_supply` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_trial_locking_conformity` | `tests.test_rtsm_supply` | *Regression/Helper* | 🟢 PASSED | < 1s |
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
| `test_deterministic_output_sorting` | `tests.test_sae_reconciliation` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_execution_client_and_adapter_methods` | `tests.test_sae_reconciliation` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_pure_comparison_differing_fields` | `tests.test_sae_reconciliation` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_pure_comparison_missing_on_either_side` | `tests.test_sae_reconciliation` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_pure_comparison_same_code_different_terms` | `tests.test_sae_reconciliation` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_pure_function_generate_stable_event_key` | `tests.test_sae_reconciliation` | Trace-14 | 🟢 PASSED | < 1s |
| `test_pure_function_normalize_edc_ae_to_sae` | `tests.test_sae_reconciliation` | Trace-14 | 🟢 PASSED | < 1s |
| `test_pure_function_normalize_external_icsr_to_saes` | `tests.test_sae_reconciliation` | Trace-14 | 🟢 PASSED | < 1s |
| `test_reconciliation_jobs_read_endpoints_and_gating` | `tests.test_sae_reconciliation` | Trace-14 | 🟢 PASSED | < 1s |
| `test_reconciliation_persistence_and_audit` | `tests.test_sae_reconciliation` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_reconciliation_runs_read_endpoints` | `tests.test_sae_reconciliation` | Trace-14 | 🟢 PASSED | < 1s |
| `test_reconciliation_version_index_increment` | `tests.test_sae_reconciliation` | Trace-14 | 🟢 PASSED | < 1s |
| `test_safety_mutations_negative_signatures` | `tests.test_sae_reconciliation` | Trace-14 | 🟢 PASSED | < 1s |
| `test_safety_reads_negative_signatures` | `tests.test_sae_reconciliation` | Trace-14 | 🟢 PASSED | < 1s |
| `test_alert_dispatch_failure_exception` | `tests.test_sae_reconciliation_jobs` | Trace-14 | 🟢 PASSED | < 1s |
| `test_alert_dispatch_failure_non_2xx` | `tests.test_sae_reconciliation_jobs` | Trace-14 | 🟢 PASSED | < 1s |
| `test_notifications_gxp_medical_monitor_alert` | `tests.test_sae_reconciliation_jobs` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_reconciliation_job_failure_path` | `tests.test_sae_reconciliation_jobs` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_trigger_and_poll_reconciliation_job_success` | `tests.test_sae_reconciliation_jobs` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_icsr_version_and_reason_for_change_rendering` | `tests.test_safety_e2b` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_invalid_namespace_fails` | `tests.test_safety_e2b` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_invalid_root_tag_fails` | `tests.test_safety_e2b` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_malformed_xml_validation_fails` | `tests.test_safety_e2b` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_missing_drugs_or_drug_fields_fails` | `tests.test_safety_e2b` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_missing_header_fails` | `tests.test_safety_e2b` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_missing_header_fields_fail` | `tests.test_safety_e2b` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_missing_patient_fails` | `tests.test_safety_e2b` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_missing_patient_fields_fail` | `tests.test_safety_e2b` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_missing_reactions_or_reaction_term_fails` | `tests.test_safety_e2b` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_missing_safety_report_fails` | `tests.test_safety_e2b` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_missing_worldwide_unique_case_id_fails` | `tests.test_safety_e2b` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_valid_icsr_rendering_and_validation` | `tests.test_safety_e2b` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_database_manager_uninitialized_raises_exception` | `tests.test_safety_service` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_invalid_xml_validation_fails` | `tests.test_safety_service` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_list_audit_logs_endpoint` | `tests.test_safety_service` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_missing_change_reason_fails_mutations` | `tests.test_safety_service` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_missing_v2_headers_or_change_reason_fails` | `tests.test_safety_service` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_nonexistent_resources_return_404` | `tests.test_safety_service` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_safety_audit_log_immutable_ledger` | `tests.test_safety_service` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_safety_case_lifecycle` | `tests.test_safety_service` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_safety_database_schema_creation` | `tests.test_safety_service` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_safety_export_job_lifecycle` | `tests.test_safety_service` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_safety_health_check` | `tests.test_safety_service` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_successful_export_and_transmission` | `tests.test_safety_service` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_unauthenticated_requests_are_rejected` | `tests.test_safety_service` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_gateway_graceful_handling_invalid_downstream` | `tests.test_schema_validation` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_rewrite_references_nested_references` | `tests.test_schema_validation` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_rewrite_references_recursion_protection` | `tests.test_schema_validation` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_static_schema_validation_script` | `tests.test_schema_validation` | *Regression/Helper* | 🟢 PASSED | < 1s |
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
| `test_compute_age` | `tests.test_sdtm_mapper` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_get_demographics` | `tests.test_sdtm_mapper` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_map_ae_flat_structure` | `tests.test_sdtm_mapper` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_map_ae_grouped_structure` | `tests.test_sdtm_mapper` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_map_cm_flat_structure` | `tests.test_sdtm_mapper` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_map_cm_grouped_structure` | `tests.test_sdtm_mapper` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_map_dm_defaults_and_fallbacks` | `tests.test_sdtm_mapper` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_map_dm_happy_path` | `tests.test_sdtm_mapper` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_map_lb` | `tests.test_sdtm_mapper` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_map_to_sdtm_orchestrator` | `tests.test_sdtm_mapper` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_map_vs` | `tests.test_sdtm_mapper` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_to_dtc` | `tests.test_sdtm_mapper` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_sdv_automatic_verification_drop_compliance` | `tests.test_sdv` | PRD-QRY-006 | 🟢 PASSED | < 1s |
| `test_sdv_signoff_endpoints_rbac_and_target_validation` | `tests.test_sdv` | PRD-QRY-005 | 🟢 PASSED | < 1s |
| `test_clinical_observation_sdv_defaults` | `tests.test_sdv_tsdv_persistence` | PRD-QRY-005, PRD-QRY-007 | 🟢 PASSED | < 1s |
| `test_sdv_automatic_verification_drop` | `tests.test_sdv_tsdv_persistence` | PRD-QRY-005, PRD-QRY-006 | 🟢 PASSED | < 1s |
| `test_sdv_sign_off_persistence_and_audit` | `tests.test_sdv_tsdv_persistence` | PRD-QRY-005 | 🟢 PASSED | < 1s |
| `test_sdv_signoff_endpoint_and_idempotency` | `tests.test_sdv_tsdv_persistence` | PRD-QRY-005, PRD-QRY-006 | 🟢 PASSED | < 1s |
| `test_sdv_signoff_page_visit_scopes` | `tests.test_sdv_tsdv_persistence` | PRD-QRY-005, PRD-QRY-006 | 🟢 PASSED | < 1s |
| `test_tsdv_config_persistence` | `tests.test_sdv_tsdv_persistence` | PRD-QRY-007 | 🟢 PASSED | < 1s |
| `test_audit_context_variables_and_decorator` | `tests.test_security_middleware` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_canonical_json_signing_and_verification` | `tests.test_security_middleware` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_downstream_signature_gated_endpoint_expired_token` | `tests.test_security_middleware` | Trace-15 | 🟢 PASSED | < 1s |
| `test_downstream_signature_gated_endpoint_mismatched_action` | `tests.test_security_middleware` | Trace-15 | 🟢 PASSED | < 1s |
| `test_downstream_signature_gated_endpoint_replay_blocked` | `tests.test_security_middleware` | Trace-15 | 🟢 PASSED | < 1s |
| `test_downstream_signature_gated_endpoint_requires_sig_token` | `tests.test_security_middleware` | Trace-15 | 🟢 PASSED | < 1s |
| `test_downstream_signature_gated_endpoint_valid_sig_token` | `tests.test_security_middleware` | Trace-15 | 🟢 PASSED | < 1s |
| `test_middleware_expired_timestamp` | `tests.test_security_middleware` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_middleware_explicit_legacy_version_accepted` | `tests.test_security_middleware` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_middleware_explicit_legacy_version_invalid_rejected` | `tests.test_security_middleware` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_middleware_health_bypass` | `tests.test_security_middleware` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_middleware_invalid_timestamp_format` | `tests.test_security_middleware` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_middleware_missing_headers` | `tests.test_security_middleware` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_middleware_missing_signature_version_rejected` | `tests.test_security_middleware` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_middleware_tenant_context_and_state` | `tests.test_security_middleware` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_middleware_tenant_missing_fallback` | `tests.test_security_middleware` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_middleware_tenant_signature_tampering_rejected` | `tests.test_security_middleware` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_middleware_unblinded_access_parametrization` | `tests.test_security_middleware` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_middleware_unsupported_version_rejected` | `tests.test_security_middleware` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_middleware_v2_invalid_signature` | `tests.test_security_middleware` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_middleware_v2_mismatched_reason` | `tests.test_security_middleware` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_middleware_v2_missing_reason` | `tests.test_security_middleware` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_middleware_v2_safe_method_no_reason_success` | `tests.test_security_middleware` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_middleware_v2_success` | `tests.test_security_middleware` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_mutation_unsigned_and_non_compliant_rejections` | `tests.test_security_middleware` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_verify_gateway_signature_scope_fallback_restrictions` | `tests.test_security_middleware` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_verify_sig_token_helper_scenarios` | `tests.test_security_middleware` | Trace-15 | 🟢 PASSED | < 1s |
| `test_asymmetric_sign_and_verify` | `tests.test_signature_manifestation` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_async_signature_context_decorator` | `tests.test_signature_manifestation` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_capture_certificate_identifiers` | `tests.test_signature_manifestation` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_controlled_enums` | `tests.test_signature_manifestation` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_sha256_hashing_helper` | `tests.test_signature_manifestation` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_signature_context_propagation` | `tests.test_signature_manifestation` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_signature_manifestation_lifecycle` | `tests.test_signature_manifestation` | Trace-13 | 🟢 PASSED | < 1s |
| `test_api_audit_reason_enforcement` | `tests.test_soa_endpoints` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_api_concurrent_locking_conflict_exception_translation` | `tests.test_soa_endpoints` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_api_invalid_signature_exception_translation` | `tests.test_soa_endpoints` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_api_rule_soft_delete` | `tests.test_soa_endpoints` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_api_soa_crud_lifecycle_endpoints` | `tests.test_soa_endpoints` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_api_soa_immutability_guards` | `tests.test_soa_endpoints` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_api_soa_immutability_guards_updates` | `tests.test_soa_endpoints` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_api_soa_linking_and_matrix_projection` | `tests.test_soa_endpoints` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_api_soa_retirement_and_projection_exclusion` | `tests.test_soa_endpoints` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_api_soa_typed_validation_and_timing_rejection` | `tests.test_soa_endpoints` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_api_unauthorized_requests` | `tests.test_soa_endpoints` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_api_validation_failures` | `tests.test_soa_endpoints` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_api_with_mocked_neo4j_driver` | `tests.test_soa_endpoints` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_assert_study_version_mutable` | `tests.test_soa_persistence` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_epoch_neo4j` | `tests.test_soa_persistence` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_form_neo4j` | `tests.test_soa_persistence` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_get_soa_matrix_projection_neo4j` | `tests.test_soa_persistence` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_links_neo4j` | `tests.test_soa_persistence` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_mock_soa_entity_lifecycle` | `tests.test_soa_persistence` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_mutability_guard_rejects_locked_versions` | `tests.test_soa_persistence` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_neo4j_driver_operations` | `tests.test_soa_persistence` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_procedure_neo4j` | `tests.test_soa_persistence` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_timing_window_neo4j` | `tests.test_soa_persistence` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_update_study_arm_neo4j` | `tests.test_soa_persistence` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_visit_neo4j` | `tests.test_soa_persistence` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_with_transaction_retry_failure_exceeded` | `tests.test_soa_persistence` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_with_transaction_retry_success_after_retries` | `tests.test_soa_persistence` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_protocol_capture_and_reconciliation_lifecycle` | `tests.test_study_migration` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_api_protocol_amendment_concurrency_race` | `tests.test_study_versions` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_api_protocol_amendment_invalid_signature_rejected` | `tests.test_study_versions` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_api_protocol_amendment_invalid_study_404` | `tests.test_study_versions` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_api_protocol_amendment_minor_and_major_bumps` | `tests.test_study_versions` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_api_protocol_approval_and_immutability` | `tests.test_study_versions` | *Regression/Helper* | 🟢 PASSED | < 1s |
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
| `test_gxp_ruleset_file_structures` | `tests.test_sync_ruleset` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_sync_ruleset_create_new` | `tests.test_sync_ruleset` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_sync_ruleset_dry_run` | `tests.test_sync_ruleset` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_sync_ruleset_multiple_files_integration` | `tests.test_sync_ruleset` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_sync_ruleset_permission_denied_403` | `tests.test_sync_ruleset` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_sync_ruleset_permission_denied_403_graceful` | `tests.test_sync_ruleset` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_sync_ruleset_update_existing` | `tests.test_sync_ruleset` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_events_captured_in_part_11_audit_history` | `tests.test_system_coding_queries` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_manual_coding_resolution_associates_with_query_and_closes_it` | `tests.test_system_coding_queries` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_resolving_query_reverts_assignment_to_uncoded` | `tests.test_system_coding_queries` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_uncodable_term_creates_query_pending_and_actionable_query` | `tests.test_system_coding_queries` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_uncodable_term_query_creation_is_idempotent` | `tests.test_system_coding_queries` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_terminology_cache_capacity_eviction` | `tests.test_terminology_cache` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_terminology_cache_hit_and_expiration` | `tests.test_terminology_cache` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_terminology_cache_thread_safety` | `tests.test_terminology_cache` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_terminology_cache_ttl_config` | `tests.test_terminology_cache` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_terminology_cache_unreachable_db_fallback` | `tests.test_terminology_cache` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_cache_hit_performs_no_external_lookup` | `tests.test_terminology_integration` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_existing_cache_consumers_receive_expected_shape` | `tests.test_terminology_integration` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_expired_entry_fallback_on_unreachable_evs` | `tests.test_terminology_integration` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_get_terminology_from_db_delegation` | `tests.test_terminology_integration` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_get_terminology_from_db_nci_evs_offline_fallback` | `tests.test_terminology_integration` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_get_terminology_from_db_not_found_anywhere` | `tests.test_terminology_integration` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_get_terminology_from_db_not_found_in_evs_but_in_mock` | `tests.test_terminology_integration` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_get_terminology_from_db_transport_error_and_not_in_mock` | `tests.test_terminology_integration` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_get_terminology_from_db_transport_error_but_in_mock` | `tests.test_terminology_integration` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_offline_fallback_resolves_supported_seed_concepts` | `tests.test_terminology_integration` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_terminology_cache_unreachable_database_exception_fallback` | `tests.test_terminology_integration` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_search_terminology_endpoint_degraded` | `tests.test_terminology_validation` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_search_terminology_endpoint_invalid_input` | `tests.test_terminology_validation` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_search_terminology_endpoint_success` | `tests.test_terminology_validation` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_validate_concept_codes_degraded` | `tests.test_terminology_validation` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_validate_concept_codes_success` | `tests.test_terminology_validation` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_validate_single_code_endpoint` | `tests.test_terminology_validation` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_validate_single_code_endpoint_degraded` | `tests.test_terminology_validation` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_validate_single_code_endpoint_invalid_data` | `tests.test_terminology_validation` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_validate_single_code_endpoint_marked_invalid` | `tests.test_terminology_validation` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_validate_single_code_endpoint_not_found` | `tests.test_terminology_validation` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_validate_study_ct_endpoint` | `tests.test_terminology_validation` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_validate_study_ct_endpoint_not_found` | `tests.test_terminology_validation` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_validate_study_terminology` | `tests.test_terminology_validation` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_validate_study_terminology_endpoint_client_degraded` | `tests.test_terminology_validation` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_validate_study_terminology_endpoint_client_not_found` | `tests.test_terminology_validation` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_validate_study_terminology_endpoint_client_success` | `tests.test_terminology_validation` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_validate_study_terminology_endpoint_not_found` | `tests.test_terminology_validation` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_validate_study_terminology_endpoint_success` | `tests.test_terminology_validation` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_validate_study_terminology_fully_valid` | `tests.test_terminology_validation` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_audit_log_creation_on_escalate` | `tests.test_tickets_escalation` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_bounded_priority_advancement` | `tests.test_tickets_escalation` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_cooldown_gating_and_idempotency` | `tests.test_tickets_escalation` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_escalation_eligibility_rules` | `tests.test_tickets_escalation` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_notification_deduplication_and_partial_failures` | `tests.test_tickets_escalation` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_startup_shutdown_and_resilience` | `tests.test_tickets_escalation` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_publish_notification_non_2xx_failure` | `tests.test_tickets_notifications_client` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_publish_notification_success` | `tests.test_tickets_notifications_client` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_publish_notification_transport_exception` | `tests.test_tickets_notifications_client` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_notification_failure_isolation` | `tests.test_tickets_notifications_integration` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_notification_idempotency` | `tests.test_tickets_notifications_integration` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_ticket_assignment_notification` | `tests.test_tickets_notifications_integration` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_ticket_comment_notification` | `tests.test_tickets_notifications_integration` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_ticket_transition_notification` | `tests.test_tickets_notifications_integration` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_update_ticket_notifications` | `tests.test_tickets_notifications_integration` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_comments_creation_and_retrieval_scoped` | `tests.test_tickets_service` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_list_ticket_audit_logs_endpoint` | `tests.test_tickets_service` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_missing_change_reason_fails_mutations` | `tests.test_tickets_service` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_nonexistent_resources_return_404` | `tests.test_tickets_service` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_ticket_audit_log_immutable_ledger` | `tests.test_tickets_service` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_ticket_concurrent_reference_generation` | `tests.test_tickets_service` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_ticket_scoped_audit_logs` | `tests.test_tickets_service` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_tickets_audit_logs_pagination` | `tests.test_tickets_service` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_tickets_audit_logs_query_boundaries` | `tests.test_tickets_service` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_tickets_audit_logs_time_filtering` | `tests.test_tickets_service` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_tickets_auditor_comments_access` | `tests.test_tickets_service` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_tickets_database_schema_creation` | `tests.test_tickets_service` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_tickets_enums_and_models_attributes` | `tests.test_tickets_service` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_tickets_get_by_reference` | `tests.test_tickets_service` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_tickets_health_check` | `tests.test_tickets_service` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_tickets_in_scope_success_and_self_audit` | `tests.test_tickets_service` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_tickets_lifecycle` | `tests.test_tickets_service` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_tickets_optimistic_locking_and_explicit_endpoints` | `tests.test_tickets_service` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_tickets_rbac_auditor_cannot_mutate_but_can_read` | `tests.test_tickets_service` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_tickets_scope_aware_filtering` | `tests.test_tickets_service` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_tickets_site_scope_filtering_audit_logs_unfiltered` | `tests.test_tickets_service` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_tickets_terminal_state_rejection` | `tests.test_tickets_service` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_tickets_unauthorized_site_scope_blocking` | `tests.test_tickets_service` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_tickets_validation_invalid_enums` | `tests.test_tickets_service` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_unauthenticated_requests_are_rejected` | `tests.test_tickets_service` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_active_version_selection` | `tests.test_tmf_reference_model` | PRD-TMF-001 | 🟢 PASSED | < 1s |
| `test_artifact_parent_identification` | `tests.test_tmf_reference_model` | PRD-TMF-001 | 🟢 PASSED | < 1s |
| `test_canonical_11_zones` | `tests.test_tmf_reference_model` | PRD-TMF-001 | 🟢 PASSED | < 1s |
| `test_complete_catalog_manifest_and_uniqueness` | `tests.test_tmf_reference_model` | PRD-TMF-001 | 🟢 PASSED | < 1s |
| `test_explicit_version_selection` | `tests.test_tmf_reference_model` | PRD-TMF-001 | 🟢 PASSED | < 1s |
| `test_get_mandatory_artifacts_failures` | `tests.test_tmf_reference_model` | PRD-TMF-004 | 🟢 PASSED | < 1s |
| `test_get_mandatory_artifacts_success` | `tests.test_tmf_reference_model` | PRD-TMF-004 | 🟢 PASSED | < 1s |
| `test_hierarchy_integrity_v3_2_0_complete` | `tests.test_tmf_reference_model` | PRD-TMF-001 | 🟢 PASSED | < 1s |
| `test_immutability_properties` | `tests.test_tmf_reference_model` | PRD-TMF-001 | 🟢 PASSED | < 1s |
| `test_no_database_dependencies` | `tests.test_tmf_reference_model` | PRD-TMF-001 | 🟢 PASSED | < 1s |
| `test_reproducibility_and_version_isolation` | `tests.test_tmf_reference_model` | PRD-TMF-001 | 🟢 PASSED | < 1s |
| `test_resolve_artifact_failures` | `tests.test_tmf_reference_model` | PRD-TMF-001 | 🟢 PASSED | < 1s |
| `test_resolve_artifact_success` | `tests.test_tmf_reference_model` | PRD-TMF-001 | 🟢 PASSED | < 1s |
| `test_standard_versus_extension_policy` | `tests.test_tmf_reference_model` | PRD-TMF-001 | 🟢 PASSED | < 1s |
| `test_validate_hierarchy_failures` | `tests.test_tmf_reference_model` | PRD-TMF-002 | 🟢 PASSED | < 1s |
| `test_validate_hierarchy_success` | `tests.test_tmf_reference_model` | PRD-TMF-002 | 🟢 PASSED | < 1s |
| `test_version_isolation` | `tests.test_tmf_reference_model` | PRD-TMF-001 | 🟢 PASSED | < 1s |
| `test_admin_cache_clear_forces_fresh_read` | `tests.test_transformers` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_legacy_endpoint_returns_original_schema` | `tests.test_transformers` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_terminology_cache_prevents_db_queries` | `tests.test_transformers` | PRD-MDR-001 | 🟢 PASSED | < 1s |
| `test_usdm_endpoint_returns_nested_schema_and_fast` | `tests.test_transformers` | PRD-MDR-003, PRD-MDR-004 | 🟢 PASSED | < 1s |
| `test_usdm_validation_error_on_invalid_data` | `tests.test_transformers` | PRD-MDR-001 | 🟢 PASSED | < 1s |
| `test_security_gate_unauthenticated_requests` | `tests.test_translation_recovery` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_translation_error_status_and_rollback` | `tests.test_translation_recovery` | Trace-12 | 🟢 PASSED | < 1s |
| `test_translation_status_and_listing_success` | `tests.test_translation_recovery` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_worker_context_and_session_cleanup` | `tests.test_translation_recovery` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_audit_safe_context_binds_and_cleans_up` | `tests.test_translator` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_audit_safe_context_cleans_up_on_error` | `tests.test_translator` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_background_translation_records_user_audit` | `tests.test_translator` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_identifier_sanitization_during_translation` | `tests.test_translator` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_multi_language_localization_and_hint_system` | `tests.test_translator` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_rules_compilation_and_artifact_generation` | `tests.test_translator` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_study_published_event_triggers_translation` | `tests.test_translator` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_study_published_expired_timestamp_rejection` | `tests.test_translator` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_study_published_invalid_signature_rejection` | `tests.test_translator` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_translation_validation_failure` | `tests.test_translator` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_site_and_visit_locks` | `tests.test_trial_lock` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_subject_and_form_locks` | `tests.test_trial_lock` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_trial_lock_freeze` | `tests.test_trial_lock` | PRD-SYS-003, Trace-3 | 🟢 PASSED | < 1s |
| `test_api_tsdv_config_validation_rules` | `tests.test_tsdv` | PRD-QRY-007 | 🟢 PASSED | < 1s |
| `test_api_tsdv_configuration_rbac` | `tests.test_tsdv` | PRD-QRY-007 | 🟢 PASSED | < 1s |
| `test_api_tsdv_evaluation_integration_and_context_errors` | `tests.test_tsdv` | PRD-QRY-007 | 🟢 PASSED | < 1s |
| `test_api_tsdv_immutable_enrollment_index_stability` | `tests.test_tsdv` | PRD-QRY-007 | 🟢 PASSED | < 1s |
| `test_tsdv_pure_deterministic_sampling` | `tests.test_tsdv` | PRD-QRY-007 | 🟢 PASSED | < 1s |
| `test_tsdv_pure_different_seeds_produce_different_values` | `tests.test_tsdv` | PRD-QRY-007 | 🟢 PASSED | < 1s |
| `test_tsdv_pure_evaluation_sampling_models` | `tests.test_tsdv` | PRD-QRY-007 | 🟢 PASSED | < 1s |
| `test_tsdv_pure_field_requirement_precedence` | `tests.test_tsdv` | PRD-QRY-007 | 🟢 PASSED | < 1s |
| `test_tsdv_pure_first_n_selection` | `tests.test_tsdv` | PRD-QRY-007 | 🟢 PASSED | < 1s |
| `test_tsdv_pure_percentage_boundaries` | `tests.test_tsdv` | PRD-QRY-007 | 🟢 PASSED | < 1s |
| `test_api_tsdv_config_authorization_and_upsert` | `tests.test_tsdv_logic` | PRD-QRY-007 | 🟢 PASSED | < 1s |
| `test_api_tsdv_config_validation` | `tests.test_tsdv_logic` | PRD-QRY-007 | 🟢 PASSED | < 1s |
| `test_api_tsdv_evaluation_endpoint` | `tests.test_tsdv_logic` | PRD-QRY-007 | 🟢 PASSED | < 1s |
| `test_tsdv_evaluation_models` | `tests.test_tsdv_logic` | PRD-QRY-007 | 🟢 PASSED | < 1s |
| `test_tsdv_field_required_precedence` | `tests.test_tsdv_logic` | PRD-QRY-007 | 🟢 PASSED | < 1s |
| `test_tsdv_subject_selection_boundaries` | `tests.test_tsdv_logic` | PRD-QRY-007 | 🟢 PASSED | < 1s |
| `test_tsdv_subject_selection_deterministic` | `tests.test_tsdv_logic` | PRD-QRY-007 | 🟢 PASSED | < 1s |
| `test_tsdv_subject_selection_first_n` | `tests.test_tsdv_logic` | PRD-QRY-007 | 🟢 PASSED | < 1s |
| `test_convert_unit_errors` | `tests.test_ucum_coverage` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_convert_unit_identical` | `tests.test_ucum_coverage` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_convert_unit_multiplicative` | `tests.test_ucum_coverage` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_convert_unit_temperature` | `tests.test_ucum_coverage` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_get_normalized_representation` | `tests.test_ucum_coverage` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_normalize_unit_name` | `tests.test_ucum_coverage` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_api_validate_usdm_endpoint_invalid_422` | `tests.test_usdm_ingestion` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_api_validate_usdm_endpoint_valid` | `tests.test_usdm_ingestion` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_normalize_usdm_payload_v2_to_v3` | `tests.test_usdm_ingestion` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_resolve_usdm_version_override` | `tests.test_usdm_ingestion` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_resolve_usdm_version_v2` | `tests.test_usdm_ingestion` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_resolve_usdm_version_v3` | `tests.test_usdm_ingestion` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_safe_parse_payload_invalid` | `tests.test_usdm_ingestion` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_safe_parse_payload_json` | `tests.test_usdm_ingestion` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_safe_parse_payload_yaml` | `tests.test_usdm_ingestion` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_validate_usdm_payload_circular_skip_logic` | `tests.test_usdm_ingestion` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_validate_usdm_payload_duplicate_ids` | `tests.test_usdm_ingestion` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_validate_usdm_payload_invalid_structure` | `tests.test_usdm_ingestion` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_validate_usdm_payload_stochastic_math_operators` | `tests.test_usdm_ingestion` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_validate_usdm_payload_valid_v3` | `tests.test_usdm_ingestion` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_validate_usdm_payload_warnings_custom_elements` | `tests.test_usdm_ingestion` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_round_trip_canonical_serialization_verification` | `tests.test_usdm_serialization` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_serialize_usdm_canonical_json` | `tests.test_usdm_serialization` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_serialize_usdm_canonical_yaml` | `tests.test_usdm_serialization` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_serialize_usdm_validation_errors` | `tests.test_usdm_serialization` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_adr_compliance_validation_logic` | `tests.test_validate_adrs` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_check_architectural_changes_require_adr_missing_adr` | `tests.test_validate_adrs` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_check_architectural_changes_require_adr_no_changes` | `tests.test_validate_adrs` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_check_architectural_changes_require_adr_with_deleted_adr` | `tests.test_validate_adrs` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_check_architectural_changes_require_adr_with_valid_adr` | `tests.test_validate_adrs` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_compliance_utility_extraction_and_normalization` | `tests.test_validate_adrs` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_compliance_utility_parsing` | `tests.test_validate_adrs` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_get_changed_files_bypasses_merge_commits_and_parses_status` | `tests.test_validate_adrs` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_get_changed_files_from_git_fallbacks` | `tests.test_validate_adrs` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_get_changed_files_from_txt` | `tests.test_validate_adrs` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_get_closest_local_branch_point_fallback_to_root` | `tests.test_validate_adrs` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_get_closest_local_branch_point_multiple_branches` | `tests.test_validate_adrs` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_is_architectural_file` | `tests.test_validate_adrs` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_validate_existing_adrs_valid_case` | `tests.test_validate_adrs` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_validate_existing_adrs_with_targets_outside_folder` | `tests.test_validate_adrs` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_validate_existing_adrs_with_targets_valid` | `tests.test_validate_adrs` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_check_file_imports_cross_service_violation` | `tests.test_validate_imports` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_check_file_imports_invalid_syntax` | `tests.test_validate_imports` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_check_file_imports_relative_cross_service` | `tests.test_validate_imports` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_check_file_imports_relative_same_service` | `tests.test_validate_imports` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_check_file_imports_same_service` | `tests.test_validate_imports` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_check_file_imports_shared_packages` | `tests.test_validate_imports` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_get_service_name` | `tests.test_validate_imports` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_designer_validation_error_rfc7807` | `tests.test_validation_problem_details` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_execution_validation_error_rfc7807` | `tests.test_validation_problem_details` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_generate_alignment_report` | `tests.test_validator` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_generate_alignment_report_with_mappings` | `tests.test_validator` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_extract_active_frontend_vulnerabilities_invalid` | `tests.test_vulnerabilities` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_extract_active_frontend_vulnerabilities_valid` | `tests.test_vulnerabilities` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_extract_active_vulnerabilities_invalid` | `tests.test_vulnerabilities` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_extract_active_vulnerabilities_valid` | `tests.test_vulnerabilities` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_load_and_validate_ledger_frontend_invalid_justification` | `tests.test_vulnerabilities` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_load_and_validate_ledger_frontend_invalid_rpn` | `tests.test_vulnerabilities` | *Regression/Helper* | 🟢 PASSED | < 1s |
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
| `test_cdisc_xml_structure_validation` | `tests.validation.environment_integrity_tests` | PRD-MDR-001 | 🟢 PASSED | < 1s |
| `test_cryptographic_tamper_evident_safeguards` | `tests.validation.environment_integrity_tests` | PRD-SYS-003 | 🟢 PASSED | < 1s |
| `test_environment_integrity` | `tests.validation.environment_integrity_tests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `test_site_level_data_isolation` | `tests.validation.environment_integrity_tests` | PRD-SYS-004 | 🟢 PASSED | < 1s |
| `test_gxp_compliance_drifts_identified` | `tests.validation.gxp_compliance_suite` | PRD-SYS-001 | 🟢 PASSED | < 1s |
| `test_blinding_constraints_on_ui_data_rendering` | `tests.validation.prd_compliance_traceability_tests` | PRD-MDR-006 | 🟢 PASSED | < 1s |
| `test_ecrf_version_control_history` | `tests.validation.prd_compliance_traceability_tests` | PRD-EDC-005 | 🟢 PASSED | < 1s |
| `test_edc_archival_integration` | `tests.validation.prd_compliance_traceability_tests` | PRD-EDC-010 | 🟢 PASSED | < 1s |
| `test_edc_audit_trail_and_signatures` | `tests.validation.prd_compliance_traceability_tests` | PRD-EDC-006 | 🟢 PASSED | < 1s |
| `test_edc_concurrent_review_locks` | `tests.validation.prd_compliance_traceability_tests` | PRD-EDC-009 | 🟢 PASSED | < 1s |
| `test_edc_electronic_signatures` | `tests.validation.prd_compliance_traceability_tests` | PRD-EDC-007 | 🟢 PASSED | < 1s |
| `test_edc_reconsent_and_versioning` | `tests.validation.prd_compliance_traceability_tests` | PRD-EDC-008 | 🟢 PASSED | < 1s |
| `test_fda_compliant_pdf_generation` | `tests.validation.prd_compliance_traceability_tests` | PRD-SUB-007 | 🟢 PASSED | < 1s |
| `test_field_level_ingestion_validations` | `tests.validation.prd_compliance_traceability_tests` | PRD-EDC-002 | 🟢 PASSED | < 1s |
| `test_ie_criteria_logical_mapping_to_ecrf` | `tests.validation.prd_compliance_traceability_tests` | PRD-MDR-007 | 🟢 PASSED | < 1s |
| `test_query_lifecycle_states` | `tests.validation.prd_compliance_traceability_tests` | PRD-QRY-001 | 🟢 PASSED | < 1s |
| `test_spreadsheet_ingestion_sheet_structure` | `tests.validation.prd_compliance_traceability_tests` | PRD-EDC-001 | 🟢 PASSED | < 1s |
| `test_submission_archival_integration` | `tests.validation.prd_compliance_traceability_tests` | PRD-SUB-006 | 🟢 PASSED | < 1s |
| `test_submission_audit_trail` | `tests.validation.prd_compliance_traceability_tests` | PRD-SUB-004 | 🟢 PASSED | < 1s |
| `test_submission_e_signatures` | `tests.validation.prd_compliance_traceability_tests` | PRD-SUB-003 | 🟢 PASSED | < 1s |
| `test_submission_locks` | `tests.validation.prd_compliance_traceability_tests` | PRD-SUB-005 | 🟢 PASSED | < 1s |
| `test_submission_version_control` | `tests.validation.prd_compliance_traceability_tests` | PRD-SUB-002 | 🟢 PASSED | < 1s |
| `test_system_generated_validation_queries` | `tests.validation.prd_compliance_traceability_tests` | PRD-QRY-002 | 🟢 PASSED | < 1s |

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

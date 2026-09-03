# EDC Execution Engine

> **Functional Domain:** `Clinical Operations & Execution`
> **Service Host Port:** `localhost:8002`
> **Gateway Proxy Prefix:** `/api/v1/execution`
> **Persistence Backend:** `PostgreSQL Relational DB`

## Overview

Runtime Electronic Data Capture (EDC), dynamic subject eCRF schema projection, real-time edit checks, medical coding, lab toxicity, and Dataset-JSON exports.

---

## Endpoints Summary

| Method   | Path                                                                                                                                                | Summary                                   | GxP Auth & Signatures |
| :------- | :-------------------------------------------------------------------------------------------------------------------------------------------------- | :---------------------------------------- | :-------------------- |
| `GET`    | [/api/v1/admin/outbox](#get-api-v1-admin-outbox)                                                                                                    | Execution Admin Outbox Endpoint           | 👀 Read-Only          |
| `POST`   | [/api/v1/dictionaries/import](#post-api-v1-dictionaries-import)                                                                                     | Import Dictionary                         | ✍️ Audited Write      |
| `GET`    | [/api/v1/dictionaries/jobs/{job_id}](#get-api-v1-dictionaries-jobs-job_id)                                                                          | Get Dictionary Import Job                 | 👀 Read-Only          |
| `GET`    | [/api/v1/dictionaries/meddra/code](#get-api-v1-dictionaries-meddra-code)                                                                            | Get Meddra Code                           | 👀 Read-Only          |
| `POST`   | [/api/v1/dictionaries/ucum/convert](#post-api-v1-dictionaries-ucum-convert)                                                                         | Post Ucum Convert                         | ✍️ Audited Write      |
| `GET`    | [/api/v1/dictionaries/whodrug/code](#get-api-v1-dictionaries-whodrug-code)                                                                          | Get Whodrug Code                          | 👀 Read-Only          |
| `POST`   | [/api/v1/documents/upload](#post-api-v1-documents-upload)                                                                                           | Upload Document                           | ✍️ Audited Write      |
| `GET`    | [/api/v1/documents/{doc_id}](#get-api-v1-documents-doc_id)                                                                                          | Download Document                         | 👀 Read-Only          |
| `GET`    | [/api/v1/documents/{doc_id}/versions](#get-api-v1-documents-doc_id-versions)                                                                        | List Document Versions                    | 👀 Read-Only          |
| `POST`   | [/api/v1/execution/amendments/bulk-reconsent](#post-api-v1-execution-amendments-bulk-reconsent)                                                     | Register Bulk Subject Reconsent           | ✍️ Audited Write      |
| `POST`   | [/api/v1/execution/amendments/publish](#post-api-v1-execution-amendments-publish)                                                                   | Publish Amendment Endpoint                | ✍️ Audited Write      |
| `POST`   | [/api/v1/execution/amendments/reconsent](#post-api-v1-execution-amendments-reconsent)                                                               | Register Subject Reconsent                | ✍️ Audited Write      |
| `GET`    | [/api/v1/execution/amendments/summary/{study_id}/{version}](#get-api-v1-execution-amendments-summary-study_id-version)                              | Get Amendment Summary Endpoint            | 👀 Read-Only          |
| `POST`   | [/api/v1/execution/amendments/validate-gating](#post-api-v1-execution-amendments-validate-gating)                                                   | Validate Gating Endpoint                  | ✍️ Audited Write      |
| `POST`   | [/api/v1/execution/amendments/verify-training-gate](#post-api-v1-execution-amendments-verify-training-gate)                                         | Verify Training Gate Endpoint             | ✍️ Audited Write      |
| `GET`    | [/api/v1/execution/amendments/{study_id}/subject-impact](#get-api-v1-execution-amendments-study_id-subject-impact)                                  | Get Subject Impact Analysis               | 👀 Read-Only          |
| `GET`    | [/api/v1/execution/anomalies/candidates](#get-api-v1-execution-anomalies-candidates)                                                                | List Candidate Queries                    | 👀 Read-Only          |
| `POST`   | [/api/v1/execution/anomalies/candidates/{query_id}/adjudicate](#post-api-v1-execution-anomalies-candidates-query_id-adjudicate)                     | Adjudicate Candidate Query                | ✍️ Audited Write      |
| `POST`   | [/api/v1/execution/anomalies/evaluate](#post-api-v1-execution-anomalies-evaluate)                                                                   | Evaluate Anomalies                        | ✍️ Audited Write      |
| `POST`   | [/api/v1/execution/anonymization/redact-pdf](#post-api-v1-execution-anonymization-redact-pdf)                                                       | Redact Pdf Endpoint                       | ✍️ Audited Write      |
| `POST`   | [/api/v1/execution/anonymization/scan-phi](#post-api-v1-execution-anonymization-scan-phi)                                                           | Scan Phi Endpoint                         | ✍️ Audited Write      |
| `GET`    | [/api/v1/execution/audit/integrity](#get-api-v1-execution-audit-integrity)                                                                          | Get Execution Audit Integrity             | 👀 Read-Only          |
| `GET`    | [/api/v1/execution/auditor/inspect/audit-trail/{study_id}](#get-api-v1-execution-auditor-inspect-audit-trail-study_id)                              | Inspect Study Audit Trail Endpoint        | 👀 Read-Only          |
| `POST`   | [/api/v1/execution/auditor/token/generate](#post-api-v1-execution-auditor-token-generate)                                                           | Generate Auditor Token Endpoint           | ✍️ Audited Write      |
| `POST`   | [/api/v1/execution/batch-sign-off](#post-api-v1-execution-batch-sign-off)                                                                           | Post Batch Sign Off                       | 🔒 Step-Up Sig Token  |
| `GET`    | [/api/v1/execution/biostat/adam/{dataset}](#get-api-v1-execution-biostat-adam-dataset)                                                              | Export Adam Dataset                       | 👀 Read-Only          |
| `GET`    | [/api/v1/execution/biostat/bundle](#get-api-v1-execution-biostat-bundle)                                                                            | Export Biostat Bundle                     | 👀 Read-Only          |
| `GET`    | [/api/v1/execution/biostat/sdtm/{domain}](#get-api-v1-execution-biostat-sdtm-domain)                                                                | Export Sdtm Domain                        | 👀 Read-Only          |
| `POST`   | [/api/v1/execution/cec/charters](#post-api-v1-execution-cec-charters)                                                                               | Create Or Update Charter                  | ✍️ Audited Write      |
| `GET`    | [/api/v1/execution/cec/charters/{study_id}](#get-api-v1-execution-cec-charters-study_id)                                                            | Get Study Charter                         | 👀 Read-Only          |
| `POST`   | [/api/v1/execution/cec/dossiers](#post-api-v1-execution-cec-dossiers)                                                                               | Create And Package Dossier                | ✍️ Audited Write      |
| `GET`    | [/api/v1/execution/cec/dossiers](#get-api-v1-execution-cec-dossiers)                                                                                | List Dossiers                             | 👀 Read-Only          |
| `GET`    | [/api/v1/execution/cec/dossiers/{dossier_id}](#get-api-v1-execution-cec-dossiers-dossier_id)                                                        | Get Dossier Blinded View                  | 👀 Read-Only          |
| `POST`   | [/api/v1/execution/cec/dossiers/{dossier_id}/assessments](#post-api-v1-execution-cec-dossiers-dossier_id-assessments)                               | Submit Reader Assessment                  | ✍️ Audited Write      |
| `POST`   | [/api/v1/execution/cec/dossiers/{dossier_id}/chair-adjudicate](#post-api-v1-execution-cec-dossiers-dossier_id-chair-adjudicate)                     | Adjudicate Chair Tie Breaker              | ✍️ Audited Write      |
| `GET`    | [/api/v1/execution/cec/dossiers/{dossier_id}/full](#get-api-v1-execution-cec-dossiers-dossier_id-full)                                              | Get Dossier Full                          | 👀 Read-Only          |
| `POST`   | [/api/v1/execution/cec/dossiers/{dossier_id}/writeback](#post-api-v1-execution-cec-dossiers-dossier_id-writeback)                                   | Trigger Edc Writeback                     | ✍️ Audited Write      |
| `POST`   | [/api/v1/execution/cec/meetings](#post-api-v1-execution-cec-meetings)                                                                               | Record Meeting Quorum                     | ✍️ Audited Write      |
| `GET`    | [/api/v1/execution/cec/meetings/{study_id}](#get-api-v1-execution-cec-meetings-study_id)                                                            | List Study Meetings                       | 👀 Read-Only          |
| `POST`   | [/api/v1/execution/cec/sdtm-ce/compile](#post-api-v1-execution-cec-sdtm-ce-compile)                                                                 | Compile Sdtm Ce Dataset                   | ✍️ Audited Write      |
| `GET`    | [/api/v1/execution/coding/assignments](#get-api-v1-execution-coding-assignments)                                                                    | List Coding Assignments                   | 👀 Read-Only          |
| `POST`   | [/api/v1/execution/coding/assignments/batch-assign](#post-api-v1-execution-coding-assignments-batch-assign)                                         | Post Batch Assign                         | 🔒 Step-Up Sig Token  |
| `GET`    | [/api/v1/execution/coding/assignments/{assignment_id}](#get-api-v1-execution-coding-assignments-assignment_id)                                      | Get Coding Assignment                     | 👀 Read-Only          |
| `POST`   | [/api/v1/execution/coding/assignments/{assignment_id}/action](#post-api-v1-execution-coding-assignments-assignment_id-action)                       | Process Coding Action                     | 🔒 Step-Up Sig Token  |
| `POST`   | [/api/v1/execution/coding/assignments/{assignment_id}/raise-query](#post-api-v1-execution-coding-assignments-assignment_id-raise-query)             | Post Raise Coding Query                   | 🔒 Step-Up Sig Token  |
| `POST`   | [/api/v1/execution/coding/assignments/{assignment_id}/suggest](#post-api-v1-execution-coding-assignments-assignment_id-suggest)                     | Post Suggest Coding                       | 🔒 Step-Up Sig Token  |
| `POST`   | [/api/v1/execution/coding/impact-analysis](#post-api-v1-execution-coding-impact-analysis)                                                           | Post Impact Analysis                      | ✍️ Audited Write      |
| `GET`    | [/api/v1/execution/coding/queue](#get-api-v1-execution-coding-queue)                                                                                | Get Coding Queue                          | 👀 Read-Only          |
| `POST`   | [/api/v1/execution/devices/deficiencies](#post-api-v1-execution-devices-deficiencies)                                                               | Log Deficiency                            | ✍️ Audited Write      |
| `GET`    | [/api/v1/execution/devices/deficiencies](#get-api-v1-execution-devices-deficiencies)                                                                | List Deficiencies                         | 👀 Read-Only          |
| `GET`    | [/api/v1/execution/devices/deficiencies/{deficiency_id}](#get-api-v1-execution-devices-deficiencies-deficiency_id)                                  | Get Deficiency                            | 👀 Read-Only          |
| `POST`   | [/api/v1/execution/devices/dispense](#post-api-v1-execution-devices-dispense)                                                                       | Dispense                                  | ✍️ Audited Write      |
| `POST`   | [/api/v1/execution/devices/gudid/lookup](#post-api-v1-execution-devices-gudid-lookup)                                                               | Lookup Gudid Endpoint                     | ✍️ Audited Write      |
| `POST`   | [/api/v1/execution/devices/inventory](#post-api-v1-execution-devices-inventory)                                                                     | Register Inventory                        | ✍️ Audited Write      |
| `GET`    | [/api/v1/execution/devices/inventory](#get-api-v1-execution-devices-inventory)                                                                      | List Inventory                            | 👀 Read-Only          |
| `POST`   | [/api/v1/execution/devices/ivd/assays/performance](#post-api-v1-execution-devices-ivd-assays-performance)                                           | Compute Assay Performance Endpoint        | ✍️ Audited Write      |
| `POST`   | [/api/v1/execution/devices/ivd/specimens](#post-api-v1-execution-devices-ivd-specimens)                                                             | Register Specimen Endpoint                | ✍️ Audited Write      |
| `GET`    | [/api/v1/execution/devices/ivd/specimens](#get-api-v1-execution-devices-ivd-specimens)                                                              | List Specimens Endpoint                   | 👀 Read-Only          |
| `GET`    | [/api/v1/execution/devices/ivd/specimens/{specimen_id}](#get-api-v1-execution-devices-ivd-specimens-specimen_id)                                    | Get Specimen Endpoint                     | 👀 Read-Only          |
| `POST`   | [/api/v1/execution/devices/ivd/specimens/{specimen_id}/chain-of-custody](#post-api-v1-execution-devices-ivd-specimens-specimen_id-chain-of-custody) | Log Custody Endpoint                      | ✍️ Audited Write      |
| `POST`   | [/api/v1/execution/devices/ivd/specimens/{specimen_id}/freeze-thaw](#post-api-v1-execution-devices-ivd-specimens-specimen_id-freeze-thaw)           | Log Freeze Thaw Endpoint                  | ✍️ Audited Write      |
| `POST`   | [/api/v1/execution/devices/ivd/specimens/{specimen_id}/integrity-check](#post-api-v1-execution-devices-ivd-specimens-specimen_id-integrity-check)   | Sample Integrity Check Endpoint           | ✍️ Audited Write      |
| `POST`   | [/api/v1/execution/devices/parse-udi](#post-api-v1-execution-devices-parse-udi)                                                                     | Parse Udi                                 | ✍️ Audited Write      |
| `POST`   | [/api/v1/execution/devices/quarantine](#post-api-v1-execution-devices-quarantine)                                                                   | Initiate Quarantine                       | ✍️ Audited Write      |
| `GET`    | [/api/v1/execution/devices/quarantine](#get-api-v1-execution-devices-quarantine)                                                                    | List Quarantines                          | 👀 Read-Only          |
| `POST`   | [/api/v1/execution/devices/quarantine/release](#post-api-v1-execution-devices-quarantine-release)                                                   | Release Quarantine                        | ✍️ Audited Write      |
| `GET`    | [/api/v1/execution/devices/summary](#get-api-v1-execution-devices-summary)                                                                          | Get Device Summary                        | 👀 Read-Only          |
| `POST`   | [/api/v1/execution/devices/verify-udi](#post-api-v1-execution-devices-verify-udi)                                                                   | Verify Udi Endpoint                       | ✍️ Audited Write      |
| `POST`   | [/api/v1/execution/doa/assignment](#post-api-v1-execution-doa-assignment)                                                                           | Add Doa Assignment Endpoint               | 🔒 Step-Up Sig Token  |
| `GET`    | [/api/v1/execution/doa/audit-logs](#get-api-v1-execution-doa-audit-logs)                                                                            | Get Audit Logs Endpoint                   | 👀 Read-Only          |
| `POST`   | [/api/v1/execution/doa/delegate](#post-api-v1-execution-doa-delegate)                                                                               | Delegate Task Endpoint                    | ✍️ Audited Write      |
| `GET`    | [/api/v1/execution/doa/delegations](#get-api-v1-execution-doa-delegations)                                                                          | Get Delegations Endpoint                  | 👀 Read-Only          |
| `POST`   | [/api/v1/execution/doa/endorse](#post-api-v1-execution-doa-endorse)                                                                                 | Approve Delegation Endpoint               | ✍️ Audited Write      |
| `POST`   | [/api/v1/execution/doa/endorse_task](#post-api-v1-execution-doa-endorse_task)                                                                       | Approve Task Endpoint                     | ✍️ Audited Write      |
| `GET`    | [/api/v1/execution/doa/log/{study_id}/{site_id}](#get-api-v1-execution-doa-log-study_id-site_id)                                                    | Get Site Doa Log Endpoint                 | 👀 Read-Only          |
| `POST`   | [/api/v1/execution/doa/revoke](#post-api-v1-execution-doa-revoke)                                                                                   | Revoke Delegation Endpoint                | ✍️ Audited Write      |
| `POST`   | [/api/v1/execution/doa/sign-off](#post-api-v1-execution-doa-sign-off)                                                                               | Sign Off Doa Assignment Endpoint          | 🔒 Step-Up Sig Token  |
| `POST`   | [/api/v1/execution/doa/staff](#post-api-v1-execution-doa-staff)                                                                                     | Create Staff Endpoint                     | ✍️ Audited Write      |
| `GET`    | [/api/v1/execution/eisf/binder/{study_id}/{site_id}](#get-api-v1-execution-eisf-binder-study_id-site_id)                                            | Get Site Regulatory Binder Endpoint       | 👀 Read-Only          |
| `POST`   | [/api/v1/execution/eisf/upload](#post-api-v1-execution-eisf-upload)                                                                                 | Upload Eisf Document Endpoint             | ✍️ Audited Write      |
| `GET`    | [/api/v1/execution/export](#get-api-v1-execution-export)                                                                                            | Export Study Cdisc Odm Xml                | 👀 Read-Only          |
| `POST`   | [/api/v1/execution/exports/wizard](#post-api-v1-execution-exports-wizard)                                                                           | Execute Export Wizard                     | ✍️ Audited Write      |
| `POST`   | [/api/v1/execution/form-submissions](#post-api-v1-execution-form-submissions)                                                                       | Create Form Submission                    | ✍️ Audited Write      |
| `GET`    | [/api/v1/execution/form-submissions](#get-api-v1-execution-form-submissions)                                                                        | List Form Submissions                     | 👀 Read-Only          |
| `GET`    | [/api/v1/execution/form-submissions/{submission_id}](#get-api-v1-execution-form-submissions-submission_id)                                          | Get Form Submission                       | 👀 Read-Only          |
| `POST`   | [/api/v1/execution/form-submissions/{submission_id}/approve](#post-api-v1-execution-form-submissions-submission_id-approve)                         | Approve Form Submission                   | ✍️ Audited Write      |
| `POST`   | [/api/v1/execution/form-submissions/{submission_id}/complete](#post-api-v1-execution-form-submissions-submission_id-complete)                       | Complete Form Submission                  | ✍️ Audited Write      |
| `POST`   | [/api/v1/execution/lab-ranges](#post-api-v1-execution-lab-ranges)                                                                                   | Create Lab Range                          | ✍️ Audited Write      |
| `GET`    | [/api/v1/execution/lab-ranges](#get-api-v1-execution-lab-ranges)                                                                                    | List Lab Ranges                           | 👀 Read-Only          |
| `POST`   | [/api/v1/execution/lab-ranges/recalculate](#post-api-v1-execution-lab-ranges-recalculate)                                                           | Trigger Lab Range Recalculation           | ✍️ Audited Write      |
| `GET`    | [/api/v1/execution/lab-ranges/{range_id}](#get-api-v1-execution-lab-ranges-range_id)                                                                | Get Lab Range                             | 👀 Read-Only          |
| `PUT`    | [/api/v1/execution/lab-ranges/{range_id}](#put-api-v1-execution-lab-ranges-range_id)                                                                | Update Lab Range                          | ✍️ Audited Write      |
| `DELETE` | [/api/v1/execution/lab-ranges/{range_id}](#delete-api-v1-execution-lab-ranges-range_id)                                                             | Delete Lab Range                          | ✍️ Audited Write      |
| `GET`    | [/api/v1/execution/labs/batch-status](#get-api-v1-execution-labs-batch-status)                                                                      | Get Batch Status Endpoint                 | 👀 Read-Only          |
| `GET`    | [/api/v1/execution/labs/batch-status/{batch_id}](#get-api-v1-execution-labs-batch-status-batch_id)                                                  | Get Batch Status By Id Endpoint           | 👀 Read-Only          |
| `POST`   | [/api/v1/execution/labs/certifications](#post-api-v1-execution-labs-certifications)                                                                 | Create Local Lab Certification            | ✍️ Audited Write      |
| `GET`    | [/api/v1/execution/labs/certifications](#get-api-v1-execution-labs-certifications)                                                                  | List Local Lab Certifications             | 👀 Read-Only          |
| `POST`   | [/api/v1/execution/labs/certifications/verify](#post-api-v1-execution-labs-certifications-verify)                                                   | Verify Local Lab Certification Gate       | ✍️ Audited Write      |
| `PUT`    | [/api/v1/execution/labs/certifications/{cert_id}](#put-api-v1-execution-labs-certifications-cert_id)                                                | Update Local Lab Certification            | ✍️ Audited Write      |
| `POST`   | [/api/v1/execution/labs/certifications/{site_id}/verify](#post-api-v1-execution-labs-certifications-site_id-verify)                                 | Verify Local Lab Certification Gate       | ✍️ Audited Write      |
| `POST`   | [/api/v1/execution/labs/convert-unit](#post-api-v1-execution-labs-convert-unit)                                                                     | Convert Unit Endpoint                     | ✍️ Audited Write      |
| `POST`   | [/api/v1/execution/labs/ingest](#post-api-v1-execution-labs-ingest)                                                                                 | Ingest Lab Batch Endpoint                 | ✍️ Audited Write      |
| `GET`    | [/api/v1/execution/labs/loinc-catalog](#get-api-v1-execution-labs-loinc-catalog)                                                                    | Get Loinc Catalog Endpoint                | 👀 Read-Only          |
| `POST`   | [/api/v1/execution/labs/toxicity/evaluate](#post-api-v1-execution-labs-toxicity-evaluate)                                                           | Evaluate Lab Toxicity Endpoint            | ✍️ Audited Write      |
| `GET`    | [/api/v1/execution/labs/toxicity/review-queue](#get-api-v1-execution-labs-toxicity-review-queue)                                                    | List Staged Ae Review Queue               | 👀 Read-Only          |
| `POST`   | [/api/v1/execution/labs/toxicity/review/{review_id}/action](#post-api-v1-execution-labs-toxicity-review-review_id-action)                           | Action Staged Ae Review                   | ✍️ Audited Write      |
| `GET`    | [/api/v1/execution/locks](#get-api-v1-execution-locks)                                                                                              | Get Lock Status                           | 👀 Read-Only          |
| `POST`   | [/api/v1/execution/locks/form/{form_id}/freeze](#post-api-v1-execution-locks-form-form_id-freeze)                                                   | Lock Form Endpoint                        | ✍️ Audited Write      |
| `POST`   | [/api/v1/execution/locks/form/{form_id}/lock](#post-api-v1-execution-locks-form-form_id-lock)                                                       | Lock Form Endpoint                        | ✍️ Audited Write      |
| `POST`   | [/api/v1/execution/locks/form/{form_id}/unfreeze](#post-api-v1-execution-locks-form-form_id-unfreeze)                                               | Unlock Form Endpoint                      | ✍️ Audited Write      |
| `POST`   | [/api/v1/execution/locks/form/{form_id}/unlock](#post-api-v1-execution-locks-form-form_id-unlock)                                                   | Unlock Form Endpoint                      | ✍️ Audited Write      |
| `POST`   | [/api/v1/execution/locks/lock](#post-api-v1-execution-locks-lock)                                                                                   | Lock Data Endpoint                        | ✍️ Audited Write      |
| `POST`   | [/api/v1/execution/locks/site/{site_id}/freeze](#post-api-v1-execution-locks-site-site_id-freeze)                                                   | Lock Site Endpoint                        | ✍️ Audited Write      |
| `POST`   | [/api/v1/execution/locks/site/{site_id}/lock](#post-api-v1-execution-locks-site-site_id-lock)                                                       | Lock Site Endpoint                        | ✍️ Audited Write      |
| `POST`   | [/api/v1/execution/locks/site/{site_id}/unfreeze](#post-api-v1-execution-locks-site-site_id-unfreeze)                                               | Unlock Site Endpoint                      | ✍️ Audited Write      |
| `POST`   | [/api/v1/execution/locks/site/{site_id}/unlock](#post-api-v1-execution-locks-site-site_id-unlock)                                                   | Unlock Site Endpoint                      | ✍️ Audited Write      |
| `GET`    | [/api/v1/execution/locks/status/{form_id}](#get-api-v1-execution-locks-status-form_id)                                                              | Get Form Lock Status Endpoint             | 👀 Read-Only          |
| `POST`   | [/api/v1/execution/locks/subject/{subject_id}/freeze](#post-api-v1-execution-locks-subject-subject_id-freeze)                                       | Lock Subject Endpoint                     | ✍️ Audited Write      |
| `POST`   | [/api/v1/execution/locks/subject/{subject_id}/lock](#post-api-v1-execution-locks-subject-subject_id-lock)                                           | Lock Subject Endpoint                     | ✍️ Audited Write      |
| `POST`   | [/api/v1/execution/locks/subject/{subject_id}/unfreeze](#post-api-v1-execution-locks-subject-subject_id-unfreeze)                                   | Unlock Subject Endpoint                   | ✍️ Audited Write      |
| `POST`   | [/api/v1/execution/locks/subject/{subject_id}/unlock](#post-api-v1-execution-locks-subject-subject_id-unlock)                                       | Unlock Subject Endpoint                   | ✍️ Audited Write      |
| `GET`    | [/api/v1/execution/locks/tree](#get-api-v1-execution-locks-tree)                                                                                    | Get Lock Hierarchy Tree Endpoint          | 👀 Read-Only          |
| `POST`   | [/api/v1/execution/locks/trial/freeze](#post-api-v1-execution-locks-trial-freeze)                                                                   | Lock Trial Endpoint                       | ✍️ Audited Write      |
| `POST`   | [/api/v1/execution/locks/trial/lock](#post-api-v1-execution-locks-trial-lock)                                                                       | Lock Trial Endpoint                       | ✍️ Audited Write      |
| `POST`   | [/api/v1/execution/locks/trial/unfreeze](#post-api-v1-execution-locks-trial-unfreeze)                                                               | Unlock Trial Endpoint                     | ✍️ Audited Write      |
| `POST`   | [/api/v1/execution/locks/trial/unlock](#post-api-v1-execution-locks-trial-unlock)                                                                   | Unlock Trial Endpoint                     | ✍️ Audited Write      |
| `POST`   | [/api/v1/execution/locks/unlock](#post-api-v1-execution-locks-unlock)                                                                               | Unlock Data Endpoint                      | ✍️ Audited Write      |
| `POST`   | [/api/v1/execution/locks/visit/{visit_id}/freeze](#post-api-v1-execution-locks-visit-visit_id-freeze)                                               | Lock Visit Endpoint                       | ✍️ Audited Write      |
| `POST`   | [/api/v1/execution/locks/visit/{visit_id}/lock](#post-api-v1-execution-locks-visit-visit_id-lock)                                                   | Lock Visit Endpoint                       | ✍️ Audited Write      |
| `POST`   | [/api/v1/execution/locks/visit/{visit_id}/unfreeze](#post-api-v1-execution-locks-visit-visit_id-unfreeze)                                           | Unlock Visit Endpoint                     | ✍️ Audited Write      |
| `POST`   | [/api/v1/execution/locks/visit/{visit_id}/unlock](#post-api-v1-execution-locks-visit-visit_id-unlock)                                               | Unlock Visit Endpoint                     | ✍️ Audited Write      |
| `POST`   | [/api/v1/execution/migration-rules](#post-api-v1-execution-migration-rules)                                                                         | Create Migration Rule                     | ✍️ Audited Write      |
| `GET`    | [/api/v1/execution/migration-rules](#get-api-v1-execution-migration-rules)                                                                          | List Migration Rules                      | 👀 Read-Only          |
| `POST`   | [/api/v1/execution/observations](#post-api-v1-execution-observations)                                                                               | Create Observation                        | ✍️ Audited Write      |
| `POST`   | [/api/v1/execution/offline/sync](#post-api-v1-execution-offline-sync)                                                                               | Offline Sync Endpoint                     | ✍️ Audited Write      |
| `POST`   | [/api/v1/execution/outliers/recalculate](#post-api-v1-execution-outliers-recalculate)                                                               | Trigger Outlier Recalculation             | ✍️ Audited Write      |
| `GET`    | [/api/v1/execution/queries](#get-api-v1-execution-queries)                                                                                          | List Queries                              | 👀 Read-Only          |
| `POST`   | [/api/v1/execution/queries](#post-api-v1-execution-queries)                                                                                         | Open Query                                | ✍️ Audited Write      |
| `POST`   | [/api/v1/execution/queries/generate](#post-api-v1-execution-queries-generate)                                                                       | Bulk Generate Queries                     | ✍️ Audited Write      |
| `POST`   | [/api/v1/execution/queries/sync](#post-api-v1-execution-queries-sync)                                                                               | Sync Queries                              | ✍️ Audited Write      |
| `GET`    | [/api/v1/execution/queries/{query_id}](#get-api-v1-execution-queries-query_id)                                                                      | Get Query                                 | 👀 Read-Only          |
| `PATCH`  | [/api/v1/execution/queries/{query_id}](#patch-api-v1-execution-queries-query_id)                                                                    | Update Query State                        | ✍️ Audited Write      |
| `POST`   | [/api/v1/execution/queries/{query_id}/cancel](#post-api-v1-execution-queries-query_id-cancel)                                                       | Cancel Query                              | ✍️ Audited Write      |
| `POST`   | [/api/v1/execution/queries/{query_id}/close](#post-api-v1-execution-queries-query_id-close)                                                         | Close Query                               | ✍️ Audited Write      |
| `POST`   | [/api/v1/execution/queries/{query_id}/reopen](#post-api-v1-execution-queries-query_id-reopen)                                                       | Reopen Query                              | ✍️ Audited Write      |
| `POST`   | [/api/v1/execution/queries/{query_id}/respond](#post-api-v1-execution-queries-query_id-respond)                                                     | Respond Query                             | ✍️ Audited Write      |
| `POST`   | [/api/v1/execution/rtsm/adaptive/boin/calculate-boundaries](#post-api-v1-execution-rtsm-adaptive-boin-calculate-boundaries)                         | Calculate Boin Boundaries Endpoint        | ✍️ Audited Write      |
| `POST`   | [/api/v1/execution/rtsm/adaptive/cohorts/evaluate](#post-api-v1-execution-rtsm-adaptive-cohorts-evaluate)                                           | Evaluate Adaptive Cohort Endpoint         | ✍️ Audited Write      |
| `GET`    | [/api/v1/execution/rtsm/adaptive/cohorts/history](#get-api-v1-execution-rtsm-adaptive-cohorts-history)                                              | Get Adaptive Cohorts History Endpoint     | 👀 Read-Only          |
| `POST`   | [/api/v1/execution/rtsm/adaptive/cohorts/transition](#post-api-v1-execution-rtsm-adaptive-cohorts-transition)                                       | Transition Adaptive Cohort Endpoint       | ✍️ Audited Write      |
| `POST`   | [/api/v1/execution/rtsm/adaptive/config](#post-api-v1-execution-rtsm-adaptive-config)                                                               | Create Or Update Adaptive Config Endpoint | ✍️ Audited Write      |
| `GET`    | [/api/v1/execution/rtsm/adaptive/config](#get-api-v1-execution-rtsm-adaptive-config)                                                                | Get Adaptive Config Endpoint              | 👀 Read-Only          |
| `POST`   | [/api/v1/execution/rtsm/adaptive/crm/posterior](#post-api-v1-execution-rtsm-adaptive-crm-posterior)                                                 | Calculate Crm Posterior Endpoint          | ✍️ Audited Write      |
| `POST`   | [/api/v1/execution/rtsm/adaptive/rar/arms](#post-api-v1-execution-rtsm-adaptive-rar-arms)                                                           | Expand Or Terminate Rar Arm Endpoint      | ✍️ Audited Write      |
| `POST`   | [/api/v1/execution/rtsm/adaptive/rar/evaluate](#post-api-v1-execution-rtsm-adaptive-rar-evaluate)                                                   | Evaluate Rar Endpoint                     | ✍️ Audited Write      |
| `GET`    | [/api/v1/execution/rtsm/adaptive/rar/state](#get-api-v1-execution-rtsm-adaptive-rar-state)                                                          | Get Rar State Endpoint                    | 👀 Read-Only          |
| `POST`   | [/api/v1/execution/rtsm/cgt/apheresis](#post-api-v1-execution-rtsm-cgt-apheresis)                                                                   | Initialize Apheresis Endpoint             | ✍️ Audited Write      |
| `POST`   | [/api/v1/execution/rtsm/cgt/bedside-verify](#post-api-v1-execution-rtsm-cgt-bedside-verify)                                                         | Bedside Dual Verification Endpoint        | ✍️ Audited Write      |
| `GET`    | [/api/v1/execution/rtsm/cgt/certificates/{study_id}](#get-api-v1-execution-rtsm-cgt-certificates-study_id)                                          | List Destruction Certificates Endpoint    | 👀 Read-Only          |
| `GET`    | [/api/v1/execution/rtsm/cgt/chain-of-custody/{study_id}/{subject_id}](#get-api-v1-execution-rtsm-cgt-chain-of-custody-study_id-subject_id)          | Get Subject Cgt Traceability Endpoint     | 👀 Read-Only          |
| `POST`   | [/api/v1/execution/rtsm/cgt/destruction-certificate](#post-api-v1-execution-rtsm-cgt-destruction-certificate)                                       | Generate Destruction Certificate Endpoint | ✍️ Audited Write      |
| `POST`   | [/api/v1/execution/rtsm/cgt/infuse](#post-api-v1-execution-rtsm-cgt-infuse)                                                                         | Execute Infusion Endpoint                 | ✍️ Audited Write      |
| `POST`   | [/api/v1/execution/rtsm/cgt/manufacturing-bind](#post-api-v1-execution-rtsm-cgt-manufacturing-bind)                                                 | Bind Manufacturing Batch Endpoint         | ✍️ Audited Write      |
| `POST`   | [/api/v1/execution/rtsm/cgt/milestone](#post-api-v1-execution-rtsm-cgt-milestone)                                                                   | Record Cgt Milestone Endpoint             | ✍️ Audited Write      |
| `GET`    | [/api/v1/execution/rtsm/cgt/reconciliations/{study_id}](#get-api-v1-execution-rtsm-cgt-reconciliations-study_id)                                    | List Vial Reconciliations Endpoint        | 👀 Read-Only          |
| `POST`   | [/api/v1/execution/rtsm/cgt/returns](#post-api-v1-execution-rtsm-cgt-returns)                                                                       | Log Vial Return Endpoint                  | ✍️ Audited Write      |
| `POST`   | [/api/v1/execution/rtsm/cgt/verify-barcode](#post-api-v1-execution-rtsm-cgt-verify-barcode)                                                         | Verify Cgt Barcode Endpoint               | ✍️ Audited Write      |
| `POST`   | [/api/v1/execution/rtsm/cold-chain/evaluate-mkt](#post-api-v1-execution-rtsm-cold-chain-evaluate-mkt)                                               | Evaluate Mkt Direct Endpoint              | ✍️ Audited Write      |
| `GET`    | [/api/v1/execution/rtsm/cold-chain/evaluations](#get-api-v1-execution-rtsm-cold-chain-evaluations)                                                  | List Excursion Evaluations Endpoint       | 👀 Read-Only          |
| `POST`   | [/api/v1/execution/rtsm/cold-chain/ingest](#post-api-v1-execution-rtsm-cold-chain-ingest)                                                           | Ingest Temperature Logger File Endpoint   | ✍️ Audited Write      |
| `POST`   | [/api/v1/execution/rtsm/cold-chain/loggers](#post-api-v1-execution-rtsm-cold-chain-loggers)                                                         | Register Temperature Logger Endpoint      | ✍️ Audited Write      |
| `GET`    | [/api/v1/execution/rtsm/cold-chain/loggers](#get-api-v1-execution-rtsm-cold-chain-loggers)                                                          | List Temperature Loggers Endpoint         | 👀 Read-Only          |
| `POST`   | [/api/v1/execution/rtsm/dispense](#post-api-v1-execution-rtsm-dispense)                                                                             | Dispense Kit Endpoint                     | ✍️ Audited Write      |
| `POST`   | [/api/v1/execution/rtsm/forecast/site](#post-api-v1-execution-rtsm-forecast-site)                                                                   | Forecast Site Resupply Endpoint           | ✍️ Audited Write      |
| `POST`   | [/api/v1/execution/rtsm/forecast/study](#post-api-v1-execution-rtsm-forecast-study)                                                                 | Forecast Study Resupply Endpoint          | ✍️ Audited Write      |
| `GET`    | [/api/v1/execution/rtsm/hierarchy](#get-api-v1-execution-rtsm-hierarchy)                                                                            | Get Supply Hierarchy Endpoint             | 👀 Read-Only          |
| `POST`   | [/api/v1/execution/rtsm/kits](#post-api-v1-execution-rtsm-kits)                                                                                     | Create Kit Endpoint                       | ✍️ Audited Write      |
| `GET`    | [/api/v1/execution/rtsm/kits](#get-api-v1-execution-rtsm-kits)                                                                                      | List Kits Endpoint                        | 👀 Read-Only          |
| `POST`   | [/api/v1/execution/rtsm/kits/quarantine](#post-api-v1-execution-rtsm-kits-quarantine)                                                               | Quarantine Kits Endpoint                  | ✍️ Audited Write      |
| `POST`   | [/api/v1/execution/rtsm/kits/release-quarantine](#post-api-v1-execution-rtsm-kits-release-quarantine)                                               | Release Quarantine Kits Endpoint          | ✍️ Audited Write      |
| `POST`   | [/api/v1/execution/rtsm/lots/extend-expiry](#post-api-v1-execution-rtsm-lots-extend-expiry)                                                         | Extend Lot Expiry Endpoint                | ✍️ Audited Write      |
| `GET`    | [/api/v1/execution/rtsm/lots/extensions](#get-api-v1-execution-rtsm-lots-extensions)                                                                | List Lot Expiry Extensions Endpoint       | 👀 Read-Only          |
| `POST`   | [/api/v1/execution/rtsm/nodes](#post-api-v1-execution-rtsm-nodes)                                                                                   | Create Supply Node Endpoint               | ✍️ Audited Write      |
| `GET`    | [/api/v1/execution/rtsm/nodes/{node_id}/inventory](#get-api-v1-execution-rtsm-nodes-node_id-inventory)                                              | Get Node Inventory Endpoint               | 👀 Read-Only          |
| `POST`   | [/api/v1/execution/rtsm/nodes/{node_id}/inventory/adjust](#post-api-v1-execution-rtsm-nodes-node_id-inventory-adjust)                               | Adjust Node Inventory Endpoint            | ✍️ Audited Write      |
| `GET`    | [/api/v1/execution/rtsm/relabeling-tasks](#get-api-v1-execution-rtsm-relabeling-tasks)                                                              | List Relabeling Tasks Endpoint            | 👀 Read-Only          |
| `POST`   | [/api/v1/execution/rtsm/relabeling-tasks/{task_id}/complete](#post-api-v1-execution-rtsm-relabeling-tasks-task_id-complete)                         | Complete Relabeling Task Endpoint         | ✍️ Audited Write      |
| `POST`   | [/api/v1/execution/rtsm/relabeling-tasks/{task_id}/verify](#post-api-v1-execution-rtsm-relabeling-tasks-task_id-verify)                             | Verify Relabeling Task Endpoint           | ✍️ Audited Write      |
| `GET`    | [/api/v1/execution/rtsm/resupply-events](#get-api-v1-execution-rtsm-resupply-events)                                                                | List Resupply Events Endpoint             | 👀 Read-Only          |
| `POST`   | [/api/v1/execution/rtsm/resupply-events/{event_id}/confirm](#post-api-v1-execution-rtsm-resupply-events-event_id-confirm)                           | Approve Resupply Event Endpoint           | ✍️ Audited Write      |
| `POST`   | [/api/v1/execution/rtsm/resupply-events/{event_id}/reject](#post-api-v1-execution-rtsm-resupply-events-event_id-reject)                             | Reject Resupply Event Endpoint            | ✍️ Audited Write      |
| `POST`   | [/api/v1/execution/rtsm/stability-budgets](#post-api-v1-execution-rtsm-stability-budgets)                                                           | Create Stability Budget Endpoint          | ✍️ Audited Write      |
| `GET`    | [/api/v1/execution/rtsm/stability-budgets](#get-api-v1-execution-rtsm-stability-budgets)                                                            | List Stability Budgets Endpoint           | 👀 Read-Only          |
| `POST`   | [/api/v1/execution/rtsm/transfer-orders](#post-api-v1-execution-rtsm-transfer-orders)                                                               | Create Transfer Order Endpoint            | ✍️ Audited Write      |
| `GET`    | [/api/v1/execution/rtsm/transfer-orders](#get-api-v1-execution-rtsm-transfer-orders)                                                                | List Transfer Orders Endpoint             | 👀 Read-Only          |
| `POST`   | [/api/v1/execution/rtsm/transfer-orders/{order_id}/approve](#post-api-v1-execution-rtsm-transfer-orders-order_id-approve)                           | Approve Transfer Order Endpoint           | ✍️ Audited Write      |
| `POST`   | [/api/v1/execution/rtsm/transfer-orders/{order_id}/manifest](#post-api-v1-execution-rtsm-transfer-orders-order_id-manifest)                         | Generate Packing Manifest Endpoint        | ✍️ Audited Write      |
| `POST`   | [/api/v1/execution/rtsm/transfer-orders/{order_id}/receive](#post-api-v1-execution-rtsm-transfer-orders-order_id-receive)                           | Receive Transfer Order Endpoint           | ✍️ Audited Write      |
| `POST`   | [/api/v1/execution/rtsm/transfer-orders/{order_id}/ship](#post-api-v1-execution-rtsm-transfer-orders-order_id-ship)                                 | Ship Transfer Order Endpoint              | ✍️ Audited Write      |
| `POST`   | [/api/v1/execution/safety/dispatch](#post-api-v1-execution-safety-dispatch)                                                                         | Dispatch Safety Report Endpoint           | ✍️ Audited Write      |
| `POST`   | [/api/v1/execution/safety/reconcile](#post-api-v1-execution-safety-reconcile)                                                                       | Reconcile Sae Cases Endpoint              | ✍️ Audited Write      |
| `POST`   | [/api/v1/execution/sdv/bulk-sign-off](#post-api-v1-execution-sdv-bulk-sign-off)                                                                     | Bulk Sdv Signoff                          | 🔒 Step-Up Sig Token  |
| `POST`   | [/api/v1/execution/sdv/signoff](#post-api-v1-execution-sdv-signoff)                                                                                 | Sdv Signoff                               | 🔒 Step-Up Sig Token  |
| `POST`   | [/api/v1/execution/signatures/batch-sign-off](#post-api-v1-execution-signatures-batch-sign-off)                                                     | Batch Signature Sign Off Endpoint         | 🔒 Step-Up Sig Token  |
| `POST`   | [/api/v1/execution/signatures/sign](#post-api-v1-execution-signatures-sign)                                                                         | Sign Payload Endpoint                     | 🔒 Step-Up Sig Token  |
| `POST`   | [/api/v1/execution/signatures/verify](#post-api-v1-execution-signatures-verify)                                                                     | Verify Signature Endpoint                 | 🔒 Step-Up Sig Token  |
| `POST`   | [/api/v1/execution/sites/{site_id}/activate](#post-api-v1-execution-sites-site_id-activate)                                                         | Activate Site Endpoint                    | ✍️ Audited Write      |
| `GET`    | [/api/v1/execution/sites/{site_id}/compliance-status](#get-api-v1-execution-sites-site_id-compliance-status)                                        | Get Site Compliance Status                | 👀 Read-Only          |
| `POST`   | [/api/v1/execution/subjects](#post-api-v1-execution-subjects)                                                                                       | Create Subject                            | ✍️ Audited Write      |
| `PUT`    | [/api/v1/execution/subjects/{id}/demographics](#put-api-v1-execution-subjects-id-demographics)                                                      | Update Subject Demographics Endpoint      | ✍️ Audited Write      |
| `DELETE` | [/api/v1/execution/subjects/{id}/demographics](#delete-api-v1-execution-subjects-id-demographics)                                                   | Delete Subject Demographics Endpoint      | ✍️ Audited Write      |
| `PATCH`  | [/api/v1/execution/subjects/{id}/state](#patch-api-v1-execution-subjects-id-state)                                                                  | Update Subject State Endpoint             | ✍️ Audited Write      |
| `GET`    | [/api/v1/execution/subjects/{subject_id}](#get-api-v1-execution-subjects-subject_id)                                                                | Get Subject Detail                        | 👀 Read-Only          |
| `POST`   | [/api/v1/execution/subjects/{subject_id}/consent](#post-api-v1-execution-subjects-subject_id-consent)                                               | Record Subject Consent Endpoint           | ✍️ Audited Write      |
| `POST`   | [/api/v1/execution/subjects/{subject_id}/consents](#post-api-v1-execution-subjects-subject_id-consents)                                             | Record Subject Consent Endpoint           | ✍️ Audited Write      |
| `POST`   | [/api/v1/execution/subjects/{subject_id}/randomize](#post-api-v1-execution-subjects-subject_id-randomize)                                           | Randomize Subject Endpoint                | ✍️ Audited Write      |
| `POST`   | [/api/v1/execution/subjects/{subject_id}/screening](#post-api-v1-execution-subjects-subject_id-screening)                                           | Evaluate And Transition Screening         | ✍️ Audited Write      |
| `POST`   | [/api/v1/execution/subjects/{subject_id}/unblind](#post-api-v1-execution-subjects-subject_id-unblind)                                               | Unblind Subject                           | ✍️ Audited Write      |
| `GET`    | [/api/v1/execution/translation/jobs](#get-api-v1-execution-translation-jobs)                                                                        | List Translation Jobs                     | 👀 Read-Only          |
| `GET`    | [/api/v1/execution/translation/jobs/{job_id}](#get-api-v1-execution-translation-jobs-job_id)                                                        | Get Translation Job                       | 👀 Read-Only          |
| `POST`   | [/api/v1/execution/tsdv/config](#post-api-v1-execution-tsdv-config)                                                                                 | Create Or Update Tsdv Config              | ✍️ Audited Write      |
| `GET`    | [/api/v1/execution/tsdv/config/{study_id}](#get-api-v1-execution-tsdv-config-study_id)                                                              | Get Tsdv Config                           | 👀 Read-Only          |
| `GET`    | [/api/v1/execution/tsdv/required](#get-api-v1-execution-tsdv-required)                                                                              | Evaluate Tsdv Rule                        | 👀 Read-Only          |
| `POST`   | [/api/v1/execution/visits](#post-api-v1-execution-visits)                                                                                           | Create Visit                              | ✍️ Audited Write      |
| `GET`    | [/api/v1/execution/visits/{visit_id}](#get-api-v1-execution-visits-visit_id)                                                                        | Get Visit Detail                          | 👀 Read-Only          |
| `POST`   | [/api/v1/execution/webhooks/etmf](#post-api-v1-execution-webhooks-etmf)                                                                             | Etmf Completeness Webhook                 | ✍️ Audited Write      |
| `POST`   | [/api/v1/offline/sync-batch](#post-api-v1-offline-sync-batch)                                                                                       | Sync Offline Batch                        | ✍️ Audited Write      |
| `GET`    | [/dictionary/export](#get-dictionary-export)                                                                                                        | Get Cdisc Export Dictionary               | 👀 Read-Only          |
| `POST`   | [/dictionary/unit-conversion](#post-dictionary-unit-conversion)                                                                                     | Post Unit Conversion Dictionary           | ✍️ Audited Write      |
| `GET`    | [/dictionary/unit-conversion](#get-dictionary-unit-conversion)                                                                                      | Get Unit Conversion Dictionary            | 👀 Read-Only          |
| `POST`   | [/events/study-published](#post-events-study-published)                                                                                             | Study Published                           | ✍️ Audited Write      |
| `GET`    | [/health](#get-health)                                                                                                                              | Health Check                              | 👀 Read-Only          |
| `PUT`    | [/subjects/{id}/demographics](#put-subjects-id-demographics)                                                                                        | Update Subject Demographics Endpoint      | ✍️ Audited Write      |
| `DELETE` | [/subjects/{id}/demographics](#delete-subjects-id-demographics)                                                                                     | Delete Subject Demographics Endpoint      | ✍️ Audited Write      |
| `PATCH`  | [/subjects/{id}/state](#patch-subjects-id-state)                                                                                                    | Update Subject State Endpoint             | ✍️ Audited Write      |
| `POST`   | [/unit-conversion](#post-unit-conversion)                                                                                                           | Post Unit Conversion Execution            | ✍️ Audited Write      |
| `GET`    | [/unit-conversion](#get-unit-conversion)                                                                                                            | Get Unit Conversion Execution             | 👀 Read-Only          |

---

## Granular Endpoint Specifications

### `GET` `/api/v1/admin/outbox`

**Summary:** Execution Admin Outbox Endpoint
**Operation ID:** `execution_admin_outbox_endpoint_api_v1_admin_outbox_get`

**Clinical & GxP Rationale:**
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name         | In      | Type     | Required | Description |
| :----------- | :------ | :------- | :------- | :---------- |
| `status`     | `query` | `string` | `null`   | No          | —   |
| `event_type` | `query` | `string` | `null`   | No          | —   |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema       |
| :---------- | :------------------ | :-------------------- |
| `200`       | Successful Response | `Array<object>`       |
| `422`       | Validation Error    | `HTTPValidationError` |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/admin/outbox",
  {
    method: "GET",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/execution/api/v1/admin/outbox',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/execution/api/v1/admin/outbox' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/dictionaries/import`

**Summary:** Import Dictionary
**Operation ID:** `import_dictionary_api_v1_dictionaries_import_post`

**Clinical & GxP Rationale:**
Imports raw dictionary files and schedules a background parsing task.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `any`

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema       |
| :---------- | :------------------ | :-------------------- |
| `202`       | Successful Response | `JobStatusResponse`   |
| `422`       | Validation Error    | `HTTPValidationError` |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/dictionaries/import",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/dictionaries/import',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/dictionaries/import' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `GET` `/api/v1/dictionaries/jobs/{job_id}`

**Summary:** Get Dictionary Import Job
**Operation ID:** `get_dictionary_import_job_api_v1_dictionaries_jobs__job_id__get`

**Clinical & GxP Rationale:**
Query the execution status, progress, and import counts of a dictionary import job by ID.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name     | In     | Type     | Required | Description |
| :------- | :----- | :------- | :------- | :---------- |
| `job_id` | `path` | `string` | ✔ Yes    | —           |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema       |
| :---------- | :------------------ | :-------------------- |
| `200`       | Successful Response | `JobStatusResponse`   |
| `422`       | Validation Error    | `HTTPValidationError` |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/dictionaries/jobs/{job_id}",
  {
    method: "GET",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/execution/api/v1/dictionaries/jobs/{job_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/execution/api/v1/dictionaries/jobs/{job_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `GET` `/api/v1/dictionaries/meddra/code`

**Summary:** Get Meddra Code
**Operation ID:** `get_meddra_code_api_v1_dictionaries_meddra_code_get`

**Clinical & GxP Rationale:**
Performs coding or interactive auto-complete lookup on adverse events using version-aware matcher.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name           | In      | Type                    | Required | Description |
| :------------- | :------ | :---------------------- | :------- | :---------- |
| `term`         | `query` | `string`                | ✔ Yes    | —           |
| `version`      | `query` | `string`                | `null`   | No          | —   |
| `target_level` | `query` | `MedDRATargetLevelEnum` | `null`   | No          | —   |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema            |
| :---------- | :------------------ | :------------------------- |
| `200`       | Successful Response | `MedDRACodeLookupResponse` |
| `422`       | Validation Error    | `HTTPValidationError`      |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/dictionaries/meddra/code",
  {
    method: "GET",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/execution/api/v1/dictionaries/meddra/code',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/execution/api/v1/dictionaries/meddra/code' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/dictionaries/ucum/convert`

**Summary:** Post Ucum Convert
**Operation ID:** `post_ucum_convert_api_v1_dictionaries_ucum_convert_post`

**Clinical & GxP Rationale:**
Standardizes numeric values and verifies scale compatibility between source and target codes.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `UCUMConvertRequest`

| Field         | Type     | Required | Description |
| :------------ | :------- | :------- | :---------- |
| `value`       | `number` | ✔ Yes    | —           |
| `source_unit` | `string` | ✔ Yes    | —           |
| `target_unit` | `string` | ✔ Yes    | —           |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema       |
| :---------- | :------------------ | :-------------------- |
| `200`       | Successful Response | `UCUMConvertResponse` |
| `400`       | Bad Request         | `ProblemDetails`      |
| `422`       | Validation Error    | `HTTPValidationError` |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/dictionaries/ucum/convert",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/dictionaries/ucum/convert',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/dictionaries/ucum/convert' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `GET` `/api/v1/dictionaries/whodrug/code`

**Summary:** Get Whodrug Code
**Operation ID:** `get_whodrug_code_api_v1_dictionaries_whodrug_code_get`

**Clinical & GxP Rationale:**
Performs coding or interactive lookup on WHODrug database using version-aware matcher.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name      | In      | Type     | Required | Description |
| :-------- | :------ | :------- | :------- | :---------- |
| `term`    | `query` | `string` | ✔ Yes    | —           |
| `version` | `query` | `string` | ✔ Yes    | —           |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema             |
| :---------- | :------------------ | :-------------------------- |
| `200`       | Successful Response | `WHODrugCodeLookupResponse` |
| `422`       | Validation Error    | `HTTPValidationError`       |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/dictionaries/whodrug/code",
  {
    method: "GET",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/execution/api/v1/dictionaries/whodrug/code',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/execution/api/v1/dictionaries/whodrug/code' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/documents/upload`

**Summary:** Upload Document
**Operation ID:** `upload_document_api_v1_documents_upload_post`

**Clinical & GxP Rationale:**
Upload regulated document, compute SHA-256 hash, and record GxP audit trail.

Requirements: PRD-SYS-001

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `any`

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema          |
| :---------- | :------------------ | :----------------------- |
| `201`       | Successful Response | `DocumentUploadResponse` |
| `422`       | Validation Error    | `HTTPValidationError`    |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/documents/upload",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/documents/upload',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/documents/upload' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `GET` `/api/v1/documents/{doc_id}`

**Summary:** Download Document
**Operation ID:** `download_document_api_v1_documents__doc_id__get`

**Clinical & GxP Rationale:**
Stream file content with dynamic watermarking.

Requirements: PRD-SYS-001

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name     | In     | Type     | Required | Description |
| :------- | :----- | :------- | :------- | :---------- |
| `doc_id` | `path` | `string` | ✔ Yes    | —           |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema       |
| :---------- | :------------------ | :-------------------- |
| `200`       | Successful Response | `any`                 |
| `422`       | Validation Error    | `HTTPValidationError` |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/documents/{doc_id}",
  {
    method: "GET",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/execution/api/v1/documents/{doc_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/execution/api/v1/documents/{doc_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `GET` `/api/v1/documents/{doc_id}/versions`

**Summary:** List Document Versions
**Operation ID:** `list_document_versions_api_v1_documents__doc_id__versions_get`

**Clinical & GxP Rationale:**
RETURN complete version history list.

Requirements: PRD-SYS-001

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name     | In     | Type     | Required | Description |
| :------- | :----- | :------- | :------- | :---------- |
| `doc_id` | `path` | `string` | ✔ Yes    | —           |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema                   |
| :---------- | :------------------ | :-------------------------------- |
| `200`       | Successful Response | `Array<DocumentMetadataResponse>` |
| `422`       | Validation Error    | `HTTPValidationError`             |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/documents/{doc_id}/versions",
  {
    method: "GET",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/execution/api/v1/documents/{doc_id}/versions',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/execution/api/v1/documents/{doc_id}/versions' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/amendments/bulk-reconsent`

**Summary:** Register Bulk Subject Reconsent
**Operation ID:** `register_bulk_subject_reconsent_api_v1_execution_amendments_bulk_reconsent_post`

**Clinical & GxP Rationale:**
Registers batch subject re-consent for an amended version, unlocking gating in bulk.

Requirements: PRD-SUB-007, PRD-SYS-001

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `BulkSubjectReconsentRequest`

| Field               | Type            | Required | Description                                  |
| :------------------ | :-------------- | :------- | :------------------------------------------- |
| `subject_ids`       | `Array<string>` | ✔ Yes    | List of subject identifiers                  |
| `study_id`          | `string`        | ✔ Yes    | Study identifier                             |
| `protocol_version`  | `string`        | ✔ Yes    | Amended protocol version tag                 |
| `version_index`     | `integer`       | No       | Protocol version index                       |
| `icf_signed`        | `boolean`       | No       | Whether ICF is signed                        |
| `signature_type`    | `string`        | No       | Signature type: 'ECONSENT' or 'PAPER_UPLOAD' |
| `signer_name`       | `string`        | No       | Full name or user ID of batch signer         |
| `reason_for_change` | `string`        | No       | GxP reason for change                        |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema       |
| :---------- | :------------------ | :-------------------- |
| `200`       | Successful Response | `object`              |
| `422`       | Validation Error    | `HTTPValidationError` |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/amendments/bulk-reconsent",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/amendments/bulk-reconsent',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/amendments/bulk-reconsent' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/amendments/publish`

**Summary:** Publish Amendment Endpoint
**Operation ID:** `publish_amendment_endpoint_api_v1_execution_amendments_publish_post`

**Clinical & GxP Rationale:**
Publish protocol amendment version and compute structural summary of changes.

Requirements: PRD-SYS-001

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `PublishAmendmentRequest`

| Field               | Type     | Required | Description                                |
| :------------------ | :------- | :------- | :----------------------------------------- |
| `study_id`          | `string` | ✔ Yes    | Target protocol study ID                   |
| `version_number`    | `string` | ✔ Yes    | Amended protocol version string (e.g. 2.0) |
| `description`       | `string` | ✔ Yes    | Amendment summary description              |
| `baseline_snapshot` | `object` | ✔ Yes    | Previous USDM snapshot                     |
| `amended_snapshot`  | `object` | ✔ Yes    | New amended USDM snapshot                  |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema            |
| :---------- | :------------------ | :------------------------- |
| `200`       | Successful Response | `PublishAmendmentResponse` |
| `422`       | Validation Error    | `HTTPValidationError`      |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/amendments/publish",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/amendments/publish',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/amendments/publish' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/amendments/reconsent`

**Summary:** Register Subject Reconsent
**Operation ID:** `register_subject_reconsent_api_v1_execution_amendments_reconsent_post`

**Clinical & GxP Rationale:**
Registers subject signed re-consent for an amended version, unlocking gating.

Requirements: PRD-SUB-007, PRD-SYS-001

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `SubjectReconsentRequest`

| Field              | Type      | Required | Description                                  |
| :----------------- | :-------- | :------- | :------------------------------------------- |
| `subject_id`       | `string`  | ✔ Yes    | Subject identifier                           |
| `study_id`         | `string`  | ✔ Yes    | Study identifier                             |
| `protocol_version` | `string`  | ✔ Yes    | Amended protocol version tag                 |
| `version_index`    | `integer` | No       | Protocol version index                       |
| `icf_signed`       | `boolean` | No       | Whether ICF is signed                        |
| `signature_type`   | `string`  | No       | Signature type: 'ECONSENT' or 'PAPER_UPLOAD' |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema       |
| :---------- | :------------------ | :-------------------- |
| `200`       | Successful Response | `object`              |
| `422`       | Validation Error    | `HTTPValidationError` |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/amendments/reconsent",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/amendments/reconsent',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/amendments/reconsent' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `GET` `/api/v1/execution/amendments/summary/{study_id}/{version}`

**Summary:** Get Amendment Summary Endpoint
**Operation ID:** `get_amendment_summary_endpoint_api_v1_execution_amendments_summary__study_id___version__get`

**Clinical & GxP Rationale:**
Export Summary of Changes report for specified study version.

Requirements: PRD-SYS-001

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name       | In     | Type     | Required | Description |
| :--------- | :----- | :------- | :------- | :---------- |
| `study_id` | `path` | `string` | ✔ Yes    | —           |
| `version`  | `path` | `string` | ✔ Yes    | —           |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema       |
| :---------- | :------------------ | :-------------------- |
| `200`       | Successful Response | `object`              |
| `422`       | Validation Error    | `HTTPValidationError` |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/amendments/summary/{study_id}/{version}",
  {
    method: "GET",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/execution/api/v1/execution/amendments/summary/{study_id}/{version}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/execution/api/v1/execution/amendments/summary/{study_id}/{version}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/amendments/validate-gating`

**Summary:** Validate Gating Endpoint
**Operation ID:** `validate_gating_endpoint_api_v1_execution_amendments_validate_gating_post`

**Clinical & GxP Rationale:**
Validates subject re-consent gating before form data entry.

Requirements: PRD-SUB-007

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `ValidateGatingRequest`

| Field                     | Type      | Required | Description                                 |
| :------------------------ | :-------- | :------- | :------------------------------------------ |
| `subject_id`              | `string`  | ✔ Yes    | Subject identifier                          |
| `target_visit_id`         | `string`  | ✔ Yes    | Target visit identifier                     |
| `active_protocol_version` | `string`  | ✔ Yes    | Active protocol version tag (e.g., '2.0.0') |
| `requires_reconsent`      | `boolean` | No       | Whether amendment requires re-consent       |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema       |
| :---------- | :------------------ | :-------------------- |
| `200`       | Successful Response | `object`              |
| `422`       | Validation Error    | `HTTPValidationError` |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/amendments/validate-gating",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/amendments/validate-gating',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/amendments/validate-gating' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/amendments/verify-training-gate`

**Summary:** Verify Training Gate Endpoint
**Operation ID:** `verify_training_gate_endpoint_api_v1_execution_amendments_verify_training_gate_post`

**Clinical & GxP Rationale:**
Programmatically verify training qualification for EDC write and signature permissions.

Requirements: PRD-TRN-002

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `VerifyTrainingGatingRequest`

| Field              | Type     | Required | Description                 |
| :----------------- | :------- | :------- | :-------------------------- |
| `study_id`         | `string` | ✔ Yes    | Clinical study ID           |
| `protocol_version` | `string` | ✔ Yes    | Active protocol version tag |
| `user_id`          | `string` | ✔ Yes    | User ID to evaluate         |
| `site_id`          | `string` | `null`   | No                          | Optional clinical site ID   |
| `user_role`        | `string` | `null`   | No                          | Optional user clinical role |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema       |
| :---------- | :------------------ | :-------------------- |
| `200`       | Successful Response | `object`              |
| `422`       | Validation Error    | `HTTPValidationError` |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/amendments/verify-training-gate",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/amendments/verify-training-gate',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/amendments/verify-training-gate' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `GET` `/api/v1/execution/amendments/{study_id}/subject-impact`

**Summary:** Get Subject Impact Analysis
**Operation ID:** `get_subject_impact_analysis_api_v1_execution_amendments__study_id__subject_impact_get`

**Clinical & GxP Rationale:**
Calculates subject migration impact for an active protocol amendment.

Requirements: PRD-SYS-001, PRD-SUB-007

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name             | In      | Type     | Required | Description |
| :--------------- | :------ | :------- | :------- | :---------- |
| `study_id`       | `path`  | `string` | ✔ Yes    | —           |
| `target_version` | `query` | `string` | No       | —           |
| `site_id`        | `query` | `string` | `null`   | No          | —   |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema       |
| :---------- | :------------------ | :-------------------- |
| `200`       | Successful Response | `object`              |
| `422`       | Validation Error    | `HTTPValidationError` |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/amendments/{study_id}/subject-impact",
  {
    method: "GET",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/execution/api/v1/execution/amendments/{study_id}/subject-impact',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/execution/api/v1/execution/amendments/{study_id}/subject-impact' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `GET` `/api/v1/execution/anomalies/candidates`

**Summary:** List Candidate Queries
**Operation ID:** `list_candidate_queries_api_v1_execution_anomalies_candidates_get`

**Clinical & GxP Rationale:**
Lists staged CANDIDATE clinical queries flagged by the anomaly worker for review.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name         | In      | Type     | Required | Description |
| :----------- | :------ | :------- | :------- | :---------- |
| `study_id`   | `query` | `string` | `null`   | No          | Filter by study ID       |
| `subject_id` | `query` | `string` | `null`   | No          | Filter by subject ID     |
| `domain`     | `query` | `string` | `null`   | No          | Filter by primary domain |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema               |
| :---------- | :------------------ | :---------------------------- |
| `200`       | Successful Response | `Array<AnomalyCandidateItem>` |
| `422`       | Validation Error    | `HTTPValidationError`         |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/anomalies/candidates",
  {
    method: "GET",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/execution/api/v1/execution/anomalies/candidates',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/execution/api/v1/execution/anomalies/candidates' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/anomalies/candidates/{query_id}/adjudicate`

**Summary:** Adjudicate Candidate Query
**Operation ID:** `adjudicate_candidate_query_api_v1_execution_anomalies_candidates__query_id__adjudicate_post`

**Clinical & GxP Rationale:**
Adjudicates a staged CANDIDATE query: promotes to OPEN or dismisses to CANCELLED.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name       | In     | Type     | Required | Description |
| :--------- | :----- | :------- | :------- | :---------- |
| `query_id` | `path` | `string` | ✔ Yes    | —           |

#### Request Payload Schema (`application/json`)

**Schema Type:** `AdjudicationRequest`

| Field             | Type                     | Required | Description                                                                     |
| :---------------- | :----------------------- | :------- | :------------------------------------------------------------------------------ |
| `action`          | `AdjudicationActionEnum` | ✔ Yes    | Adjudication decision: APPROVE (promote to OPEN) or REJECT (dismiss/CANCELLED). |
| `reason`          | `string`                 | ✔ Yes    | Mandatory 21 CFR Part 11 justification or reason for change.                    |
| `updated_message` | `string`                 | `null`   | No                                                                              | Optional edited query text before opening to site staff. |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema        |
| :---------- | :------------------ | :--------------------- |
| `200`       | Successful Response | `AdjudicationResponse` |
| `422`       | Validation Error    | `HTTPValidationError`  |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/anomalies/candidates/{query_id}/adjudicate",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/anomalies/candidates/{query_id}/adjudicate',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/anomalies/candidates/{query_id}/adjudicate' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/anomalies/evaluate`

**Summary:** Evaluate Anomalies
**Operation ID:** `evaluate_anomalies_api_v1_execution_anomalies_evaluate_post`

**Clinical & GxP Rationale:**
Evaluates cross-domain clinical data for a subject and stages detected candidate queries.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `AnomalyEvaluateRequest`

| Field                | Type      | Required | Description                                                             |
| :------------------- | :-------- | :------- | :---------------------------------------------------------------------- |
| `subject_id`         | `string`  | ✔ Yes    | Subject identifier to evaluate.                                         |
| `study_id`           | `string`  | ✔ Yes    | Study identifier.                                                       |
| `enable_ai`          | `boolean` | No       | Whether to run AI Gateway semantic reasoning if available.              |
| `auto_stage_queries` | `boolean` | No       | Whether to automatically stage detected anomalies as CANDIDATE queries. |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema           |
| :---------- | :------------------ | :------------------------ |
| `200`       | Successful Response | `AnomalyEvaluateResponse` |
| `422`       | Validation Error    | `HTTPValidationError`     |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/anomalies/evaluate",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/anomalies/evaluate',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/anomalies/evaluate' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/anonymization/redact-pdf`

**Summary:** Redact Pdf Endpoint
**Operation ID:** `redact_pdf_endpoint_api_v1_execution_anonymization_redact_pdf_post`

**Clinical & GxP Rationale:**
Apply non-destructive PHI redaction overlays to PDF document.

Requirements: PRD-SYS-001

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `RedactPDFRequest`

| Field             | Type            | Required | Description                         |
| :---------------- | :-------------- | :------- | :---------------------------------- |
| `pdf_base64`      | `string`        | ✔ Yes    | Base64 encoded PDF document content |
| `target_snippets` | `Array<string>` | No       | Target PHI strings to redact        |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema       |
| :---------- | :------------------ | :-------------------- |
| `200`       | Successful Response | `object`              |
| `422`       | Validation Error    | `HTTPValidationError` |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/anonymization/redact-pdf",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/anonymization/redact-pdf',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/anonymization/redact-pdf' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/anonymization/scan-phi`

**Summary:** Scan Phi Endpoint
**Operation ID:** `scan_phi_endpoint_api_v1_execution_anonymization_scan_phi_post`

**Clinical & GxP Rationale:**
Scan text payload for Protected Health Information (PHI) identifiers.

Requirements: PRD-SYS-001

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `PHIScanRequest`

| Field  | Type     | Required | Description                           |
| :----- | :------- | :------- | :------------------------------------ |
| `text` | `string` | ✔ Yes    | Document text content to scan for PHI |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema       |
| :---------- | :------------------ | :-------------------- |
| `200`       | Successful Response | `object`              |
| `422`       | Validation Error    | `HTTPValidationError` |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/anonymization/scan-phi",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/anonymization/scan-phi',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/anonymization/scan-phi' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `GET` `/api/v1/execution/audit/integrity`

**Summary:** Get Execution Audit Integrity
**Operation ID:** `get_execution_audit_integrity_api_v1_execution_audit_integrity_get`

**Clinical & GxP Rationale:**
Verify the GxP clinical execution ledger integrity via block-sealing validation.

Ensures that chronological audit logs, block-level seals, and sequential chaining
remain structurally unbroken.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema |
| :---------- | :------------------ | :-------------- |
| `200`       | Successful Response | `object`        |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/audit/integrity",
  {
    method: "GET",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/execution/api/v1/execution/audit/integrity',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/execution/api/v1/execution/audit/integrity' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `GET` `/api/v1/execution/auditor/inspect/audit-trail/{study_id}`

**Summary:** Inspect Study Audit Trail Endpoint
**Operation ID:** `inspect_study_audit_trail_endpoint_api_v1_execution_auditor_inspect_audit_trail__study_id__get`

**Clinical & GxP Rationale:**
Expose read-only 21 CFR Part 11 audit trail inspection endpoint for study.

Requirements: PRD-SYS-001

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name       | In      | Type      | Required | Description |
| :--------- | :------ | :-------- | :------- | :---------- |
| `study_id` | `path`  | `string`  | ✔ Yes    | —           |
| `limit`    | `query` | `integer` | `null`   | No          | —   |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema       |
| :---------- | :------------------ | :-------------------- |
| `200`       | Successful Response | `object`              |
| `422`       | Validation Error    | `HTTPValidationError` |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/auditor/inspect/audit-trail/{study_id}",
  {
    method: "GET",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/execution/api/v1/execution/auditor/inspect/audit-trail/{study_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/execution/api/v1/execution/auditor/inspect/audit-trail/{study_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/auditor/token/generate`

**Summary:** Generate Auditor Token Endpoint
**Operation ID:** `generate_auditor_token_endpoint_api_v1_execution_auditor_token_generate_post`

**Clinical & GxP Rationale:**
Generate temporary time-bounded access token for regulatory auditors.

Requirements: PRD-SYS-001

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `GenerateAuditorTokenRequest`

| Field               | Type      | Required | Description                                |
| :------------------ | :-------- | :------- | :----------------------------------------- |
| `auditor_email`     | `string`  | ✔ Yes    | Target auditor email address               |
| `study_id`          | `string`  | ✔ Yes    | Target protocol study ID                   |
| `duration_hours`    | `integer` | No       | Token validity in hours                    |
| `reason_for_access` | `string`  | ✔ Yes    | GxP reason for provisioning auditor access |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema       |
| :---------- | :------------------ | :-------------------- |
| `200`       | Successful Response | `object`              |
| `422`       | Validation Error    | `HTTPValidationError` |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/auditor/token/generate",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/auditor/token/generate',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/auditor/token/generate' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/batch-sign-off`

**Summary:** Post Batch Sign Off
**Operation ID:** `post_batch_sign_off_api_v1_execution_batch_sign_off_post`

**Clinical & GxP Rationale:**
Perform a PI-only, atomic batch electronic-signature for form-, visit-, and subject-level sign-off.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Mandatory 60s `sig_token` required for Part 11 sign-off

#### Request Payload Schema (`application/json`)

**Schema Type:** `BatchSignOffRequest`

| Field            | Type            | Required | Description |
| :--------------- | :-------------- | :------- | :---------- |
| `study_id`       | `string`        | ✔ Yes    | —           |
| `target_type`    | `string`        | ✔ Yes    | —           |
| `target_ids`     | `Array<string>` | ✔ Yes    | —           |
| `signing_reason` | `string`        | ✔ Yes    | —           |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema        |
| :---------- | :------------------ | :--------------------- |
| `200`       | Successful Response | `BatchSignOffResponse` |
| `422`       | Validation Error    | `HTTPValidationError`  |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/batch-sign-off",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/batch-sign-off',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/batch-sign-off' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `GET` `/api/v1/execution/biostat/adam/{dataset}`

**Summary:** Export Adam Dataset
**Operation ID:** `export_adam_dataset_api_v1_execution_biostat_adam__dataset__get`

**Clinical & GxP Rationale:**
Exports ADaM dataset data (ADSL, ADAE, ADVS) in Dataset-JSON, SAS XPT, ODM-XML, or CSV format.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name              | In      | Type     | Required | Description                                                                     |
| :---------------- | :------ | :------- | :------- | :------------------------------------------------------------------------------ |
| `dataset`         | `path`  | `string` | ✔ Yes    | —                                                                               |
| `study_id`        | `query` | `string` | ✔ Yes    | The unique study identifier                                                     |
| `format`          | `query` | `string` | No       | Target export format: json, xpt, odm, csv                                       |
| `version`         | `query` | `string` | No       | SAS XPT version if format is xpt (v5 or v8)                                     |
| `privacy_profile` | `query` | `string` | No       | Privacy policy: SAFE_HARBOR, LIMITED_DATA_SET, GDPR_PSEUDONYMIZED, UNRESTRICTED |
| `salt`            | `query` | `string` | `null`   | No                                                                              | HMAC pseudonymization salt |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema       |
| :---------- | :------------------ | :-------------------- |
| `200`       | Successful Response | `object`              |
| `422`       | Validation Error    | `HTTPValidationError` |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/biostat/adam/{dataset}",
  {
    method: "GET",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/execution/api/v1/execution/biostat/adam/{dataset}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/execution/api/v1/execution/biostat/adam/{dataset}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `GET` `/api/v1/execution/biostat/bundle`

**Summary:** Export Biostat Bundle
**Operation ID:** `export_biostat_bundle_api_v1_execution_biostat_bundle_get`

**Clinical & GxP Rationale:**
Exports all SDTM domains and ADaM datasets bundled in Dataset-JSON, CSV ZIP, or ODM-XML.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name              | In      | Type     | Required | Description                                                                     |
| :---------------- | :------ | :------- | :------- | :------------------------------------------------------------------------------ |
| `study_id`        | `query` | `string` | ✔ Yes    | The unique study identifier                                                     |
| `format`          | `query` | `string` | No       | Target export format: json, zip, csv_zip, odm                                   |
| `privacy_profile` | `query` | `string` | No       | Privacy policy: SAFE_HARBOR, LIMITED_DATA_SET, GDPR_PSEUDONYMIZED, UNRESTRICTED |
| `salt`            | `query` | `string` | `null`   | No                                                                              | HMAC pseudonymization salt |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema       |
| :---------- | :------------------ | :-------------------- |
| `200`       | Successful Response | `object`              |
| `422`       | Validation Error    | `HTTPValidationError` |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/biostat/bundle",
  {
    method: "GET",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/execution/api/v1/execution/biostat/bundle',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/execution/api/v1/execution/biostat/bundle' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `GET` `/api/v1/execution/biostat/sdtm/{domain}`

**Summary:** Export Sdtm Domain
**Operation ID:** `export_sdtm_domain_api_v1_execution_biostat_sdtm__domain__get`

**Clinical & GxP Rationale:**
Exports SDTM domain data (DM, AE, VS, LB, MH, CM) in Dataset-JSON, SAS XPT, ODM-XML, or CSV format.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name              | In      | Type     | Required | Description                                                                     |
| :---------------- | :------ | :------- | :------- | :------------------------------------------------------------------------------ |
| `domain`          | `path`  | `string` | ✔ Yes    | —                                                                               |
| `study_id`        | `query` | `string` | ✔ Yes    | The unique study identifier                                                     |
| `format`          | `query` | `string` | No       | Target export format: json, xpt, odm, csv                                       |
| `version`         | `query` | `string` | No       | SAS XPT version if format is xpt (v5 or v8)                                     |
| `privacy_profile` | `query` | `string` | No       | Privacy policy: SAFE_HARBOR, LIMITED_DATA_SET, GDPR_PSEUDONYMIZED, UNRESTRICTED |
| `salt`            | `query` | `string` | `null`   | No                                                                              | HMAC pseudonymization salt |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema       |
| :---------- | :------------------ | :-------------------- |
| `200`       | Successful Response | `object`              |
| `422`       | Validation Error    | `HTTPValidationError` |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/biostat/sdtm/{domain}",
  {
    method: "GET",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/execution/api/v1/execution/biostat/sdtm/{domain}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/execution/api/v1/execution/biostat/sdtm/{domain}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/cec/charters`

**Summary:** Create Or Update Charter
**Operation ID:** `create_or_update_charter_api_v1_execution_cec_charters_post`

**Clinical & GxP Rationale:**
Configures or updates committee charter voting rules, quorum, and diagnostic criteria forms.

@req:PRD-SYS-124

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `CharterCreateDTO`

| Field                           | Type            | Required | Description     |
| :------------------------------ | :-------------- | :------- | :-------------- |
| `study_id`                      | `string`        | ✔ Yes    | Target study ID |
| `committee_name`                | `string`        | ✔ Yes    | Committee name  |
| `version`                       | `string`        | No       | —               |
| `voting_rule`                   | `VotingRule`    | No       | —               |
| `minimum_quorum_percentage`     | `number`        | No       | —               |
| `minimum_members_count`         | `integer`       | No       | —               |
| `eligible_members`              | `Array<object>` | No       | —               |
| `endpoint_definitions`          | `Array<string>` | No       | —               |
| `diagnostic_criteria_templates` | `object`        | No       | —               |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema       |
| :---------- | :------------------ | :-------------------- |
| `201`       | Successful Response | `CecCharterConfig`    |
| `422`       | Validation Error    | `HTTPValidationError` |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/cec/charters",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/cec/charters',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/cec/charters' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `GET` `/api/v1/execution/cec/charters/{study_id}`

**Summary:** Get Study Charter
**Operation ID:** `get_study_charter_api_v1_execution_cec_charters__study_id__get`

**Clinical & GxP Rationale:**
Retrieves active CEC charter governance configuration.

@req:PRD-SYS-124

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name       | In     | Type     | Required | Description |
| :--------- | :----- | :------- | :------- | :---------- |
| `study_id` | `path` | `string` | ✔ Yes    | —           |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema       |
| :---------- | :------------------ | :-------------------- |
| `200`       | Successful Response | `CecCharterConfig`    |
| `422`       | Validation Error    | `HTTPValidationError` |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/cec/charters/{study_id}",
  {
    method: "GET",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/execution/api/v1/execution/cec/charters/{study_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/execution/api/v1/execution/cec/charters/{study_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/cec/dossiers`

**Summary:** Create And Package Dossier
**Operation ID:** `create_and_package_dossier_api_v1_execution_cec_dossiers_post`

**Clinical & GxP Rationale:**
Compiles and packages a de-identified clinical event dossier for 2+1 blinded adjudication.

@req:PRD-CEC-001, @req:PRD-SYS-124, @req:ADR-2199

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `DossierPackageCreateDTO`

| Field                    | Type                          | Required | Description                                |
| :----------------------- | :---------------------------- | :------- | :----------------------------------------- |
| `study_id`               | `string`                      | ✔ Yes    | Target protocol study ID                   |
| `site_id`                | `string`                      | ✔ Yes    | Investigational site ID                    |
| `subject_id`             | `string`                      | ✔ Yes    | Subject UUID                               |
| `event_id`               | `string`                      | ✔ Yes    | Adverse event or clinical observation ID   |
| `candidate_event_type`   | `EndpointCategory`            | ✔ Yes    | Endpoint category (e.g. MACE, STROKE, MI)  |
| `reported_term`          | `string`                      | ✔ Yes    | Reported event term                        |
| `verbatim_description`   | `string`                      | No       | Verbatim investigator clinical description |
| `reported_onset_date`    | `string`                      | ✔ Yes    | Reported onset date (ISO 8601 / DTC)       |
| `medical_narrative`      | `string`                      | No       | Detailed clinical narrative                |
| `bundled_forms`          | `Array<BundledEcrfForm>`      | No       | —                                          |
| `lab_panels`             | `Array<LabPanelItem>`         | No       | —                                          |
| `ecg_records`            | `Array<ECGRecord>`            | No       | —                                          |
| `diagnostic_attachments` | `Array<DiagnosticAttachment>` | No       | —                                          |
| `reader_1_id`            | `string`                      | No       | —                                          |
| `reader_1_name`          | `string`                      | No       | —                                          |
| `reader_2_id`            | `string`                      | No       | —                                          |
| `reader_2_name`          | `string`                      | No       | —                                          |
| `chair_id`               | `string`                      | No       | —                                          |
| `chair_name`             | `string`                      | No       | —                                          |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema       |
| :---------- | :------------------ | :-------------------- |
| `201`       | Successful Response | `object`              |
| `422`       | Validation Error    | `HTTPValidationError` |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/cec/dossiers",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/cec/dossiers',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/cec/dossiers' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `GET` `/api/v1/execution/cec/dossiers`

**Summary:** List Dossiers
**Operation ID:** `list_dossiers_api_v1_execution_cec_dossiers_get`

**Clinical & GxP Rationale:**
Lists clinical event dossiers with filtering options.

@req:PRD-CEC-002

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name        | In      | Type     | Required | Description |
| :---------- | :------ | :------- | :------- | :---------- |
| `study_id`  | `query` | `string` | `null`   | No          | Filter by study ID                 |
| `status`    | `query` | `string` | `null`   | No          | Filter by workflow status          |
| `category`  | `query` | `string` | `null`   | No          | Filter by candidate event category |
| `reader_id` | `query` | `string` | `null`   | No          | Filter by assigned reader          |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema                |
| :---------- | :------------------ | :----------------------------- |
| `200`       | Successful Response | `Array<CecDossierSummaryItem>` |
| `422`       | Validation Error    | `HTTPValidationError`          |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/cec/dossiers",
  {
    method: "GET",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/execution/api/v1/execution/cec/dossiers',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/execution/api/v1/execution/cec/dossiers' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `GET` `/api/v1/execution/cec/dossiers/{dossier_id}`

**Summary:** Get Dossier Blinded View
**Operation ID:** `get_dossier_blinded_view_api_v1_execution_cec_dossiers__dossier_id__get`

**Clinical & GxP Rationale:**
Retrieves a role-blinded projection of a dossier, enforcing reader isolation.

@req:PRD-CEC-001, @req:PRD-CEC-002, @req:ADR-2199

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name            | In       | Type     | Required | Description |
| :-------------- | :------- | :------- | :------- | :---------- |
| `dossier_id`    | `path`   | `string` | ✔ Yes    | —           |
| `x-reader-role` | `header` | `string` | `null`   | No          | Reader Role override for testing/simulation |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema       |
| :---------- | :------------------ | :-------------------- |
| `200`       | Successful Response | `DossierBlindedView`  |
| `422`       | Validation Error    | `HTTPValidationError` |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/cec/dossiers/{dossier_id}",
  {
    method: "GET",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/execution/api/v1/execution/cec/dossiers/{dossier_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/execution/api/v1/execution/cec/dossiers/{dossier_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/cec/dossiers/{dossier_id}/assessments`

**Summary:** Submit Reader Assessment
**Operation ID:** `submit_reader_assessment_api_v1_execution_cec_dossiers__dossier_id__assessments_post`

**Clinical & GxP Rationale:**
Submits an expert reader assessment for Reader 1 or Reader 2 and auto-evaluates consensus.

@req:PRD-CEC-002, @req:ADR-2199

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name         | In     | Type     | Required | Description |
| :----------- | :----- | :------- | :------- | :---------- |
| `dossier_id` | `path` | `string` | ✔ Yes    | —           |

#### Request Payload Schema (`application/json`)

**Schema Type:** `ReaderAssessmentSubmitDTO`

| Field                    | Type                         | Required | Description                       |
| :----------------------- | :--------------------------- | :------- | :-------------------------------- |
| `reader_role`            | `ReaderRole`                 | ✔ Yes    | Reader role: READER_1 or READER_2 |
| `classification`         | `AdjudicationClassification` | ✔ Yes    | Event classification              |
| `subtype`                | `string`                     | `null`   | No                                | Specific event subtype (e.g. Type 1 MI, Ischemic Stroke) |
| `adjudicated_onset_date` | `string`                     | `null`   | No                                | Adjudicated onset date (ISO 8601)                        |
| `criteria_met`           | `Array<string>`              | No       | Diagnostic criteria satisfied     |
| `confidence_score`       | `integer`                    | No       | Confidence score 1 to 5           |
| `clinical_rationale`     | `string`                     | No       | Clinical rationale narrative      |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema       |
| :---------- | :------------------ | :-------------------- |
| `200`       | Successful Response | `object`              |
| `422`       | Validation Error    | `HTTPValidationError` |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/cec/dossiers/{dossier_id}/assessments",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/cec/dossiers/{dossier_id}/assessments',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/cec/dossiers/{dossier_id}/assessments' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/cec/dossiers/{dossier_id}/chair-adjudicate`

**Summary:** Adjudicate Chair Tie Breaker
**Operation ID:** `adjudicate_chair_tie_breaker_api_v1_execution_cec_dossiers__dossier_id__chair_adjudicate_post`

**Clinical & GxP Rationale:**
Executes Adjudication Committee Chair tie-breaker ruling with 21 CFR Part 11 electronic signature.

@req:PRD-CEC-002, @req:PRD-SYS-124, @req:ADR-2199

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name         | In     | Type     | Required | Description |
| :----------- | :----- | :------- | :------- | :---------- |
| `dossier_id` | `path` | `string` | ✔ Yes    | —           |

#### Request Payload Schema (`application/json`)

**Schema Type:** `ChairAdjudicateDTO`

| Field                          | Type                         | Required | Description                                  |
| :----------------------------- | :--------------------------- | :------- | :------------------------------------------- |
| `final_classification`         | `AdjudicationClassification` | ✔ Yes    | Definitive classification                    |
| `final_subtype`                | `string`                     | `null`   | No                                           | Definitive subtype           |
| `final_adjudicated_onset_date` | `string`                     | ✔ Yes    | Definitive onset date (ISO 8601)             |
| `binding_reader`               | `BindingDeterminationSource` | No       | Binding determination source                 |
| `criteria_confirmed`           | `Array<string>`              | No       | Criteria confirmed                           |
| `adjudication_rationale`       | `string`                     | ✔ Yes    | Formal adjudication rationale                |
| `signature_token`              | `string`                     | `null`   | No                                           | Step-up HMAC signature token |
| `signer_name`                  | `string`                     | No       | Printed signer name                          |
| `password`                     | `string`                     | No       | Re-authentication password                   |
| `signature_meaning`            | `string`                     | No       | 21 CFR Part 11 legal signature manifestation |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema       |
| :---------- | :------------------ | :-------------------- |
| `200`       | Successful Response | `object`              |
| `422`       | Validation Error    | `HTTPValidationError` |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/cec/dossiers/{dossier_id}/chair-adjudicate",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/cec/dossiers/{dossier_id}/chair-adjudicate',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/cec/dossiers/{dossier_id}/chair-adjudicate' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `GET` `/api/v1/execution/cec/dossiers/{dossier_id}/full`

**Summary:** Get Dossier Full
**Operation ID:** `get_dossier_full_api_v1_execution_cec_dossiers__dossier_id__full_get`

**Clinical & GxP Rationale:**
Retrieves unblinded full dossier details for Committee Chair, Data Manager, or Auditor.

@req:PRD-CEC-002, @req:PRD-SYS-001

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name         | In     | Type     | Required | Description |
| :----------- | :----- | :------- | :------- | :---------- |
| `dossier_id` | `path` | `string` | ✔ Yes    | —           |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema            |
| :---------- | :------------------ | :------------------------- |
| `200`       | Successful Response | `DossierDetailResponseDTO` |
| `422`       | Validation Error    | `HTTPValidationError`      |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/cec/dossiers/{dossier_id}/full",
  {
    method: "GET",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/execution/api/v1/execution/cec/dossiers/{dossier_id}/full',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/execution/api/v1/execution/cec/dossiers/{dossier_id}/full' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/cec/dossiers/{dossier_id}/writeback`

**Summary:** Trigger Edc Writeback
**Operation ID:** `trigger_edc_writeback_api_v1_execution_cec_dossiers__dossier_id__writeback_post`

**Clinical & GxP Rationale:**
Manually or automatically triggers bi-directional EDC casebook writeback for an adjudicated event.

@req:PRD-CEC-003

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name         | In     | Type     | Required | Description |
| :----------- | :----- | :------- | :------- | :---------- |
| `dossier_id` | `path` | `string` | ✔ Yes    | —           |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema              |
| :---------- | :------------------ | :--------------------------- |
| `200`       | Successful Response | `EdcCasebookWritebackResult` |
| `422`       | Validation Error    | `HTTPValidationError`        |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/cec/dossiers/{dossier_id}/writeback",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/cec/dossiers/{dossier_id}/writeback',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/cec/dossiers/{dossier_id}/writeback' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/cec/meetings`

**Summary:** Record Meeting Quorum
**Operation ID:** `record_meeting_quorum_api_v1_execution_cec_meetings_post`

**Clinical & GxP Rationale:**
Records committee meeting attendance, evaluates quorum compliance, and captures minutes.

@req:PRD-SYS-124

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `MeetingQuorumRecordDTO`

| Field                    | Type            | Required | Description                                 |
| :----------------------- | :-------------- | :------- | :------------------------------------------ |
| `study_id`               | `string`        | ✔ Yes    | Study ID                                    |
| `charter_id`             | `string`        | ✔ Yes    | Charter ID                                  |
| `meeting_date`           | `string`        | ✔ Yes    | Meeting date (ISO 8601)                     |
| `attendees`              | `Array<object>` | ✔ Yes    | Attendees: [{member_id, member_name, role}] |
| `total_eligible_members` | `integer`       | ✔ Yes    | —                                           |
| `dossiers_reviewed`      | `Array<string>` | No       | —                                           |
| `voting_results`         | `object`        | No       | —                                           |
| `meeting_minutes`        | `string`        | No       | —                                           |
| `chair_signature`        | `object`        | `null`   | No                                          | —   |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema       |
| :---------- | :------------------ | :-------------------- |
| `201`       | Successful Response | `object`              |
| `422`       | Validation Error    | `HTTPValidationError` |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/cec/meetings",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/cec/meetings',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/cec/meetings' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `GET` `/api/v1/execution/cec/meetings/{study_id}`

**Summary:** List Study Meetings
**Operation ID:** `list_study_meetings_api_v1_execution_cec_meetings__study_id__get`

**Clinical & GxP Rationale:**
Lists all recorded committee meetings and quorum statuses for a study.

@req:PRD-SYS-124

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name       | In     | Type     | Required | Description |
| :--------- | :----- | :------- | :------- | :---------- |
| `study_id` | `path` | `string` | ✔ Yes    | —           |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema       |
| :---------- | :------------------ | :-------------------- |
| `200`       | Successful Response | `Array<object>`       |
| `422`       | Validation Error    | `HTTPValidationError` |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/cec/meetings/{study_id}",
  {
    method: "GET",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/execution/api/v1/execution/cec/meetings/{study_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/execution/api/v1/execution/cec/meetings/{study_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/cec/sdtm-ce/compile`

**Summary:** Compile Sdtm Ce Dataset
**Operation ID:** `compile_sdtm_ce_dataset_api_v1_execution_cec_sdtm_ce_compile_post`

**Clinical & GxP Rationale:**
Compiles all finalized adjudicated events for a study directly into CDISC SDTM CE dataset.

@req:PRD-CEC-003, @req:PRD-SYS-001

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `SdtmCeCompileRequestDTO`

| Field      | Type     | Required | Description |
| :--------- | :------- | :------- | :---------- |
| `study_id` | `string` | ✔ Yes    | Study ID    |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema           |
| :---------- | :------------------ | :------------------------ |
| `200`       | Successful Response | `SdtmCeCompilationResult` |
| `422`       | Validation Error    | `HTTPValidationError`     |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/cec/sdtm-ce/compile",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/cec/sdtm-ce/compile',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/cec/sdtm-ce/compile' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `GET` `/api/v1/execution/coding/assignments`

**Summary:** List Coding Assignments
**Operation ID:** `list_coding_assignments_api_v1_execution_coding_assignments_get`

**Clinical & GxP Rationale:**
Lists and filters medical coding assignments.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name              | In      | Type     | Required | Description |
| :---------------- | :------ | :------- | :------- | :---------- |
| `observation_id`  | `query` | `string` | `null`   | No          | —   |
| `status`          | `query` | `string` | `null`   | No          | —   |
| `verbatim_text`   | `query` | `string` | `null`   | No          | —   |
| `dictionary_type` | `query` | `string` | `null`   | No          | —   |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema                   |
| :---------- | :------------------ | :-------------------------------- |
| `200`       | Successful Response | `Array<CodingAssignmentResponse>` |
| `422`       | Validation Error    | `HTTPValidationError`             |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/coding/assignments",
  {
    method: "GET",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/execution/api/v1/execution/coding/assignments',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/execution/api/v1/execution/coding/assignments' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/coding/assignments/batch-assign`

**Summary:** Post Batch Assign
**Operation ID:** `post_batch_assign_api_v1_execution_coding_assignments_batch_assign_post`

**Clinical & GxP Rationale:**
Performs batch medical coding assignment across multiple assignments with GxP audit logging.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Mandatory 60s `sig_token` required for Part 11 sign-off

#### Request Payload Schema (`application/json`)

**Schema Type:** `BatchAssignRequest`

| Field                | Type                     | Required | Description |
| :------------------- | :----------------------- | :------- | :---------- |
| `assignment_ids`     | `Array<string>`          | `null`   | No          | —   |
| `items`              | `Array<BatchAssignItem>` | `null`   | No          | —   |
| `code`               | `string`                 | `null`   | No          | —   |
| `term`               | `string`                 | `null`   | No          | —   |
| `dictionary_type`    | `string`                 | `null`   | No          | —   |
| `dictionary_version` | `string`                 | `null`   | No          | —   |
| `reason`             | `string`                 | `null`   | No          | —   |
| `reason_for_change`  | `string`                 | `null`   | No          | —   |
| `action`             | `string`                 | No       | —           |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema       |
| :---------- | :------------------ | :-------------------- |
| `200`       | Successful Response | `BatchAssignResponse` |
| `422`       | Validation Error    | `HTTPValidationError` |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/coding/assignments/batch-assign",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/coding/assignments/batch-assign',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/coding/assignments/batch-assign' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `GET` `/api/v1/execution/coding/assignments/{assignment_id}`

**Summary:** Get Coding Assignment
**Operation ID:** `get_coding_assignment_api_v1_execution_coding_assignments__assignment_id__get`

**Clinical & GxP Rationale:**
Retrieves a single medical coding assignment by ID.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name            | In     | Type     | Required | Description |
| :-------------- | :----- | :------- | :------- | :---------- |
| `assignment_id` | `path` | `string` | ✔ Yes    | —           |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema            |
| :---------- | :------------------ | :------------------------- |
| `200`       | Successful Response | `CodingAssignmentResponse` |
| `422`       | Validation Error    | `HTTPValidationError`      |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/coding/assignments/{assignment_id}",
  {
    method: "GET",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/execution/api/v1/execution/coding/assignments/{assignment_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/execution/api/v1/execution/coding/assignments/{assignment_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/coding/assignments/{assignment_id}/action`

**Summary:** Process Coding Action
**Operation ID:** `process_coding_action_api_v1_execution_coding_assignments__assignment_id__action_post`

**Clinical & GxP Rationale:**
Accepts a suggestion or submits a manual override, persisting results and updating the ledger.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Mandatory 60s `sig_token` required for Part 11 sign-off

#### Request Parameters

| Name            | In     | Type     | Required | Description |
| :-------------- | :----- | :------- | :------- | :---------- |
| `assignment_id` | `path` | `string` | ✔ Yes    | —           |

#### Request Payload Schema (`application/json`)

**Schema Type:** `CoderActionRequest`

| Field               | Type      | Required | Description |
| :------------------ | :-------- | :------- | :---------- |
| `action`            | `string`  | ✔ Yes    | —           |
| `code`              | `string`  | `null`   | No          | —   |
| `term`              | `string`  | `null`   | No          | —   |
| `suggestion_index`  | `integer` | `null`   | No          | —   |
| `reason_for_change` | `string`  | `null`   | No          | —   |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema            |
| :---------- | :------------------ | :------------------------- |
| `200`       | Successful Response | `CodingAssignmentResponse` |
| `422`       | Validation Error    | `HTTPValidationError`      |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/coding/assignments/{assignment_id}/action",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/coding/assignments/{assignment_id}/action',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/coding/assignments/{assignment_id}/action' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/coding/assignments/{assignment_id}/raise-query`

**Summary:** Post Raise Coding Query
**Operation ID:** `post_raise_coding_query_api_v1_execution_coding_assignments__assignment_id__raise_query_post`

**Clinical & GxP Rationale:**
Escalates a coding discrepancy into a ClinicalQuery on the associated observation/eCRF record.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Mandatory 60s `sig_token` required for Part 11 sign-off

#### Request Parameters

| Name            | In     | Type     | Required | Description |
| :-------------- | :----- | :------- | :------- | :---------- |
| `assignment_id` | `path` | `string` | ✔ Yes    | —           |

#### Request Payload Schema (`application/json`)

**Schema Type:** `RaiseQueryRequest`

| Field               | Type     | Required | Description |
| :------------------ | :------- | :------- | :---------- |
| `query_text`        | `string` | `null`   | No          | —   |
| `message`           | `string` | `null`   | No          | —   |
| `explanation`       | `string` | `null`   | No          | —   |
| `reason`            | `string` | `null`   | No          | —   |
| `reason_for_change` | `string` | `null`   | No          | —   |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema       |
| :---------- | :------------------ | :-------------------- |
| `200`       | Successful Response | `RaiseQueryResponse`  |
| `422`       | Validation Error    | `HTTPValidationError` |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/coding/assignments/{assignment_id}/raise-query",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/coding/assignments/{assignment_id}/raise-query',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/coding/assignments/{assignment_id}/raise-query' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/coding/assignments/{assignment_id}/suggest`

**Summary:** Post Suggest Coding
**Operation ID:** `post_suggest_coding_api_v1_execution_coding_assignments__assignment_id__suggest_post`

**Clinical & GxP Rationale:**
Generates AI Tier 1 semantic coding suggestions for an assignment.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Mandatory 60s `sig_token` required for Part 11 sign-off

#### Request Parameters

| Name            | In     | Type     | Required | Description |
| :-------------- | :----- | :------- | :------- | :---------- |
| `assignment_id` | `path` | `string` | ✔ Yes    | —           |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema            |
| :---------- | :------------------ | :------------------------- |
| `200`       | Successful Response | `CodingAssignmentResponse` |
| `422`       | Validation Error    | `HTTPValidationError`      |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/coding/assignments/{assignment_id}/suggest",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/coding/assignments/{assignment_id}/suggest',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/coding/assignments/{assignment_id}/suggest' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/coding/impact-analysis`

**Summary:** Post Impact Analysis
**Operation ID:** `post_impact_analysis_api_v1_execution_coding_impact_analysis_post`

**Clinical & GxP Rationale:**
Manually triggers up-versioning impact analysis on existing coded assignments.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `ImpactAnalysisRequest`

| Field             | Type           | Required | Description |
| :---------------- | :------------- | :------- | :---------- |
| `dictionary_type` | `DictTypeEnum` | ✔ Yes    | —           |
| `new_version`     | `string`       | ✔ Yes    | —           |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema          |
| :---------- | :------------------ | :----------------------- |
| `200`       | Successful Response | `ImpactAnalysisResponse` |
| `422`       | Validation Error    | `HTTPValidationError`    |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/coding/impact-analysis",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/coding/impact-analysis',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/coding/impact-analysis' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `GET` `/api/v1/execution/coding/queue`

**Summary:** Get Coding Queue
**Operation ID:** `get_coding_queue_api_v1_execution_coding_queue_get`

**Clinical & GxP Rationale:**
Retrieves the active coding queue.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name              | In      | Type     | Required | Description |
| :---------------- | :------ | :------- | :------- | :---------- |
| `observation_id`  | `query` | `string` | `null`   | No          | —   |
| `status`          | `query` | `string` | `null`   | No          | —   |
| `verbatim_text`   | `query` | `string` | `null`   | No          | —   |
| `dictionary_type` | `query` | `string` | `null`   | No          | —   |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema                   |
| :---------- | :------------------ | :-------------------------------- |
| `200`       | Successful Response | `Array<CodingAssignmentResponse>` |
| `422`       | Validation Error    | `HTTPValidationError`             |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/coding/queue",
  {
    method: "GET",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/execution/api/v1/execution/coding/queue',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/execution/api/v1/execution/coding/queue' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/devices/deficiencies`

**Summary:** Log Deficiency
**Operation ID:** `log_deficiency_api_v1_execution_devices_deficiencies_post`

**Clinical & GxP Rationale:**
Logs a device deficiency/malfunction and triggers automated quarantine + safety cases.

@req:PRD-DEV-002, @req:PRD-DEV-003, @req:PRD-SYS-126, @req:ADR-2209

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `DeviceDeficiencyCreateRequest`

| Field                        | Type                   | Required | Description |
| :--------------------------- | :--------------------- | :------- | :---------- |
| `study_id`                   | `string`               | ✔ Yes    | —           |
| `site_id`                    | `string`               | ✔ Yes    | —           |
| `subject_id`                 | `string`               | `null`   | No          | —   |
| `device_inventory_id`        | `string`               | `null`   | No          | —   |
| `udi_di`                     | `string`               | ✔ Yes    | —           |
| `lot_number`                 | `string`               | ✔ Yes    | —           |
| `serial_number`              | `string`               | `null`   | No          | —   |
| `device_name`                | `string`               | No       | —           |
| `deficiency_type`            | `DeviceDeficiencyType` | No       | —           |
| `deficiency_description`     | `string`               | ✔ Yes    | —           |
| `incident_date`              | `string`               | ✔ Yes    | —           |
| `awareness_date`             | `string`               | No       | —           |
| `could_lead_to_sade`         | `boolean`              | No       | —           |
| `patient_involved`           | `boolean`              | No       | —           |
| `health_impact`              | `HealthImpact`         | No       | —           |
| `action_taken_device`        | `ActionTakenDevice`    | No       | —           |
| `device_returned_to_sponsor` | `boolean`              | No       | —           |
| `return_tracking_number`     | `string`               | `null`   | No          | —   |
| `quarantine_scope`           | `QuarantineScope`      | No       | —           |
| `reason_for_change`          | `string`               | No       | —           |
| `signature_token`            | `string`               | `null`   | No          | —   |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema            |
| :---------- | :------------------ | :------------------------- |
| `201`       | Successful Response | `DeviceDeficiencyResponse` |
| `422`       | Validation Error    | `HTTPValidationError`      |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/devices/deficiencies",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/devices/deficiencies',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/devices/deficiencies' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `GET` `/api/v1/execution/devices/deficiencies`

**Summary:** List Deficiencies
**Operation ID:** `list_deficiencies_api_v1_execution_devices_deficiencies_get`

**Clinical & GxP Rationale:**
Lists reported medical device deficiencies and malfunctions for a study.

@req:PRD-DEV-002, @req:PRD-SYS-126, @req:ADR-2209

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name       | In      | Type     | Required | Description      |
| :--------- | :------ | :------- | :------- | :--------------- |
| `study_id` | `query` | `string` | ✔ Yes    | Study Identifier |
| `site_id`  | `query` | `string` | `null`   | No               | Optional Site Identifier |
| `status`   | `query` | `string` | `null`   | No               | Optional Status filter   |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema                   |
| :---------- | :------------------ | :-------------------------------- |
| `200`       | Successful Response | `Array<DeviceDeficiencyResponse>` |
| `422`       | Validation Error    | `HTTPValidationError`             |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/devices/deficiencies",
  {
    method: "GET",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/execution/api/v1/execution/devices/deficiencies',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/execution/api/v1/execution/devices/deficiencies' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `GET` `/api/v1/execution/devices/deficiencies/{deficiency_id}`

**Summary:** Get Deficiency
**Operation ID:** `get_deficiency_api_v1_execution_devices_deficiencies__deficiency_id__get`

**Clinical & GxP Rationale:**
Retrieves a single device deficiency report by ID.

@req:PRD-DEV-002, @req:PRD-SYS-126, @req:ADR-2209

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name            | In     | Type     | Required | Description |
| :-------------- | :----- | :------- | :------- | :---------- |
| `deficiency_id` | `path` | `string` | ✔ Yes    | —           |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema            |
| :---------- | :------------------ | :------------------------- |
| `200`       | Successful Response | `DeviceDeficiencyResponse` |
| `422`       | Validation Error    | `HTTPValidationError`      |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/devices/deficiencies/{deficiency_id}",
  {
    method: "GET",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/execution/api/v1/execution/devices/deficiencies/{deficiency_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/execution/api/v1/execution/devices/deficiencies/{deficiency_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/devices/dispense`

**Summary:** Dispense
**Operation ID:** `dispense_api_v1_execution_devices_dispense_post`

**Clinical & GxP Rationale:**
Dispenses an investigational medical device to a study participant.

Enforces strict quarantine and expiration blocking rules.

@req:PRD-DEV-001, @req:PRD-DEV-003, @req:PRD-SYS-126, @req:ADR-2209

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `DeviceDispensationRequest`

| Field                 | Type     | Required | Description |
| :-------------------- | :------- | :------- | :---------- |
| `study_id`            | `string` | ✔ Yes    | —           |
| `site_id`             | `string` | ✔ Yes    | —           |
| `subject_id`          | `string` | ✔ Yes    | —           |
| `device_inventory_id` | `string` | `null`   | No          | —   |
| `serial_number`       | `string` | `null`   | No          | —   |
| `scanned_barcode`     | `string` | `null`   | No          | —   |
| `reason_for_change`   | `string` | No       | —           |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema              |
| :---------- | :------------------ | :--------------------------- |
| `200`       | Successful Response | `DeviceDispensationResponse` |
| `422`       | Validation Error    | `HTTPValidationError`        |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/devices/dispense",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/devices/dispense',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/devices/dispense' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/devices/gudid/lookup`

**Summary:** Lookup Gudid Endpoint
**Operation ID:** `lookup_gudid_endpoint_api_v1_execution_devices_gudid_lookup_post`

**Clinical & GxP Rationale:**
Queries device specifications from the FDA AccessGUDID reference registry.

@req:PRD-SYS-187, @req:PRD-DEV-001, @req:ADR-2209

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `GUDIDLookupRequest`

| Field    | Type     | Required | Description                         |
| :------- | :------- | :------- | :---------------------------------- |
| `udi_di` | `string` | ✔ Yes    | Device Identifier (GTIN / HIBCC DI) |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema       |
| :---------- | :------------------ | :-------------------- |
| `200`       | Successful Response | `GUDIDDeviceRecord`   |
| `422`       | Validation Error    | `HTTPValidationError` |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/devices/gudid/lookup",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/devices/gudid/lookup',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/devices/gudid/lookup' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/devices/inventory`

**Summary:** Register Inventory
**Operation ID:** `register_inventory_api_v1_execution_devices_inventory_post`

**Clinical & GxP Rationale:**
Registers serialized investigational medical device and IVD inventory items in RTSM.

@req:PRD-DEV-001, @req:PRD-SYS-126, @req:ADR-2209

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `DeviceInventoryCreateRequest`

| Field                   | Type                   | Required | Description |
| :---------------------- | :--------------------- | :------- | :---------- |
| `study_id`              | `string`               | ✔ Yes    | —           |
| `site_id`               | `string`               | ✔ Yes    | —           |
| `device_identifier`     | `string`               | `null`   | No          | —   |
| `brand_name`            | `string`               | ✔ Yes    | —           |
| `device_model`          | `string`               | `null`   | No          | —   |
| `device_type`           | `DeviceType`           | No       | —           |
| `device_classification` | `DeviceClassification` | No       | —           |
| `raw_barcode`           | `string`               | `null`   | No          | —   |
| `udi_di`                | `string`               | ✔ Yes    | —           |
| `lot_number`            | `string`               | ✔ Yes    | —           |
| `serial_number`         | `string`               | `null`   | No          | —   |
| `expiration_date`       | `string`               | `null`   | No          | —   |
| `manufacturing_date`    | `string`               | `null`   | No          | —   |
| `software_version`      | `string`               | `null`   | No          | —   |
| `quantity`              | `integer`              | No       | —           |
| `reason_for_change`     | `string`               | No       | —           |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema                  |
| :---------- | :------------------ | :------------------------------- |
| `201`       | Successful Response | `Array<DeviceInventoryResponse>` |
| `422`       | Validation Error    | `HTTPValidationError`            |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/devices/inventory",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/devices/inventory',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/devices/inventory' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `GET` `/api/v1/execution/devices/inventory`

**Summary:** List Inventory
**Operation ID:** `list_inventory_api_v1_execution_devices_inventory_get`

**Clinical & GxP Rationale:**
Retrieves investigational medical device inventory matching query filters.

@req:PRD-DEV-001, @req:PRD-SYS-126, @req:ADR-2209

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name            | In      | Type     | Required | Description      |
| :-------------- | :------ | :------- | :------- | :--------------- |
| `study_id`      | `query` | `string` | ✔ Yes    | Study Identifier |
| `site_id`       | `query` | `string` | `null`   | No               | Optional Site Identifier |
| `status`        | `query` | `string` | `null`   | No               | Optional Status filter   |
| `udi_di`        | `query` | `string` | `null`   | No               | Optional UDI-DI filter   |
| `lot_number`    | `query` | `string` | `null`   | No               | Optional Lot filter      |
| `serial_number` | `query` | `string` | `null`   | No               | Optional Serial filter   |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema                  |
| :---------- | :------------------ | :------------------------------- |
| `200`       | Successful Response | `Array<DeviceInventoryResponse>` |
| `422`       | Validation Error    | `HTTPValidationError`            |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/devices/inventory",
  {
    method: "GET",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/execution/api/v1/execution/devices/inventory',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/execution/api/v1/execution/devices/inventory' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/devices/ivd/assays/performance`

**Summary:** Compute Assay Performance Endpoint
**Operation ID:** `compute_assay_performance_endpoint_api_v1_execution_devices_ivd_assays_performance_post`

**Clinical & GxP Rationale:**
Computes and seals diagnostic assay performance statistics (Sensitivity, Specificity, PPV, NPV, CV%, LOD/LOQ).

@req:PRD-SYS-188, @req:PRD-DEV-004, @req:ADR-2209

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `IVDAssayPerformanceRequest`

| Field                       | Type                   | Required | Description |
| :-------------------------- | :--------------------- | :------- | :---------- |
| `study_id`                  | `string`               | ✔ Yes    | —           |
| `assay_name`                | `string`               | ✔ Yes    | —           |
| `assay_lot_number`          | `string`               | ✔ Yes    | —           |
| `device_udi_di`             | `string`               | `null`   | No          | —   |
| `true_positives`            | `integer`              | ✔ Yes    | —           |
| `false_positives`           | `integer`              | ✔ Yes    | —           |
| `true_negatives`            | `integer`              | ✔ Yes    | —           |
| `false_negatives`           | `integer`              | ✔ Yes    | —           |
| `replicate_measurements`    | `Array<number>`        | No       | —           |
| `inter_assay_runs`          | `Array<Array<number>>` | No       | —           |
| `blank_measurements`        | `Array<number>`        | No       | —           |
| `low_standard_measurements` | `Array<number>`        | No       | —           |
| `linearity_concentrations`  | `Array<number>`        | No       | —           |
| `linearity_signals`         | `Array<number>`        | No       | —           |
| `reason_for_change`         | `string`               | No       | —           |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema               |
| :---------- | :------------------ | :---------------------------- |
| `201`       | Successful Response | `IVDAssayPerformanceResponse` |
| `422`       | Validation Error    | `HTTPValidationError`         |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/devices/ivd/assays/performance",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/devices/ivd/assays/performance',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/devices/ivd/assays/performance' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/devices/ivd/specimens`

**Summary:** Register Specimen Endpoint
**Operation ID:** `register_specimen_endpoint_api_v1_execution_devices_ivd_specimens_post`

**Clinical & GxP Rationale:**
Registers an IVD clinical biospecimen collection in the specimen ledger.

@req:PRD-SYS-188, @req:PRD-DEV-001, @req:ADR-2209

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `IVDSpecimenCreateRequest`

| Field                         | Type               | Required | Description |
| :---------------------------- | :----------------- | :------- | :---------- |
| `study_id`                    | `string`           | ✔ Yes    | —           |
| `site_id`                     | `string`           | ✔ Yes    | —           |
| `subject_id`                  | `string`           | ✔ Yes    | —           |
| `specimen_identifier`         | `string`           | `null`   | No          | —   |
| `specimen_type`               | `IVDSpecimenType`  | No       | —           |
| `collection_date`             | `string`           | No       | —           |
| `collector_user_id`           | `string`           | `null`   | No          | —   |
| `container_type`              | `IVDContainerType` | No       | —           |
| `initial_volume_ul`           | `number`           | No       | —           |
| `storage_temperature_celsius` | `number`           | No       | —           |
| `storage_location`            | `string`           | No       | —           |
| `max_allowed_freeze_thaw`     | `integer`          | No       | —           |
| `reason_for_change`           | `string`           | No       | —           |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema       |
| :---------- | :------------------ | :-------------------- |
| `201`       | Successful Response | `IVDSpecimenResponse` |
| `422`       | Validation Error    | `HTTPValidationError` |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/devices/ivd/specimens",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/devices/ivd/specimens',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/devices/ivd/specimens' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `GET` `/api/v1/execution/devices/ivd/specimens`

**Summary:** List Specimens Endpoint
**Operation ID:** `list_specimens_endpoint_api_v1_execution_devices_ivd_specimens_get`

**Clinical & GxP Rationale:**
Lists IVD biospecimens matching query filters.

@req:PRD-SYS-188, @req:PRD-DEV-001, @req:ADR-2209

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name         | In      | Type     | Required | Description |
| :----------- | :------ | :------- | :------- | :---------- |
| `study_id`   | `query` | `string` | `null`   | No          | Optional Study Identifier   |
| `site_id`    | `query` | `string` | `null`   | No          | Optional Site Identifier    |
| `subject_id` | `query` | `string` | `null`   | No          | Optional Subject Identifier |
| `status`     | `query` | `string` | `null`   | No          | Optional Status filter      |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema              |
| :---------- | :------------------ | :--------------------------- |
| `200`       | Successful Response | `Array<IVDSpecimenResponse>` |
| `422`       | Validation Error    | `HTTPValidationError`        |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/devices/ivd/specimens",
  {
    method: "GET",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/execution/api/v1/execution/devices/ivd/specimens',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/execution/api/v1/execution/devices/ivd/specimens' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `GET` `/api/v1/execution/devices/ivd/specimens/{specimen_id}`

**Summary:** Get Specimen Endpoint
**Operation ID:** `get_specimen_endpoint_api_v1_execution_devices_ivd_specimens__specimen_id__get`

**Clinical & GxP Rationale:**
Retrieves a single IVD biospecimen record with complete audit logs and history.

@req:PRD-SYS-188, @req:PRD-DEV-001, @req:ADR-2209

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name          | In     | Type     | Required | Description |
| :------------ | :----- | :------- | :------- | :---------- |
| `specimen_id` | `path` | `string` | ✔ Yes    | —           |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema       |
| :---------- | :------------------ | :-------------------- |
| `200`       | Successful Response | `IVDSpecimenResponse` |
| `422`       | Validation Error    | `HTTPValidationError` |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/devices/ivd/specimens/{specimen_id}",
  {
    method: "GET",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/execution/api/v1/execution/devices/ivd/specimens/{specimen_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/execution/api/v1/execution/devices/ivd/specimens/{specimen_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/devices/ivd/specimens/{specimen_id}/chain-of-custody`

**Summary:** Log Custody Endpoint
**Operation ID:** `log_custody_endpoint_api_v1_execution_devices_ivd_specimens__specimen_id__chain_of_custody_post`

**Clinical & GxP Rationale:**
Logs an auditable chain-of-custody transfer event for an IVD specimen.

@req:PRD-SYS-188, @req:PRD-DEV-001, @req:ADR-2209

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name          | In     | Type     | Required | Description |
| :------------ | :----- | :------- | :------- | :---------- |
| `specimen_id` | `path` | `string` | ✔ Yes    | —           |

#### Request Payload Schema (`application/json`)

**Schema Type:** `IVDSpecimenCustodyEventRequest`

| Field                             | Type      | Required | Description |
| :-------------------------------- | :-------- | :------- | :---------- |
| `from_user_id`                    | `string`  | ✔ Yes    | —           |
| `to_user_id`                      | `string`  | ✔ Yes    | —           |
| `from_location`                   | `string`  | ✔ Yes    | —           |
| `to_location`                     | `string`  | ✔ Yes    | —           |
| `transport_condition`             | `string`  | No       | —           |
| `temperature_at_transfer_celsius` | `number`  | `null`   | No          | —   |
| `seal_intact`                     | `boolean` | No       | —           |
| `signature_token`                 | `string`  | `null`   | No          | —   |
| `reason_for_change`               | `string`  | No       | —           |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema                   |
| :---------- | :------------------ | :-------------------------------- |
| `201`       | Successful Response | `IVDSpecimenCustodyEventResponse` |
| `422`       | Validation Error    | `HTTPValidationError`             |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/devices/ivd/specimens/{specimen_id}/chain-of-custody",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/devices/ivd/specimens/{specimen_id}/chain-of-custody',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/devices/ivd/specimens/{specimen_id}/chain-of-custody' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/devices/ivd/specimens/{specimen_id}/freeze-thaw`

**Summary:** Log Freeze Thaw Endpoint
**Operation ID:** `log_freeze_thaw_endpoint_api_v1_execution_devices_ivd_specimens__specimen_id__freeze_thaw_post`

**Clinical & GxP Rationale:**
Logs a freeze-thaw thermal cycle with automated quarantine enforcement upon threshold breach.

@req:PRD-SYS-188, @req:PRD-DEV-003, @req:ADR-2209

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name          | In     | Type     | Required | Description |
| :------------ | :----- | :------- | :------- | :---------- |
| `specimen_id` | `path` | `string` | ✔ Yes    | —           |

#### Request Payload Schema (`application/json`)

**Schema Type:** `IVDSpecimenFreezeThawEventRequest`

| Field                      | Type      | Required | Description |
| :------------------------- | :-------- | :------- | :---------- |
| `thaw_start_timestamp`     | `string`  | No       | —           |
| `thaw_end_timestamp`       | `string`  | `null`   | No          | —   |
| `thaw_duration_minutes`    | `number`  | No       | —           |
| `thaw_temperature_celsius` | `number`  | No       | —           |
| `operator_user_id`         | `string`  | ✔ Yes    | —           |
| `purpose`                  | `string`  | ✔ Yes    | —           |
| `is_excursion`             | `boolean` | No       | —           |
| `reason_for_change`        | `string`  | No       | —           |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema                      |
| :---------- | :------------------ | :----------------------------------- |
| `201`       | Successful Response | `IVDSpecimenFreezeThawEventResponse` |
| `422`       | Validation Error    | `HTTPValidationError`                |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/devices/ivd/specimens/{specimen_id}/freeze-thaw",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/devices/ivd/specimens/{specimen_id}/freeze-thaw',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/devices/ivd/specimens/{specimen_id}/freeze-thaw' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/devices/ivd/specimens/{specimen_id}/integrity-check`

**Summary:** Sample Integrity Check Endpoint
**Operation ID:** `sample_integrity_check_endpoint_api_v1_execution_devices_ivd_specimens__specimen_id__integrity_check_post`

**Clinical & GxP Rationale:**
Assesses specimen integrity using Hemolysis, Lipemia, and Icterus (HIL) index validation.

@req:PRD-SYS-188, @req:PRD-DEV-002, @req:ADR-2209

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name          | In     | Type     | Required | Description |
| :------------ | :----- | :------- | :------- | :---------- |
| `specimen_id` | `path` | `string` | ✔ Yes    | —           |

#### Request Payload Schema (`application/json`)

**Schema Type:** `IVDSampleIntegrityCheckRequest`

| Field                   | Type      | Required | Description |
| :---------------------- | :-------- | :------- | :---------- |
| `hemolysis_index_mg_dl` | `number`  | No       | —           |
| `lipemia_index_mg_dl`   | `number`  | No       | —           |
| `icterus_index_mg_dl`   | `number`  | No       | —           |
| `clot_detected`         | `boolean` | No       | —           |
| `temperature_excursion` | `boolean` | No       | —           |
| `volume_adequate`       | `boolean` | No       | —           |
| `integrity_notes`       | `string`  | `null`   | No          | —   |
| `reason_for_change`     | `string`  | No       | —           |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema                   |
| :---------- | :------------------ | :-------------------------------- |
| `200`       | Successful Response | `IVDSampleIntegrityCheckResponse` |
| `422`       | Validation Error    | `HTTPValidationError`             |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/devices/ivd/specimens/{specimen_id}/integrity-check",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/devices/ivd/specimens/{specimen_id}/integrity-check',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/devices/ivd/specimens/{specimen_id}/integrity-check' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/devices/parse-udi`

**Summary:** Parse Udi
**Operation ID:** `parse_udi_api_v1_execution_devices_parse_udi_post`

**Clinical & GxP Rationale:**
Parses and validates a Unique Device Identifier (UDI) barcode string.

Supports GS1-128, GS1-DataMatrix (AIs 01, 10, 11, 17, 21, 8012), and HIBCC formats.

@req:PRD-DEV-001, @req:PRD-SYS-126, @req:ADR-2209

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `UDIParseRequest`

| Field         | Type     | Required | Description                                |
| :------------ | :------- | :------- | :----------------------------------------- |
| `raw_barcode` | `string` | ✔ Yes    | Raw barcode string (GS1 / HIBCC / Raw UDI) |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema       |
| :---------- | :------------------ | :-------------------- |
| `200`       | Successful Response | `ParsedUDI`           |
| `422`       | Validation Error    | `HTTPValidationError` |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/devices/parse-udi",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/devices/parse-udi',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/devices/parse-udi' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/devices/quarantine`

**Summary:** Initiate Quarantine
**Operation ID:** `initiate_quarantine_api_v1_execution_devices_quarantine_post`

**Clinical & GxP Rationale:**
Manually places a device serial or lot into QUARANTINED status with dispensation blocking.

@req:PRD-DEV-003, @req:PRD-SYS-126, @req:ADR-2209

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `DeviceQuarantineCreateRequest`

| Field                 | Type              | Required | Description |
| :-------------------- | :---------------- | :------- | :---------- |
| `study_id`            | `string`          | ✔ Yes    | —           |
| `site_id`             | `string`          | ✔ Yes    | —           |
| `scope`               | `QuarantineScope` | No       | —           |
| `device_inventory_id` | `string`          | `null`   | No          | —   |
| `lot_number`          | `string`          | `null`   | No          | —   |
| `serial_number`       | `string`          | `null`   | No          | —   |
| `reason`              | `string`          | ✔ Yes    | —           |
| `reason_for_change`   | `string`          | No       | —           |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema            |
| :---------- | :------------------ | :------------------------- |
| `201`       | Successful Response | `DeviceQuarantineResponse` |
| `422`       | Validation Error    | `HTTPValidationError`      |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/devices/quarantine",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/devices/quarantine',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/devices/quarantine' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `GET` `/api/v1/execution/devices/quarantine`

**Summary:** List Quarantines
**Operation ID:** `list_quarantines_api_v1_execution_devices_quarantine_get`

**Clinical & GxP Rationale:**
Lists device quarantine records for a clinical trial study.

@req:PRD-DEV-003, @req:PRD-SYS-126, @req:ADR-2209

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name       | In      | Type     | Required | Description      |
| :--------- | :------ | :------- | :------- | :--------------- |
| `study_id` | `query` | `string` | ✔ Yes    | Study Identifier |
| `site_id`  | `query` | `string` | `null`   | No               | Optional Site Identifier |
| `status`   | `query` | `string` | `null`   | No               | Optional Status filter   |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema                   |
| :---------- | :------------------ | :-------------------------------- |
| `200`       | Successful Response | `Array<DeviceQuarantineResponse>` |
| `422`       | Validation Error    | `HTTPValidationError`             |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/devices/quarantine",
  {
    method: "GET",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/execution/api/v1/execution/devices/quarantine',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/execution/api/v1/execution/devices/quarantine' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/devices/quarantine/release`

**Summary:** Release Quarantine
**Operation ID:** `release_quarantine_api_v1_execution_devices_quarantine_release_post`

**Clinical & GxP Rationale:**
Releases a device quarantine record under 21 CFR Part 11 electronic signature.

@req:PRD-DEV-003, @req:PRD-SYS-126, @req:ADR-2209

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `DeviceQuarantineReleaseRequest`

| Field                   | Type     | Required | Description |
| :---------------------- | :------- | :------- | :---------- |
| `quarantine_id`         | `string` | ✔ Yes    | —           |
| `release_justification` | `string` | ✔ Yes    | —           |
| `signature_token`       | `string` | ✔ Yes    | —           |
| `reason_for_change`     | `string` | No       | —           |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema            |
| :---------- | :------------------ | :------------------------- |
| `200`       | Successful Response | `DeviceQuarantineResponse` |
| `422`       | Validation Error    | `HTTPValidationError`      |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/devices/quarantine/release",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/devices/quarantine/release',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/devices/quarantine/release' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `GET` `/api/v1/execution/devices/summary`

**Summary:** Get Device Summary
**Operation ID:** `get_device_summary_api_v1_execution_devices_summary_get`

**Clinical & GxP Rationale:**
Retrieves operational metrics for the Medical Device & IVD Investigation console.

@req:PRD-DEV-001, @req:PRD-DEV-002, @req:PRD-DEV-003, @req:PRD-SYS-126, @req:ADR-2209

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name       | In      | Type     | Required | Description      |
| :--------- | :------ | :------- | :------- | :--------------- |
| `study_id` | `query` | `string` | ✔ Yes    | Study Identifier |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema              |
| :---------- | :------------------ | :--------------------------- |
| `200`       | Successful Response | `DeviceInvestigationSummary` |
| `422`       | Validation Error    | `HTTPValidationError`        |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/devices/summary",
  {
    method: "GET",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/execution/api/v1/execution/devices/summary',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/execution/api/v1/execution/devices/summary' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/devices/verify-udi`

**Summary:** Verify Udi Endpoint
**Operation ID:** `verify_udi_endpoint_api_v1_execution_devices_verify_udi_post`

**Clinical & GxP Rationale:**
Validates physical/scanned UDI data (DI + PI lot, serial, expiration) against the FDA GUDID registry.

@req:PRD-SYS-187, @req:PRD-DEV-001, @req:ADR-2209

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name              | In      | Type     | Required | Description |
| :---------------- | :------ | :------- | :------- | :---------- |
| `raw_barcode`     | `query` | `string` | `null`   | No          | Optional raw barcode string |
| `udi_di`          | `query` | `string` | `null`   | No          | Optional Device Identifier  |
| `lot_number`      | `query` | `string` | `null`   | No          | Optional Lot/Batch number   |
| `serial_number`   | `query` | `string` | `null`   | No          | Optional Serial number      |
| `expiration_date` | `query` | `string` | `null`   | No          | Optional Expiration Date    |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema           |
| :---------- | :------------------ | :------------------------ |
| `200`       | Successful Response | `GUDIDVerificationResult` |
| `422`       | Validation Error    | `HTTPValidationError`     |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/devices/verify-udi",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/devices/verify-udi',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/devices/verify-udi' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/doa/assignment`

**Summary:** Add Doa Assignment Endpoint
**Operation ID:** `add_doa_assignment_endpoint_api_v1_execution_doa_assignment_post`

**Clinical & GxP Rationale:**
Add site personnel task delegation entry to Delegation of Authority log.

Requirements: PRD-SYS-001

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Mandatory 60s `sig_token` required for Part 11 sign-off

#### Request Payload Schema (`application/json`)

**Schema Type:** `AddDOAAssignmentRequest`

| Field             | Type                           | Required | Description                        |
| :---------------- | :----------------------------- | :------- | :--------------------------------- |
| `study_id`        | `string`                       | ✔ Yes    | Target protocol study ID           |
| `site_id`         | `string`                       | ✔ Yes    | Target investigator site ID        |
| `personnel_name`  | `string`                       | ✔ Yes    | Full legal name                    |
| `personnel_email` | `string`                       | ✔ Yes    | Email address                      |
| `role`            | `DOATaskRoleEnum`              | ✔ Yes    | Site role                          |
| `delegated_tasks` | `Array<DOATaskDelegationEnum>` | ✔ Yes    | List of delegated tasks            |
| `start_date`      | `string`                       | ✔ Yes    | Delegation start date (YYYY-MM-DD) |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema       |
| :---------- | :------------------ | :-------------------- |
| `201`       | Successful Response | `DOAAssignmentRecord` |
| `422`       | Validation Error    | `HTTPValidationError` |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/doa/assignment",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/doa/assignment',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/doa/assignment' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `GET` `/api/v1/execution/doa/audit-logs`

**Summary:** Get Audit Logs Endpoint
**Operation ID:** `get_audit_logs_endpoint_api_v1_execution_doa_audit_logs_get`

**Clinical & GxP Rationale:**
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema              |
| :---------- | :------------------ | :--------------------------- |
| `200`       | Successful Response | `Array<DOAAuditLogResponse>` |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/doa/audit-logs",
  {
    method: "GET",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/execution/api/v1/execution/doa/audit-logs',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/execution/api/v1/execution/doa/audit-logs' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/doa/delegate`

**Summary:** Delegate Task Endpoint
**Operation ID:** `delegate_task_endpoint_api_v1_execution_doa_delegate_post`

**Clinical & GxP Rationale:**
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `DelegateTaskRequest`

| Field               | Type     | Required | Description |
| :------------------ | :------- | :------- | :---------- |
| `site_id`           | `string` | ✔ Yes    | —           |
| `staff_user_id`     | `string` | ✔ Yes    | —           |
| `task_code`         | `string` | ✔ Yes    | —           |
| `pi_user_id`        | `string` | ✔ Yes    | —           |
| `reason_for_change` | `string` | ✔ Yes    | —           |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema               |
| :---------- | :------------------ | :---------------------------- |
| `200`       | Successful Response | `DOADelegationRecordResponse` |
| `422`       | Validation Error    | `HTTPValidationError`         |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/doa/delegate",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/doa/delegate',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/doa/delegate' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `GET` `/api/v1/execution/doa/delegations`

**Summary:** Get Delegations Endpoint
**Operation ID:** `get_delegations_endpoint_api_v1_execution_doa_delegations_get`

**Clinical & GxP Rationale:**
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema                      |
| :---------- | :------------------ | :----------------------------------- |
| `200`       | Successful Response | `Array<DOADelegationRecordResponse>` |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/doa/delegations",
  {
    method: "GET",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/execution/api/v1/execution/doa/delegations',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/execution/api/v1/execution/doa/delegations' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/doa/endorse`

**Summary:** Approve Delegation Endpoint
**Operation ID:** `approve_delegation_endpoint_api_v1_execution_doa_endorse_post`

**Clinical & GxP Rationale:**
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `ApproveDelegationRequest`

| Field           | Type     | Required | Description |
| :-------------- | :------- | :------- | :---------- |
| `delegation_id` | `string` | ✔ Yes    | —           |
| `pi_user_id`    | `string` | ✔ Yes    | —           |
| `password`      | `string` | ✔ Yes    | —           |
| `totp_code`     | `string` | `null`   | No          | —   |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema               |
| :---------- | :------------------ | :---------------------------- |
| `200`       | Successful Response | `DOADelegationRecordResponse` |
| `422`       | Validation Error    | `HTTPValidationError`         |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/doa/endorse",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/doa/endorse',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/doa/endorse' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/doa/endorse_task`

**Summary:** Approve Task Endpoint
**Operation ID:** `approve_task_endpoint_api_v1_execution_doa_endorse_task_post`

**Clinical & GxP Rationale:**
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `ApproveTaskDelegationRequest`

| Field               | Type     | Required | Description |
| :------------------ | :------- | :------- | :---------- |
| `delegation_id`     | `string` | ✔ Yes    | —           |
| `pi_user_id`        | `string` | ✔ Yes    | —           |
| `signature_hash`    | `string` | ✔ Yes    | —           |
| `reason_for_change` | `string` | ✔ Yes    | —           |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema               |
| :---------- | :------------------ | :---------------------------- |
| `200`       | Successful Response | `DOADelegationRecordResponse` |
| `422`       | Validation Error    | `HTTPValidationError`         |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/doa/endorse_task",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/doa/endorse_task',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/doa/endorse_task' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `GET` `/api/v1/execution/doa/log/{study_id}/{site_id}`

**Summary:** Get Site Doa Log Endpoint
**Operation ID:** `get_site_doa_log_endpoint_api_v1_execution_doa_log__study_id___site_id__get`

**Clinical & GxP Rationale:**
Retrieve site-isolated Delegation of Authority log entries.

Requirements: PRD-SYS-001

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name       | In     | Type     | Required | Description |
| :--------- | :----- | :------- | :------- | :---------- |
| `study_id` | `path` | `string` | ✔ Yes    | —           |
| `site_id`  | `path` | `string` | ✔ Yes    | —           |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema              |
| :---------- | :------------------ | :--------------------------- |
| `200`       | Successful Response | `Array<DOAAssignmentRecord>` |
| `422`       | Validation Error    | `HTTPValidationError`        |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/doa/log/{study_id}/{site_id}",
  {
    method: "GET",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/execution/api/v1/execution/doa/log/{study_id}/{site_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/execution/api/v1/execution/doa/log/{study_id}/{site_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/doa/revoke`

**Summary:** Revoke Delegation Endpoint
**Operation ID:** `revoke_delegation_endpoint_api_v1_execution_doa_revoke_post`

**Clinical & GxP Rationale:**
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `RevokeDelegationRequest`

| Field               | Type     | Required | Description |
| :------------------ | :------- | :------- | :---------- |
| `delegation_id`     | `string` | ✔ Yes    | —           |
| `end_date`          | `string` | ✔ Yes    | —           |
| `reason_for_change` | `string` | ✔ Yes    | —           |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema               |
| :---------- | :------------------ | :---------------------------- |
| `200`       | Successful Response | `DOADelegationRecordResponse` |
| `422`       | Validation Error    | `HTTPValidationError`         |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/doa/revoke",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/doa/revoke',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/doa/revoke' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/doa/sign-off`

**Summary:** Sign Off Doa Assignment Endpoint
**Operation ID:** `sign_off_doa_assignment_endpoint_api_v1_execution_doa_sign_off_post`

**Clinical & GxP Rationale:**
Endorse Delegation of Authority task assignment with Principal Investigator eSignature.

Requirements: PRD-SYS-001

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Mandatory 60s `sig_token` required for Part 11 sign-off

#### Request Payload Schema (`application/json`)

**Schema Type:** `DOASignOffRequest`

| Field               | Type     | Required | Description                                |
| :------------------ | :------- | :------- | :----------------------------------------- |
| `record_id`         | `string` | ✔ Yes    | Target DOA record ID                       |
| `reason_for_change` | `string` | ✔ Yes    | Mandatory GxP 21 CFR Part 11 justification |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema       |
| :---------- | :------------------ | :-------------------- |
| `200`       | Successful Response | `DOAAssignmentRecord` |
| `422`       | Validation Error    | `HTTPValidationError` |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/doa/sign-off",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/doa/sign-off',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/doa/sign-off' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/doa/staff`

**Summary:** Create Staff Endpoint
**Operation ID:** `create_staff_endpoint_api_v1_execution_doa_staff_post`

**Clinical & GxP Rationale:**
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `SiteStaffMemberRequest`

| Field              | Type      | Required | Description |
| :----------------- | :-------- | :------- | :---------- |
| `site_id`          | `string`  | ✔ Yes    | —           |
| `staff_user_id`    | `string`  | ✔ Yes    | —           |
| `name`             | `string`  | ✔ Yes    | —           |
| `email`            | `string`  | ✔ Yes    | —           |
| `has_gcp_training` | `boolean` | No       | —           |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema           |
| :---------- | :------------------ | :------------------------ |
| `201`       | Successful Response | `SiteStaffMemberResponse` |
| `422`       | Validation Error    | `HTTPValidationError`     |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/doa/staff",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/doa/staff',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/doa/staff' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `GET` `/api/v1/execution/eisf/binder/{study_id}/{site_id}`

**Summary:** Get Site Regulatory Binder Endpoint
**Operation ID:** `get_site_regulatory_binder_endpoint_api_v1_execution_eisf_binder__study_id___site_id__get`

**Clinical & GxP Rationale:**
Retrieve site-isolated regulatory binder documents for specified study and site.

Requirements: PRD-SYS-001

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name       | In     | Type     | Required | Description |
| :--------- | :----- | :------- | :------- | :---------- |
| `study_id` | `path` | `string` | ✔ Yes    | —           |
| `site_id`  | `path` | `string` | ✔ Yes    | —           |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema             |
| :---------- | :------------------ | :-------------------------- |
| `200`       | Successful Response | `Array<EISFDocumentRecord>` |
| `422`       | Validation Error    | `HTTPValidationError`       |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/eisf/binder/{study_id}/{site_id}",
  {
    method: "GET",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/execution/api/v1/execution/eisf/binder/{study_id}/{site_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/execution/api/v1/execution/eisf/binder/{study_id}/{site_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/eisf/upload`

**Summary:** Upload Eisf Document Endpoint
**Operation ID:** `upload_eisf_document_endpoint_api_v1_execution_eisf_upload_post`

**Clinical & GxP Rationale:**
Upload eISF regulatory binder document and calculate SHA-256 integrity checksum.

Requirements: PRD-SYS-001

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `UploadEISFDocumentRequest`

| Field            | Type                       | Required | Description                        |
| :--------------- | :------------------------- | :------- | :--------------------------------- |
| `study_id`       | `string`                   | ✔ Yes    | Target protocol study ID           |
| `site_id`        | `string`                   | ✔ Yes    | Target investigator site ID        |
| `category`       | `EISFTaxonomyCategoryEnum` | ✔ Yes    | DIA taxonomy category              |
| `title`          | `string`                   | ✔ Yes    | Document title                     |
| `file_name`      | `string`                   | ✔ Yes    | Original file name                 |
| `content_base64` | `string`                   | ✔ Yes    | Base64 encoded file content string |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema       |
| :---------- | :------------------ | :-------------------- |
| `201`       | Successful Response | `EISFDocumentRecord`  |
| `422`       | Validation Error    | `HTTPValidationError` |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/eisf/upload",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/eisf/upload',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/eisf/upload' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `GET` `/api/v1/execution/export`

**Summary:** Export Study Cdisc Odm Xml
**Operation ID:** `export_study_cdisc_odm_xml_api_v1_execution_export_get`

**Clinical & GxP Rationale:**
Export all study observations, subjects, visits, and forms as CDISC ODM XML.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name       | In      | Type     | Required | Description                     |
| :--------- | :------ | :------- | :------- | :------------------------------ |
| `study_id` | `query` | `string` | ✔ Yes    | The clinical study ID to export |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema       |
| :---------- | :------------------ | :-------------------- |
| `200`       | Successful Response | `any`                 |
| `422`       | Validation Error    | `HTTPValidationError` |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/export",
  {
    method: "GET",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/execution/api/v1/execution/export',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/execution/api/v1/execution/export' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/exports/wizard`

**Summary:** Execute Export Wizard
**Operation ID:** `execute_export_wizard_api_v1_execution_exports_wizard_post`

**Clinical & GxP Rationale:**
Executes a parameterized export job from the Regulatory Export Wizard.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `ExportBundleRequest`

| Field                  | Type            | Required | Description                                                                     |
| :--------------------- | :-------------- | :------- | :------------------------------------------------------------------------------ |
| `study_id`             | `string`        | ✔ Yes    | —                                                                               |
| `format`               | `string`        | No       | Target format: dataset_json, xpt_v5, xpt_v8, odm_xml, csv, csv_zip              |
| `domains`              | `Array<string>` | No       | List of SDTM domains to include (e.g. DM, AE, VS, LB, MH, CM)                   |
| `datasets`             | `Array<string>` | No       | List of ADaM datasets to include (e.g. ADSL, ADAE, ADVS)                        |
| `site_ids`             | `Array<string>` | No       | Optional list of site IDs to filter by                                          |
| `cohorts`              | `Array<string>` | No       | Optional list of subject cohorts/arms to filter by                              |
| `privacy_profile`      | `string`        | No       | Privacy policy: SAFE_HARBOR, LIMITED_DATA_SET, GDPR_PSEUDONYMIZED, UNRESTRICTED |
| `xpt_version`          | `string`        | No       | SAS Transport version: v5 or v8                                                 |
| `include_audit_trail`  | `boolean`       | No       | Whether to include GxP audit records in ODM-XML / CSV                           |
| `metadata_version_oid` | `string`        | No       | ODM / Dataset-JSON metadata version OID                                         |
| `salt`                 | `string`        | `null`   | No                                                                              | Optional HMAC salt override for deterministic de-identification |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema       |
| :---------- | :------------------ | :-------------------- |
| `200`       | Successful Response | `object`              |
| `422`       | Validation Error    | `HTTPValidationError` |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/exports/wizard",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/exports/wizard',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/exports/wizard' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/form-submissions`

**Summary:** Create Form Submission
**Operation ID:** `create_form_submission_api_v1_execution_form_submissions_post`

**Clinical & GxP Rationale:**
Create a new FormSubmission in DRAFT status.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `FormSubmissionCreate`

| Field              | Type     | Required | Description |
| :----------------- | :------- | :------- | :---------- |
| `study_id`         | `string` | ✔ Yes    | —           |
| `site_id`          | `string` | ✔ Yes    | —           |
| `subject_id`       | `string` | ✔ Yes    | —           |
| `visit_id`         | `string` | `null`   | No          | —   |
| `form_id`          | `string` | ✔ Yes    | —           |
| `protocol_version` | `string` | `null`   | No          | —   |
| `payload`          | `object` | `null`   | No          | —   |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema          |
| :---------- | :------------------ | :----------------------- |
| `201`       | Successful Response | `FormSubmissionResponse` |
| `422`       | Validation Error    | `HTTPValidationError`    |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/form-submissions",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/form-submissions',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/form-submissions' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `GET` `/api/v1/execution/form-submissions`

**Summary:** List Form Submissions
**Operation ID:** `list_form_submissions_api_v1_execution_form_submissions_get`

**Clinical & GxP Rationale:**
List form submissions with filters.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name               | In      | Type      | Required | Description |
| :----------------- | :------ | :-------- | :------- | :---------- |
| `study_id`         | `query` | `string`  | `null`   | No          | —   |
| `subject_id`       | `query` | `string`  | `null`   | No          | —   |
| `visit_id`         | `query` | `string`  | `null`   | No          | —   |
| `form_id`          | `query` | `string`  | `null`   | No          | —   |
| `include_inactive` | `query` | `boolean` | No       | —           |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema                 |
| :---------- | :------------------ | :------------------------------ |
| `200`       | Successful Response | `Array<FormSubmissionResponse>` |
| `422`       | Validation Error    | `HTTPValidationError`           |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/form-submissions",
  {
    method: "GET",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/execution/api/v1/execution/form-submissions',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/execution/api/v1/execution/form-submissions' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `GET` `/api/v1/execution/form-submissions/{submission_id}`

**Summary:** Get Form Submission
**Operation ID:** `get_form_submission_api_v1_execution_form_submissions__submission_id__get`

**Clinical & GxP Rationale:**
Retrieve a single form submission by ID.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name            | In     | Type     | Required | Description |
| :-------------- | :----- | :------- | :------- | :---------- |
| `submission_id` | `path` | `string` | ✔ Yes    | —           |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema          |
| :---------- | :------------------ | :----------------------- |
| `200`       | Successful Response | `FormSubmissionResponse` |
| `422`       | Validation Error    | `HTTPValidationError`    |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/form-submissions/{submission_id}",
  {
    method: "GET",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/execution/api/v1/execution/form-submissions/{submission_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/execution/api/v1/execution/form-submissions/{submission_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/form-submissions/{submission_id}/approve`

**Summary:** Approve Form Submission
**Operation ID:** `approve_form_submission_api_v1_execution_form_submissions__submission_id__approve_post`

**Clinical & GxP Rationale:**
PI Approve/Sign-off a completed FormSubmission.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name            | In     | Type     | Required | Description |
| :-------------- | :----- | :------- | :------- | :---------- |
| `submission_id` | `path` | `string` | ✔ Yes    | —           |

#### Request Payload Schema (`application/json`)

**Schema Type:** `FormSubmissionApprove`

| Field                | Type     | Required | Description |
| :------------------- | :------- | :------- | :---------- |
| `signing_reason`     | `string` | ✔ Yes    | —           |
| `signature_manifest` | `object` | ✔ Yes    | —           |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema          |
| :---------- | :------------------ | :----------------------- |
| `200`       | Successful Response | `FormSubmissionResponse` |
| `422`       | Validation Error    | `HTTPValidationError`    |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/form-submissions/{submission_id}/approve",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/form-submissions/{submission_id}/approve',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/form-submissions/{submission_id}/approve' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/form-submissions/{submission_id}/complete`

**Summary:** Complete Form Submission
**Operation ID:** `complete_form_submission_api_v1_execution_form_submissions__submission_id__complete_post`

**Clinical & GxP Rationale:**
Transition a FormSubmission from DRAFT to COMPLETED.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name            | In     | Type     | Required | Description |
| :-------------- | :----- | :------- | :------- | :---------- |
| `submission_id` | `path` | `string` | ✔ Yes    | —           |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema          |
| :---------- | :------------------ | :----------------------- |
| `200`       | Successful Response | `FormSubmissionResponse` |
| `422`       | Validation Error    | `HTTPValidationError`    |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/form-submissions/{submission_id}/complete",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/form-submissions/{submission_id}/complete',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/form-submissions/{submission_id}/complete' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/lab-ranges`

**Summary:** Create Lab Range
**Operation ID:** `create_lab_range_api_v1_execution_lab_ranges_post`

**Clinical & GxP Rationale:**
Create a new lab reference range.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `LabReferenceRangeCreate`

| Field                     | Type            | Required | Description                                           |
| :------------------------ | :-------------- | :------- | :---------------------------------------------------- |
| `study_id`                | `string`        | ✔ Yes    | Unique protocol study identifier, e.g. 'STUDY-101'    |
| `test_code`               | `string`        | ✔ Yes    | Standardized laboratory test code, e.g. 'WBC'         |
| `test_name`               | `string`        | ✔ Yes    | Descriptive name of the test parameter                |
| `source`                  | `LabSourceEnum` | ✔ Yes    | Source type of the range, either 'CENTRAL' or 'LOCAL' |
| `site_id`                 | `string`        | `null`   | No                                                    | Optional investigator site identifier              |
| `unit`                    | `string`        | ✔ Yes    | The original/captured unit of measurement             |
| `normalized_unit`         | `string`        | ✔ Yes    | Standardized target unit of measurement               |
| `sex_applicability`       | `string`        | ✔ Yes    | Sex applicability of the reference range              |
| `ethnicity_applicability` | `string`        | `null`   | No                                                    | Optional ethnicity applicability constraint        |
| `age_low`                 | `number`        | `null`   | No                                                    | Nullable lower bound of age applicability          |
| `age_high`                | `number`        | `null`   | No                                                    | Nullable upper bound of age applicability          |
| `low_bound`               | `number`        | `null`   | No                                                    | Nullable lower limit of normal range               |
| `high_bound`              | `number`        | `null`   | No                                                    | Nullable upper limit of normal range               |
| `critical_low`            | `number`        | `null`   | No                                                    | Nullable lower limit of critical/panic alert range |
| `critical_high`           | `number`        | `null`   | No                                                    | Nullable upper limit of critical/panic alert range |
| `reason_for_change`       | `string`        | `null`   | No                                                    | Optional GxP 21 CFR Part 11 justification reason   |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema             |
| :---------- | :------------------ | :-------------------------- |
| `201`       | Successful Response | `LabReferenceRangeResponse` |
| `422`       | Validation Error    | `HTTPValidationError`       |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/lab-ranges",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/lab-ranges',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/lab-ranges' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `GET` `/api/v1/execution/lab-ranges`

**Summary:** List Lab Ranges
**Operation ID:** `list_lab_ranges_api_v1_execution_lab_ranges_get`

**Clinical & GxP Rationale:**
List lab reference ranges matching filters.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name              | In      | Type      | Required | Description            |
| :---------------- | :------ | :-------- | :------- | :--------------------- |
| `study_id`        | `query` | `string`  | `null`   | No                     | Filter by study ID   |
| `test_code`       | `query` | `string`  | `null`   | No                     | Filter by test code  |
| `source`          | `query` | `string`  | `null`   | No                     | Filter by source     |
| `lab_source`      | `query` | `string`  | `null`   | No                     | Filter by lab source |
| `include_deleted` | `query` | `boolean` | No       | Include deleted ranges |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema                    |
| :---------- | :------------------ | :--------------------------------- |
| `200`       | Successful Response | `Array<LabReferenceRangeResponse>` |
| `422`       | Validation Error    | `HTTPValidationError`              |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/lab-ranges",
  {
    method: "GET",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/execution/api/v1/execution/lab-ranges',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/execution/api/v1/execution/lab-ranges' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/lab-ranges/recalculate`

**Summary:** Trigger Lab Range Recalculation
**Operation ID:** `trigger_lab_range_recalculation_api_v1_execution_lab_ranges_recalculate_post`

**Clinical & GxP Rationale:**
Trigger cohort-wide reference range evaluation and recalculation on-demand.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `LabRangeRecalculateRequest`

| Field       | Type     | Required | Description |
| :---------- | :------- | :------- | :---------- |
| `study_id`  | `string` | ✔ Yes    | —           |
| `test_code` | `string` | ✔ Yes    | —           |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema               |
| :---------- | :------------------ | :---------------------------- |
| `200`       | Successful Response | `LabRangeRecalculateResponse` |
| `422`       | Validation Error    | `HTTPValidationError`         |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/lab-ranges/recalculate",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/lab-ranges/recalculate',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/lab-ranges/recalculate' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `GET` `/api/v1/execution/lab-ranges/{range_id}`

**Summary:** Get Lab Range
**Operation ID:** `get_lab_range_api_v1_execution_lab_ranges__range_id__get`

**Clinical & GxP Rationale:**
Retrieve a single lab reference range.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name       | In     | Type     | Required | Description |
| :--------- | :----- | :------- | :------- | :---------- |
| `range_id` | `path` | `string` | ✔ Yes    | —           |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema             |
| :---------- | :------------------ | :-------------------------- |
| `200`       | Successful Response | `LabReferenceRangeResponse` |
| `422`       | Validation Error    | `HTTPValidationError`       |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/lab-ranges/{range_id}",
  {
    method: "GET",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/execution/api/v1/execution/lab-ranges/{range_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/execution/api/v1/execution/lab-ranges/{range_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `PUT` `/api/v1/execution/lab-ranges/{range_id}`

**Summary:** Update Lab Range
**Operation ID:** `update_lab_range_api_v1_execution_lab_ranges__range_id__put`

**Clinical & GxP Rationale:**
Update an existing lab reference range.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name       | In     | Type     | Required | Description |
| :--------- | :----- | :------- | :------- | :---------- |
| `range_id` | `path` | `string` | ✔ Yes    | —           |

#### Request Payload Schema (`application/json`)

**Schema Type:** `LabReferenceRangeUpdate`

| Field                     | Type     | Required | Description |
| :------------------------ | :------- | :------- | :---------- |
| `study_id`                | `string` | `null`   | No          | Unique protocol study identifier                 |
| `test_code`               | `string` | `null`   | No          | Standardized laboratory test code                |
| `test_name`               | `string` | `null`   | No          | Updated descriptive name of the test             |
| `source`                  | `string` | `null`   | No          | Updated source type of the range                 |
| `site_id`                 | `string` | `null`   | No          | Updated investigator site identifier             |
| `unit`                    | `string` | `null`   | No          | Updated original unit of measurement             |
| `normalized_unit`         | `string` | `null`   | No          | Updated standardized unit of measurement         |
| `sex_applicability`       | `string` | `null`   | No          | Updated sex applicability                        |
| `ethnicity_applicability` | `string` | `null`   | No          | Updated ethnicity applicability                  |
| `age_low`                 | `number` | `null`   | No          | Updated lower bound of age applicability         |
| `age_high`                | `number` | `null`   | No          | Updated upper bound of age applicability         |
| `low_bound`               | `number` | `null`   | No          | Updated lower limit of normal range              |
| `high_bound`              | `number` | `null`   | No          | Updated upper limit of normal range              |
| `critical_low`            | `number` | `null`   | No          | Updated lower limit of critical alert range      |
| `critical_high`           | `number` | `null`   | No          | Updated upper limit of critical alert range      |
| `reason_for_change`       | `string` | `null`   | No          | Optional GxP 21 CFR Part 11 justification reason |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema             |
| :---------- | :------------------ | :-------------------------- |
| `200`       | Successful Response | `LabReferenceRangeResponse` |
| `422`       | Validation Error    | `HTTPValidationError`       |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/lab-ranges/{range_id}",
  {
    method: "PUT",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.put(
        '/api/v1/execution/api/v1/execution/lab-ranges/{range_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X PUT 'http://localhost:8000/api/v1/execution/api/v1/execution/lab-ranges/{range_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `DELETE` `/api/v1/execution/lab-ranges/{range_id}`

**Summary:** Delete Lab Range
**Operation ID:** `delete_lab_range_api_v1_execution_lab_ranges__range_id__delete`

**Clinical & GxP Rationale:**
Soft-delete a lab reference range by setting is_deleted = True.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name       | In     | Type     | Required | Description |
| :--------- | :----- | :------- | :------- | :---------- |
| `range_id` | `path` | `string` | ✔ Yes    | —           |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema             |
| :---------- | :------------------ | :-------------------------- |
| `200`       | Successful Response | `LabReferenceRangeResponse` |
| `422`       | Validation Error    | `HTTPValidationError`       |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/lab-ranges/{range_id}",
  {
    method: "DELETE",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.delete(
        '/api/v1/execution/api/v1/execution/lab-ranges/{range_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X DELETE 'http://localhost:8000/api/v1/execution/api/v1/execution/lab-ranges/{range_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `GET` `/api/v1/execution/labs/batch-status`

**Summary:** Get Batch Status Endpoint
**Operation ID:** `get_batch_status_endpoint_api_v1_execution_labs_batch_status_get`

**Clinical & GxP Rationale:**
Retrieve status and metrics for previous lab batch ingestion jobs.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name       | In      | Type     | Required | Description |
| :--------- | :------ | :------- | :------- | :---------- |
| `batch_id` | `query` | `string` | `null`   | No          | Optional batch ID to query |
| `study_id` | `query` | `string` | `null`   | No          | Optional study ID filter   |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema               |
| :---------- | :------------------ | :---------------------------- |
| `200`       | Successful Response | `Array<LabBatchIngestResult>` | `LabBatchIngestResult` |
| `422`       | Validation Error    | `HTTPValidationError`         |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/labs/batch-status",
  {
    method: "GET",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/execution/api/v1/execution/labs/batch-status',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/execution/api/v1/execution/labs/batch-status' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `GET` `/api/v1/execution/labs/batch-status/{batch_id}`

**Summary:** Get Batch Status By Id Endpoint
**Operation ID:** `get_batch_status_by_id_endpoint_api_v1_execution_labs_batch_status__batch_id__get`

**Clinical & GxP Rationale:**
Query execution status and statistics of a specific laboratory batch by ID.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name       | In     | Type     | Required | Description |
| :--------- | :----- | :------- | :------- | :---------- |
| `batch_id` | `path` | `string` | ✔ Yes    | —           |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema        |
| :---------- | :------------------ | :--------------------- |
| `200`       | Successful Response | `LabBatchIngestResult` |
| `422`       | Validation Error    | `HTTPValidationError`  |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/labs/batch-status/{batch_id}",
  {
    method: "GET",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/execution/api/v1/execution/labs/batch-status/{batch_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/execution/api/v1/execution/labs/batch-status/{batch_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/labs/certifications`

**Summary:** Create Local Lab Certification
**Operation ID:** `create_local_lab_certification_api_v1_execution_labs_certifications_post`

**Clinical & GxP Rationale:**
Register or update a local site laboratory regulatory certification (CLIA, CAP, ISO 15189).

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `LocalLabCertificationCreate`

| Field                  | Type      | Required | Description                                       |
| :--------------------- | :-------- | :------- | :------------------------------------------------ |
| `study_id`             | `string`  | ✔ Yes    | Unique protocol study identifier                  |
| `site_id`              | `string`  | ✔ Yes    | Investigator site identifier                      |
| `lab_name`             | `string`  | ✔ Yes    | Laboratory facility name                          |
| `lab_identifier`       | `string`  | `null`   | No                                                | Optional facility identifier |
| `certification_number` | `string`  | ✔ Yes    | CLIA, CAP, or ISO certification number            |
| `accreditation_body`   | `string`  | ✔ Yes    | Accrediting agency (e.g. CLIA, CAP, ISO 15189)    |
| `effective_date`       | `string`  | ✔ Yes    | Certification effective begin date                |
| `expiration_date`      | `string`  | ✔ Yes    | Certification expiration end date                 |
| `director_name`        | `string`  | `null`   | No                                                | Name of laboratory director  |
| `is_accredited`        | `boolean` | No       | Accreditation status flag                         |
| `notes`                | `string`  | `null`   | No                                                | Operational notes            |
| `reason_for_change`    | `string`  | ✔ Yes    | Mandatory GxP 21 CFR Part 11 justification reason |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema                 |
| :---------- | :------------------ | :------------------------------ |
| `201`       | Successful Response | `LocalLabCertificationResponse` |
| `422`       | Validation Error    | `HTTPValidationError`           |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/labs/certifications",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/labs/certifications',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/labs/certifications' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `GET` `/api/v1/execution/labs/certifications`

**Summary:** List Local Lab Certifications
**Operation ID:** `list_local_lab_certifications_api_v1_execution_labs_certifications_get`

**Clinical & GxP Rationale:**
Retrieve laboratory accreditation certifications across sites.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name       | In      | Type     | Required | Description |
| :--------- | :------ | :------- | :------- | :---------- |
| `study_id` | `query` | `string` | `null`   | No          | Filter by study ID             |
| `site_id`  | `query` | `string` | `null`   | No          | Filter by site ID              |
| `status`   | `query` | `string` | `null`   | No          | Filter by certification status |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema                        |
| :---------- | :------------------ | :------------------------------------- |
| `200`       | Successful Response | `Array<LocalLabCertificationResponse>` |
| `422`       | Validation Error    | `HTTPValidationError`                  |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/labs/certifications",
  {
    method: "GET",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/execution/api/v1/execution/labs/certifications',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/execution/api/v1/execution/labs/certifications' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/labs/certifications/verify`

**Summary:** Verify Local Lab Certification Gate
**Operation ID:** `verify_local_lab_certification_gate_api_v1_execution_labs_certifications_verify_post`

**Clinical & GxP Rationale:**
Pre-flight GxP verification check validating that an active lab certification is in place.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name      | In      | Type     | Required | Description |
| :-------- | :------ | :------- | :------- | :---------- |
| `site_id` | `query` | `string` | `null`   | No          | —   |

#### Request Payload Schema (`application/json`)

**Schema Type:** `LocalLabCertificationVerifyRequest`

| Field              | Type     | Required | Description                      |
| :----------------- | :------- | :------- | :------------------------------- |
| `study_id`         | `string` | ✔ Yes    | Unique protocol study identifier |
| `site_id`          | `string` | ✔ Yes    | Target site identifier           |
| `observation_date` | `string` | `null`   | No                               | Observation collection date (defaults to current time) |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema                       |
| :---------- | :------------------ | :------------------------------------ |
| `200`       | Successful Response | `LocalLabCertificationVerifyResponse` |
| `422`       | Validation Error    | `HTTPValidationError`                 |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/labs/certifications/verify",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/labs/certifications/verify',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/labs/certifications/verify' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `PUT` `/api/v1/execution/labs/certifications/{cert_id}`

**Summary:** Update Local Lab Certification
**Operation ID:** `update_local_lab_certification_api_v1_execution_labs_certifications__cert_id__put`

**Clinical & GxP Rationale:**
Update certification credentials and renewal dates for a specific site lab.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name      | In     | Type     | Required | Description |
| :-------- | :----- | :------- | :------- | :---------- |
| `cert_id` | `path` | `string` | ✔ Yes    | —           |

#### Request Payload Schema (`application/json`)

**Schema Type:** `LocalLabCertificationUpdate`

| Field                  | Type                      | Required | Description                                       |
| :--------------------- | :------------------------ | :------- | :------------------------------------------------ |
| `lab_name`             | `string`                  | `null`   | No                                                | Updated facility name        |
| `lab_identifier`       | `string`                  | `null`   | No                                                | Updated facility identifier  |
| `certification_number` | `string`                  | `null`   | No                                                | Updated certification number |
| `accreditation_body`   | `string`                  | `null`   | No                                                | Updated accrediting agency   |
| `effective_date`       | `string`                  | `null`   | No                                                | Updated effective date       |
| `expiration_date`      | `string`                  | `null`   | No                                                | Updated expiration date      |
| `status`               | `CertificationStatusEnum` | `null`   | No                                                | Updated operational status   |
| `director_name`        | `string`                  | `null`   | No                                                | Updated laboratory director  |
| `is_accredited`        | `boolean`                 | `null`   | No                                                | Updated accreditation flag   |
| `notes`                | `string`                  | `null`   | No                                                | Updated operational notes    |
| `reason_for_change`    | `string`                  | ✔ Yes    | Mandatory GxP 21 CFR Part 11 justification reason |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema                 |
| :---------- | :------------------ | :------------------------------ |
| `200`       | Successful Response | `LocalLabCertificationResponse` |
| `422`       | Validation Error    | `HTTPValidationError`           |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/labs/certifications/{cert_id}",
  {
    method: "PUT",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.put(
        '/api/v1/execution/api/v1/execution/labs/certifications/{cert_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X PUT 'http://localhost:8000/api/v1/execution/api/v1/execution/labs/certifications/{cert_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/labs/certifications/{site_id}/verify`

**Summary:** Verify Local Lab Certification Gate
**Operation ID:** `verify_local_lab_certification_gate_api_v1_execution_labs_certifications__site_id__verify_post`

**Clinical & GxP Rationale:**
Pre-flight GxP verification check validating that an active lab certification is in place.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name      | In     | Type     | Required | Description |
| :-------- | :----- | :------- | :------- | :---------- |
| `site_id` | `path` | `string` | `null`   | ✔ Yes       | —   |

#### Request Payload Schema (`application/json`)

**Schema Type:** `LocalLabCertificationVerifyRequest`

| Field              | Type     | Required | Description                      |
| :----------------- | :------- | :------- | :------------------------------- |
| `study_id`         | `string` | ✔ Yes    | Unique protocol study identifier |
| `site_id`          | `string` | ✔ Yes    | Target site identifier           |
| `observation_date` | `string` | `null`   | No                               | Observation collection date (defaults to current time) |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema                       |
| :---------- | :------------------ | :------------------------------------ |
| `200`       | Successful Response | `LocalLabCertificationVerifyResponse` |
| `422`       | Validation Error    | `HTTPValidationError`                 |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/labs/certifications/{site_id}/verify",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/labs/certifications/{site_id}/verify',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/labs/certifications/{site_id}/verify' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/labs/convert-unit`

**Summary:** Convert Unit Endpoint
**Operation ID:** `convert_unit_endpoint_api_v1_execution_labs_convert_unit_post`

**Clinical & GxP Rationale:**
Convert laboratory measurement between conventional and SI units.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `LoincConvertRequest`

| Field       | Type     | Required | Description              |
| :---------- | :------- | :------- | :----------------------- |
| `test_code` | `string` | ✔ Yes    | Laboratory analyte code  |
| `from_unit` | `string` | ✔ Yes    | Source unit              |
| `to_unit`   | `string` | ✔ Yes    | Target unit              |
| `value`     | `number` | ✔ Yes    | Numeric value to convert |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema        |
| :---------- | :------------------ | :--------------------- |
| `200`       | Successful Response | `LoincConvertResponse` |
| `422`       | Validation Error    | `HTTPValidationError`  |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/labs/convert-unit",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/labs/convert-unit',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/labs/convert-unit' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/labs/ingest`

**Summary:** Ingest Lab Batch Endpoint
**Operation ID:** `ingest_lab_batch_endpoint_api_v1_execution_labs_ingest_post`

**Clinical & GxP Rationale:**
Ingest central or local laboratory batch data (CSV, HL7 v2.x, FHIR JSON, CDISC LAB, or Local Manual).

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema        |
| :---------- | :------------------ | :--------------------- |
| `200`       | Successful Response | `LabBatchIngestResult` |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/labs/ingest",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/labs/ingest',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/labs/ingest' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `GET` `/api/v1/execution/labs/loinc-catalog`

**Summary:** Get Loinc Catalog Endpoint
**Operation ID:** `get_loinc_catalog_endpoint_api_v1_execution_labs_loinc_catalog_get`

**Clinical & GxP Rationale:**
Return dictionary of standardized LOINC mappings and CDISC Biomedical Concept bindings.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema        |
| :---------- | :------------------ | :--------------------- |
| `200`       | Successful Response | `LoincCatalogResponse` |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/labs/loinc-catalog",
  {
    method: "GET",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/execution/api/v1/execution/labs/loinc-catalog',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/execution/api/v1/execution/labs/loinc-catalog' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/labs/toxicity/evaluate`

**Summary:** Evaluate Lab Toxicity Endpoint
**Operation ID:** `evaluate_lab_toxicity_endpoint_api_v1_execution_labs_toxicity_evaluate_post`

**Clinical & GxP Rationale:**
Evaluate CTCAE v5.0 Grade, Hy's Law criteria, and delta checks for a single lab observation.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `LabToxicityEvaluateRequest`

| Field            | Type               | Required | Description                                                    |
| :--------------- | :----------------- | :------- | :------------------------------------------------------------- |
| `test_code`      | `string`           | ✔ Yes    | Standardized laboratory test code (e.g. 'ALT', 'CREAT', 'ANC') |
| `test_name`      | `string`           | `null`   | No                                                             | Optional descriptive test name                                |
| `value`          | `number`           | ✔ Yes    | Observed numeric test measurement                              |
| `unit`           | `string`           | ✔ Yes    | Reporting unit of measurement                                  |
| `ref_low`        | `number`           | `null`   | No                                                             | Lower limit of demographic normal range (LLN)                 |
| `ref_high`       | `number`           | `null`   | No                                                             | Upper limit of demographic normal range (ULN)                 |
| `baseline_value` | `number`           | `null`   | No                                                             | Optional subject baseline value for acute delta calculation   |
| `previous_value` | `number`           | `null`   | No                                                             | Optional previous observation value                           |
| `grading_scale`  | `GradingScaleEnum` | No       | Target grading scale: 'CTCAE_V5' or 'DAIDS_V2_1'               |
| `alt_value`      | `number`           | `null`   | No                                                             | Co-analyte ALT value for Hy's Law evaluation                  |
| `ast_value`      | `number`           | `null`   | No                                                             | Co-analyte AST value for Hy's Law evaluation                  |
| `bili_value`     | `number`           | `null`   | No                                                             | Co-analyte Total Bilirubin value for Hy's Law evaluation      |
| `alkp_value`     | `number`           | `null`   | No                                                             | Co-analyte Alkaline Phosphatase value for Hy's Law evaluation |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema               |
| :---------- | :------------------ | :---------------------------- |
| `200`       | Successful Response | `LabToxicityEvaluateResponse` |
| `422`       | Validation Error    | `HTTPValidationError`         |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/labs/toxicity/evaluate",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/labs/toxicity/evaluate',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/labs/toxicity/evaluate' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `GET` `/api/v1/execution/labs/toxicity/review-queue`

**Summary:** List Staged Ae Review Queue
**Operation ID:** `list_staged_ae_review_queue_api_v1_execution_labs_toxicity_review_queue_get`

**Clinical & GxP Rationale:**
Retrieve laboratory Grade 3/4 toxicity triggers staged for Investigator review.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name            | In      | Type     | Required | Description |
| :-------------- | :------ | :------- | :------- | :---------- |
| `study_id`      | `query` | `string` | `null`   | No          | Filter by study ID                                                      |
| `site_id`       | `query` | `string` | `null`   | No          | Filter by site ID                                                       |
| `review_status` | `query` | `string` | `null`   | No          | Filter by status (default: STAGED_FOR_INVESTIGATOR_REVIEW, ALL for any) |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema                           |
| :---------- | :------------------ | :---------------------------------------- |
| `200`       | Successful Response | `Array<StagedAdverseEventReviewResponse>` |
| `422`       | Validation Error    | `HTTPValidationError`                     |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/labs/toxicity/review-queue",
  {
    method: "GET",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/execution/api/v1/execution/labs/toxicity/review-queue',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/execution/api/v1/execution/labs/toxicity/review-queue' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/labs/toxicity/review/{review_id}/action`

**Summary:** Action Staged Ae Review
**Operation ID:** `action_staged_ae_review_api_v1_execution_labs_toxicity_review__review_id__action_post`

**Clinical & GxP Rationale:**
Perform Investigator workflow action on a staged Grade 3/4 AE (Confirm, Dismiss, Escalate).

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name        | In     | Type     | Required | Description |
| :---------- | :----- | :------- | :------- | :---------- |
| `review_id` | `path` | `string` | ✔ Yes    | —           |

#### Request Payload Schema (`application/json`)

**Schema Type:** `StagedAdverseEventActionRequest`

| Field                           | Type     | Required | Description                                                                                                 |
| :------------------------------ | :------- | :------- | :---------------------------------------------------------------------------------------------------------- |
| `action`                        | `string` | ✔ Yes    | Action to take: 'CONFIRM' (promote to formal AE), 'DISMISS' (not clinically significant), or 'ESCALATE_SAE' |
| `ae_term`                       | `string` | `null`   | No                                                                                                          | Optional adverse event term override     |
| `causality_assessment`          | `string` | `null`   | No                                                                                                          | Optional relationship to study treatment |
| `investigator_notes`            | `string` | `null`   | No                                                                                                          | Clinical commentary and rationale        |
| `electronic_signature_password` | `string` | `null`   | No                                                                                                          | 21 CFR Part 11 authentication credential |
| `reason_for_change`             | `string` | ✔ Yes    | Mandatory GxP 21 CFR Part 11 justification reason                                                           |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema                    |
| :---------- | :------------------ | :--------------------------------- |
| `200`       | Successful Response | `StagedAdverseEventActionResponse` |
| `422`       | Validation Error    | `HTTPValidationError`              |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/labs/toxicity/review/{review_id}/action",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/labs/toxicity/review/{review_id}/action',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/labs/toxicity/review/{review_id}/action' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `GET` `/api/v1/execution/locks`

**Summary:** Get Lock Status
**Operation ID:** `get_lock_status_api_v1_execution_locks_get`

**Clinical & GxP Rationale:**
Retrieve the current lock/freeze status of sites, visits, forms, subjects, and study-wide trial.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema      |
| :---------- | :------------------ | :------------------- |
| `200`       | Successful Response | `LockStatusResponse` |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/locks",
  {
    method: "GET",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/execution/api/v1/execution/locks',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/execution/api/v1/execution/locks' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/locks/form/{form_id}/freeze`

**Summary:** Lock Form Endpoint
**Operation ID:** `lock_form_endpoint_api_v1_execution_locks_form__form_id__freeze_post`

**Clinical & GxP Rationale:**
Locks or freezes a specific form.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name      | In     | Type     | Required | Description |
| :-------- | :----- | :------- | :------- | :---------- |
| `form_id` | `path` | `string` | ✔ Yes    | —           |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema       |
| :---------- | :------------------ | :-------------------- |
| `200`       | Successful Response | `object`              |
| `422`       | Validation Error    | `HTTPValidationError` |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/locks/form/{form_id}/freeze",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/locks/form/{form_id}/freeze',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/locks/form/{form_id}/freeze' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/locks/form/{form_id}/lock`

**Summary:** Lock Form Endpoint
**Operation ID:** `lock_form_endpoint_api_v1_execution_locks_form__form_id__lock_post`

**Clinical & GxP Rationale:**
Locks or freezes a specific form.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name      | In     | Type     | Required | Description |
| :-------- | :----- | :------- | :------- | :---------- |
| `form_id` | `path` | `string` | ✔ Yes    | —           |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema       |
| :---------- | :------------------ | :-------------------- |
| `200`       | Successful Response | `object`              |
| `422`       | Validation Error    | `HTTPValidationError` |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/locks/form/{form_id}/lock",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/locks/form/{form_id}/lock',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/locks/form/{form_id}/lock' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/locks/form/{form_id}/unfreeze`

**Summary:** Unlock Form Endpoint
**Operation ID:** `unlock_form_endpoint_api_v1_execution_locks_form__form_id__unfreeze_post`

**Clinical & GxP Rationale:**
Unlocks or unfreezes a specific form.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name      | In     | Type     | Required | Description |
| :-------- | :----- | :------- | :------- | :---------- |
| `form_id` | `path` | `string` | ✔ Yes    | —           |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema       |
| :---------- | :------------------ | :-------------------- |
| `200`       | Successful Response | `object`              |
| `422`       | Validation Error    | `HTTPValidationError` |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/locks/form/{form_id}/unfreeze",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/locks/form/{form_id}/unfreeze',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/locks/form/{form_id}/unfreeze' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/locks/form/{form_id}/unlock`

**Summary:** Unlock Form Endpoint
**Operation ID:** `unlock_form_endpoint_api_v1_execution_locks_form__form_id__unlock_post`

**Clinical & GxP Rationale:**
Unlocks or unfreezes a specific form.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name      | In     | Type     | Required | Description |
| :-------- | :----- | :------- | :------- | :---------- |
| `form_id` | `path` | `string` | ✔ Yes    | —           |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema       |
| :---------- | :------------------ | :-------------------- |
| `200`       | Successful Response | `object`              |
| `422`       | Validation Error    | `HTTPValidationError` |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/locks/form/{form_id}/unlock",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/locks/form/{form_id}/unlock',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/locks/form/{form_id}/unlock' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/locks/lock`

**Summary:** Lock Data Endpoint
**Operation ID:** `lock_data_endpoint_api_v1_execution_locks_lock_post`

**Clinical & GxP Rationale:**
Execute study, site, subject, visit, form, or field-level data lock or freeze operation.

Requirements: PRD-SYS-001, PRD-SYS-002, Trace-1, Trace-3, Trace-17

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `DataLockRequest`

| Field               | Type            | Required | Description                                        |
| :------------------ | :-------------- | :------- | :------------------------------------------------- |
| `study_id`          | `string`        | `null`   | No                                                 | Target protocol study ID                                     |
| `site_id`           | `string`        | `null`   | No                                                 | Target site ID                                               |
| `subject_id`        | `string`        | `null`   | No                                                 | Target subject ID                                            |
| `visit_id`          | `string`        | `null`   | No                                                 | Target visit ID                                              |
| `form_id`           | `string`        | `null`   | No                                                 | Target eCRF form ID                                          |
| `item_group_id`     | `string`        | `null`   | No                                                 | Optional target item group code                              |
| `field_name`        | `string`        | `null`   | No                                                 | Optional target field variable name                          |
| `scope`             | `LockScopeEnum` | `string` | `null`                                             | No                                                           | Lock scope: STUDY, TRIAL, SITE, SUBJECT, VISIT, FORM, ITEM_GROUP, FIELD |
| `scope_type`        | `string`        | `null`   | No                                                 | Scope type name: STUDY, SITE, SUBJECT, VISIT, FORM, FIELD    |
| `scope_id`          | `string`        | `null`   | No                                                 | Target identifier for the specified scope                    |
| `action`            | `string`        | No       | Action to perform: LOCK, FREEZE, HARD_LOCK, UNLOCK |
| `lock_type`         | `string`        | `null`   | No                                                 | Lock classification: FROZEN, LOCKED, HARD_LOCK, SOFT_LOCK    |
| `reason_for_change` | `string`        | `null`   | No                                                 | Mandatory GxP 21 CFR Part 11 justification reason            |
| `reason`            | `string`        | `null`   | No                                                 | Convenience alias for reason_for_change                      |
| `justification`     | `string`        | `null`   | No                                                 | Mandatory >=50 character justification for unlock operations |
| `lock_id`           | `string`        | `null`   | No                                                 | Specific lock ID targeted for unlock operation               |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema       |
| :---------- | :------------------ | :-------------------- |
| `200`       | Successful Response | `DataLockResponse`    |
| `422`       | Validation Error    | `HTTPValidationError` |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/locks/lock",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/locks/lock',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/locks/lock' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/locks/site/{site_id}/freeze`

**Summary:** Lock Site Endpoint
**Operation ID:** `lock_site_endpoint_api_v1_execution_locks_site__site_id__freeze_post`

**Clinical & GxP Rationale:**
Locks or freezes a specific site.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name      | In     | Type     | Required | Description |
| :-------- | :----- | :------- | :------- | :---------- |
| `site_id` | `path` | `string` | ✔ Yes    | —           |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema       |
| :---------- | :------------------ | :-------------------- |
| `200`       | Successful Response | `object`              |
| `422`       | Validation Error    | `HTTPValidationError` |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/locks/site/{site_id}/freeze",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/locks/site/{site_id}/freeze',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/locks/site/{site_id}/freeze' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/locks/site/{site_id}/lock`

**Summary:** Lock Site Endpoint
**Operation ID:** `lock_site_endpoint_api_v1_execution_locks_site__site_id__lock_post`

**Clinical & GxP Rationale:**
Locks or freezes a specific site.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name      | In     | Type     | Required | Description |
| :-------- | :----- | :------- | :------- | :---------- |
| `site_id` | `path` | `string` | ✔ Yes    | —           |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema       |
| :---------- | :------------------ | :-------------------- |
| `200`       | Successful Response | `object`              |
| `422`       | Validation Error    | `HTTPValidationError` |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/locks/site/{site_id}/lock",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/locks/site/{site_id}/lock',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/locks/site/{site_id}/lock' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/locks/site/{site_id}/unfreeze`

**Summary:** Unlock Site Endpoint
**Operation ID:** `unlock_site_endpoint_api_v1_execution_locks_site__site_id__unfreeze_post`

**Clinical & GxP Rationale:**
Unlocks or unfreezes a specific site.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name      | In     | Type     | Required | Description |
| :-------- | :----- | :------- | :------- | :---------- |
| `site_id` | `path` | `string` | ✔ Yes    | —           |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema       |
| :---------- | :------------------ | :-------------------- |
| `200`       | Successful Response | `object`              |
| `422`       | Validation Error    | `HTTPValidationError` |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/locks/site/{site_id}/unfreeze",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/locks/site/{site_id}/unfreeze',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/locks/site/{site_id}/unfreeze' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/locks/site/{site_id}/unlock`

**Summary:** Unlock Site Endpoint
**Operation ID:** `unlock_site_endpoint_api_v1_execution_locks_site__site_id__unlock_post`

**Clinical & GxP Rationale:**
Unlocks or unfreezes a specific site.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name      | In     | Type     | Required | Description |
| :-------- | :----- | :------- | :------- | :---------- |
| `site_id` | `path` | `string` | ✔ Yes    | —           |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema       |
| :---------- | :------------------ | :-------------------- |
| `200`       | Successful Response | `object`              |
| `422`       | Validation Error    | `HTTPValidationError` |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/locks/site/{site_id}/unlock",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/locks/site/{site_id}/unlock',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/locks/site/{site_id}/unlock' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `GET` `/api/v1/execution/locks/status/{form_id}`

**Summary:** Get Form Lock Status Endpoint
**Operation ID:** `get_form_lock_status_endpoint_api_v1_execution_locks_status__form_id__get`

**Clinical & GxP Rationale:**
Retrieve active data locks for specified eCRF form submission.

Requirements: PRD-SYS-001, Trace-1, Trace-17

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name      | In     | Type     | Required | Description |
| :-------- | :----- | :------- | :------- | :---------- |
| `form_id` | `path` | `string` | ✔ Yes    | —           |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema         |
| :---------- | :------------------ | :---------------------- |
| `200`       | Successful Response | `Array<DataLockRecord>` |
| `422`       | Validation Error    | `HTTPValidationError`   |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/locks/status/{form_id}",
  {
    method: "GET",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/execution/api/v1/execution/locks/status/{form_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/execution/api/v1/execution/locks/status/{form_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/locks/subject/{subject_id}/freeze`

**Summary:** Lock Subject Endpoint
**Operation ID:** `lock_subject_endpoint_api_v1_execution_locks_subject__subject_id__freeze_post`

**Clinical & GxP Rationale:**
Locks or freezes a specific subject.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name         | In     | Type     | Required | Description |
| :----------- | :----- | :------- | :------- | :---------- |
| `subject_id` | `path` | `string` | ✔ Yes    | —           |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema       |
| :---------- | :------------------ | :-------------------- |
| `200`       | Successful Response | `object`              |
| `422`       | Validation Error    | `HTTPValidationError` |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/locks/subject/{subject_id}/freeze",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/locks/subject/{subject_id}/freeze',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/locks/subject/{subject_id}/freeze' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/locks/subject/{subject_id}/lock`

**Summary:** Lock Subject Endpoint
**Operation ID:** `lock_subject_endpoint_api_v1_execution_locks_subject__subject_id__lock_post`

**Clinical & GxP Rationale:**
Locks or freezes a specific subject.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name         | In     | Type     | Required | Description |
| :----------- | :----- | :------- | :------- | :---------- |
| `subject_id` | `path` | `string` | ✔ Yes    | —           |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema       |
| :---------- | :------------------ | :-------------------- |
| `200`       | Successful Response | `object`              |
| `422`       | Validation Error    | `HTTPValidationError` |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/locks/subject/{subject_id}/lock",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/locks/subject/{subject_id}/lock',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/locks/subject/{subject_id}/lock' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/locks/subject/{subject_id}/unfreeze`

**Summary:** Unlock Subject Endpoint
**Operation ID:** `unlock_subject_endpoint_api_v1_execution_locks_subject__subject_id__unfreeze_post`

**Clinical & GxP Rationale:**
Unlocks or unfreezes a specific subject.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name         | In     | Type     | Required | Description |
| :----------- | :----- | :------- | :------- | :---------- |
| `subject_id` | `path` | `string` | ✔ Yes    | —           |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema       |
| :---------- | :------------------ | :-------------------- |
| `200`       | Successful Response | `object`              |
| `422`       | Validation Error    | `HTTPValidationError` |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/locks/subject/{subject_id}/unfreeze",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/locks/subject/{subject_id}/unfreeze',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/locks/subject/{subject_id}/unfreeze' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/locks/subject/{subject_id}/unlock`

**Summary:** Unlock Subject Endpoint
**Operation ID:** `unlock_subject_endpoint_api_v1_execution_locks_subject__subject_id__unlock_post`

**Clinical & GxP Rationale:**
Unlocks or unfreezes a specific subject.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name         | In     | Type     | Required | Description |
| :----------- | :----- | :------- | :------- | :---------- |
| `subject_id` | `path` | `string` | ✔ Yes    | —           |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema       |
| :---------- | :------------------ | :-------------------- |
| `200`       | Successful Response | `object`              |
| `422`       | Validation Error    | `HTTPValidationError` |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/locks/subject/{subject_id}/unlock",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/locks/subject/{subject_id}/unlock',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/locks/subject/{subject_id}/unlock' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `GET` `/api/v1/execution/locks/tree`

**Summary:** Get Lock Hierarchy Tree Endpoint
**Operation ID:** `get_lock_hierarchy_tree_endpoint_api_v1_execution_locks_tree_get`

**Clinical & GxP Rationale:**
Retrieve complete lock status tree across Study -> Sites -> Subjects -> Visits -> Forms hierarchy.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name       | In      | Type     | Required | Description       |
| :--------- | :------ | :------- | :------- | :---------------- |
| `study_id` | `query` | `string` | No       | Study ID to query |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema       |
| :---------- | :------------------ | :-------------------- |
| `200`       | Successful Response | `object`              |
| `422`       | Validation Error    | `HTTPValidationError` |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/locks/tree",
  {
    method: "GET",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/execution/api/v1/execution/locks/tree',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/execution/api/v1/execution/locks/tree' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/locks/trial/freeze`

**Summary:** Lock Trial Endpoint
**Operation ID:** `lock_trial_endpoint_api_v1_execution_locks_trial_freeze_post`

**Clinical & GxP Rationale:**
Locks or freezes the trial/study.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema |
| :---------- | :------------------ | :-------------- |
| `200`       | Successful Response | `object`        |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/locks/trial/freeze",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/locks/trial/freeze',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/locks/trial/freeze' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/locks/trial/lock`

**Summary:** Lock Trial Endpoint
**Operation ID:** `lock_trial_endpoint_api_v1_execution_locks_trial_lock_post`

**Clinical & GxP Rationale:**
Locks or freezes the trial/study.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema |
| :---------- | :------------------ | :-------------- |
| `200`       | Successful Response | `object`        |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/locks/trial/lock",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/locks/trial/lock',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/locks/trial/lock' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/locks/trial/unfreeze`

**Summary:** Unlock Trial Endpoint
**Operation ID:** `unlock_trial_endpoint_api_v1_execution_locks_trial_unfreeze_post`

**Clinical & GxP Rationale:**
Unlocks or unfreezes the trial/study.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema |
| :---------- | :------------------ | :-------------- |
| `200`       | Successful Response | `object`        |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/locks/trial/unfreeze",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/locks/trial/unfreeze',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/locks/trial/unfreeze' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/locks/trial/unlock`

**Summary:** Unlock Trial Endpoint
**Operation ID:** `unlock_trial_endpoint_api_v1_execution_locks_trial_unlock_post`

**Clinical & GxP Rationale:**
Unlocks or unfreezes the trial/study.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema |
| :---------- | :------------------ | :-------------- |
| `200`       | Successful Response | `object`        |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/locks/trial/unlock",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/locks/trial/unlock',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/locks/trial/unlock' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/locks/unlock`

**Summary:** Unlock Data Endpoint
**Operation ID:** `unlock_data_endpoint_api_v1_execution_locks_unlock_post`

**Clinical & GxP Rationale:**
Execute GxP data unlock override operation enforcing >= 50 chars justification.

Requirements: PRD-SYS-001, PRD-SYS-002, Trace-1, Trace-3, Trace-17

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `DataLockRequest`

| Field               | Type            | Required | Description                                        |
| :------------------ | :-------------- | :------- | :------------------------------------------------- |
| `study_id`          | `string`        | `null`   | No                                                 | Target protocol study ID                                     |
| `site_id`           | `string`        | `null`   | No                                                 | Target site ID                                               |
| `subject_id`        | `string`        | `null`   | No                                                 | Target subject ID                                            |
| `visit_id`          | `string`        | `null`   | No                                                 | Target visit ID                                              |
| `form_id`           | `string`        | `null`   | No                                                 | Target eCRF form ID                                          |
| `item_group_id`     | `string`        | `null`   | No                                                 | Optional target item group code                              |
| `field_name`        | `string`        | `null`   | No                                                 | Optional target field variable name                          |
| `scope`             | `LockScopeEnum` | `string` | `null`                                             | No                                                           | Lock scope: STUDY, TRIAL, SITE, SUBJECT, VISIT, FORM, ITEM_GROUP, FIELD |
| `scope_type`        | `string`        | `null`   | No                                                 | Scope type name: STUDY, SITE, SUBJECT, VISIT, FORM, FIELD    |
| `scope_id`          | `string`        | `null`   | No                                                 | Target identifier for the specified scope                    |
| `action`            | `string`        | No       | Action to perform: LOCK, FREEZE, HARD_LOCK, UNLOCK |
| `lock_type`         | `string`        | `null`   | No                                                 | Lock classification: FROZEN, LOCKED, HARD_LOCK, SOFT_LOCK    |
| `reason_for_change` | `string`        | `null`   | No                                                 | Mandatory GxP 21 CFR Part 11 justification reason            |
| `reason`            | `string`        | `null`   | No                                                 | Convenience alias for reason_for_change                      |
| `justification`     | `string`        | `null`   | No                                                 | Mandatory >=50 character justification for unlock operations |
| `lock_id`           | `string`        | `null`   | No                                                 | Specific lock ID targeted for unlock operation               |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema       |
| :---------- | :------------------ | :-------------------- |
| `200`       | Successful Response | `DataLockResponse`    |
| `422`       | Validation Error    | `HTTPValidationError` |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/locks/unlock",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/locks/unlock',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/locks/unlock' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/locks/visit/{visit_id}/freeze`

**Summary:** Lock Visit Endpoint
**Operation ID:** `lock_visit_endpoint_api_v1_execution_locks_visit__visit_id__freeze_post`

**Clinical & GxP Rationale:**
Locks or freezes a specific visit.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name       | In     | Type     | Required | Description |
| :--------- | :----- | :------- | :------- | :---------- |
| `visit_id` | `path` | `string` | ✔ Yes    | —           |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema       |
| :---------- | :------------------ | :-------------------- |
| `200`       | Successful Response | `object`              |
| `422`       | Validation Error    | `HTTPValidationError` |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/locks/visit/{visit_id}/freeze",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/locks/visit/{visit_id}/freeze',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/locks/visit/{visit_id}/freeze' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/locks/visit/{visit_id}/lock`

**Summary:** Lock Visit Endpoint
**Operation ID:** `lock_visit_endpoint_api_v1_execution_locks_visit__visit_id__lock_post`

**Clinical & GxP Rationale:**
Locks or freezes a specific visit.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name       | In     | Type     | Required | Description |
| :--------- | :----- | :------- | :------- | :---------- |
| `visit_id` | `path` | `string` | ✔ Yes    | —           |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema       |
| :---------- | :------------------ | :-------------------- |
| `200`       | Successful Response | `object`              |
| `422`       | Validation Error    | `HTTPValidationError` |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/locks/visit/{visit_id}/lock",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/locks/visit/{visit_id}/lock',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/locks/visit/{visit_id}/lock' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/locks/visit/{visit_id}/unfreeze`

**Summary:** Unlock Visit Endpoint
**Operation ID:** `unlock_visit_endpoint_api_v1_execution_locks_visit__visit_id__unfreeze_post`

**Clinical & GxP Rationale:**
Unlocks or unfreezes a specific visit.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name       | In     | Type     | Required | Description |
| :--------- | :----- | :------- | :------- | :---------- |
| `visit_id` | `path` | `string` | ✔ Yes    | —           |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema       |
| :---------- | :------------------ | :-------------------- |
| `200`       | Successful Response | `object`              |
| `422`       | Validation Error    | `HTTPValidationError` |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/locks/visit/{visit_id}/unfreeze",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/locks/visit/{visit_id}/unfreeze',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/locks/visit/{visit_id}/unfreeze' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/locks/visit/{visit_id}/unlock`

**Summary:** Unlock Visit Endpoint
**Operation ID:** `unlock_visit_endpoint_api_v1_execution_locks_visit__visit_id__unlock_post`

**Clinical & GxP Rationale:**
Unlocks or unfreezes a specific visit.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name       | In     | Type     | Required | Description |
| :--------- | :----- | :------- | :------- | :---------- |
| `visit_id` | `path` | `string` | ✔ Yes    | —           |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema       |
| :---------- | :------------------ | :-------------------- |
| `200`       | Successful Response | `object`              |
| `422`       | Validation Error    | `HTTPValidationError` |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/locks/visit/{visit_id}/unlock",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/locks/visit/{visit_id}/unlock',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/locks/visit/{visit_id}/unlock' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/migration-rules`

**Summary:** Create Migration Rule
**Operation ID:** `create_migration_rule_api_v1_execution_migration_rules_post`

**Clinical & GxP Rationale:**
Create a new protocol version migration rule.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `MigrationRuleCreate`

| Field                  | Type     | Required | Description |
| :--------------------- | :------- | :------- | :---------- |
| `study_id`             | `string` | ✔ Yes    | —           |
| `source_version`       | `string` | ✔ Yes    | —           |
| `target_version`       | `string` | ✔ Yes    | —           |
| `rule_type`            | `string` | ✔ Yes    | —           |
| `source_field`         | `string` | `null`   | No          | —   |
| `target_field`         | `string` | `null`   | No          | —   |
| `default_value_string` | `string` | `null`   | No          | —   |
| `default_value_float`  | `number` | `null`   | No          | —   |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema         |
| :---------- | :------------------ | :---------------------- |
| `201`       | Successful Response | `MigrationRuleResponse` |
| `422`       | Validation Error    | `HTTPValidationError`   |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/migration-rules",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/migration-rules',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/migration-rules' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `GET` `/api/v1/execution/migration-rules`

**Summary:** List Migration Rules
**Operation ID:** `list_migration_rules_api_v1_execution_migration_rules_get`

**Clinical & GxP Rationale:**
List migration rules for a clinical study.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name       | In      | Type     | Required | Description |
| :--------- | :------ | :------- | :------- | :---------- |
| `study_id` | `query` | `string` | ✔ Yes    | —           |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema                |
| :---------- | :------------------ | :----------------------------- |
| `200`       | Successful Response | `Array<MigrationRuleResponse>` |
| `422`       | Validation Error    | `HTTPValidationError`          |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/migration-rules",
  {
    method: "GET",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/execution/api/v1/execution/migration-rules',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/execution/api/v1/execution/migration-rules' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/observations`

**Summary:** Create Observation
**Operation ID:** `create_observation_api_v1_execution_observations_post`

**Clinical & GxP Rationale:**
CREATE a new clinical observation, performing unit normalization and outlier checks.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `ObservationCreate`

| Field              | Type     | Required | Description |
| :----------------- | :------- | :------- | :---------- |
| `subject_id`       | `string` | ✔ Yes    | —           |
| `study_id`         | `string` | `null`   | No          | —   |
| `site_id`          | `string` | `null`   | No          | —   |
| `visit_id`         | `string` | `null`   | No          | —   |
| `domain`           | `string` | ✔ Yes    | —           |
| `test_code`        | `string` | ✔ Yes    | —           |
| `test_name`        | `string` | ✔ Yes    | —           |
| `value`            | `number` | `null`   | No          | —   |
| `value_string`     | `string` | `null`   | No          | —   |
| `unit`             | `string` | `null`   | No          | —   |
| `observation_date` | `string` | `null`   | No          | —   |
| `lab_source`       | `string` | `null`   | No          | —   |
| `lab_site_id`      | `string` | `null`   | No          | —   |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema       |
| :---------- | :------------------ | :-------------------- |
| `200`       | Successful Response | `ObservationResponse` |
| `422`       | Validation Error    | `HTTPValidationError` |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/observations",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/observations',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/observations' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/offline/sync`

**Summary:** Offline Sync Endpoint
**Operation ID:** `offline_sync_endpoint_api_v1_execution_offline_sync_post`

**Clinical & GxP Rationale:**
Synchronize queued offline delta transactions.

Requirements: PRD-SYS-001

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `object`

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema       |
| :---------- | :------------------ | :-------------------- |
| `200`       | Successful Response | `object`              |
| `422`       | Validation Error    | `HTTPValidationError` |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/offline/sync",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/offline/sync',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/offline/sync' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/outliers/recalculate`

**Summary:** Trigger Outlier Recalculation
**Operation ID:** `trigger_outlier_recalculation_api_v1_execution_outliers_recalculate_post`

**Clinical & GxP Rationale:**
Trigger cohort-wide outlier recalculation on-demand.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `OutlierRecalculateRequest`

| Field       | Type     | Required | Description |
| :---------- | :------- | :------- | :---------- |
| `study_id`  | `string` | ✔ Yes    | —           |
| `test_code` | `string` | ✔ Yes    | —           |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema              |
| :---------- | :------------------ | :--------------------------- |
| `200`       | Successful Response | `OutlierRecalculateResponse` |
| `422`       | Validation Error    | `HTTPValidationError`        |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/outliers/recalculate",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/outliers/recalculate',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/outliers/recalculate' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `GET` `/api/v1/execution/queries`

**Summary:** List Queries
**Operation ID:** `list_queries_api_v1_execution_queries_get`

**Clinical & GxP Rationale:**
Retrieve a list of clinical queries with optional filtering.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name         | In      | Type     | Required | Description |
| :----------- | :------ | :------- | :------- | :---------- |
| `study_id`   | `query` | `string` | `null`   | No          | —   |
| `subject_id` | `query` | `string` | `null`   | No          | —   |
| `visit_id`   | `query` | `string` | `null`   | No          | —   |
| `status`     | `query` | `string` | `null`   | No          | —   |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema                |
| :---------- | :------------------ | :----------------------------- |
| `200`       | Successful Response | `Array<ClinicalQueryResponse>` |
| `422`       | Validation Error    | `HTTPValidationError`          |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/queries",
  {
    method: "GET",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/execution/api/v1/execution/queries',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/execution/api/v1/execution/queries' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/queries`

**Summary:** Open Query
**Operation ID:** `open_query_api_v1_execution_queries_post`

**Clinical & GxP Rationale:**
Raise a new clinical query on a specific field coordinate.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `QueryCreate`

| Field             | Type     | Required | Description |
| :---------------- | :------- | :------- | :---------- |
| `study_id`        | `string` | ✔ Yes    | —           |
| `subject_id`      | `string` | ✔ Yes    | —           |
| `visit_id`        | `string` | `null`   | No          | —   |
| `domain`          | `string` | `null`   | No          | —   |
| `test_code`       | `string` | ✔ Yes    | —           |
| `explanation`     | `string` | ✔ Yes    | —           |
| `status`          | `string` | `null`   | No          | —   |
| `observation_id`  | `string` | `null`   | No          | —   |
| `field_link`      | `string` | `null`   | No          | —   |
| `message`         | `string` | `null`   | No          | —   |
| `origin`          | `string` | `null`   | No          | —   |
| `priority`        | `string` | `null`   | No          | —   |
| `rule_id`         | `string` | `null`   | No          | —   |
| `created_by`      | `string` | `null`   | No          | —   |
| `form_id`         | `string` | `null`   | No          | —   |
| `field_id`        | `string` | `null`   | No          | —   |
| `query_type`      | `string` | `null`   | No          | —   |
| `action_required` | `string` | `null`   | No          | —   |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema         |
| :---------- | :------------------ | :---------------------- |
| `201`       | Successful Response | `ClinicalQueryResponse` |
| `422`       | Validation Error    | `HTTPValidationError`   |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/queries",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/queries',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/queries' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/queries/generate`

**Summary:** Bulk Generate Queries
**Operation ID:** `bulk_generate_queries_api_v1_execution_queries_generate_post`

**Clinical & GxP Rationale:**
CRA/monitor-gated bulk query generation endpoint.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `BulkQueryGenerationRequest`

| Field               | Type                           | Required | Description                                             |
| :------------------ | :----------------------------- | :------- | :------------------------------------------------------ |
| `study_id`          | `string`                       | `null`   | No                                                      | Target protocol study ID           |
| `site_id`           | `string`                       | `null`   | No                                                      | Optional target site identifier    |
| `subject_id`        | `string`                       | `null`   | No                                                      | Optional target subject identifier |
| `targets`           | `Array<QueryTargetDescriptor>` | ✔ Yes    | List of query target coordinate fields and explanations |
| `reason_for_change` | `string`                       | ✔ Yes    | Mandatory GxP 21 CFR Part 11 justification reason       |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema               |
| :---------- | :------------------ | :---------------------------- |
| `200`       | Successful Response | `BulkQueryGenerationResponse` |
| `422`       | Validation Error    | `HTTPValidationError`         |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/queries/generate",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/queries/generate',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/queries/generate' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/queries/sync`

**Summary:** Sync Queries
**Operation ID:** `sync_queries_api_v1_execution_queries_sync_post`

**Clinical & GxP Rationale:**
Synchronize clinical query local ledger blocks to the target database.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `SyncRequest`

| Field    | Type                      | Required | Description |
| :------- | :------------------------ | :------- | :---------- |
| `blocks` | `Array<LocalLedgerBlock>` | ✔ Yes    | —           |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema       |
| :---------- | :------------------ | :-------------------- |
| `200`       | Successful Response | `object`              |
| `422`       | Validation Error    | `HTTPValidationError` |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/queries/sync",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/queries/sync',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/queries/sync' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `GET` `/api/v1/execution/queries/{query_id}`

**Summary:** Get Query
**Operation ID:** `get_query_api_v1_execution_queries__query_id__get`

**Clinical & GxP Rationale:**
Query a single clinical query by ID, returning its full audit history.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name       | In     | Type     | Required | Description |
| :--------- | :----- | :------- | :------- | :---------- |
| `query_id` | `path` | `string` | ✔ Yes    | —           |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema         |
| :---------- | :------------------ | :---------------------- |
| `200`       | Successful Response | `ClinicalQueryResponse` |
| `422`       | Validation Error    | `HTTPValidationError`   |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/queries/{query_id}",
  {
    method: "GET",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/execution/api/v1/execution/queries/{query_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/execution/api/v1/execution/queries/{query_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `PATCH` `/api/v1/execution/queries/{query_id}`

**Summary:** Update Query State
**Operation ID:** `update_query_state_api_v1_execution_queries__query_id__patch`

**Clinical & GxP Rationale:**
Transition a query through the designated state sequence and perform role checks.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name       | In     | Type     | Required | Description |
| :--------- | :----- | :------- | :------- | :---------- |
| `query_id` | `path` | `string` | ✔ Yes    | —           |

#### Request Payload Schema (`application/json`)

**Schema Type:** `QueryUpdate`

| Field                 | Type     | Required | Description |
| :-------------------- | :------- | :------- | :---------- |
| `status`              | `string` | ✔ Yes    | —           |
| `explanation`         | `string` | `null`   | No          | —   |
| `response`            | `string` | `null`   | No          | —   |
| `observation_id`      | `string` | `null`   | No          | —   |
| `field_link`          | `string` | `null`   | No          | —   |
| `message`             | `string` | `null`   | No          | —   |
| `origin`              | `string` | `null`   | No          | —   |
| `priority`            | `string` | `null`   | No          | —   |
| `rule_id`             | `string` | `null`   | No          | —   |
| `created_by`          | `string` | `null`   | No          | —   |
| `responder`           | `string` | `null`   | No          | —   |
| `resolver`            | `string` | `null`   | No          | —   |
| `resolved_at`         | `string` | `null`   | No          | —   |
| `cancellation_reason` | `string` | `null`   | No          | —   |
| `escalated_at`        | `string` | `null`   | No          | —   |
| `form_id`             | `string` | `null`   | No          | —   |
| `field_id`            | `string` | `null`   | No          | —   |
| `query_type`          | `string` | `null`   | No          | —   |
| `action_required`     | `string` | `null`   | No          | —   |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema         |
| :---------- | :------------------ | :---------------------- |
| `200`       | Successful Response | `ClinicalQueryResponse` |
| `422`       | Validation Error    | `HTTPValidationError`   |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/queries/{query_id}",
  {
    method: "PATCH",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.patch(
        '/api/v1/execution/api/v1/execution/queries/{query_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X PATCH 'http://localhost:8000/api/v1/execution/api/v1/execution/queries/{query_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/queries/{query_id}/cancel`

**Summary:** Cancel Query
**Operation ID:** `cancel_query_api_v1_execution_queries__query_id__cancel_post`

**Clinical & GxP Rationale:**
Cancel a clinical query raised in error.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name       | In     | Type     | Required | Description |
| :--------- | :----- | :------- | :------- | :---------- |
| `query_id` | `path` | `string` | ✔ Yes    | —           |

#### Request Payload Schema (`application/json`)

**Schema Type:** `QueryCancel`

| Field    | Type     | Required | Description |
| :------- | :------- | :------- | :---------- |
| `reason` | `string` | ✔ Yes    | —           |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema         |
| :---------- | :------------------ | :---------------------- |
| `200`       | Successful Response | `ClinicalQueryResponse` |
| `422`       | Validation Error    | `HTTPValidationError`   |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/queries/{query_id}/cancel",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/queries/{query_id}/cancel',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/queries/{query_id}/cancel' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/queries/{query_id}/close`

**Summary:** Close Query
**Operation ID:** `close_query_api_v1_execution_queries__query_id__close_post`

**Clinical & GxP Rationale:**
Close an answered query (resolving the discrepancy loop).

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name       | In     | Type     | Required | Description |
| :--------- | :----- | :------- | :------- | :---------- |
| `query_id` | `path` | `string` | ✔ Yes    | —           |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema         |
| :---------- | :------------------ | :---------------------- |
| `200`       | Successful Response | `ClinicalQueryResponse` |
| `422`       | Validation Error    | `HTTPValidationError`   |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/queries/{query_id}/close",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/queries/{query_id}/close',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/queries/{query_id}/close' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/queries/{query_id}/reopen`

**Summary:** Reopen Query
**Operation ID:** `reopen_query_api_v1_execution_queries__query_id__reopen_post`

**Clinical & GxP Rationale:**
Reopen an answered or closed clinical query for further clarification.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name       | In     | Type     | Required | Description |
| :--------- | :----- | :------- | :------- | :---------- |
| `query_id` | `path` | `string` | ✔ Yes    | —           |

#### Request Payload Schema (`application/json`)

**Schema Type:** `QueryReopen` | `null`

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema         |
| :---------- | :------------------ | :---------------------- |
| `200`       | Successful Response | `ClinicalQueryResponse` |
| `422`       | Validation Error    | `HTTPValidationError`   |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/queries/{query_id}/reopen",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/queries/{query_id}/reopen',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/queries/{query_id}/reopen' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/queries/{query_id}/respond`

**Summary:** Respond Query
**Operation ID:** `respond_query_api_v1_execution_queries__query_id__respond_post`

**Clinical & GxP Rationale:**
Submit an investigator response/answer to an open or reopened clinical query.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name       | In     | Type     | Required | Description |
| :--------- | :----- | :------- | :------- | :---------- |
| `query_id` | `path` | `string` | ✔ Yes    | —           |

#### Request Payload Schema (`application/json`)

**Schema Type:** `QueryRespond`

| Field       | Type     | Required | Description |
| :---------- | :------- | :------- | :---------- |
| `response`  | `string` | ✔ Yes    | —           |
| `responder` | `string` | `null`   | No          | —   |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema         |
| :---------- | :------------------ | :---------------------- |
| `200`       | Successful Response | `ClinicalQueryResponse` |
| `422`       | Validation Error    | `HTTPValidationError`   |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/queries/{query_id}/respond",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/queries/{query_id}/respond',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/queries/{query_id}/respond' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/rtsm/adaptive/boin/calculate-boundaries`

**Summary:** Calculate Boin Boundaries Endpoint
**Operation ID:** `calculate_boin_boundaries_endpoint_api_v1_execution_rtsm_adaptive_boin_calculate_boundaries_post`

**Clinical & GxP Rationale:**
Calculates theoretical BOIN escalation and de-escalation toxicity boundaries (lambda_e, lambda_d).

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `BoinBoundariesCalculationRequest`

| Field                  | Type     | Required | Description |
| :--------------------- | :------- | :------- | :---------- |
| `target_toxicity_rate` | `number` | No       | —           |
| `factor_e`             | `number` | No       | —           |
| `factor_d`             | `number` | No       | —           |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema                     |
| :---------- | :------------------ | :---------------------------------- |
| `200`       | Successful Response | `BoinBoundariesCalculationResponse` |
| `422`       | Validation Error    | `HTTPValidationError`               |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/rtsm/adaptive/boin/calculate-boundaries",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/rtsm/adaptive/boin/calculate-boundaries',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/rtsm/adaptive/boin/calculate-boundaries' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/rtsm/adaptive/cohorts/evaluate`

**Summary:** Evaluate Adaptive Cohort Endpoint
**Operation ID:** `evaluate_adaptive_cohort_endpoint_api_v1_execution_rtsm_adaptive_cohorts_evaluate_post`

**Clinical & GxP Rationale:**
Evaluates safety cohort DLTs and computes real-time recommendation flags (BOIN, CRM, 3+3).

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `CohortEvaluationInput`

| Field                | Type            | Required | Description |
| :------------------- | :-------------- | :------- | :---------- |
| `study_id`           | `string`        | ✔ Yes    | —           |
| `cohort_number`      | `integer`       | ✔ Yes    | —           |
| `dose_level_index`   | `integer`       | ✔ Yes    | —           |
| `subjects_evaluated` | `integer`       | ✔ Yes    | —           |
| `dlts_observed`      | `integer`       | ✔ Yes    | —           |
| `subject_ids`        | `Array<string>` | No       | —           |
| `dlt_subject_ids`    | `Array<string>` | No       | —           |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema          |
| :---------- | :------------------ | :----------------------- |
| `200`       | Successful Response | `CohortEvaluationResult` |
| `422`       | Validation Error    | `HTTPValidationError`    |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/rtsm/adaptive/cohorts/evaluate",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/rtsm/adaptive/cohorts/evaluate',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/rtsm/adaptive/cohorts/evaluate' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `GET` `/api/v1/execution/rtsm/adaptive/cohorts/history`

**Summary:** Get Adaptive Cohorts History Endpoint
**Operation ID:** `get_adaptive_cohorts_history_endpoint_api_v1_execution_rtsm_adaptive_cohorts_history_get`

**Clinical & GxP Rationale:**
Returns the immutable 21 CFR Part 11 audited transition log history for a study.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name       | In      | Type     | Required | Description               |
| :--------- | :------ | :------- | :------- | :------------------------ |
| `study_id` | `query` | `string` | ✔ Yes    | Clinical study identifier |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema                      |
| :---------- | :------------------ | :----------------------------------- |
| `200`       | Successful Response | `Array<CohortTransitionLogResponse>` |
| `422`       | Validation Error    | `HTTPValidationError`                |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/rtsm/adaptive/cohorts/history",
  {
    method: "GET",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/execution/api/v1/execution/rtsm/adaptive/cohorts/history',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/execution/api/v1/execution/rtsm/adaptive/cohorts/history' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/rtsm/adaptive/cohorts/transition`

**Summary:** Transition Adaptive Cohort Endpoint
**Operation ID:** `transition_adaptive_cohort_endpoint_api_v1_execution_rtsm_adaptive_cohorts_transition_post`

**Clinical & GxP Rationale:**
Advances or overrides a dose escalation cohort with 21 CFR Part 11 audit logging.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `CohortTransitionRequest`

| Field                     | Type      | Required | Description |
| :------------------------ | :-------- | :------- | :---------- |
| `study_id`                | `string`  | ✔ Yes    | —           |
| `cohort_number`           | `integer` | ✔ Yes    | —           |
| `decision_action`         | `string`  | ✔ Yes    | —           |
| `target_dose_level_index` | `integer` | ✔ Yes    | —           |
| `override_reason`         | `string`  | `null`   | No          | —   |
| `electronic_signature`    | `string`  | `null`   | No          | —   |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema               |
| :---------- | :------------------ | :---------------------------- |
| `200`       | Successful Response | `CohortTransitionLogResponse` |
| `422`       | Validation Error    | `HTTPValidationError`         |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/rtsm/adaptive/cohorts/transition",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/rtsm/adaptive/cohorts/transition',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/rtsm/adaptive/cohorts/transition' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/rtsm/adaptive/config`

**Summary:** Create Or Update Adaptive Config Endpoint
**Operation ID:** `create_or_update_adaptive_config_endpoint_api_v1_execution_rtsm_adaptive_config_post`

**Clinical & GxP Rationale:**
Registers or updates adaptive oncology dose-escalation or RAR trial design parameters.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `AdaptiveTrialConfigDTO`

| Field                     | Type                   | Required | Description                                           |
| :------------------------ | :--------------------- | :------- | :---------------------------------------------------- |
| `study_id`                | `string`               | ✔ Yes    | —                                                     |
| `design_type`             | `string`               | ✔ Yes    | Design type: 'THREE_PLUS_THREE', 'BOIN', 'CRM', 'RAR' |
| `dose_levels`             | `Array<DoseLevelDef>`  | No       | —                                                     |
| `boin_config`             | `BoinConfig`           | `null`   | No                                                    | —   |
| `crm_config`              | `CrmConfig`            | `null`   | No                                                    | —   |
| `three_plus_three_config` | `ThreePlusThreeConfig` | `null`   | No                                                    | —   |
| `rar_config`              | `RarConfig`            | `null`   | No                                                    | —   |
| `cohort_size`             | `integer`              | No       | —                                                     |
| `max_sample_size`         | `integer`              | `null`   | No                                                    | —   |
| `is_active`               | `boolean`              | No       | —                                                     |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema       |
| :---------- | :------------------ | :-------------------- |
| `201`       | Successful Response | `object`              |
| `422`       | Validation Error    | `HTTPValidationError` |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/rtsm/adaptive/config",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/rtsm/adaptive/config',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/rtsm/adaptive/config' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `GET` `/api/v1/execution/rtsm/adaptive/config`

**Summary:** Get Adaptive Config Endpoint
**Operation ID:** `get_adaptive_config_endpoint_api_v1_execution_rtsm_adaptive_config_get`

**Clinical & GxP Rationale:**
Retrieves current adaptive dose-escalation trial configuration, active cohort state, and history.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name       | In      | Type     | Required | Description               |
| :--------- | :------ | :------- | :------- | :------------------------ |
| `study_id` | `query` | `string` | ✔ Yes    | Clinical study identifier |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema       |
| :---------- | :------------------ | :-------------------- |
| `200`       | Successful Response | `object`              |
| `422`       | Validation Error    | `HTTPValidationError` |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/rtsm/adaptive/config",
  {
    method: "GET",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/execution/api/v1/execution/rtsm/adaptive/config',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/execution/api/v1/execution/rtsm/adaptive/config' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/rtsm/adaptive/crm/posterior`

**Summary:** Calculate Crm Posterior Endpoint
**Operation ID:** `calculate_crm_posterior_endpoint_api_v1_execution_rtsm_adaptive_crm_posterior_post`

**Clinical & GxP Rationale:**
Calculates Bayesian posterior probabilities and recommended next dose under Continual Reassessment Method.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `CrmPosteriorCalculationRequest`

| Field                  | Type            | Required | Description                                                                      |
| :--------------------- | :-------------- | :------- | :------------------------------------------------------------------------------- |
| `skeleton_probs`       | `Array<number>` | ✔ Yes    | —                                                                                |
| `target_toxicity_rate` | `number`        | No       | —                                                                                |
| `prior_std`            | `number`        | No       | —                                                                                |
| `dose_evaluations`     | `Array<object>` | ✔ Yes    | List of dicts per dose level: [{'dose_index': 1, 'enrolled': 3, 'dlts': 0}, ...] |
| `current_dose_index`   | `integer`       | No       | —                                                                                |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema                   |
| :---------- | :------------------ | :-------------------------------- |
| `200`       | Successful Response | `CrmPosteriorCalculationResponse` |
| `422`       | Validation Error    | `HTTPValidationError`             |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/rtsm/adaptive/crm/posterior",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/rtsm/adaptive/crm/posterior',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/rtsm/adaptive/crm/posterior' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/rtsm/adaptive/rar/arms`

**Summary:** Expand Or Terminate Rar Arm Endpoint
**Operation ID:** `expand_or_terminate_rar_arm_endpoint_api_v1_execution_rtsm_adaptive_rar_arms_post`

**Clinical & GxP Rationale:**
Seamlessly adds or terminates a treatment arm in RAR with zero study downtime.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `RARExpandOrTerminateArmRequest`

| Field         | Type      | Required | Description                                                 |
| :------------ | :-------- | :------- | :---------------------------------------------------------- |
| `study_id`    | `string`  | ✔ Yes    | —                                                           |
| `arm_id`      | `string`  | ✔ Yes    | —                                                           |
| `arm_name`    | `string`  | ✔ Yes    | —                                                           |
| `is_control`  | `boolean` | No       | —                                                           |
| `action`      | `string`  | ✔ Yes    | 'ADD_ARM', 'DROP_FUTILITY', 'GRADUATE_SUCCESS', 'TERMINATE' |
| `prior_alpha` | `number`  | No       | —                                                           |
| `prior_beta`  | `number`  | No       | —                                                           |
| `reason`      | `string`  | ✔ Yes    | —                                                           |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema       |
| :---------- | :------------------ | :-------------------- |
| `200`       | Successful Response | `RAREvaluationResult` |
| `422`       | Validation Error    | `HTTPValidationError` |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/rtsm/adaptive/rar/arms",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/rtsm/adaptive/rar/arms',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/rtsm/adaptive/rar/arms' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/rtsm/adaptive/rar/evaluate`

**Summary:** Evaluate Rar Endpoint
**Operation ID:** `evaluate_rar_endpoint_api_v1_execution_rtsm_adaptive_rar_evaluate_post`

**Clinical & GxP Rationale:**
Calculates dynamic Response-Adaptive Randomization allocation weights across active arms.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name       | In      | Type     | Required | Description               |
| :--------- | :------ | :------- | :------- | :------------------------ |
| `study_id` | `query` | `string` | ✔ Yes    | Clinical study identifier |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema       |
| :---------- | :------------------ | :-------------------- |
| `200`       | Successful Response | `RAREvaluationResult` |
| `422`       | Validation Error    | `HTTPValidationError` |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/rtsm/adaptive/rar/evaluate",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/rtsm/adaptive/rar/evaluate',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/rtsm/adaptive/rar/evaluate' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `GET` `/api/v1/execution/rtsm/adaptive/rar/state`

**Summary:** Get Rar State Endpoint
**Operation ID:** `get_rar_state_endpoint_api_v1_execution_rtsm_adaptive_rar_state_get`

**Clinical & GxP Rationale:**
Retrieves current Response-Adaptive Randomization arm allocation weights and posterior states.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name       | In      | Type     | Required | Description               |
| :--------- | :------ | :------- | :------- | :------------------------ |
| `study_id` | `query` | `string` | ✔ Yes    | Clinical study identifier |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema       |
| :---------- | :------------------ | :-------------------- |
| `200`       | Successful Response | `object`              |
| `422`       | Validation Error    | `HTTPValidationError` |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/rtsm/adaptive/rar/state",
  {
    method: "GET",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/execution/api/v1/execution/rtsm/adaptive/rar/state',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/execution/api/v1/execution/rtsm/adaptive/rar/state' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/rtsm/cgt/apheresis`

**Summary:** Initialize Apheresis Endpoint
**Operation ID:** `initialize_apheresis_endpoint_api_v1_execution_rtsm_cgt_apheresis_post`

**Clinical & GxP Rationale:**
Initializes Vein-to-Vein tracking upon initial apheresis cellular harvest.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `CGTApheresisCreateRequest`

| Field                  | Type     | Required | Description |
| :--------------------- | :------- | :------- | :---------- |
| `study_id`             | `string` | ✔ Yes    | —           |
| `site_id`              | `string` | ✔ Yes    | —           |
| `subject_id`           | `string` | ✔ Yes    | —           |
| `collector_id`         | `string` | ✔ Yes    | —           |
| `collector_name`       | `string` | ✔ Yes    | —           |
| `collector_role`       | `string` | No       | —           |
| `initial_temp_celsius` | `number` | No       | —           |
| `din`                  | `string` | `null`   | No          | —   |
| `dry_shipper_id`       | `string` | `null`   | No          | —   |
| `seal_id`              | `string` | `null`   | No          | —   |
| `notes`                | `string` | `null`   | No          | —   |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema             |
| :---------- | :------------------ | :-------------------------- |
| `201`       | Successful Response | `CGTChainOfCustodyResponse` |
| `422`       | Validation Error    | `HTTPValidationError`       |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/rtsm/cgt/apheresis",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/rtsm/cgt/apheresis',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/rtsm/cgt/apheresis' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/rtsm/cgt/bedside-verify`

**Summary:** Bedside Dual Verification Endpoint
**Operation ID:** `bedside_dual_verification_endpoint_api_v1_execution_rtsm_cgt_bedside_verify_post`

**Clinical & GxP Rationale:**
Executes mandatory two-person electronic bedside verification, unlocking the drug infusion eCRF.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `CGTBedsideVerificationRequest`

| Field                          | Type     | Required | Description |
| :----------------------------- | :------- | :------- | :---------- |
| `coc_id`                       | `string` | ✔ Yes    | —           |
| `scanned_wristband_subject_id` | `string` | ✔ Yes    | —           |
| `scanned_bag_barcode`          | `string` | ✔ Yes    | —           |
| `primary_verifier_id`          | `string` | ✔ Yes    | —           |
| `primary_verifier_name`        | `string` | ✔ Yes    | —           |
| `primary_verifier_role`        | `string` | ✔ Yes    | —           |
| `primary_signature_token`      | `string` | ✔ Yes    | —           |
| `secondary_verifier_id`        | `string` | ✔ Yes    | —           |
| `secondary_verifier_name`      | `string` | ✔ Yes    | —           |
| `secondary_verifier_role`      | `string` | ✔ Yes    | —           |
| `secondary_signature_token`    | `string` | ✔ Yes    | —           |
| `notes`                        | `string` | `null`   | No          | —   |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema                  |
| :---------- | :------------------ | :------------------------------- |
| `200`       | Successful Response | `CGTBedsideVerificationResponse` |
| `422`       | Validation Error    | `HTTPValidationError`            |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/rtsm/cgt/bedside-verify",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/rtsm/cgt/bedside-verify',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/rtsm/cgt/bedside-verify' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `GET` `/api/v1/execution/rtsm/cgt/certificates/{study_id}`

**Summary:** List Destruction Certificates Endpoint
**Operation ID:** `list_destruction_certificates_endpoint_api_v1_execution_rtsm_cgt_certificates__study_id__get`

**Clinical & GxP Rationale:**
Lists all Certificates of Destruction for a study.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name       | In     | Type     | Required | Description |
| :--------- | :----- | :------- | :------- | :---------- |
| `study_id` | `path` | `string` | ✔ Yes    | —           |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema                            |
| :---------- | :------------------ | :----------------------------------------- |
| `200`       | Successful Response | `Array<CGTDestructionCertificateResponse>` |
| `422`       | Validation Error    | `HTTPValidationError`                      |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/rtsm/cgt/certificates/{study_id}",
  {
    method: "GET",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/execution/api/v1/execution/rtsm/cgt/certificates/{study_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/execution/api/v1/execution/rtsm/cgt/certificates/{study_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `GET` `/api/v1/execution/rtsm/cgt/chain-of-custody/{study_id}/{subject_id}`

**Summary:** Get Subject Cgt Traceability Endpoint
**Operation ID:** `get_subject_cgt_traceability_endpoint_api_v1_execution_rtsm_cgt_chain_of_custody__study_id___subject_id__get`

**Clinical & GxP Rationale:**
Retrieves full Vein-to-Vein traceability ledger for a subject.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name         | In     | Type     | Required | Description |
| :----------- | :----- | :------- | :------- | :---------- |
| `study_id`   | `path` | `string` | ✔ Yes    | —           |
| `subject_id` | `path` | `string` | ✔ Yes    | —           |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema               |
| :---------- | :------------------ | :---------------------------- |
| `200`       | Successful Response | `CGTFullTraceabilityResponse` |
| `422`       | Validation Error    | `HTTPValidationError`         |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/rtsm/cgt/chain-of-custody/{study_id}/{subject_id}",
  {
    method: "GET",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/execution/api/v1/execution/rtsm/cgt/chain-of-custody/{study_id}/{subject_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/execution/api/v1/execution/rtsm/cgt/chain-of-custody/{study_id}/{subject_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/rtsm/cgt/destruction-certificate`

**Summary:** Generate Destruction Certificate Endpoint
**Operation ID:** `generate_destruction_certificate_endpoint_api_v1_execution_rtsm_cgt_destruction_certificate_post`

**Clinical & GxP Rationale:**
Generates a formal Certificate of Destruction with dual witness electronic signatures.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `CGTDestructionCertificateCreateRequest`

| Field                       | Type            | Required | Description |
| :-------------------------- | :-------------- | :------- | :---------- |
| `study_id`                  | `string`        | ✔ Yes    | —           |
| `site_id`                   | `string`        | ✔ Yes    | —           |
| `lot_number`                | `string`        | ✔ Yes    | —           |
| `kit_numbers`               | `Array<string>` | ✔ Yes    | —           |
| `vial_identifiers`          | `Array<string>` | ✔ Yes    | —           |
| `destruction_reason`        | `string`        | ✔ Yes    | —           |
| `destruction_method`        | `string`        | No       | —           |
| `destruction_facility`      | `string`        | ✔ Yes    | —           |
| `witness_1_id`              | `string`        | ✔ Yes    | —           |
| `witness_1_name`            | `string`        | ✔ Yes    | —           |
| `witness_1_role`            | `string`        | ✔ Yes    | —           |
| `witness_1_signature_token` | `string`        | ✔ Yes    | —           |
| `witness_2_id`              | `string`        | ✔ Yes    | —           |
| `witness_2_name`            | `string`        | ✔ Yes    | —           |
| `witness_2_role`            | `string`        | ✔ Yes    | —           |
| `witness_2_signature_token` | `string`        | ✔ Yes    | —           |
| `quantity_destroyed`        | `integer`       | No       | —           |
| `residual_volume_ml`        | `number`        | `null`   | No          | —   |
| `subject_id`                | `string`        | `null`   | No          | —   |
| `din`                       | `string`        | `null`   | No          | —   |
| `coc_id`                    | `string`        | `null`   | No          | —   |
| `notes`                     | `string`        | `null`   | No          | —   |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema                     |
| :---------- | :------------------ | :---------------------------------- |
| `201`       | Successful Response | `CGTDestructionCertificateResponse` |
| `422`       | Validation Error    | `HTTPValidationError`               |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/rtsm/cgt/destruction-certificate",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/rtsm/cgt/destruction-certificate',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/rtsm/cgt/destruction-certificate' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/rtsm/cgt/infuse`

**Summary:** Execute Infusion Endpoint
**Operation ID:** `execute_infusion_endpoint_api_v1_execution_rtsm_cgt_infuse_post`

**Clinical & GxP Rationale:**
Records cellular drug infusion administration, gated by the bedside verification unlock token.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `CGTInfusionExecuteRequest`

| Field                           | Type     | Required | Description |
| :------------------------------ | :------- | :------- | :---------- |
| `coc_id`                        | `string` | ✔ Yes    | —           |
| `unlock_token`                  | `string` | ✔ Yes    | —           |
| `administrator_id`              | `string` | ✔ Yes    | —           |
| `administrator_name`            | `string` | ✔ Yes    | —           |
| `administrator_role`            | `string` | No       | —           |
| `volume_infused_ml`             | `number` | No       | —           |
| `vital_signs_pre_temp_celsius`  | `number` | No       | —           |
| `vital_signs_post_temp_celsius` | `number` | No       | —           |
| `notes`                         | `string` | `null`   | No          | —   |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema             |
| :---------- | :------------------ | :-------------------------- |
| `200`       | Successful Response | `CGTChainOfCustodyResponse` |
| `422`       | Validation Error    | `HTTPValidationError`       |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/rtsm/cgt/infuse",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/rtsm/cgt/infuse',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/rtsm/cgt/infuse' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/rtsm/cgt/manufacturing-bind`

**Summary:** Bind Manufacturing Batch Endpoint
**Operation ID:** `bind_manufacturing_batch_endpoint_api_v1_execution_rtsm_cgt_manufacturing_bind_post`

**Clinical & GxP Rationale:**
Binds manufacturing batch record to original subject identity and regenerates barcode.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `CGTManufacturingBindRequest`

| Field                       | Type     | Required | Description |
| :-------------------------- | :------- | :------- | :---------- |
| `coc_id`                    | `string` | ✔ Yes    | —           |
| `batch_id`                  | `string` | ✔ Yes    | —           |
| `manufacturing_facility_id` | `string` | ✔ Yes    | —           |
| `operator_id`               | `string` | ✔ Yes    | —           |
| `operator_name`             | `string` | ✔ Yes    | —           |
| `operator_role`             | `string` | No       | —           |
| `temperature_celsius`       | `number` | No       | —           |
| `notes`                     | `string` | `null`   | No          | —   |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema             |
| :---------- | :------------------ | :-------------------------- |
| `200`       | Successful Response | `CGTChainOfCustodyResponse` |
| `422`       | Validation Error    | `HTTPValidationError`       |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/rtsm/cgt/manufacturing-bind",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/rtsm/cgt/manufacturing-bind',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/rtsm/cgt/manufacturing-bind' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/rtsm/cgt/milestone`

**Summary:** Record Cgt Milestone Endpoint
**Operation ID:** `record_cgt_milestone_endpoint_api_v1_execution_rtsm_cgt_milestone_post`

**Clinical & GxP Rationale:**
Records a cryogenic transit milestone with GPS coordinates and temperature logging.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `CGTMilestoneCreateRequest`

| Field                 | Type      | Required | Description |
| :-------------------- | :-------- | :------- | :---------- |
| `coc_id`              | `string`  | ✔ Yes    | —           |
| `stage`               | `string`  | ✔ Yes    | —           |
| `location_name`       | `string`  | ✔ Yes    | —           |
| `temperature_celsius` | `number`  | ✔ Yes    | —           |
| `custodian_id`        | `string`  | ✔ Yes    | —           |
| `custodian_name`      | `string`  | ✔ Yes    | —           |
| `custodian_role`      | `string`  | ✔ Yes    | —           |
| `facility_id`         | `string`  | `null`   | No          | —   |
| `latitude`            | `number`  | `null`   | No          | —   |
| `longitude`           | `number`  | `null`   | No          | —   |
| `carrier`             | `string`  | `null`   | No          | —   |
| `tracking_number`     | `string`  | `null`   | No          | —   |
| `seal_intact`         | `boolean` | No       | —           |
| `seal_id`             | `string`  | `null`   | No          | —   |
| `dry_shipper_id`      | `string`  | `null`   | No          | —   |
| `signature_token`     | `string`  | `null`   | No          | —   |
| `notes`               | `string`  | `null`   | No          | —   |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema        |
| :---------- | :------------------ | :--------------------- |
| `201`       | Successful Response | `CGTMilestoneResponse` |
| `422`       | Validation Error    | `HTTPValidationError`  |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/rtsm/cgt/milestone",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/rtsm/cgt/milestone',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/rtsm/cgt/milestone' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `GET` `/api/v1/execution/rtsm/cgt/reconciliations/{study_id}`

**Summary:** List Vial Reconciliations Endpoint
**Operation ID:** `list_vial_reconciliations_endpoint_api_v1_execution_rtsm_cgt_reconciliations__study_id__get`

**Clinical & GxP Rationale:**
Lists all product return and accountability reconciliation records for a study.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name       | In     | Type     | Required | Description |
| :--------- | :----- | :------- | :------- | :---------- |
| `study_id` | `path` | `string` | ✔ Yes    | —           |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema                              |
| :---------- | :------------------ | :------------------------------------------- |
| `200`       | Successful Response | `Array<CGTVialReturnReconciliationResponse>` |
| `422`       | Validation Error    | `HTTPValidationError`                        |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/rtsm/cgt/reconciliations/{study_id}",
  {
    method: "GET",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/execution/api/v1/execution/rtsm/cgt/reconciliations/{study_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/execution/api/v1/execution/rtsm/cgt/reconciliations/{study_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/rtsm/cgt/returns`

**Summary:** Log Vial Return Endpoint
**Operation ID:** `log_vial_return_endpoint_api_v1_execution_rtsm_cgt_returns_post`

**Clinical & GxP Rationale:**
Logs returned kit/vial accountability and calculates patient adherence compliance.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `CGTVialReturnReconciliationRequest`

| Field                | Type      | Required | Description |
| :------------------- | :-------- | :------- | :---------- |
| `study_id`           | `string`  | ✔ Yes    | —           |
| `site_id`            | `string`  | ✔ Yes    | —           |
| `subject_id`         | `string`  | ✔ Yes    | —           |
| `kit_number`         | `string`  | ✔ Yes    | —           |
| `vial_identifier`    | `string`  | ✔ Yes    | —           |
| `lot_number`         | `string`  | ✔ Yes    | —           |
| `expected_quantity`  | `integer` | No       | —           |
| `returned_quantity`  | `integer` | No       | —           |
| `intact_vials`       | `integer` | No       | —           |
| `used_vials`         | `integer` | No       | —           |
| `residual_volume_ml` | `number`  | No       | —           |
| `din`                | `string`  | `null`   | No          | —   |
| `logged_by`          | `string`  | ✔ Yes    | —           |
| `disposition`        | `string`  | No       | —           |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema                       |
| :---------- | :------------------ | :------------------------------------ |
| `201`       | Successful Response | `CGTVialReturnReconciliationResponse` |
| `422`       | Validation Error    | `HTTPValidationError`                 |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/rtsm/cgt/returns",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/rtsm/cgt/returns',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/rtsm/cgt/returns' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/rtsm/cgt/verify-barcode`

**Summary:** Verify Cgt Barcode Endpoint
**Operation ID:** `verify_cgt_barcode_endpoint_api_v1_execution_rtsm_cgt_verify_barcode_post`

**Clinical & GxP Rationale:**
Verifies cryptographic authenticity and entity bindings for a scanned barcode.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `CGTBarcodeVerifyRequest`

| Field                 | Type     | Required | Description |
| :-------------------- | :------- | :------- | :---------- |
| `barcode_payload`     | `string` | ✔ Yes    | —           |
| `expected_subject_id` | `string` | `null`   | No          | —   |
| `expected_din`        | `string` | `null`   | No          | —   |
| `expected_batch_id`   | `string` | `null`   | No          | —   |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema            |
| :---------- | :------------------ | :------------------------- |
| `200`       | Successful Response | `CGTBarcodeVerifyResponse` |
| `422`       | Validation Error    | `HTTPValidationError`      |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/rtsm/cgt/verify-barcode",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/rtsm/cgt/verify-barcode',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/rtsm/cgt/verify-barcode' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/rtsm/cold-chain/evaluate-mkt`

**Summary:** Evaluate Mkt Direct Endpoint
**Operation ID:** `evaluate_mkt_direct_endpoint_api_v1_execution_rtsm_cold_chain_evaluate_mkt_post`

**Clinical & GxP Rationale:**
Direct Arrhenius Mean Kinetic Temperature (MKT) calculation per USP <1079> / PDA TR 39.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `MktCalculationDirectRequest`

| Field                      | Type            | Required | Description |
| :------------------------- | :-------------- | :------- | :---------- |
| `temperatures_celsius`     | `Array<number>` | ✔ Yes    | —           |
| `activation_energy_over_r` | `number`        | No       | —           |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema                |
| :---------- | :------------------ | :----------------------------- |
| `200`       | Successful Response | `MktCalculationDirectResponse` |
| `422`       | Validation Error    | `HTTPValidationError`          |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/rtsm/cold-chain/evaluate-mkt",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/rtsm/cold-chain/evaluate-mkt',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/rtsm/cold-chain/evaluate-mkt' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `GET` `/api/v1/execution/rtsm/cold-chain/evaluations`

**Summary:** List Excursion Evaluations Endpoint
**Operation ID:** `list_excursion_evaluations_endpoint_api_v1_execution_rtsm_cold_chain_evaluations_get`

**Clinical & GxP Rationale:**
Lists cold chain excursion evaluation audit records.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name        | In      | Type     | Required | Description               |
| :---------- | :------ | :------- | :------- | :------------------------ |
| `study_id`  | `query` | `string` | ✔ Yes    | Clinical study identifier |
| `entity_id` | `query` | `string` | `null`   | No                        | Optional filter by transfer order or node ID |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema                      |
| :---------- | :------------------ | :----------------------------------- |
| `200`       | Successful Response | `Array<ExcursionEvaluationResponse>` |
| `422`       | Validation Error    | `HTTPValidationError`                |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/rtsm/cold-chain/evaluations",
  {
    method: "GET",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/execution/api/v1/execution/rtsm/cold-chain/evaluations',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/execution/api/v1/execution/rtsm/cold-chain/evaluations' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/rtsm/cold-chain/ingest`

**Summary:** Ingest Temperature Logger File Endpoint
**Operation ID:** `ingest_temperature_logger_file_endpoint_api_v1_execution_rtsm_cold_chain_ingest_post`

**Clinical & GxP Rationale:**
Ingests a digital logger file (CSV, XML, JSON), calculates Arrhenius MKT, and enforces automated kit quarantine.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `LoggerIngestionRequest`

| Field                       | Type                           | Required | Description |
| :-------------------------- | :----------------------------- | :------- | :---------- |
| `study_id`                  | `string`                       | ✔ Yes    | —           |
| `logger_id`                 | `string`                       | ✔ Yes    | —           |
| `file_format`               | `string`                       | No       | —           |
| `raw_payload`               | `string`                       | ✔ Yes    | —           |
| `filename`                  | `string`                       | ✔ Yes    | —           |
| `entity_type`               | `string`                       | No       | —           |
| `entity_id`                 | `string`                       | ✔ Yes    | —           |
| `stability_budget_override` | `StabilityBudgetCreateRequest` | `null`   | No          | —   |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema               |
| :---------- | :------------------ | :---------------------------- |
| `201`       | Successful Response | `ExcursionEvaluationResponse` |
| `422`       | Validation Error    | `HTTPValidationError`         |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/rtsm/cold-chain/ingest",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/rtsm/cold-chain/ingest',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/rtsm/cold-chain/ingest' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/rtsm/cold-chain/loggers`

**Summary:** Register Temperature Logger Endpoint
**Operation ID:** `register_temperature_logger_endpoint_api_v1_execution_rtsm_cold_chain_loggers_post`

**Clinical & GxP Rationale:**
Registers a temperature data logger device bound to a shipment or storage facility.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `TemperatureLoggerRegisterRequest`

| Field                        | Type     | Required | Description |
| :--------------------------- | :------- | :------- | :---------- |
| `study_id`                   | `string` | ✔ Yes    | —           |
| `logger_id`                  | `string` | ✔ Yes    | —           |
| `device_model`               | `string` | No       | —           |
| `serial_number`              | `string` | `null`   | No          | —   |
| `logger_type`                | `string` | No       | —           |
| `assigned_transfer_order_id` | `string` | `null`   | No          | —   |
| `assigned_node_id`           | `string` | `null`   | No          | —   |
| `assigned_container_id`      | `string` | `null`   | No          | —   |
| `calibration_expiry_date`    | `string` | `null`   | No          | —   |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema             |
| :---------- | :------------------ | :-------------------------- |
| `201`       | Successful Response | `TemperatureLoggerResponse` |
| `422`       | Validation Error    | `HTTPValidationError`       |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/rtsm/cold-chain/loggers",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/rtsm/cold-chain/loggers',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/rtsm/cold-chain/loggers' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `GET` `/api/v1/execution/rtsm/cold-chain/loggers`

**Summary:** List Temperature Loggers Endpoint
**Operation ID:** `list_temperature_loggers_endpoint_api_v1_execution_rtsm_cold_chain_loggers_get`

**Clinical & GxP Rationale:**
Lists registered temperature loggers for a study.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name       | In      | Type     | Required | Description               |
| :--------- | :------ | :------- | :------- | :------------------------ |
| `study_id` | `query` | `string` | ✔ Yes    | Clinical study identifier |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema                    |
| :---------- | :------------------ | :--------------------------------- |
| `200`       | Successful Response | `Array<TemperatureLoggerResponse>` |
| `422`       | Validation Error    | `HTTPValidationError`              |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/rtsm/cold-chain/loggers",
  {
    method: "GET",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/execution/api/v1/execution/rtsm/cold-chain/loggers',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/execution/api/v1/execution/rtsm/cold-chain/loggers' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/rtsm/dispense`

**Summary:** Dispense Kit Endpoint
**Operation ID:** `dispense_kit_endpoint_api_v1_execution_rtsm_dispense_post`

**Clinical & GxP Rationale:**
End-point to dispense investigational product (IP) kits against site inventory.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `DispenseRequest`

| Field        | Type      | Required | Description |
| :----------- | :-------- | :------- | :---------- |
| `study_id`   | `string`  | ✔ Yes    | —           |
| `site_id`    | `string`  | ✔ Yes    | —           |
| `subject_id` | `string`  | ✔ Yes    | —           |
| `visit_id`   | `string`  | ✔ Yes    | —           |
| `kit_id`     | `string`  | ✔ Yes    | —           |
| `quantity`   | `integer` | No       | —           |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema       |
| :---------- | :------------------ | :-------------------- |
| `201`       | Successful Response | `DispenseResponse`    |
| `422`       | Validation Error    | `HTTPValidationError` |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/rtsm/dispense",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/rtsm/dispense',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/rtsm/dispense' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/rtsm/forecast/site`

**Summary:** Forecast Site Resupply Endpoint
**Operation ID:** `forecast_site_resupply_endpoint_api_v1_execution_rtsm_forecast_site_post`

**Clinical & GxP Rationale:**
Executes a predictive demand and resupply forecast for a clinical site.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `PredictiveForecastRequest`

| Field                        | Type      | Required | Description |
| :--------------------------- | :-------- | :------- | :---------- |
| `study_id`                   | `string`  | ✔ Yes    | —           |
| `site_id`                    | `string`  | ✔ Yes    | —           |
| `kit_type`                   | `string`  | No       | —           |
| `horizon_days`               | `integer` | No       | —           |
| `enrollment_velocity_weekly` | `number`  | No       | —           |
| `screening_pipeline_count`   | `integer` | No       | —           |
| `screening_conversion_rate`  | `number`  | No       | —           |
| `lead_time_days`             | `integer` | No       | —           |
| `buffer_days`                | `integer` | No       | —           |
| `safety_stock_threshold`     | `integer` | No       | —           |
| `storage_capacity`           | `integer` | No       | —           |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema              |
| :---------- | :------------------ | :--------------------------- |
| `200`       | Successful Response | `PredictiveForecastResponse` |
| `422`       | Validation Error    | `HTTPValidationError`        |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/rtsm/forecast/site",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/rtsm/forecast/site',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/rtsm/forecast/site' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/rtsm/forecast/study`

**Summary:** Forecast Study Resupply Endpoint
**Operation ID:** `forecast_study_resupply_endpoint_api_v1_execution_rtsm_forecast_study_post`

**Clinical & GxP Rationale:**
Generates concurrent predictive forecasts across all active clinical sites in a study.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name           | In      | Type      | Required | Description               |
| :------------- | :------ | :-------- | :------- | :------------------------ |
| `study_id`     | `query` | `string`  | ✔ Yes    | Clinical study identifier |
| `horizon_days` | `query` | `integer` | No       | —                         |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema                     |
| :---------- | :------------------ | :---------------------------------- |
| `200`       | Successful Response | `Array<PredictiveForecastResponse>` |
| `422`       | Validation Error    | `HTTPValidationError`               |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/rtsm/forecast/study",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/rtsm/forecast/study',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/rtsm/forecast/study' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `GET` `/api/v1/execution/rtsm/hierarchy`

**Summary:** Get Supply Hierarchy Endpoint
**Operation ID:** `get_supply_hierarchy_endpoint_api_v1_execution_rtsm_hierarchy_get`

**Clinical & GxP Rationale:**
Returns the complete multi-echelon supply hierarchy tree and stock rollup for a study.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name       | In      | Type     | Required | Description               |
| :--------- | :------ | :------- | :------- | :------------------------ |
| `study_id` | `query` | `string` | ✔ Yes    | Clinical study identifier |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema       |
| :---------- | :------------------ | :-------------------- |
| `200`       | Successful Response | `MultiEchelonRollup`  |
| `422`       | Validation Error    | `HTTPValidationError` |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/rtsm/hierarchy",
  {
    method: "GET",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/execution/api/v1/execution/rtsm/hierarchy',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/execution/api/v1/execution/rtsm/hierarchy' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/rtsm/kits`

**Summary:** Create Kit Endpoint
**Operation ID:** `create_kit_endpoint_api_v1_execution_rtsm_kits_post`

**Clinical & GxP Rationale:**
Registers an investigational product kit into the blinded RTSM inventory catalog.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `IPKitCreateRequest`

| Field             | Type     | Required | Description |
| :---------------- | :------- | :------- | :---------- |
| `study_id`        | `string` | ✔ Yes    | —           |
| `kit_number`      | `string` | ✔ Yes    | —           |
| `kit_type`        | `string` | ✔ Yes    | —           |
| `description`     | `string` | `null`   | No          | —   |
| `lot_number`      | `string` | `null`   | No          | —   |
| `expiration_date` | `string` | `null`   | No          | —   |
| `node_id`         | `string` | `null`   | No          | —   |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema       |
| :---------- | :------------------ | :-------------------- |
| `201`       | Successful Response | `IPKitResponse`       |
| `422`       | Validation Error    | `HTTPValidationError` |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/rtsm/kits",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/rtsm/kits',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/rtsm/kits' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `GET` `/api/v1/execution/rtsm/kits`

**Summary:** List Kits Endpoint
**Operation ID:** `list_kits_endpoint_api_v1_execution_rtsm_kits_get`

**Clinical & GxP Rationale:**
Lists investigational product kits.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name         | In      | Type     | Required | Description               |
| :----------- | :------ | :------- | :------- | :------------------------ |
| `study_id`   | `query` | `string` | ✔ Yes    | Clinical study identifier |
| `node_id`    | `query` | `string` | `null`   | No                        | Optional node/site ID filter                         |
| `lot_number` | `query` | `string` | `null`   | No                        | Optional lot number filter                           |
| `status`     | `query` | `string` | `null`   | No                        | Optional status filter (e.g. AVAILABLE, QUARANTINED) |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema        |
| :---------- | :------------------ | :--------------------- |
| `200`       | Successful Response | `Array<IPKitResponse>` |
| `422`       | Validation Error    | `HTTPValidationError`  |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/rtsm/kits",
  {
    method: "GET",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/execution/api/v1/execution/rtsm/kits',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/execution/api/v1/execution/rtsm/kits' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/rtsm/kits/quarantine`

**Summary:** Quarantine Kits Endpoint
**Operation ID:** `quarantine_kits_endpoint_api_v1_execution_rtsm_kits_quarantine_post`

**Clinical & GxP Rationale:**
Manually places investigational product kits in QUARANTINED status with Part 11 electronic audit trail.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `KitQuarantineRequest`

| Field               | Type            | Required | Description |
| :------------------ | :-------------- | :------- | :---------- |
| `study_id`          | `string`        | ✔ Yes    | —           |
| `kit_numbers`       | `Array<string>` | ✔ Yes    | —           |
| `quarantine_reason` | `string`        | ✔ Yes    | —           |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema       |
| :---------- | :------------------ | :-------------------- |
| `200`       | Successful Response | `object`              |
| `422`       | Validation Error    | `HTTPValidationError` |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/rtsm/kits/quarantine",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/rtsm/kits/quarantine',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/rtsm/kits/quarantine' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/rtsm/kits/release-quarantine`

**Summary:** Release Quarantine Kits Endpoint
**Operation ID:** `release_quarantine_kits_endpoint_api_v1_execution_rtsm_kits_release_quarantine_post`

**Clinical & GxP Rationale:**
Authorizes QA disposition release of quarantined kits back into available inventory.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `KitReleaseQuarantineRequest`

| Field                      | Type            | Required | Description |
| :------------------------- | :-------------- | :------- | :---------- |
| `study_id`                 | `string`        | ✔ Yes    | —           |
| `kit_numbers`              | `Array<string>` | ✔ Yes    | —           |
| `qa_release_justification` | `string`        | ✔ Yes    | —           |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema       |
| :---------- | :------------------ | :-------------------- |
| `200`       | Successful Response | `object`              |
| `422`       | Validation Error    | `HTTPValidationError` |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/rtsm/kits/release-quarantine",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/rtsm/kits/release-quarantine',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/rtsm/kits/release-quarantine' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/rtsm/lots/extend-expiry`

**Summary:** Extend Lot Expiry Endpoint
**Operation ID:** `extend_lot_expiry_endpoint_api_v1_execution_rtsm_lots_extend_expiry_post`

**Clinical & GxP Rationale:**
Executes automated lot expiration extension workflow, updating kits across all sites and depots.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `LotExpiryExtensionRequest`

| Field                          | Type     | Required | Description |
| :----------------------------- | :------- | :------- | :---------- |
| `study_id`                     | `string` | ✔ Yes    | —           |
| `lot_number`                   | `string` | ✔ Yes    | —           |
| `kit_type`                     | `string` | ✔ Yes    | —           |
| `extended_expiry_date`         | `string` | ✔ Yes    | —           |
| `extension_reason`             | `string` | ✔ Yes    | —           |
| `stability_protocol_reference` | `string` | ✔ Yes    | —           |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema              |
| :---------- | :------------------ | :--------------------------- |
| `201`       | Successful Response | `LotExpiryExtensionResponse` |
| `422`       | Validation Error    | `HTTPValidationError`        |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/rtsm/lots/extend-expiry",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/rtsm/lots/extend-expiry',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/rtsm/lots/extend-expiry' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `GET` `/api/v1/execution/rtsm/lots/extensions`

**Summary:** List Lot Expiry Extensions Endpoint
**Operation ID:** `list_lot_expiry_extensions_endpoint_api_v1_execution_rtsm_lots_extensions_get`

**Clinical & GxP Rationale:**
Lists lot expiration extension audit records.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name         | In      | Type     | Required | Description               |
| :----------- | :------ | :------- | :------- | :------------------------ |
| `study_id`   | `query` | `string` | ✔ Yes    | Clinical study identifier |
| `lot_number` | `query` | `string` | `null`   | No                        | Optional lot number filter |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema                     |
| :---------- | :------------------ | :---------------------------------- |
| `200`       | Successful Response | `Array<LotExpiryExtensionResponse>` |
| `422`       | Validation Error    | `HTTPValidationError`               |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/rtsm/lots/extensions",
  {
    method: "GET",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/execution/api/v1/execution/rtsm/lots/extensions',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/execution/api/v1/execution/rtsm/lots/extensions' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/rtsm/nodes`

**Summary:** Create Supply Node Endpoint
**Operation ID:** `create_supply_node_endpoint_api_v1_execution_rtsm_nodes_post`

**Clinical & GxP Rationale:**
Registers or updates a supply node in the clinical supply hierarchy.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `SupplyNodeCreateRequest`

| Field                    | Type            | Required | Description |
| :----------------------- | :-------------- | :------- | :---------- |
| `study_id`               | `string`        | ✔ Yes    | —           |
| `node_id`                | `string`        | ✔ Yes    | —           |
| `node_type`              | `string`        | No       | —           |
| `name`                   | `string`        | ✔ Yes    | —           |
| `country`                | `string`        | ✔ Yes    | —           |
| `address`                | `string`        | `null`   | No          | —   |
| `parent_node_id`         | `string`        | `null`   | No          | —   |
| `storage_capacity`       | `integer`       | No       | —           |
| `lead_time_days`         | `integer`       | No       | —           |
| `temperature_conditions` | `Array<string>` | No       | —           |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema       |
| :---------- | :------------------ | :-------------------- |
| `201`       | Successful Response | `SupplyNodeResponse`  |
| `422`       | Validation Error    | `HTTPValidationError` |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/rtsm/nodes",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/rtsm/nodes',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/rtsm/nodes' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `GET` `/api/v1/execution/rtsm/nodes/{node_id}/inventory`

**Summary:** Get Node Inventory Endpoint
**Operation ID:** `get_node_inventory_endpoint_api_v1_execution_rtsm_nodes__node_id__inventory_get`

**Clinical & GxP Rationale:**
Lists inventory records and stock levels for a specific supply node.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name       | In      | Type     | Required | Description               |
| :--------- | :------ | :------- | :------- | :------------------------ |
| `node_id`  | `path`  | `string` | ✔ Yes    | —                         |
| `study_id` | `query` | `string` | ✔ Yes    | Clinical study identifier |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema                      |
| :---------- | :------------------ | :----------------------------------- |
| `200`       | Successful Response | `Array<SupplyNodeInventoryResponse>` |
| `422`       | Validation Error    | `HTTPValidationError`                |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/rtsm/nodes/{node_id}/inventory",
  {
    method: "GET",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/execution/api/v1/execution/rtsm/nodes/{node_id}/inventory',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/execution/api/v1/execution/rtsm/nodes/{node_id}/inventory' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/rtsm/nodes/{node_id}/inventory/adjust`

**Summary:** Adjust Node Inventory Endpoint
**Operation ID:** `adjust_node_inventory_endpoint_api_v1_execution_rtsm_nodes__node_id__inventory_adjust_post`

**Clinical & GxP Rationale:**
Adjusts or initializes stock levels for a supply node.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name      | In     | Type     | Required | Description |
| :-------- | :----- | :------- | :------- | :---------- |
| `node_id` | `path` | `string` | ✔ Yes    | —           |

#### Request Payload Schema (`application/json`)

**Schema Type:** `SupplyNodeInventoryAdjustRequest`

| Field                    | Type      | Required | Description |
| :----------------------- | :-------- | :------- | :---------- |
| `study_id`               | `string`  | ✔ Yes    | —           |
| `node_id`                | `string`  | ✔ Yes    | —           |
| `kit_type`               | `string`  | ✔ Yes    | —           |
| `lot_number`             | `string`  | `null`   | No          | —   |
| `on_hand_qty`            | `integer` | ✔ Yes    | —           |
| `safety_stock_threshold` | `integer` | No       | —           |
| `reorder_point`          | `integer` | No       | —           |
| `max_capacity`           | `integer` | No       | —           |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema               |
| :---------- | :------------------ | :---------------------------- |
| `200`       | Successful Response | `SupplyNodeInventoryResponse` |
| `422`       | Validation Error    | `HTTPValidationError`         |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/rtsm/nodes/{node_id}/inventory/adjust",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/rtsm/nodes/{node_id}/inventory/adjust',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/rtsm/nodes/{node_id}/inventory/adjust' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `GET` `/api/v1/execution/rtsm/relabeling-tasks`

**Summary:** List Relabeling Tasks Endpoint
**Operation ID:** `list_relabeling_tasks_endpoint_api_v1_execution_rtsm_relabeling_tasks_get`

**Clinical & GxP Rationale:**
Lists physical re-labeling action tasks across facilities.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name         | In      | Type     | Required | Description               |
| :----------- | :------ | :------- | :------- | :------------------------ |
| `study_id`   | `query` | `string` | ✔ Yes    | Clinical study identifier |
| `node_id`    | `query` | `string` | `null`   | No                        | Optional node/site ID filter |
| `lot_number` | `query` | `string` | `null`   | No                        | Optional lot number filter   |
| `status`     | `query` | `string` | `null`   | No                        | Optional status filter       |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema                 |
| :---------- | :------------------ | :------------------------------ |
| `200`       | Successful Response | `Array<ReLabelingTaskResponse>` |
| `422`       | Validation Error    | `HTTPValidationError`           |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/rtsm/relabeling-tasks",
  {
    method: "GET",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/execution/api/v1/execution/rtsm/relabeling-tasks',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/execution/api/v1/execution/rtsm/relabeling-tasks' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/rtsm/relabeling-tasks/{task_id}/complete`

**Summary:** Complete Relabeling Task Endpoint
**Operation ID:** `complete_relabeling_task_endpoint_api_v1_execution_rtsm_relabeling_tasks__task_id__complete_post`

**Clinical & GxP Rationale:**
Records completion of a physical kit re-labeling task with electronic signature justification.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name      | In     | Type     | Required | Description |
| :-------- | :----- | :------- | :------- | :---------- |
| `task_id` | `path` | `string` | ✔ Yes    | —           |

#### Request Payload Schema (`application/json`)

**Schema Type:** `ReLabelingTaskCompleteRequest`

| Field   | Type     | Required | Description |
| :------ | :------- | :------- | :---------- |
| `notes` | `string` | `null`   | No          | —   |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema          |
| :---------- | :------------------ | :----------------------- |
| `200`       | Successful Response | `ReLabelingTaskResponse` |
| `422`       | Validation Error    | `HTTPValidationError`    |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/rtsm/relabeling-tasks/{task_id}/complete",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/rtsm/relabeling-tasks/{task_id}/complete',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/rtsm/relabeling-tasks/{task_id}/complete' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/rtsm/relabeling-tasks/{task_id}/verify`

**Summary:** Verify Relabeling Task Endpoint
**Operation ID:** `verify_relabeling_task_endpoint_api_v1_execution_rtsm_relabeling_tasks__task_id__verify_post`

**Clinical & GxP Rationale:**
Dual QA verification of completed re-labeling task with barcode verification hash.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name      | In     | Type     | Required | Description |
| :-------- | :----- | :------- | :------- | :---------- |
| `task_id` | `path` | `string` | ✔ Yes    | —           |

#### Request Payload Schema (`application/json`)

**Schema Type:** `ReLabelingTaskVerifyRequest`

| Field                | Type     | Required | Description |
| :------------------- | :------- | :------- | :---------- |
| `label_barcode_hash` | `string` | `null`   | No          | —   |
| `notes`              | `string` | `null`   | No          | —   |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema          |
| :---------- | :------------------ | :----------------------- |
| `200`       | Successful Response | `ReLabelingTaskResponse` |
| `422`       | Validation Error    | `HTTPValidationError`    |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/rtsm/relabeling-tasks/{task_id}/verify",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/rtsm/relabeling-tasks/{task_id}/verify',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/rtsm/relabeling-tasks/{task_id}/verify' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `GET` `/api/v1/execution/rtsm/resupply-events`

**Summary:** List Resupply Events Endpoint
**Operation ID:** `list_resupply_events_endpoint_api_v1_execution_rtsm_resupply_events_get`

**Clinical & GxP Rationale:**
List resupply events filtered by role and site permissions.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name       | In      | Type     | Required | Description |
| :--------- | :------ | :------- | :------- | :---------- |
| `study_id` | `query` | `string` | `null`   | No          | —   |
| `site_id`  | `query` | `string` | `null`   | No          | —   |
| `status`   | `query` | `string` | `null`   | No          | —   |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema                |
| :---------- | :------------------ | :----------------------------- |
| `200`       | Successful Response | `Array<ResupplyEventResponse>` |
| `422`       | Validation Error    | `HTTPValidationError`          |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/rtsm/resupply-events",
  {
    method: "GET",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/execution/api/v1/execution/rtsm/resupply-events',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/execution/api/v1/execution/rtsm/resupply-events' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/rtsm/resupply-events/{event_id}/confirm`

**Summary:** Approve Resupply Event Endpoint
**Operation ID:** `approve_resupply_event_endpoint_api_v1_execution_rtsm_resupply_events__event_id__confirm_post`

**Clinical & GxP Rationale:**
Approve a resupply event to trigger shipment generation.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name       | In     | Type     | Required | Description |
| :--------- | :----- | :------- | :------- | :---------- |
| `event_id` | `path` | `string` | ✔ Yes    | —           |

#### Request Payload Schema (`application/json`)

**Schema Type:** `ResupplyEventApprovalRequest`

| Field                  | Type     | Required | Description |
| :--------------------- | :------- | :------- | :---------- |
| `change_justification` | `string` | ✔ Yes    | —           |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema         |
| :---------- | :------------------ | :---------------------- |
| `200`       | Successful Response | `ResupplyEventResponse` |
| `422`       | Validation Error    | `HTTPValidationError`   |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/rtsm/resupply-events/{event_id}/confirm",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/rtsm/resupply-events/{event_id}/confirm',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/rtsm/resupply-events/{event_id}/confirm' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/rtsm/resupply-events/{event_id}/reject`

**Summary:** Reject Resupply Event Endpoint
**Operation ID:** `reject_resupply_event_endpoint_api_v1_execution_rtsm_resupply_events__event_id__reject_post`

**Clinical & GxP Rationale:**
Reject a resupply event.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name       | In     | Type     | Required | Description |
| :--------- | :----- | :------- | :------- | :---------- |
| `event_id` | `path` | `string` | ✔ Yes    | —           |

#### Request Payload Schema (`application/json`)

**Schema Type:** `ResupplyEventApprovalRequest`

| Field                  | Type     | Required | Description |
| :--------------------- | :------- | :------- | :---------- |
| `change_justification` | `string` | ✔ Yes    | —           |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema         |
| :---------- | :------------------ | :---------------------- |
| `200`       | Successful Response | `ResupplyEventResponse` |
| `422`       | Validation Error    | `HTTPValidationError`   |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/rtsm/resupply-events/{event_id}/reject",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/rtsm/resupply-events/{event_id}/reject',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/rtsm/resupply-events/{event_id}/reject' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/rtsm/stability-budgets`

**Summary:** Create Stability Budget Endpoint
**Operation ID:** `create_stability_budget_endpoint_api_v1_execution_rtsm_stability_budgets_post`

**Clinical & GxP Rationale:**
Configures allowable thermal excursion limits and Arrhenius activation parameters for a kit type or lot.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `StabilityBudgetCreateRequest`

| Field                              | Type     | Required | Description |
| :--------------------------------- | :------- | :------- | :---------- |
| `study_id`                         | `string` | ✔ Yes    | —           |
| `kit_type`                         | `string` | ✔ Yes    | —           |
| `lot_number`                       | `string` | `null`   | No          | —   |
| `storage_condition`                | `string` | No       | —           |
| `min_temp_celsius`                 | `number` | No       | —           |
| `max_temp_celsius`                 | `number` | No       | —           |
| `max_mkt_celsius`                  | `number` | No       | —           |
| `max_cumulative_hours_above_upper` | `number` | No       | —           |
| `max_cumulative_hours_below_lower` | `number` | No       | —           |
| `activation_energy_over_r`         | `number` | No       | —           |
| `hard_min_temp_celsius`            | `number` | `null`   | No          | —   |
| `hard_max_temp_celsius`            | `number` | `null`   | No          | —   |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema           |
| :---------- | :------------------ | :------------------------ |
| `201`       | Successful Response | `StabilityBudgetResponse` |
| `422`       | Validation Error    | `HTTPValidationError`     |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/rtsm/stability-budgets",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/rtsm/stability-budgets',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/rtsm/stability-budgets' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `GET` `/api/v1/execution/rtsm/stability-budgets`

**Summary:** List Stability Budgets Endpoint
**Operation ID:** `list_stability_budgets_endpoint_api_v1_execution_rtsm_stability_budgets_get`

**Clinical & GxP Rationale:**
Lists defined drug stability budgets for a clinical study.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name       | In      | Type     | Required | Description               |
| :--------- | :------ | :------- | :------- | :------------------------ |
| `study_id` | `query` | `string` | ✔ Yes    | Clinical study identifier |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema                  |
| :---------- | :------------------ | :------------------------------- |
| `200`       | Successful Response | `Array<StabilityBudgetResponse>` |
| `422`       | Validation Error    | `HTTPValidationError`            |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/rtsm/stability-budgets",
  {
    method: "GET",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/execution/api/v1/execution/rtsm/stability-budgets',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/execution/api/v1/execution/rtsm/stability-budgets' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/rtsm/transfer-orders`

**Summary:** Create Transfer Order Endpoint
**Operation ID:** `create_transfer_order_endpoint_api_v1_execution_rtsm_transfer_orders_post`

**Clinical & GxP Rationale:**
Creates a stock transfer order between supply nodes.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `TransferOrderCreateRequest`

| Field                   | Type      | Required | Description |
| :---------------------- | :-------- | :------- | :---------- |
| `study_id`              | `string`  | ✔ Yes    | —           |
| `source_node_id`        | `string`  | ✔ Yes    | —           |
| `destination_node_id`   | `string`  | ✔ Yes    | —           |
| `kit_type`              | `string`  | ✔ Yes    | —           |
| `quantity`              | `integer` | ✔ Yes    | —           |
| `priority`              | `string`  | No       | —           |
| `temperature_condition` | `string`  | No       | —           |
| `carrier`               | `string`  | `null`   | No          | —   |
| `transfer_order_number` | `string`  | `null`   | No          | —   |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema         |
| :---------- | :------------------ | :---------------------- |
| `201`       | Successful Response | `TransferOrderResponse` |
| `422`       | Validation Error    | `HTTPValidationError`   |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/rtsm/transfer-orders",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/rtsm/transfer-orders',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/rtsm/transfer-orders' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `GET` `/api/v1/execution/rtsm/transfer-orders`

**Summary:** List Transfer Orders Endpoint
**Operation ID:** `list_transfer_orders_endpoint_api_v1_execution_rtsm_transfer_orders_get`

**Clinical & GxP Rationale:**
Lists active transfer orders with optional filtering.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name                  | In      | Type     | Required | Description               |
| :-------------------- | :------ | :------- | :------- | :------------------------ |
| `study_id`            | `query` | `string` | ✔ Yes    | Clinical study identifier |
| `source_node_id`      | `query` | `string` | `null`   | No                        | —   |
| `destination_node_id` | `query` | `string` | `null`   | No                        | —   |
| `status`              | `query` | `string` | `null`   | No                        | —   |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema                |
| :---------- | :------------------ | :----------------------------- |
| `200`       | Successful Response | `Array<TransferOrderResponse>` |
| `422`       | Validation Error    | `HTTPValidationError`          |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/rtsm/transfer-orders",
  {
    method: "GET",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/execution/api/v1/execution/rtsm/transfer-orders',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/execution/api/v1/execution/rtsm/transfer-orders' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/rtsm/transfer-orders/{order_id}/approve`

**Summary:** Approve Transfer Order Endpoint
**Operation ID:** `approve_transfer_order_endpoint_api_v1_execution_rtsm_transfer_orders__order_id__approve_post`

**Clinical & GxP Rationale:**
Approves a transfer order for warehouse packing.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name       | In     | Type     | Required | Description |
| :--------- | :----- | :------- | :------- | :---------- |
| `order_id` | `path` | `string` | ✔ Yes    | —           |

#### Request Payload Schema (`application/json`)

**Schema Type:** `TransferOrderApprovalRequest`

| Field                  | Type     | Required | Description |
| :--------------------- | :------- | :------- | :---------- |
| `change_justification` | `string` | ✔ Yes    | —           |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema         |
| :---------- | :------------------ | :---------------------- |
| `200`       | Successful Response | `TransferOrderResponse` |
| `422`       | Validation Error    | `HTTPValidationError`   |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/rtsm/transfer-orders/{order_id}/approve",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/rtsm/transfer-orders/{order_id}/approve',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/rtsm/transfer-orders/{order_id}/approve' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/rtsm/transfer-orders/{order_id}/manifest`

**Summary:** Generate Packing Manifest Endpoint
**Operation ID:** `generate_packing_manifest_endpoint_api_v1_execution_rtsm_transfer_orders__order_id__manifest_post`

**Clinical & GxP Rationale:**
Generates a formal GxP packing manifest binding serialized kits to an approved transfer order.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name       | In     | Type     | Required | Description |
| :--------- | :----- | :------- | :------- | :---------- |
| `order_id` | `path` | `string` | ✔ Yes    | —           |

#### Request Payload Schema (`application/json`)

**Schema Type:** `PackingManifestCreateRequest`

| Field                     | Type            | Required | Description |
| :------------------------ | :-------------- | :------- | :---------- |
| `manifest_number`         | `string`        | ✔ Yes    | —           |
| `serialized_kit_numbers`  | `Array<string>` | ✔ Yes    | —           |
| `lot_number`              | `string`        | ✔ Yes    | —           |
| `expiration_date`         | `string`        | `null`   | No          | —   |
| `container_serial_number` | `string`        | `null`   | No          | —   |
| `data_logger_id`          | `string`        | `null`   | No          | —   |
| `tamper_seal_id`          | `string`        | `null`   | No          | —   |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema           |
| :---------- | :------------------ | :------------------------ |
| `201`       | Successful Response | `PackingManifestResponse` |
| `422`       | Validation Error    | `HTTPValidationError`     |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/rtsm/transfer-orders/{order_id}/manifest",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/rtsm/transfer-orders/{order_id}/manifest',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/rtsm/transfer-orders/{order_id}/manifest' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/rtsm/transfer-orders/{order_id}/receive`

**Summary:** Receive Transfer Order Endpoint
**Operation ID:** `receive_transfer_order_endpoint_api_v1_execution_rtsm_transfer_orders__order_id__receive_post`

**Clinical & GxP Rationale:**
Marks a transfer order delivered/received at destination node, transferring in-transit stock to on-hand.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name       | In     | Type     | Required | Description |
| :--------- | :----- | :------- | :------- | :---------- |
| `order_id` | `path` | `string` | ✔ Yes    | —           |

#### Request Payload Schema (`application/json`)

**Schema Type:** `TransferOrderReceiveRequest`

| Field                  | Type     | Required | Description |
| :--------------------- | :------- | :------- | :---------- |
| `change_justification` | `string` | ✔ Yes    | —           |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema         |
| :---------- | :------------------ | :---------------------- |
| `200`       | Successful Response | `TransferOrderResponse` |
| `422`       | Validation Error    | `HTTPValidationError`   |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/rtsm/transfer-orders/{order_id}/receive",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/rtsm/transfer-orders/{order_id}/receive',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/rtsm/transfer-orders/{order_id}/receive' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/rtsm/transfer-orders/{order_id}/ship`

**Summary:** Ship Transfer Order Endpoint
**Operation ID:** `ship_transfer_order_endpoint_api_v1_execution_rtsm_transfer_orders__order_id__ship_post`

**Clinical & GxP Rationale:**
Marks a transfer order shipped, moves inventory to in-transit, and dispatches depot notification.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name       | In     | Type     | Required | Description |
| :--------- | :----- | :------- | :------- | :---------- |
| `order_id` | `path` | `string` | ✔ Yes    | —           |

#### Request Payload Schema (`application/json`)

**Schema Type:** `TransferOrderShipRequest`

| Field                     | Type     | Required | Description |
| :------------------------ | :------- | :------- | :---------- |
| `tracking_number`         | `string` | ✔ Yes    | —           |
| `carrier`                 | `string` | `null`   | No          | —   |
| `estimated_delivery_date` | `string` | `null`   | No          | —   |
| `change_justification`    | `string` | ✔ Yes    | —           |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema         |
| :---------- | :------------------ | :---------------------- |
| `200`       | Successful Response | `TransferOrderResponse` |
| `422`       | Validation Error    | `HTTPValidationError`   |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/rtsm/transfer-orders/{order_id}/ship",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/rtsm/transfer-orders/{order_id}/ship',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/rtsm/transfer-orders/{order_id}/ship' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/safety/dispatch`

**Summary:** Dispatch Safety Report Endpoint
**Operation ID:** `dispatch_safety_report_endpoint_api_v1_execution_safety_dispatch_post`

**Clinical & GxP Rationale:**
Dispatch ICH E2B(R3) safety report to external pharmacovigilance gateway.

Requirements: PRD-SYS-001

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `SafetyDispatchRequest`

| Field                 | Type      | Required | Description                                              |
| :-------------------- | :-------- | :------- | :------------------------------------------------------- |
| `study_id`            | `string`  | ✔ Yes    | Target protocol study ID                                 |
| `subject_id`          | `string`  | ✔ Yes    | Target subject ID                                        |
| `safety_report_id`    | `string`  | ✔ Yes    | Unique E2B(R3) Safety Report ID                          |
| `destination_gateway` | `string`  | No       | Destination safety gateway: ARGUS, ARISG, EUDRAVIGILANCE |
| `expedited`           | `boolean` | No       | True for expedited 7/15-day reporting                    |
| `reason_for_change`   | `string`  | ✔ Yes    | Mandatory GxP 21 CFR Part 11 justification reason        |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema          |
| :---------- | :------------------ | :----------------------- |
| `200`       | Successful Response | `SafetyDispatchResponse` |
| `422`       | Validation Error    | `HTTPValidationError`    |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/safety/dispatch",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/safety/dispatch',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/safety/dispatch' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/safety/reconcile`

**Summary:** Reconcile Sae Cases Endpoint
**Operation ID:** `reconcile_sae_cases_endpoint_api_v1_execution_safety_reconcile_post`

**Clinical & GxP Rationale:**
Execute automated EDC AE to Safety ICSR case reconciliation.

Requirements: PRD-SYS-001

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `SAEReconcileRequest`

| Field              | Type            | Required | Description                    |
| :----------------- | :-------------- | :------- | :----------------------------- |
| `study_id`         | `string`        | ✔ Yes    | Target study ID                |
| `edc_ae_events`    | `Array<object>` | ✔ Yes    | List of EDC AE form data dicts |
| `safety_cases_xml` | `Array<string>` | `null`   | No                             | Optional raw E2B XML reports to parse |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema       |
| :---------- | :------------------ | :-------------------- |
| `200`       | Successful Response | `object`              |
| `422`       | Validation Error    | `HTTPValidationError` |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/safety/reconcile",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/safety/reconcile',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/safety/reconcile' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/sdv/bulk-sign-off`

**Summary:** Bulk Sdv Signoff
**Operation ID:** `bulk_sdv_signoff_api_v1_execution_sdv_bulk_sign_off_post`

**Clinical & GxP Rationale:**
CRA/monitor-gated bulk SDV sign-off endpoint for Field, Page, or Visit scopes.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Mandatory 60s `sig_token` required for Part 11 sign-off

#### Request Payload Schema (`application/json`)

**Schema Type:** `BulkSdvSignOffRequest`

| Field               | Type            | Required | Description                                                        |
| :------------------ | :-------------- | :------- | :----------------------------------------------------------------- |
| `study_id`          | `string`        | ✔ Yes    | Target protocol study ID                                           |
| `subject_id`        | `string`        | ✔ Yes    | Target subject ID                                                  |
| `scope`             | `string`        | ✔ Yes    | SDV scope boundary: FIELD, PAGE, or VISIT                          |
| `target_ids`        | `Array<string>` | ✔ Yes    | List of target database or artifact IDs corresponding to the scope |
| `reason_for_change` | `string`        | ✔ Yes    | Mandatory GxP 21 CFR Part 11 justification reason                  |
| `site_id`           | `string`        | `null`   | No                                                                 | Optional site identifier for the targets |
| `signing_reason`    | `string`        | No       | GxP Part 11 signature meaning or reason                            |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema          |
| :---------- | :------------------ | :----------------------- |
| `200`       | Successful Response | `BulkSdvSignOffResponse` |
| `422`       | Validation Error    | `HTTPValidationError`    |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/sdv/bulk-sign-off",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/sdv/bulk-sign-off',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/sdv/bulk-sign-off' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/sdv/signoff`

**Summary:** Sdv Signoff
**Operation ID:** `sdv_signoff_api_v1_execution_sdv_signoff_post`

**Clinical & GxP Rationale:**
CRA/monitor-gated SDV sign-off endpoint for Field, Page, or Visit scopes.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Mandatory 60s `sig_token` required for Part 11 sign-off

#### Request Payload Schema (`application/json`)

**Schema Type:** `SDVSignOffRequest`

| Field        | Type           | Required | Description |
| :----------- | :------------- | :------- | :---------- |
| `scope`      | `SDVScopeEnum` | ✔ Yes    | —           |
| `target_id`  | `string`       | ✔ Yes    | —           |
| `subject_id` | `string`       | ✔ Yes    | —           |
| `study_id`   | `string`       | ✔ Yes    | —           |
| `site_id`    | `string`       | `null`   | No          | —   |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema       |
| :---------- | :------------------ | :-------------------- |
| `200`       | Successful Response | `SDVSignOffResponse`  |
| `422`       | Validation Error    | `HTTPValidationError` |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/sdv/signoff",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/sdv/signoff',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/sdv/signoff' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/signatures/batch-sign-off`

**Summary:** Batch Signature Sign Off Endpoint
**Operation ID:** `batch_signature_sign_off_endpoint_api_v1_execution_signatures_batch_sign_off_post`

**Clinical & GxP Rationale:**
Execute 21 CFR Part 11 batch electronic signature casebook sign-off.

Requirements: PRD-SYS-001

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Mandatory 60s `sig_token` required for Part 11 sign-off

#### Request Payload Schema (`application/json`)

**Schema Type:** `BatchSignatureRequest`

| Field             | Type            | Required | Description                                          |
| :---------------- | :-------------- | :------- | :--------------------------------------------------- |
| `study_id`        | `string`        | ✔ Yes    | Target protocol study ID                             |
| `subject_id`      | `string`        | ✔ Yes    | Target subject ID                                    |
| `target_type`     | `string`        | No       | Target artifact type: FORM, CASEBOOK, DOC            |
| `target_ids`      | `Array<string>` | No       | List of target artifact IDs                          |
| `target_form_ids` | `Array<string>` | No       | List of eCRF form IDs to sign                        |
| `signing_reason`  | `string`        | ✔ Yes    | 21 CFR Part 11 signature purpose/meaning             |
| `password`        | `string`        | ✔ Yes    | Re-authentication password for identity confirmation |
| `printed_name`    | `string`        | ✔ Yes    | Printed full name of Principal Investigator          |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema          |
| :---------- | :------------------ | :----------------------- |
| `201`       | Successful Response | `BatchSignatureResponse` |
| `422`       | Validation Error    | `HTTPValidationError`    |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/signatures/batch-sign-off",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/signatures/batch-sign-off',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/signatures/batch-sign-off' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/signatures/sign`

**Summary:** Sign Payload Endpoint
**Operation ID:** `sign_payload_endpoint_api_v1_execution_signatures_sign_post`

**Clinical & GxP Rationale:**
Signs a given string payload using the secure backend PKCS#7 signer.

Requirements: PRD-SYS-001

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Mandatory 60s `sig_token` required for Part 11 sign-off

#### Request Payload Schema (`application/json`)

**Schema Type:** `SignRequest`

| Field  | Type     | Required | Description |
| :----- | :------- | :------- | :---------- |
| `data` | `string` | ✔ Yes    | —           |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema       |
| :---------- | :------------------ | :-------------------- |
| `200`       | Successful Response | `SignResponse`        |
| `422`       | Validation Error    | `HTTPValidationError` |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/signatures/sign",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/signatures/sign',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/signatures/sign' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/signatures/verify`

**Summary:** Verify Signature Endpoint
**Operation ID:** `verify_signature_endpoint_api_v1_execution_signatures_verify_post`

**Clinical & GxP Rationale:**
Verifies a PKCS#7 signed document payload using the ESignatureVerifier and trust store.

Requirements: PRD-SYS-001

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Mandatory 60s `sig_token` required for Part 11 sign-off

#### Request Payload Schema (`application/json`)

**Schema Type:** `VerifyRequest`

| Field         | Type     | Required | Description |
| :------------ | :------- | :------- | :---------- |
| `signed_data` | `string` | ✔ Yes    | —           |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema       |
| :---------- | :------------------ | :-------------------- |
| `200`       | Successful Response | `VerifyResponse`      |
| `422`       | Validation Error    | `HTTPValidationError` |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/signatures/verify",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/signatures/verify',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/signatures/verify' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/sites/{site_id}/activate`

**Summary:** Activate Site Endpoint
**Operation ID:** `activate_site_endpoint_api_v1_execution_sites__site_id__activate_post`

**Clinical & GxP Rationale:**
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name      | In     | Type     | Required | Description |
| :-------- | :----- | :------- | :------- | :---------- |
| `site_id` | `path` | `string` | ✔ Yes    | —           |

#### Request Payload Schema (`application/json`)

**Schema Type:** `SiteActivationRequest`

| Field      | Type     | Required | Description |
| :--------- | :------- | :------- | :---------- |
| `study_id` | `string` | ✔ Yes    | —           |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema       |
| :---------- | :------------------ | :-------------------- |
| `200`       | Successful Response | `object`              |
| `422`       | Validation Error    | `HTTPValidationError` |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/sites/{site_id}/activate",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/sites/{site_id}/activate',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/sites/{site_id}/activate' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `GET` `/api/v1/execution/sites/{site_id}/compliance-status`

**Summary:** Get Site Compliance Status
**Operation ID:** `get_site_compliance_status_api_v1_execution_sites__site_id__compliance_status_get`

**Clinical & GxP Rationale:**
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name        | In      | Type     | Required | Description            |
| :---------- | :------ | :------- | :------- | :--------------------- |
| `site_id`   | `path`  | `string` | ✔ Yes    | —                      |
| `study_id`  | `query` | `string` | ✔ Yes    | The clinical study ID  |
| `milestone` | `query` | `string` | No       | The milestone to check |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema            |
| :---------- | :------------------ | :------------------------- |
| `200`       | Successful Response | `ComplianceStatusResponse` |
| `422`       | Validation Error    | `HTTPValidationError`      |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/sites/{site_id}/compliance-status",
  {
    method: "GET",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/execution/api/v1/execution/sites/{site_id}/compliance-status',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/execution/api/v1/execution/sites/{site_id}/compliance-status' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/subjects`

**Summary:** Create Subject
**Operation ID:** `create_subject_api_v1_execution_subjects_post`

**Clinical & GxP Rationale:**
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `SubjectCreate`

| Field          | Type           | Required | Description |
| :------------- | :------------- | :------- | :---------- |
| `subject_id`   | `string`       | ✔ Yes    | —           |
| `study_id`     | `string`       | ✔ Yes    | —           |
| `site_id`      | `string`       | `null`   | No          | —   |
| `arm_id`       | `string`       | `null`   | No          | —   |
| `demographics` | `Demographics` | `null`   | No          | —   |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema       |
| :---------- | :------------------ | :-------------------- |
| `200`       | Successful Response | `SubjectResponse`     |
| `422`       | Validation Error    | `HTTPValidationError` |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/subjects",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/subjects',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/subjects' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `PUT` `/api/v1/execution/subjects/{id}/demographics`

**Summary:** Update Subject Demographics Endpoint
**Operation ID:** `update_subject_demographics_endpoint_api_v1_execution_subjects__id__demographics_put`

**Clinical & GxP Rationale:**
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In     | Type     | Required | Description |
| :--- | :----- | :------- | :------- | :---------- |
| `id` | `path` | `string` | ✔ Yes    | —           |

#### Request Payload Schema (`application/json`)

**Schema Type:** `SubjectDemographicsUpdateRequest`

| Field           | Type           | Required | Description |
| :-------------- | :------------- | :------- | :---------- |
| `demographics`  | `Demographics` | `null`   | No          | —   |
| `strat_factors` | `object`       | `null`   | No          | —   |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema       |
| :---------- | :------------------ | :-------------------- |
| `200`       | Successful Response | `SubjectResponse`     |
| `422`       | Validation Error    | `HTTPValidationError` |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/subjects/{id}/demographics",
  {
    method: "PUT",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.put(
        '/api/v1/execution/api/v1/execution/subjects/{id}/demographics',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X PUT 'http://localhost:8000/api/v1/execution/api/v1/execution/subjects/{id}/demographics' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `DELETE` `/api/v1/execution/subjects/{id}/demographics`

**Summary:** Delete Subject Demographics Endpoint
**Operation ID:** `delete_subject_demographics_endpoint_api_v1_execution_subjects__id__demographics_delete`

**Clinical & GxP Rationale:**
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In     | Type     | Required | Description |
| :--- | :----- | :------- | :------- | :---------- |
| `id` | `path` | `string` | ✔ Yes    | —           |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema       |
| :---------- | :------------------ | :-------------------- |
| `200`       | Successful Response | `SubjectResponse`     |
| `422`       | Validation Error    | `HTTPValidationError` |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/subjects/{id}/demographics",
  {
    method: "DELETE",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.delete(
        '/api/v1/execution/api/v1/execution/subjects/{id}/demographics',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X DELETE 'http://localhost:8000/api/v1/execution/api/v1/execution/subjects/{id}/demographics' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `PATCH` `/api/v1/execution/subjects/{id}/state`

**Summary:** Update Subject State Endpoint
**Operation ID:** `update_subject_state_endpoint_api_v1_execution_subjects__id__state_patch`

**Clinical & GxP Rationale:**
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In     | Type     | Required | Description |
| :--- | :----- | :------- | :------- | :---------- |
| `id` | `path` | `string` | ✔ Yes    | —           |

#### Request Payload Schema (`application/json`)

**Schema Type:** `SubjectStateUpdateRequest`

| Field    | Type     | Required | Description |
| :------- | :------- | :------- | :---------- |
| `status` | `string` | `null`   | No          | —   |
| `state`  | `string` | `null`   | No          | —   |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema       |
| :---------- | :------------------ | :-------------------- |
| `200`       | Successful Response | `SubjectResponse`     |
| `422`       | Validation Error    | `HTTPValidationError` |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/subjects/{id}/state",
  {
    method: "PATCH",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.patch(
        '/api/v1/execution/api/v1/execution/subjects/{id}/state',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X PATCH 'http://localhost:8000/api/v1/execution/api/v1/execution/subjects/{id}/state' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `GET` `/api/v1/execution/subjects/{subject_id}`

**Summary:** Get Subject Detail
**Operation ID:** `get_subject_detail_api_v1_execution_subjects__subject_id__get`

**Clinical & GxP Rationale:**
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name         | In     | Type     | Required | Description |
| :----------- | :----- | :------- | :------- | :---------- |
| `subject_id` | `path` | `string` | ✔ Yes    | —           |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema         |
| :---------- | :------------------ | :---------------------- |
| `200`       | Successful Response | `SubjectDetailResponse` |
| `422`       | Validation Error    | `HTTPValidationError`   |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/subjects/{subject_id}",
  {
    method: "GET",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/execution/api/v1/execution/subjects/{subject_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/execution/api/v1/execution/subjects/{subject_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/subjects/{subject_id}/consent`

**Summary:** Record Subject Consent Endpoint
**Operation ID:** `record_subject_consent_endpoint_api_v1_execution_subjects__subject_id__consent_post`

**Clinical & GxP Rationale:**
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name         | In     | Type     | Required | Description |
| :----------- | :----- | :------- | :------- | :---------- |
| `subject_id` | `path` | `string` | ✔ Yes    | —           |

#### Request Payload Schema (`application/json`)

**Schema Type:** `SubjectConsentRequest`

| Field                | Type                    | Required | Description |
| :------------------- | :---------------------- | :------- | :---------- |
| `protocol_version`   | `ProtocolVersionRefDTO` | ✔ Yes    | —           |
| `icf_signed`         | `boolean`               | ✔ Yes    | —           |
| `icf_signed_date`    | `string`                | `null`   | No          | —   |
| `requires_reconsent` | `boolean`               | No       | —           |
| `is_paper_override`  | `boolean`               | No       | —           |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema          |
| :---------- | :------------------ | :----------------------- |
| `200`       | Successful Response | `SubjectConsentResponse` |
| `422`       | Validation Error    | `HTTPValidationError`    |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/subjects/{subject_id}/consent",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/subjects/{subject_id}/consent',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/subjects/{subject_id}/consent' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/subjects/{subject_id}/consents`

**Summary:** Record Subject Consent Endpoint
**Operation ID:** `record_subject_consent_endpoint_api_v1_execution_subjects__subject_id__consents_post`

**Clinical & GxP Rationale:**
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name         | In     | Type     | Required | Description |
| :----------- | :----- | :------- | :------- | :---------- |
| `subject_id` | `path` | `string` | ✔ Yes    | —           |

#### Request Payload Schema (`application/json`)

**Schema Type:** `SubjectConsentRequest`

| Field                | Type                    | Required | Description |
| :------------------- | :---------------------- | :------- | :---------- |
| `protocol_version`   | `ProtocolVersionRefDTO` | ✔ Yes    | —           |
| `icf_signed`         | `boolean`               | ✔ Yes    | —           |
| `icf_signed_date`    | `string`                | `null`   | No          | —   |
| `requires_reconsent` | `boolean`               | No       | —           |
| `is_paper_override`  | `boolean`               | No       | —           |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema          |
| :---------- | :------------------ | :----------------------- |
| `200`       | Successful Response | `SubjectConsentResponse` |
| `422`       | Validation Error    | `HTTPValidationError`    |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/subjects/{subject_id}/consents",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/subjects/{subject_id}/consents',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/subjects/{subject_id}/consents' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/subjects/{subject_id}/randomize`

**Summary:** Randomize Subject Endpoint
**Operation ID:** `randomize_subject_endpoint_api_v1_execution_subjects__subject_id__randomize_post`

**Clinical & GxP Rationale:**
Execute GxP compliant subject randomization allocation and block-index advancement.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name         | In     | Type     | Required | Description |
| :----------- | :----- | :------- | :------- | :---------- |
| `subject_id` | `path` | `string` | ✔ Yes    | —           |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema                |
| :---------- | :------------------ | :----------------------------- |
| `200`       | Successful Response | `SubjectRandomizationResponse` |
| `422`       | Validation Error    | `HTTPValidationError`          |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/subjects/{subject_id}/randomize",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/subjects/{subject_id}/randomize',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/subjects/{subject_id}/randomize' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/subjects/{subject_id}/screening`

**Summary:** Evaluate And Transition Screening
**Operation ID:** `evaluate_and_transition_screening_api_v1_execution_subjects__subject_id__screening_post`

**Clinical & GxP Rationale:**
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name         | In     | Type     | Required | Description |
| :----------- | :----- | :------- | :------- | :---------- |
| `subject_id` | `path` | `string` | ✔ Yes    | —           |

#### Request Payload Schema (`application/json`)

**Schema Type:** `SubjectScreeningRequest` | `null`

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema            |
| :---------- | :------------------ | :------------------------- |
| `200`       | Successful Response | `SubjectScreeningResponse` |
| `422`       | Validation Error    | `HTTPValidationError`      |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/subjects/{subject_id}/screening",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/subjects/{subject_id}/screening',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/subjects/{subject_id}/screening' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/subjects/{subject_id}/unblind`

**Summary:** Unblind Subject
**Operation ID:** `unblind_subject_api_v1_execution_subjects__subject_id__unblind_post`

**Clinical & GxP Rationale:**
Execute an emergency treatment-allocation unblinding for a randomised subject.

This endpoint implements the GxP / 21 CFR Part 11 compliant emergency
unblinding workflow: it validates step-up re-authentication, performs
Shamir dual-custody reconstruction of the encrypted allocation, builds a
cryptographically signed evidence record, writes an immutable audit-log
entry, and dispatches a critical-priority dashboard notification — all
within a single atomic database transaction.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name         | In     | Type     | Required | Description |
| :----------- | :----- | :------- | :------- | :---------- |
| `subject_id` | `path` | `string` | ✔ Yes    | —           |

#### Request Payload Schema (`application/json`)

**Schema Type:** `UnblindRequest`

| Field           | Type                    | Required | Description                                                        |
| :-------------- | :---------------------- | :------- | :----------------------------------------------------------------- |
| `reason_code`   | `UnblindingReasonCode`  | ✔ Yes    | —                                                                  |
| `justification` | `string`                | ✔ Yes    | —                                                                  |
| `shares`        | `Array<CustodianShare>` | ✔ Yes    | Exactly two custodian shares are required (dual-custody contract). |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema          |
| :---------- | :------------------ | :----------------------- |
| `200`       | Successful Response | `SubjectUnblindResponse` |
| `422`       | Validation Error    | `HTTPValidationError`    |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/subjects/{subject_id}/unblind",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/subjects/{subject_id}/unblind',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/subjects/{subject_id}/unblind' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `GET` `/api/v1/execution/translation/jobs`

**Summary:** List Translation Jobs
**Operation ID:** `list_translation_jobs_api_v1_execution_translation_jobs_get`

**Clinical & GxP Rationale:**
Retrieve a list of historical translation jobs.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema                 |
| :---------- | :------------------ | :------------------------------ |
| `200`       | Successful Response | `Array<TranslationJobResponse>` |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/translation/jobs",
  {
    method: "GET",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/execution/api/v1/execution/translation/jobs',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/execution/api/v1/execution/translation/jobs' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `GET` `/api/v1/execution/translation/jobs/{job_id}`

**Summary:** Get Translation Job
**Operation ID:** `get_translation_job_api_v1_execution_translation_jobs__job_id__get`

**Clinical & GxP Rationale:**
Query the execution status, output metadata, and error messages of a single translation job by ID.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name     | In     | Type     | Required | Description |
| :------- | :----- | :------- | :------- | :---------- |
| `job_id` | `path` | `string` | ✔ Yes    | —           |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema          |
| :---------- | :------------------ | :----------------------- |
| `200`       | Successful Response | `TranslationJobResponse` |
| `422`       | Validation Error    | `HTTPValidationError`    |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/translation/jobs/{job_id}",
  {
    method: "GET",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/execution/api/v1/execution/translation/jobs/{job_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/execution/api/v1/execution/translation/jobs/{job_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/tsdv/config`

**Summary:** Create Or Update Tsdv Config
**Operation ID:** `create_or_update_tsdv_config_api_v1_execution_tsdv_config_post`

**Clinical & GxP Rationale:**
CREATE or UPDATE Targeted SDV (TSDV) configuration for a study.

Restricts config writes to CRA/Data Manager roles WITH GxP change justifications.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `TSDVConfigCreate`

| Field                            | Type                | Required | Description |
| :------------------------------- | :------------------ | :------- | :---------- |
| `study_id`                       | `string`            | ✔ Yes    | —           |
| `sampling_model`                 | `SamplingModelEnum` | ✔ Yes    | —           |
| `initial_full_sdv_subject_count` | `integer`           | No       | —           |
| `random_sample_percentage`       | `number`            | No       | —           |
| `full_sdv_domains`               | `Array<string>`     | `null`   | No          | —   |
| `safety_endpoints`               | `Array<string>`     | `null`   | No          | —   |
| `zero_sdv_domains`               | `Array<string>`     | `null`   | No          | —   |
| `trial_random_seed`              | `integer`           | `null`   | No          | —   |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema       |
| :---------- | :------------------ | :-------------------- |
| `201`       | Successful Response | `TSDVConfigResponse`  |
| `422`       | Validation Error    | `HTTPValidationError` |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/tsdv/config",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/tsdv/config',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/tsdv/config' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `GET` `/api/v1/execution/tsdv/config/{study_id}`

**Summary:** Get Tsdv Config
**Operation ID:** `get_tsdv_config_api_v1_execution_tsdv_config__study_id__get`

**Clinical & GxP Rationale:**
Retrieve existing TSDV configuration for a study.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name       | In     | Type     | Required | Description |
| :--------- | :----- | :------- | :------- | :---------- |
| `study_id` | `path` | `string` | ✔ Yes    | —           |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema       |
| :---------- | :------------------ | :-------------------- |
| `200`       | Successful Response | `TSDVConfigResponse`  |
| `422`       | Validation Error    | `HTTPValidationError` |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/tsdv/config/{study_id}",
  {
    method: "GET",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/execution/api/v1/execution/tsdv/config/{study_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/execution/api/v1/execution/tsdv/config/{study_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `GET` `/api/v1/execution/tsdv/required`

**Summary:** Evaluate Tsdv Rule
**Operation ID:** `evaluate_tsdv_rule_api_v1_execution_tsdv_required_get`

**Clinical & GxP Rationale:**
Evaluate Targeted SDV (TSDV) requirement for a given context.

Calculates deterministic sampling decisions and returns component results with an audit explanation.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name               | In      | Type      | Required | Description |
| :----------------- | :------ | :-------- | :------- | :---------- |
| `study_id`         | `query` | `string`  | ✔ Yes    | —           |
| `subject_id`       | `query` | `string`  | ✔ Yes    | —           |
| `domain`           | `query` | `string`  | `null`   | No          | —   |
| `enrollment_index` | `query` | `integer` | `null`   | No          | —   |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema          |
| :---------- | :------------------ | :----------------------- |
| `200`       | Successful Response | `TSDVEvaluationResponse` |
| `422`       | Validation Error    | `HTTPValidationError`    |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/tsdv/required",
  {
    method: "GET",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/execution/api/v1/execution/tsdv/required',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/execution/api/v1/execution/tsdv/required' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/visits`

**Summary:** Create Visit
**Operation ID:** `create_visit_api_v1_execution_visits_post`

**Clinical & GxP Rationale:**
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `VisitCreate`

| Field           | Type     | Required | Description |
| :-------------- | :------- | :------- | :---------- |
| `subject_id`    | `string` | ✔ Yes    | —           |
| `visit_name`    | `string` | ✔ Yes    | —           |
| `study_id`      | `string` | ✔ Yes    | —           |
| `visit_date`    | `string` | `null`   | No          | —   |
| `planned_date`  | `string` | `null`   | No          | —   |
| `window_start`  | `string` | `null`   | No          | —   |
| `window_end`    | `string` | `null`   | No          | —   |
| `window_status` | `string` | `null`   | No          | —   |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema       |
| :---------- | :------------------ | :-------------------- |
| `200`       | Successful Response | `VisitResponse`       |
| `422`       | Validation Error    | `HTTPValidationError` |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/visits",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/visits',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/visits' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `GET` `/api/v1/execution/visits/{visit_id}`

**Summary:** Get Visit Detail
**Operation ID:** `get_visit_detail_api_v1_execution_visits__visit_id__get`

**Clinical & GxP Rationale:**
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name       | In     | Type     | Required | Description |
| :--------- | :----- | :------- | :------- | :---------- |
| `visit_id` | `path` | `string` | ✔ Yes    | —           |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema       |
| :---------- | :------------------ | :-------------------- |
| `200`       | Successful Response | `VisitDetailResponse` |
| `422`       | Validation Error    | `HTTPValidationError` |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/visits/{visit_id}",
  {
    method: "GET",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/execution/api/v1/execution/visits/{visit_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/execution/api/v1/execution/visits/{visit_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/execution/webhooks/etmf`

**Summary:** Etmf Completeness Webhook
**Operation ID:** `etmf_completeness_webhook_api_v1_execution_webhooks_etmf_post`

**Clinical & GxP Rationale:**
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `ETMFCompletenessWebhookPayload`

| Field               | Type            | Required | Description |
| :------------------ | :-------------- | :------- | :---------- |
| `study_id`          | `string`        | ✔ Yes    | —           |
| `site_id`           | `string`        | `null`   | No          | —   |
| `milestone`         | `string`        | ✔ Yes    | —           |
| `is_complete`       | `boolean`       | ✔ Yes    | —           |
| `missing_artifacts` | `Array<string>` | No       | —           |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema       |
| :---------- | :------------------ | :-------------------- |
| `200`       | Successful Response | `object`              |
| `422`       | Validation Error    | `HTTPValidationError` |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/execution/webhooks/etmf",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/execution/webhooks/etmf',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/execution/webhooks/etmf' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/api/v1/offline/sync-batch`

**Summary:** Sync Offline Batch
**Operation ID:** `sync_offline_batch_api_v1_offline_sync_batch_post`

**Clinical & GxP Rationale:**
Ingest batch of queued offline eCRF/ePRO deltas idempotently.

Requirements: PRD-SYS-001

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `OfflineBatchSyncRequest`

| Field             | Type                      | Required | Description                                             |
| :---------------- | :------------------------ | :------- | :------------------------------------------------------ |
| `client_batch_id` | `string`                  | ✔ Yes    | Unique client-supplied batch identifier for idempotency |
| `device_id`       | `string`                  | ✔ Yes    | Identifier of the device performing the sync            |
| `deltas`          | `Array<OfflineDeltaItem>` | ✔ Yes    | List of sync deltas to process                          |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema            |
| :---------- | :------------------ | :------------------------- |
| `200`       | Successful Response | `OfflineBatchSyncResponse` |
| `422`       | Validation Error    | `HTTPValidationError`      |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/api/v1/offline/sync-batch",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/api/v1/offline/sync-batch',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/api/v1/offline/sync-batch' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `GET` `/dictionary/export`

**Summary:** Get Cdisc Export Dictionary
**Operation ID:** `get_cdisc_export_dictionary_dictionary_export_get`

**Clinical & GxP Rationale:**
Export stored clinical subject observations in CDISC ODM XML format (Dictionary API).

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name       | In      | Type     | Required | Description                     |
| :--------- | :------ | :------- | :------- | :------------------------------ |
| `study_id` | `query` | `string` | ✔ Yes    | The clinical study ID to export |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema       |
| :---------- | :------------------ | :-------------------- |
| `200`       | Successful Response | `any`                 |
| `422`       | Validation Error    | `HTTPValidationError` |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/dictionary/export",
  {
    method: "GET",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/execution/dictionary/export',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/execution/dictionary/export' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/dictionary/unit-conversion`

**Summary:** Post Unit Conversion Dictionary
**Operation ID:** `post_unit_conversion_dictionary_dictionary_unit_conversion_post`

**Clinical & GxP Rationale:**
Translate incoming values using UCUM mapping rules (Dictionary API).

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `UnitConversionRequest`

| Field       | Type     | Required | Description |
| :---------- | :------- | :------- | :---------- |
| `value`     | `number` | ✔ Yes    | —           |
| `from_unit` | `string` | ✔ Yes    | —           |
| `to_unit`   | `string` | ✔ Yes    | —           |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema          |
| :---------- | :------------------ | :----------------------- |
| `200`       | Successful Response | `UnitConversionResponse` |
| `422`       | Validation Error    | `HTTPValidationError`    |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/dictionary/unit-conversion",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/dictionary/unit-conversion',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/dictionary/unit-conversion' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `GET` `/dictionary/unit-conversion`

**Summary:** Get Unit Conversion Dictionary
**Operation ID:** `get_unit_conversion_dictionary_dictionary_unit_conversion_get`

**Clinical & GxP Rationale:**
Translate incoming values using UCUM mapping rules via GET (Dictionary API).

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name        | In      | Type     | Required | Description |
| :---------- | :------ | :------- | :------- | :---------- |
| `value`     | `query` | `number` | ✔ Yes    | —           |
| `from_unit` | `query` | `string` | ✔ Yes    | —           |
| `to_unit`   | `query` | `string` | ✔ Yes    | —           |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema          |
| :---------- | :------------------ | :----------------------- |
| `200`       | Successful Response | `UnitConversionResponse` |
| `422`       | Validation Error    | `HTTPValidationError`    |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/dictionary/unit-conversion",
  {
    method: "GET",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/execution/dictionary/unit-conversion',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/execution/dictionary/unit-conversion' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/events/study-published`

**Summary:** Study Published
**Operation ID:** `study_published_events_study_published_post`

**Clinical & GxP Rationale:**
Ingest study publication events and trigger layout generation asynchronously.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `StudyEvent`

| Field      | Type     | Required | Description |
| :--------- | :------- | :------- | :---------- |
| `study_id` | `string` | ✔ Yes    | —           |
| `payload`  | `object` | No       | —           |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema       |
| :---------- | :------------------ | :-------------------- |
| `200`       | Successful Response | `object`              |
| `422`       | Validation Error    | `HTTPValidationError` |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/events/study-published",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/events/study-published',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/events/study-published' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `GET` `/health`

**Summary:** Health Check
**Operation ID:** `health_check_health_get`

**Clinical & GxP Rationale:**
Service health check endpoint.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema |
| :---------- | :------------------ | :-------------- |
| `200`       | Successful Response | `object`        |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch("http://localhost:8000/api/v1/execution/health", {
  method: "GET",
  headers: {
    Authorization: `Bearer ${userToken}`,
    "Content-Type": "application/json",
    "X-Change-Reason": "Clinical routine data capture",
  },
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/execution/health',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/execution/health' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `PUT` `/subjects/{id}/demographics`

**Summary:** Update Subject Demographics Endpoint
**Operation ID:** `update_subject_demographics_endpoint_subjects__id__demographics_put`

**Clinical & GxP Rationale:**
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In     | Type     | Required | Description |
| :--- | :----- | :------- | :------- | :---------- |
| `id` | `path` | `string` | ✔ Yes    | —           |

#### Request Payload Schema (`application/json`)

**Schema Type:** `SubjectDemographicsUpdateRequest`

| Field           | Type           | Required | Description |
| :-------------- | :------------- | :------- | :---------- |
| `demographics`  | `Demographics` | `null`   | No          | —   |
| `strat_factors` | `object`       | `null`   | No          | —   |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema       |
| :---------- | :------------------ | :-------------------- |
| `200`       | Successful Response | `SubjectResponse`     |
| `422`       | Validation Error    | `HTTPValidationError` |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/subjects/{id}/demographics",
  {
    method: "PUT",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.put(
        '/api/v1/execution/subjects/{id}/demographics',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X PUT 'http://localhost:8000/api/v1/execution/subjects/{id}/demographics' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `DELETE` `/subjects/{id}/demographics`

**Summary:** Delete Subject Demographics Endpoint
**Operation ID:** `delete_subject_demographics_endpoint_subjects__id__demographics_delete`

**Clinical & GxP Rationale:**
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In     | Type     | Required | Description |
| :--- | :----- | :------- | :------- | :---------- |
| `id` | `path` | `string` | ✔ Yes    | —           |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema       |
| :---------- | :------------------ | :-------------------- |
| `200`       | Successful Response | `SubjectResponse`     |
| `422`       | Validation Error    | `HTTPValidationError` |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/subjects/{id}/demographics",
  {
    method: "DELETE",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.delete(
        '/api/v1/execution/subjects/{id}/demographics',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X DELETE 'http://localhost:8000/api/v1/execution/subjects/{id}/demographics' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `PATCH` `/subjects/{id}/state`

**Summary:** Update Subject State Endpoint
**Operation ID:** `update_subject_state_endpoint_subjects__id__state_patch`

**Clinical & GxP Rationale:**
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In     | Type     | Required | Description |
| :--- | :----- | :------- | :------- | :---------- |
| `id` | `path` | `string` | ✔ Yes    | —           |

#### Request Payload Schema (`application/json`)

**Schema Type:** `SubjectStateUpdateRequest`

| Field    | Type     | Required | Description |
| :------- | :------- | :------- | :---------- |
| `status` | `string` | `null`   | No          | —   |
| `state`  | `string` | `null`   | No          | —   |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema       |
| :---------- | :------------------ | :-------------------- |
| `200`       | Successful Response | `SubjectResponse`     |
| `422`       | Validation Error    | `HTTPValidationError` |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/subjects/{id}/state",
  {
    method: "PATCH",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.patch(
        '/api/v1/execution/subjects/{id}/state',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X PATCH 'http://localhost:8000/api/v1/execution/subjects/{id}/state' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `POST` `/unit-conversion`

**Summary:** Post Unit Conversion Execution
**Operation ID:** `post_unit_conversion_execution_unit_conversion_post`

**Clinical & GxP Rationale:**
Translate incoming values using UCUM mapping rules (Execution API).

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `UnitConversionRequest`

| Field       | Type     | Required | Description |
| :---------- | :------- | :------- | :---------- |
| `value`     | `number` | ✔ Yes    | —           |
| `from_unit` | `string` | ✔ Yes    | —           |
| `to_unit`   | `string` | ✔ Yes    | —           |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema          |
| :---------- | :------------------ | :----------------------- |
| `200`       | Successful Response | `UnitConversionResponse` |
| `422`       | Validation Error    | `HTTPValidationError`    |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/unit-conversion",
  {
    method: "POST",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
    body: JSON.stringify(payload),
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/execution/unit-conversion',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/execution/unit-conversion' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

### `GET` `/unit-conversion`

**Summary:** Get Unit Conversion Execution
**Operation ID:** `get_unit_conversion_execution_unit_conversion_get`

**Clinical & GxP Rationale:**
Translate incoming values using UCUM mapping rules via GET (Execution API).

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name        | In      | Type     | Required | Description |
| :---------- | :------ | :------- | :------- | :---------- |
| `value`     | `query` | `number` | ✔ Yes    | —           |
| `from_unit` | `query` | `string` | ✔ Yes    | —           |
| `to_unit`   | `query` | `string` | ✔ Yes    | —           |

#### Response Codes & Schemas

| HTTP Status | Description         | Response Schema          |
| :---------- | :------------------ | :----------------------- |
| `200`       | Successful Response | `UnitConversionResponse` |
| `422`       | Validation Error    | `HTTPValidationError`    |

#### Client Code Examples

::: code-group

```typescript [TypeScript (Fetch)]
const response = await fetch(
  "http://localhost:8000/api/v1/execution/unit-conversion",
  {
    method: "GET",
    headers: {
      Authorization: `Bearer ${userToken}`,
      "Content-Type": "application/json",
      "X-Change-Reason": "Clinical routine data capture",
    },
  }
);
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/execution/unit-conversion',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/execution/unit-conversion' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```

:::

---

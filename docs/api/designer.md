# Study Designer & MDR (USDM)

> **Functional Domain:** `Protocol & Study Design`  
> **Service Host Port:** `localhost:8001`  
> **Gateway Proxy Prefix:** `/api/v1/designer`  
> **Persistence Backend:** `Neo4j Graph Database`  

## Overview

Clinical study metadata authoring, CDISC USDM v3.0/v4.0 graph modeling, SoA matrix generation, and Biomedical Concept libraries.

---

## Endpoints Summary

| Method | Path | Summary | GxP Auth & Signatures |
| :--- | :--- | :--- | :--- |
| `POST` | [/api/admin/cache/clear](#post-api-admin-cache-clear) | Clear Cache | ✍️ Audited Write |
| `GET` | [/api/admin/cache/status](#get-api-admin-cache-status) | Cache Status | 👀 Read-Only |
| `POST` | [/api/designer/protocols/{id}/amend](#post-api-designer-protocols-id-amend) | Amend Protocol | 🔒 Step-Up Sig Token |
| `POST` | [/api/v1/designer/amendments/analyze-ripple](#post-api-v1-designer-amendments-analyze-ripple) | Analyze Ripple Effects Endpoint | 🔒 Step-Up Sig Token |
| `POST` | [/api/v1/designer/amendments/branch](#post-api-v1-designer-amendments-branch) | Branch Protocol Amendment Endpoint | 🔒 Step-Up Sig Token |
| `POST` | [/api/v1/designer/amendments/diff](#post-api-v1-designer-amendments-diff) | Compute Semantic Diff Endpoint | 🔒 Step-Up Sig Token |
| `POST` | [/api/v1/designer/amendments/dispatch-tickets](#post-api-v1-designer-amendments-dispatch-tickets) | Dispatch Operational Tickets Endpoint | 🔒 Step-Up Sig Token |
| `POST` | [/api/v1/designer/amendments/impact](#post-api-v1-designer-amendments-impact) | Compute Amendment Impact Endpoint | 🔒 Step-Up Sig Token |
| `GET` | [/api/v1/designer/amendments/{study_id}/diff](#get-api-v1-designer-amendments-study_id-diff) | Get Study Semantic Diff Endpoint | 👀 Read-Only |
| `POST` | [/api/v1/designer/cascade/propagate](#post-api-v1-designer-cascade-propagate) | Propagate Cascade Endpoint | 🔒 Step-Up Sig Token |
| `POST` | [/api/v1/designer/cohort-feasibility/evaluate](#post-api-v1-designer-cohort-feasibility-evaluate) | Trigger multi-criteria cohort reasoning and synthetic feasibility simulation | 🔒 Step-Up Sig Token |
| `GET` | [/api/v1/designer/cohort-feasibility/jobs/{job_id}](#get-api-v1-designer-cohort-feasibility-jobs-job_id) | Poll status and results of an asynchronous cohort feasibility job | 👀 Read-Only |
| `POST` | [/api/v1/designer/cohort-feasibility/synthetic-simulate](#post-api-v1-designer-cohort-feasibility-synthetic-simulate) | Simulate cohort feasibility under missing data scenarios using 3-valued logic | 🔒 Step-Up Sig Token |
| `PATCH` | [/api/v1/designer/comments/{comment_id}/resolve](#patch-api-v1-designer-comments-comment_id-resolve) | Resolve Comment | 🔒 Step-Up Sig Token |
| `POST` | [/api/v1/designer/digitization/dag/jobs](#post-api-v1-designer-digitization-dag-jobs) | Start Digitization Dag Job | 🔒 Step-Up Sig Token |
| `GET` | [/api/v1/designer/digitization/dag/jobs/{job_id}](#get-api-v1-designer-digitization-dag-jobs-job_id) | Get Digitization Dag Job Status | 👀 Read-Only |
| `POST` | [/api/v1/designer/digitization/dag/jobs/{job_id}/compile-usdm](#post-api-v1-designer-digitization-dag-jobs-job_id-compile-usdm) | Compile Usdm From Dag Job | 🔒 Step-Up Sig Token |
| `POST` | [/api/v1/designer/digitization/dag/jobs/{job_id}/resume](#post-api-v1-designer-digitization-dag-jobs-job_id-resume) | Resume Digitization Dag Job | 🔒 Step-Up Sig Token |
| `POST` | [/api/v1/designer/digitization/dag/jobs/{job_id}/stages/{stage}/correct](#post-api-v1-designer-digitization-dag-jobs-job_id-stages-stage-correct) | Submit Stage Correction Endpoint | 🔒 Step-Up Sig Token |
| `POST` | [/api/v1/designer/digitization/extract](#post-api-v1-designer-digitization-extract) | Extract Protocol Digitization | 🔒 Step-Up Sig Token |
| `GET` | [/api/v1/designer/digitization/sample](#get-api-v1-designer-digitization-sample) | Get Sample Protocol Extraction | 👀 Read-Only |
| `GET` | [/api/v1/designer/export/m11/{study_id}](#get-api-v1-designer-export-m11-study_id) | Export Protocol M11 Endpoint | 👀 Read-Only |
| `GET` | [/api/v1/designer/forms/{form_id}/comments](#get-api-v1-designer-forms-form_id-comments) | Get Form Comments | 👀 Read-Only |
| `POST` | [/api/v1/designer/forms/{form_id}/comments](#post-api-v1-designer-forms-form_id-comments) | Post Form Comment | 🔒 Step-Up Sig Token |
| `GET` | [/api/v1/designer/ingestion/candidates/{candidate_id}](#get-api-v1-designer-ingestion-candidates-candidate_id) | Get Ingestion Candidate | 👀 Read-Only |
| `POST` | [/api/v1/designer/ingestion/candidates/{candidate_id}/items/{item_id}/transition](#post-api-v1-designer-ingestion-candidates-candidate_id-items-item_id-transition) | Transition Ingestion Item | 🔒 Step-Up Sig Token |
| `POST` | [/api/v1/designer/ingestion/candidates/{candidate_id}/promote](#post-api-v1-designer-ingestion-candidates-candidate_id-promote) | Promote Ingestion Candidate | 🔒 Step-Up Sig Token |
| `GET` | [/api/v1/designer/ingestion/jobs/{job_id}](#get-api-v1-designer-ingestion-jobs-job_id) | Get Ingestion Job Status | 👀 Read-Only |
| `POST` | [/api/v1/designer/ingestion/upload](#post-api-v1-designer-ingestion-upload) | Upload Protocol Ingestion | 🔒 Step-Up Sig Token |
| `GET` | [/api/v1/designer/library/smart-blocks](#get-api-v1-designer-library-smart-blocks) | List Smart Blocks | 👀 Read-Only |
| `POST` | [/api/v1/designer/library/smart-blocks/{block_id}/instantiate](#post-api-v1-designer-library-smart-blocks-block_id-instantiate) | Instantiate Smart Block | 🔒 Step-Up Sig Token |
| `POST` | [/api/v1/designer/round-trip](#post-api-v1-designer-round-trip) | Run Round Trip Endpoint | 🔒 Step-Up Sig Token |
| `POST` | [/api/v1/designer/sentinel/evaluate](#post-api-v1-designer-sentinel-evaluate) | Evaluate Protocol Quality Endpoint | 🔒 Step-Up Sig Token |
| `POST` | [/api/v1/designer/startup-matrix/generate-template](#post-api-v1-designer-startup-matrix-generate-template) | Generate Startup Template Endpoint | 🔒 Step-Up Sig Token |
| `GET` | [/api/v1/designer/startup-matrix/jurisdictions](#get-api-v1-designer-startup-matrix-jurisdictions) | Get Supported Jurisdictions Endpoint | 👀 Read-Only |
| `GET` | [/api/v1/designer/startup-matrix/presets](#get-api-v1-designer-startup-matrix-presets) | Get Startup Presets Endpoint | 👀 Read-Only |
| `POST` | [/api/v1/designer/studies/{study_id}/amendments/diff](#post-api-v1-designer-studies-study_id-amendments-diff) | Calculate Amendment Diff | 🔒 Step-Up Sig Token |
| `POST` | [/api/v1/designer/studies/{study_id}/commit-usdm](#post-api-v1-designer-studies-study_id-commit-usdm) | Commit Usdm To Study | 🔒 Step-Up Sig Token |
| `POST` | [/api/v1/designer/studies/{study_id}/forms/compile](#post-api-v1-designer-studies-study_id-forms-compile) | Compile Form Schema | 🔒 Step-Up Sig Token |
| `POST` | [/api/v1/designer/studies/{study_id}/forms/{form_id}/publish](#post-api-v1-designer-studies-study_id-forms-form_id-publish) | Publish And Seal Form | 🔒 Step-Up Sig Token |
| `POST` | [/api/v1/designer/studies/{study_id}/forms/{form_id}/uat/execute](#post-api-v1-designer-studies-study_id-forms-form_id-uat-execute) | Execute Uat Simulation | 🔒 Step-Up Sig Token |
| `POST` | [/api/v1/designer/usdm/validate](#post-api-v1-designer-usdm-validate) | Validate Usdm Endpoint | 🔒 Step-Up Sig Token |
| `POST` | [/api/v1/mappings/upload](#post-api-v1-mappings-upload) | Upload Mapping Csv | ✍️ Audited Write |
| `GET` | [/api/v1/mdr/concepts](#get-api-v1-mdr-concepts) | Get Concepts | 👀 Read-Only |
| `POST` | [/api/v1/mdr/concepts](#post-api-v1-mdr-concepts) | Create Concept | ✍️ Audited Write |
| `PUT` | [/api/v1/mdr/concepts/{id}](#put-api-v1-mdr-concepts-id) | Update Concept | ✍️ Audited Write |
| `DELETE` | [/api/v1/mdr/concepts/{id}](#delete-api-v1-mdr-concepts-id) | Delete Concept | ✍️ Audited Write |
| `POST` | [/api/v1/mdr/concepts/{id}/rename](#post-api-v1-mdr-concepts-id-rename) | Rename Concept | ✍️ Audited Write |
| `POST` | [/api/v1/mdr/library](#post-api-v1-mdr-library) | Create Library Object Endpoint | ✍️ Audited Write |
| `GET` | [/api/v1/mdr/library](#get-api-v1-mdr-library) | List Library Objects Endpoint | 👀 Read-Only |
| `GET` | [/api/v1/mdr/library/{id}](#get-api-v1-mdr-library-id) | Get Library Object Endpoint | 👀 Read-Only |
| `PUT` | [/api/v1/mdr/library/{id}](#put-api-v1-mdr-library-id) | Update Library Object Endpoint | ✍️ Audited Write |
| `POST` | [/api/v1/mdr/library/{id}/amend](#post-api-v1-mdr-library-id-amend) | Amend Library Object Endpoint | ✍️ Audited Write |
| `GET` | [/api/v1/mdr/library/{id}/history](#get-api-v1-mdr-library-id-history) | Get Library Object History Endpoint | 👀 Read-Only |
| `POST` | [/api/v1/mdr/library/{id}/transition](#post-api-v1-mdr-library-id-transition) | Transition Library Object Endpoint | ✍️ Audited Write |
| `GET` | [/api/v1/studies/{study_id}](#get-api-v1-studies-study_id) | Get Legacy Study | 👀 Read-Only |
| `GET` | [/api/v1/studies/{study_id}/alignment-validation](#get-api-v1-studies-study_id-alignment-validation) | Validate Study Alignment | 👀 Read-Only |
| `POST` | [/api/v1/studies/{study_id}/blocks/{block_id}/suggestions](#post-api-v1-studies-study_id-blocks-block_id-suggestions) | Create Suggestion Endpoint | ✍️ Audited Write |
| `GET` | [/api/v1/studies/{study_id}/blocks/{block_id}/suggestions](#get-api-v1-studies-study_id-blocks-block_id-suggestions) | Get Suggestions Endpoint | 👀 Read-Only |
| `GET` | [/api/v1/studies/{study_id}/ct-validation](#get-api-v1-studies-study_id-ct-validation) | Validate Study Ct Endpoint | 👀 Read-Only |
| `GET` | [/api/v1/studies/{study_id}/differences](#get-api-v1-studies-study_id-differences) | Study Differences | 👀 Read-Only |
| `GET` | [/api/v1/studies/{study_id}/eligibility-criteria](#get-api-v1-studies-study_id-eligibility-criteria) | List Eligibility Criteria | 👀 Read-Only |
| `POST` | [/api/v1/studies/{study_id}/eligibility-criteria](#post-api-v1-studies-study_id-eligibility-criteria) | Create Eligibility Criterion Endpoint | ✍️ Audited Write |
| `GET` | [/api/v1/studies/{study_id}/eligibility-criteria/{criterion_id}](#get-api-v1-studies-study_id-eligibility-criteria-criterion_id) | Get Eligibility Criterion Detail | 👀 Read-Only |
| `PUT` | [/api/v1/studies/{study_id}/eligibility-criteria/{criterion_id}](#put-api-v1-studies-study_id-eligibility-criteria-criterion_id) | Update Eligibility Criterion Endpoint | ✍️ Audited Write |
| `GET` | [/api/v1/studies/{study_id}/export](#get-api-v1-studies-study_id-export) | Export Protocol | 👀 Read-Only |
| `POST` | [/api/v1/studies/{study_id}/library-instances](#post-api-v1-studies-study_id-library-instances) | Instantiate Library Object Endpoint | ✍️ Audited Write |
| `PUT` | [/api/v1/studies/{study_id}/library-instances/{instance_id}](#put-api-v1-studies-study_id-library-instances-instance_id) | Update Library Instance Endpoint | ✍️ Audited Write |
| `GET` | [/api/v1/studies/{study_id}/library-instances/{instance_id}/diff](#get-api-v1-studies-study_id-library-instances-instance_id-diff) | Get Library Instance Diff Endpoint | 👀 Read-Only |
| `GET` | [/api/v1/studies/{study_id}/rules](#get-api-v1-studies-study_id-rules) | Get Study Rules | 👀 Read-Only |
| `POST` | [/api/v1/studies/{study_id}/rules](#post-api-v1-studies-study_id-rules) | Create Study Rule | ✍️ Audited Write |
| `POST` | [/api/v1/studies/{study_id}/rules/preview](#post-api-v1-studies-study_id-rules-preview) | Compile Preview Rule | ✍️ Audited Write |
| `POST` | [/api/v1/studies/{study_id}/rules/validate](#post-api-v1-studies-study_id-rules-validate) | Compile Validate Rule | ✍️ Audited Write |
| `GET` | [/api/v1/studies/{study_id}/rules/{rule_id}](#get-api-v1-studies-study_id-rules-rule_id) | Get Study Rule By Id | 👀 Read-Only |
| `PUT` | [/api/v1/studies/{study_id}/rules/{rule_id}](#put-api-v1-studies-study_id-rules-rule_id) | Update Study Rule By Id | ✍️ Audited Write |
| `DELETE` | [/api/v1/studies/{study_id}/rules/{rule_id}](#delete-api-v1-studies-study_id-rules-rule_id) | Delete Study Rule By Id | ✍️ Audited Write |
| `GET` | [/api/v1/studies/{study_id}/sections/{section_id}/status](#get-api-v1-studies-study_id-sections-section_id-status) | Get Section Review Status | 👀 Read-Only |
| `POST` | [/api/v1/studies/{study_id}/sections/{section_id}/threads](#post-api-v1-studies-study_id-sections-section_id-threads) | Create Thread Endpoint | ✍️ Audited Write |
| `GET` | [/api/v1/studies/{study_id}/sections/{section_id}/threads](#get-api-v1-studies-study_id-sections-section_id-threads) | Get Threads Endpoint | 👀 Read-Only |
| `POST` | [/api/v1/studies/{study_id}/sections/{section_id}/transition](#post-api-v1-studies-study_id-sections-section_id-transition) | Transition Section | ✍️ Audited Write |
| `POST` | [/api/v1/studies/{study_id}/suggestions/{suggestion_id}/decision](#post-api-v1-studies-study_id-suggestions-suggestion_id-decision) | Decide Suggestion Endpoint | ✍️ Audited Write |
| `GET` | [/api/v1/studies/{study_id}/terminology-validation](#get-api-v1-studies-study_id-terminology-validation) | Validate Study Terminology Endpoint | 👀 Read-Only |
| `POST` | [/api/v1/studies/{study_id}/threads/{thread_id}/comments](#post-api-v1-studies-study_id-threads-thread_id-comments) | Add Comment Endpoint | ✍️ Audited Write |
| `POST` | [/api/v1/studies/{study_id}/threads/{thread_id}/resolve](#post-api-v1-studies-study_id-threads-thread_id-resolve) | Resolve Thread Endpoint | ✍️ Audited Write |
| `POST` | [/api/v1/studies/{study_id}/versions](#post-api-v1-studies-study_id-versions) | Post Study Version | ✍️ Audited Write |
| `GET` | [/api/v1/studies/{study_id}/versions/diff](#get-api-v1-studies-study_id-versions-diff) | Get Versions Diff Endpoint | 👀 Read-Only |
| `POST` | [/api/v1/studies/{study_id}/versions/{version_id}/approve](#post-api-v1-studies-study_id-versions-version_id-approve) | Approve Study Version Endpoint | ✍️ Audited Write |
| `POST` | [/api/v1/studies/{study_id}/versions/{version_id}/arms](#post-api-v1-studies-study_id-versions-version_id-arms) | Create Arm Endpoint | ✍️ Audited Write |
| `GET` | [/api/v1/studies/{study_id}/versions/{version_id}/arms](#get-api-v1-studies-study_id-versions-version_id-arms) | List Arms Endpoint | 👀 Read-Only |
| `POST` | [/api/v1/studies/{study_id}/versions/{version_id}/arms/reorder](#post-api-v1-studies-study_id-versions-version_id-arms-reorder) | Reorder Arms Endpoint | ✍️ Audited Write |
| `DELETE` | [/api/v1/studies/{study_id}/versions/{version_id}/arms/{arm_id}](#delete-api-v1-studies-study_id-versions-version_id-arms-arm_id) | Retire Arm Endpoint | ✍️ Audited Write |
| `GET` | [/api/v1/studies/{study_id}/versions/{version_id}/arms/{arm_id}](#get-api-v1-studies-study_id-versions-version_id-arms-arm_id) | Get Arm Endpoint | 👀 Read-Only |
| `PUT` | [/api/v1/studies/{study_id}/versions/{version_id}/arms/{arm_id}](#put-api-v1-studies-study_id-versions-version_id-arms-arm_id) | Update Arm Endpoint | ✍️ Audited Write |
| `POST` | [/api/v1/studies/{study_id}/versions/{version_id}/assignments/activities](#post-api-v1-studies-study_id-versions-version_id-assignments-activities) | Assign Activities To Visit Endpoint | 🔒 Step-Up Sig Token |
| `POST` | [/api/v1/studies/{study_id}/versions/{version_id}/assignments/arms](#post-api-v1-studies-study_id-versions-version_id-assignments-arms) | Assign Visits To Arm Endpoint | 🔒 Step-Up Sig Token |
| `POST` | [/api/v1/studies/{study_id}/versions/{version_id}/assignments/epochs](#post-api-v1-studies-study_id-versions-version_id-assignments-epochs) | Assign Visits To Epoch Endpoint | 🔒 Step-Up Sig Token |
| `POST` | [/api/v1/studies/{study_id}/versions/{version_id}/blocks](#post-api-v1-studies-study_id-versions-version_id-blocks) | Create Block Endpoint | ✍️ Audited Write |
| `GET` | [/api/v1/studies/{study_id}/versions/{version_id}/blocks](#get-api-v1-studies-study_id-versions-version_id-blocks) | List Blocks Endpoint | 👀 Read-Only |
| `POST` | [/api/v1/studies/{study_id}/versions/{version_id}/blocks/reorder](#post-api-v1-studies-study_id-versions-version_id-blocks-reorder) | Reorder Blocks Endpoint | ✍️ Audited Write |
| `PUT` | [/api/v1/studies/{study_id}/versions/{version_id}/blocks/{block_id}](#put-api-v1-studies-study_id-versions-version_id-blocks-block_id) | Update Block Endpoint | ✍️ Audited Write |
| `DELETE` | [/api/v1/studies/{study_id}/versions/{version_id}/blocks/{block_id}](#delete-api-v1-studies-study_id-versions-version_id-blocks-block_id) | Delete Block Endpoint | ✍️ Audited Write |
| `GET` | [/api/v1/studies/{study_id}/versions/{version_id}/blocks/{block_id}](#get-api-v1-studies-study_id-versions-version_id-blocks-block_id) | Get Block Endpoint | 👀 Read-Only |
| `POST` | [/api/v1/studies/{study_id}/versions/{version_id}/epochs](#post-api-v1-studies-study_id-versions-version_id-epochs) | Create Epoch Endpoint | ✍️ Audited Write |
| `GET` | [/api/v1/studies/{study_id}/versions/{version_id}/epochs](#get-api-v1-studies-study_id-versions-version_id-epochs) | List Epochs Endpoint | 👀 Read-Only |
| `POST` | [/api/v1/studies/{study_id}/versions/{version_id}/epochs/reorder](#post-api-v1-studies-study_id-versions-version_id-epochs-reorder) | Reorder Epochs Endpoint | ✍️ Audited Write |
| `DELETE` | [/api/v1/studies/{study_id}/versions/{version_id}/epochs/{epoch_id}](#delete-api-v1-studies-study_id-versions-version_id-epochs-epoch_id) | Retire Epoch Endpoint | ✍️ Audited Write |
| `GET` | [/api/v1/studies/{study_id}/versions/{version_id}/epochs/{epoch_id}](#get-api-v1-studies-study_id-versions-version_id-epochs-epoch_id) | Get Epoch Endpoint | 👀 Read-Only |
| `PUT` | [/api/v1/studies/{study_id}/versions/{version_id}/epochs/{epoch_id}](#put-api-v1-studies-study_id-versions-version_id-epochs-epoch_id) | Update Epoch Endpoint | ✍️ Audited Write |
| `DELETE` | [/api/v1/studies/{study_id}/versions/{version_id}/links/arm-applicability](#delete-api-v1-studies-study_id-versions-version_id-links-arm-applicability) | Retire Arm Applicability Endpoint | ✍️ Audited Write |
| `POST` | [/api/v1/studies/{study_id}/versions/{version_id}/links/arm-applicability](#post-api-v1-studies-study_id-versions-version_id-links-arm-applicability) | Link Arm Applicability Endpoint | ✍️ Audited Write |
| `DELETE` | [/api/v1/studies/{study_id}/versions/{version_id}/links/epoch-visit](#delete-api-v1-studies-study_id-versions-version_id-links-epoch-visit) | Retire Epoch Visit Endpoint | ✍️ Audited Write |
| `POST` | [/api/v1/studies/{study_id}/versions/{version_id}/links/epoch-visit](#post-api-v1-studies-study_id-versions-version_id-links-epoch-visit) | Link Epoch Visit Endpoint | ✍️ Audited Write |
| `DELETE` | [/api/v1/studies/{study_id}/versions/{version_id}/links/timing](#delete-api-v1-studies-study_id-versions-version_id-links-timing) | Retire Timing Endpoint | ✍️ Audited Write |
| `POST` | [/api/v1/studies/{study_id}/versions/{version_id}/links/timing](#post-api-v1-studies-study_id-versions-version_id-links-timing) | Link Timing Endpoint | ✍️ Audited Write |
| `DELETE` | [/api/v1/studies/{study_id}/versions/{version_id}/links/visit-procedure](#delete-api-v1-studies-study_id-versions-version_id-links-visit-procedure) | Retire Visit Procedure Endpoint | ✍️ Audited Write |
| `POST` | [/api/v1/studies/{study_id}/versions/{version_id}/links/visit-procedure](#post-api-v1-studies-study_id-versions-version_id-links-visit-procedure) | Link Visit Procedure Endpoint | ✍️ Audited Write |
| `POST` | [/api/v1/studies/{study_id}/versions/{version_id}/procedures](#post-api-v1-studies-study_id-versions-version_id-procedures) | Create Procedure Endpoint | ✍️ Audited Write |
| `GET` | [/api/v1/studies/{study_id}/versions/{version_id}/procedures](#get-api-v1-studies-study_id-versions-version_id-procedures) | List Procedures Endpoint | 👀 Read-Only |
| `POST` | [/api/v1/studies/{study_id}/versions/{version_id}/procedures/reorder](#post-api-v1-studies-study_id-versions-version_id-procedures-reorder) | Reorder Procedures Endpoint | ✍️ Audited Write |
| `DELETE` | [/api/v1/studies/{study_id}/versions/{version_id}/procedures/{procedure_id}](#delete-api-v1-studies-study_id-versions-version_id-procedures-procedure_id) | Retire Procedure Endpoint | ✍️ Audited Write |
| `GET` | [/api/v1/studies/{study_id}/versions/{version_id}/procedures/{procedure_id}](#get-api-v1-studies-study_id-versions-version_id-procedures-procedure_id) | Get Procedure Endpoint | 👀 Read-Only |
| `PUT` | [/api/v1/studies/{study_id}/versions/{version_id}/procedures/{procedure_id}](#put-api-v1-studies-study_id-versions-version_id-procedures-procedure_id) | Update Procedure Endpoint | ✍️ Audited Write |
| `POST` | [/api/v1/studies/{study_id}/versions/{version_id}/publish](#post-api-v1-studies-study_id-versions-version_id-publish) | Publish Study Version Endpoint | ✍️ Audited Write |
| `POST` | [/api/v1/studies/{study_id}/versions/{version_id}/sign-off](#post-api-v1-studies-study_id-versions-version_id-sign-off) | Approve Study Version Endpoint | 🔒 Step-Up Sig Token |
| `GET` | [/api/v1/studies/{study_id}/versions/{version_id}/soa-projection](#get-api-v1-studies-study_id-versions-version_id-soa-projection) | Get Soa Projection Endpoint | 👀 Read-Only |
| `POST` | [/api/v1/studies/{study_id}/versions/{version_id}/timing-windows](#post-api-v1-studies-study_id-versions-version_id-timing-windows) | Create Timing Window Endpoint | ✍️ Audited Write |
| `GET` | [/api/v1/studies/{study_id}/versions/{version_id}/timing-windows](#get-api-v1-studies-study_id-versions-version_id-timing-windows) | List Timing Windows Endpoint | 👀 Read-Only |
| `DELETE` | [/api/v1/studies/{study_id}/versions/{version_id}/timing-windows/{timing_id}](#delete-api-v1-studies-study_id-versions-version_id-timing-windows-timing_id) | Retire Timing Window Endpoint | ✍️ Audited Write |
| `GET` | [/api/v1/studies/{study_id}/versions/{version_id}/timing-windows/{timing_id}](#get-api-v1-studies-study_id-versions-version_id-timing-windows-timing_id) | Get Timing Window Endpoint | 👀 Read-Only |
| `PUT` | [/api/v1/studies/{study_id}/versions/{version_id}/timing-windows/{timing_id}](#put-api-v1-studies-study_id-versions-version_id-timing-windows-timing_id) | Update Timing Window Endpoint | ✍️ Audited Write |
| `POST` | [/api/v1/studies/{study_id}/versions/{version_id}/visits](#post-api-v1-studies-study_id-versions-version_id-visits) | Create Visit Endpoint | ✍️ Audited Write |
| `GET` | [/api/v1/studies/{study_id}/versions/{version_id}/visits](#get-api-v1-studies-study_id-versions-version_id-visits) | List Visits Endpoint | 👀 Read-Only |
| `POST` | [/api/v1/studies/{study_id}/versions/{version_id}/visits/reorder](#post-api-v1-studies-study_id-versions-version_id-visits-reorder) | Reorder Visits Endpoint | ✍️ Audited Write |
| `DELETE` | [/api/v1/studies/{study_id}/versions/{version_id}/visits/{visit_id}](#delete-api-v1-studies-study_id-versions-version_id-visits-visit_id) | Retire Visit Endpoint | ✍️ Audited Write |
| `GET` | [/api/v1/studies/{study_id}/versions/{version_id}/visits/{visit_id}](#get-api-v1-studies-study_id-versions-version_id-visits-visit_id) | Get Visit Endpoint | 👀 Read-Only |
| `PUT` | [/api/v1/studies/{study_id}/versions/{version_id}/visits/{visit_id}](#put-api-v1-studies-study_id-versions-version_id-visits-visit_id) | Update Visit Endpoint | ✍️ Audited Write |
| `POST` | [/api/v1/synopsis/export](#post-api-v1-synopsis-export) | Export Synopsis Document | ✍️ Audited Write |
| `GET` | [/api/v1/synopsis/render/{study_id}](#get-api-v1-synopsis-render-study_id) | Render Synopsis Download | 👀 Read-Only |
| `GET` | [/api/v1/terminology/search](#get-api-v1-terminology-search) | Search Terminology | 👀 Read-Only |
| `GET` | [/api/v1/terminology/validate/{code}](#get-api-v1-terminology-validate-code) | Validate Single Code | 👀 Read-Only |
| `GET` | [/api/v2/studies/{study_id}/usdm](#get-api-v2-studies-study_id-usdm) | Get Usdm Study | 👀 Read-Only |
| `POST` | [/api/v2/studies/{study_id}/usdm](#post-api-v2-studies-study_id-usdm) | Import Usdm Study | ✍️ Audited Write |
| `GET` | [/health](#get-health) | Health Check | 👀 Read-Only |

---

## Granular Endpoint Specifications

### `POST` `/api/admin/cache/clear`

**Summary:** Clear Cache  
**Operation ID:** `clear_cache_api_admin_cache_clear_post`  

**Clinical & GxP Rationale:**  
Flushes the controlled terminology cache.

Returns:
    Dict[str, str]: A success message indicating the cache was cleared.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `object` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/admin/cache/clear', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/designer/api/admin/cache/clear',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/designer/api/admin/cache/clear' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/admin/cache/status`

**Summary:** Cache Status  
**Operation ID:** `cache_status_api_admin_cache_status_get`  

**Clinical & GxP Rationale:**  
Returns the current size and status of the terminology cache.

Returns:
    Dict[str, int]: The status dictionary containing size and max_size.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `object` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/admin/cache/status', {
  method: 'GET',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/designer/api/admin/cache/status',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/designer/api/admin/cache/status' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/designer/protocols/{id}/amend`

**Summary:** Amend Protocol  
**Operation ID:** `amend_protocol_api_designer_protocols__id__amend_post`  

**Clinical & GxP Rationale:**  
Exposes POST /api/designer/protocols/{id}/amend with 201, new_version, status, and parent_version.
Creates a transaction-safe DRAFT successor with incremented version index.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Mandatory 60s `sig_token` required for Part 11 sign-off

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `ProtocolAmendRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `amendment_type` | `string` | `null` | No | — |
| `type` | `string` | `null` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `object` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/designer/protocols/{id}/amend', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/designer/api/designer/protocols/{id}/amend',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/designer/api/designer/protocols/{id}/amend' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/designer/amendments/analyze-ripple`

**Summary:** Analyze Ripple Effects Endpoint  
**Operation ID:** `analyze_ripple_effects_endpoint_api_v1_designer_amendments_analyze_ripple_post`  

**Clinical & GxP Rationale:**  
Analyze ripple-effect deltas across USDM graph, SoA matrix, and narrative to generate ProtocolImpactAssessment.

Requirements: PRD-SYS-001, PRD-SUB-007, PRD-SYS-051

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Mandatory 60s `sig_token` required for Part 11 sign-off

#### Request Payload Schema (`application/json`)

**Schema Type:** `RippleAnalysisRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `study_id` | `string` | ✔ Yes | Target study ID |
| `base_version_tag` | `string` | No | Base protocol version |
| `amended_version_tag` | `string` | No | Amended protocol version |
| `amendment_type` | `string` | No | 'major' or 'minor' |
| `requires_reconsent` | `boolean` | `null` | No | Explicit override for re-consent requirement |
| `base_payload` | `object` | `null` | No | Explicit base protocol payload dictionary |
| `draft_payload` | `object` | `null` | No | Explicit amended protocol payload dictionary |
| `active_subject_ids` | `Array<string>` | `null` | No | Optional explicit active cohort subject IDs to evaluate for gating |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `ProtocolImpactAssessment-Output` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/designer/amendments/analyze-ripple', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/designer/api/v1/designer/amendments/analyze-ripple',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/designer/api/v1/designer/amendments/analyze-ripple' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/designer/amendments/branch`

**Summary:** Branch Protocol Amendment Endpoint  
**Operation ID:** `branch_protocol_amendment_endpoint_api_v1_designer_amendments_branch_post`  

**Clinical & GxP Rationale:**  
Create an isolated, immutable protocol amendment working branch from an approved baseline.

Requirements: PRD-SYS-001, PRD-SUB-007

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Mandatory 60s `sig_token` required for Part 11 sign-off

#### Request Payload Schema (`application/json`)

**Schema Type:** `BranchAmendmentRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `study_id` | `string` | ✔ Yes | Target study ID |
| `base_version_tag` | `string` | No | Base approved version tag to branch from |
| `amendment_type` | `string` | No | Amendment classification: 'major' or 'minor' |
| `requires_reconsent` | `boolean` | No | Flag indicating if subject re-consent is mandated |
| `change_reason` | `string` | ✔ Yes | GxP 21 CFR Part 11 change justification reason |
| `branch_name` | `string` | `null` | No | Optional custom branch name (e.g. amendment-v2.0-draft) |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `BranchAmendmentResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/designer/amendments/branch', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/designer/api/v1/designer/amendments/branch',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/designer/api/v1/designer/amendments/branch' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/designer/amendments/diff`

**Summary:** Compute Semantic Diff Endpoint  
**Operation ID:** `compute_semantic_diff_endpoint_api_v1_designer_amendments_diff_post`  

**Clinical & GxP Rationale:**  
Compute multi-layer visual and semantic protocol diff across USDM Graph, SoA, Eligibility, and eCRF forms.

Requirements: PRD-SYS-001, PRD-SUB-007

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Mandatory 60s `sig_token` required for Part 11 sign-off

#### Request Payload Schema (`application/json`)

**Schema Type:** `SemanticDiffRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `study_id` | `string` | ✔ Yes | Protocol study identifier |
| `base_version_tag` | `string` | ✔ Yes | Base version tag |
| `amended_version_tag` | `string` | ✔ Yes | Amended target version tag |
| `base_payload` | `object` | `null` | No | Optional explicit base protocol payload |
| `draft_payload` | `object` | `null` | No | Optional explicit amended protocol payload |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `SemanticDiffResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/designer/amendments/diff', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/designer/api/v1/designer/amendments/diff',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/designer/api/v1/designer/amendments/diff' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/designer/amendments/dispatch-tickets`

**Summary:** Dispatch Operational Tickets Endpoint  
**Operation ID:** `dispatch_operational_tickets_endpoint_api_v1_designer_amendments_dispatch_tickets_post`  

**Clinical & GxP Rationale:**  
Generate and dispatch multi-domain operational tickets for a protocol amendment to apps/tickets.

Requirements: PRD-SYS-001, PRD-SUB-007, PRD-SYS-051

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Mandatory 60s `sig_token` required for Part 11 sign-off

#### Request Payload Schema (`application/json`)

**Schema Type:** `TicketDispatchRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `study_id` | `string` | ✔ Yes | Target study ID |
| `impact_assessment` | `ProtocolImpactAssessment-Input` | `null` | No | Optional pre-calculated impact assessment |
| `base_version_tag` | `string` | `null` | No | Base protocol version |
| `amended_version_tag` | `string` | `null` | No | Amended protocol version |
| `base_payload` | `object` | `null` | No | Base payload |
| `draft_payload` | `object` | `null` | No | Amended payload |
| `selected_domain_queues` | `Array<DomainQueue>` | `null` | No | Optional subset of domain queues to dispatch |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `TicketDispatchResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/designer/amendments/dispatch-tickets', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/designer/api/v1/designer/amendments/dispatch-tickets',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/designer/api/v1/designer/amendments/dispatch-tickets' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/designer/amendments/impact`

**Summary:** Compute Amendment Impact Endpoint  
**Operation ID:** `compute_amendment_impact_endpoint_api_v1_designer_amendments_impact_post`  

**Clinical & GxP Rationale:**  
Compute quantitative Amendment Impact Summary (burden delta, affected visits, schema revisions).

Requirements: PRD-SYS-001, PRD-SUB-007

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Mandatory 60s `sig_token` required for Part 11 sign-off

#### Request Payload Schema (`application/json`)

**Schema Type:** `SemanticDiffRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `study_id` | `string` | ✔ Yes | Protocol study identifier |
| `base_version_tag` | `string` | ✔ Yes | Base version tag |
| `amended_version_tag` | `string` | ✔ Yes | Amended target version tag |
| `base_payload` | `object` | `null` | No | Optional explicit base protocol payload |
| `draft_payload` | `object` | `null` | No | Optional explicit amended protocol payload |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `AmendmentImpactSummary` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/designer/amendments/impact', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/designer/api/v1/designer/amendments/impact',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/designer/api/v1/designer/amendments/impact' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/designer/amendments/{study_id}/diff`

**Summary:** Get Study Semantic Diff Endpoint  
**Operation ID:** `get_study_semantic_diff_endpoint_api_v1_designer_amendments__study_id__diff_get`  

**Clinical & GxP Rationale:**  
Get multi-layer semantic diff for a study between two version tags.

Requirements: PRD-SYS-001, PRD-SUB-007

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |
| `base_version` | `query` | `string` | No | — |
| `amended_version` | `query` | `string` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `SemanticDiffResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/designer/amendments/{study_id}/diff', {
  method: 'GET',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/designer/api/v1/designer/amendments/{study_id}/diff',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/designer/api/v1/designer/amendments/{study_id}/diff' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/designer/cascade/propagate`

**Summary:** Propagate Cascade Endpoint  
**Operation ID:** `propagate_cascade_endpoint_api_v1_designer_cascade_propagate_post`  

**Clinical & GxP Rationale:**  
Cascade authored USDM protocol specification changes to downstream eCRFs and SoA matrices.

Requirements: PRD-SYS-001

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Mandatory 60s `sig_token` required for Part 11 sign-off

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `amendment_version` | `query` | `integer` | No | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `object`

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `CascadeSummaryReport` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/designer/cascade/propagate', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/designer/api/v1/designer/cascade/propagate',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/designer/api/v1/designer/cascade/propagate' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/designer/cohort-feasibility/evaluate`

**Summary:** Trigger multi-criteria cohort reasoning and synthetic feasibility simulation  
**Operation ID:** `evaluate_cohort_feasibility_endpoint_api_v1_designer_cohort_feasibility_evaluate_post`  

**Clinical & GxP Rationale:**  
Executes multi-criteria cohort reasoning and synthetic subject profile feasibility simulation.

Supports:
- Asynchronous background worker execution guarded by PostgreSQL advisory locks (non-blocking)
- De-identification scrubbing on evaluation context and prompts (packages/deid)
- Grounded RAG protocol criteria matching (packages/compliance)
- Three-valued logic (3VL) evaluation under complete, partial, and missing data scenarios
- Granular node-level fit trace explanations

Args:
    payload: Inbound CohortFeasibilityRequest payload.
    background_tasks: FastAPI BackgroundTasks queue.
    principal: Authenticated principal security context.

Returns:
    CohortFeasibilityJob when asynchronous background processing is requested,
    or CohortFeasibilityReport when executed synchronously.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Mandatory 60s `sig_token` required for Part 11 sign-off

#### Request Payload Schema (`application/json`)

**Schema Type:** `CohortFeasibilityRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `analysis_id` | `string` | `null` | No | Optional caller-supplied job/analysis ID |
| `study_id` | `string` | ✔ Yes | Target protocol study ID, e.g., 'PROTOCOL-2026-T2D' |
| `cohort_id` | `string` | `null` | No | Target cohort or arm identifier |
| `unstructured_protocol_text` | `string` | `Array<ProtocolRAGContextChunk>` | `null` | No | Unstructured protocol excerpts or RAG chunks for semantic vector matching |
| `structured_criteria` | `Array<EligibilityCriterion-Input>` | No | Structured inclusion and exclusion criteria AST definitions |
| `patient_profiles` | `Array<object>` | `null` | No | Optional patient observation data contexts. If omitted, synthetic profiles are generated. |
| `synthetic_simulation_count` | `integer` | No | Number of GxP synthetic subject profiles to generate for feasibility simulation |
| `missing_data_scenarios` | `Array<string>` | `null` | No | Optional specific field groups to simulate missing data conditions |
| `is_async` | `boolean` | No | Whether to execute asynchronously in a background worker |
| `compliance_profile` | `string` | No | De-identification compliance profile (HIPAA or GXP) |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `CohortFeasibilityReport` | `CohortFeasibilityJob` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/designer/cohort-feasibility/evaluate', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/designer/api/v1/designer/cohort-feasibility/evaluate',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/designer/api/v1/designer/cohort-feasibility/evaluate' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/designer/cohort-feasibility/jobs/{job_id}`

**Summary:** Poll status and results of an asynchronous cohort feasibility job  
**Operation ID:** `get_feasibility_job_status_endpoint_api_v1_designer_cohort_feasibility_jobs__job_id__get`  

**Clinical & GxP Rationale:**  
Retrieves asynchronous cohort feasibility job status and report.

Args:
    job_id: Unique job identifier to query.
    principal: Authenticated principal security context.

Returns:
    CohortFeasibilityJob status wrapper and report.

Raises:
    HTTPException: If the job with job_id is not found (404).

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `job_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `CohortFeasibilityJob` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/designer/cohort-feasibility/jobs/{job_id}', {
  method: 'GET',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/designer/api/v1/designer/cohort-feasibility/jobs/{job_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/designer/api/v1/designer/cohort-feasibility/jobs/{job_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/designer/cohort-feasibility/synthetic-simulate`

**Summary:** Simulate cohort feasibility under missing data scenarios using 3-valued logic  
**Operation ID:** `simulate_synthetic_feasibility_endpoint_api_v1_designer_cohort_feasibility_synthetic_simulate_post`  

**Clinical & GxP Rationale:**  
Directly executes synthetic patient population simulation under missing data conditions.

Args:
    payload: Inbound CohortFeasibilityRequest parameters.
    principal: Authenticated principal security context.

Returns:
    CohortFeasibilityReport containing simulation results.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Mandatory 60s `sig_token` required for Part 11 sign-off

#### Request Payload Schema (`application/json`)

**Schema Type:** `CohortFeasibilityRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `analysis_id` | `string` | `null` | No | Optional caller-supplied job/analysis ID |
| `study_id` | `string` | ✔ Yes | Target protocol study ID, e.g., 'PROTOCOL-2026-T2D' |
| `cohort_id` | `string` | `null` | No | Target cohort or arm identifier |
| `unstructured_protocol_text` | `string` | `Array<ProtocolRAGContextChunk>` | `null` | No | Unstructured protocol excerpts or RAG chunks for semantic vector matching |
| `structured_criteria` | `Array<EligibilityCriterion-Input>` | No | Structured inclusion and exclusion criteria AST definitions |
| `patient_profiles` | `Array<object>` | `null` | No | Optional patient observation data contexts. If omitted, synthetic profiles are generated. |
| `synthetic_simulation_count` | `integer` | No | Number of GxP synthetic subject profiles to generate for feasibility simulation |
| `missing_data_scenarios` | `Array<string>` | `null` | No | Optional specific field groups to simulate missing data conditions |
| `is_async` | `boolean` | No | Whether to execute asynchronously in a background worker |
| `compliance_profile` | `string` | No | De-identification compliance profile (HIPAA or GXP) |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `CohortFeasibilityReport` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/designer/cohort-feasibility/synthetic-simulate', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/designer/api/v1/designer/cohort-feasibility/synthetic-simulate',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/designer/api/v1/designer/cohort-feasibility/synthetic-simulate' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `PATCH` `/api/v1/designer/comments/{comment_id}/resolve`

**Summary:** Resolve Comment  
**Operation ID:** `resolve_comment_api_v1_designer_comments__comment_id__resolve_patch`  

**Clinical & GxP Rationale:**  
Mark a comment thread/item as resolved and log a GxP audit event.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Mandatory 60s `sig_token` required for Part 11 sign-off

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `comment_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `FormReviewCommentResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/designer/comments/{comment_id}/resolve', {
  method: 'PATCH',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.patch(
        '/api/v1/designer/api/v1/designer/comments/{comment_id}/resolve',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X PATCH 'http://localhost:8000/api/v1/designer/api/v1/designer/comments/{comment_id}/resolve' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/designer/digitization/dag/jobs`

**Summary:** Start Digitization Dag Job  
**Operation ID:** `start_digitization_dag_job_api_v1_designer_digitization_dag_jobs_post`  

**Clinical & GxP Rationale:**  
Starts an asynchronous multi-stage DAG protocol digitization job.

Decomposes ingestion into checkpointed transformations (layout parsing, SoA extraction,
biomedical concept mapping, eCRF synthesis, and USDM compilation) with schema validation gates.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Mandatory 60s `sig_token` required for Part 11 sign-off

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `202` | Successful Response | `StartDAGJobResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/designer/digitization/dag/jobs', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/designer/api/v1/designer/digitization/dag/jobs',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/designer/api/v1/designer/digitization/dag/jobs' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/designer/digitization/dag/jobs/{job_id}`

**Summary:** Get Digitization Dag Job Status  
**Operation ID:** `get_digitization_dag_job_status_api_v1_designer_digitization_dag_jobs__job_id__get`  

**Clinical & GxP Rationale:**  
Retrieves real-time execution status, stage checkpoints, and progress for a DAG job.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `job_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `DAGJobStatusResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/designer/digitization/dag/jobs/{job_id}', {
  method: 'GET',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/designer/api/v1/designer/digitization/dag/jobs/{job_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/designer/api/v1/designer/digitization/dag/jobs/{job_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/designer/digitization/dag/jobs/{job_id}/compile-usdm`

**Summary:** Compile Usdm From Dag Job  
**Operation ID:** `compile_usdm_from_dag_job_api_v1_designer_digitization_dag_jobs__job_id__compile_usdm_post`  

**Clinical & GxP Rationale:**  
Commits the finalized USDM model generated by a completed DAG job into Neo4j with Part 11 audit attribution.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Mandatory 60s `sig_token` required for Part 11 sign-off

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `job_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `CompileUSDMFromJobRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `study_id` | `string` | ✔ Yes | Target clinical study ID |
| `change_reason` | `string` | ✔ Yes | GxP change justification reason |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `CommitUSDMResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/designer/digitization/dag/jobs/{job_id}/compile-usdm', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/designer/api/v1/designer/digitization/dag/jobs/{job_id}/compile-usdm',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/designer/api/v1/designer/digitization/dag/jobs/{job_id}/compile-usdm' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/designer/digitization/dag/jobs/{job_id}/resume`

**Summary:** Resume Digitization Dag Job  
**Operation ID:** `resume_digitization_dag_job_api_v1_designer_digitization_dag_jobs__job_id__resume_post`  

**Clinical & GxP Rationale:**  
Resumes execution of a paused or failed DAG digitization job from a specified checkpoint.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Mandatory 60s `sig_token` required for Part 11 sign-off

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `job_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `ResumeDAGJobRequest` | `null`

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `DAGJobStatusResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/designer/digitization/dag/jobs/{job_id}/resume', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/designer/api/v1/designer/digitization/dag/jobs/{job_id}/resume',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/designer/api/v1/designer/digitization/dag/jobs/{job_id}/resume' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/designer/digitization/dag/jobs/{job_id}/stages/{stage}/correct`

**Summary:** Submit Stage Correction Endpoint  
**Operation ID:** `submit_stage_correction_endpoint_api_v1_designer_digitization_dag_jobs__job_id__stages__stage__correct_post`  

**Clinical & GxP Rationale:**  
Submits manual corrections and electronic signature for a flagged stage gate, resuming execution.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Mandatory 60s `sig_token` required for Part 11 sign-off

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `job_id` | `path` | `string` | ✔ Yes | — |
| `stage` | `path` | `DigitizationStage` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `SubmitStageCorrectionRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `stage` | `DigitizationStage` | ✔ Yes | Stage gate being corrected |
| `corrected_data` | `object` | ✔ Yes | Corrected stage data payload |
| `signature_username` | `string` | ✔ Yes | Part 11 signer username/ID |
| `change_reason` | `string` | ✔ Yes | Part 11 GxP reason for manual correction |
| `signature_token` | `string` | `null` | No | Optional authentication or signature token |
| `resume_pipeline` | `boolean` | No | Whether to resume DAG execution immediately after saving correction |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `DAGJobStatusResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/designer/digitization/dag/jobs/{job_id}/stages/{stage}/correct', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/designer/api/v1/designer/digitization/dag/jobs/{job_id}/stages/{stage}/correct',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/designer/api/v1/designer/digitization/dag/jobs/{job_id}/stages/{stage}/correct' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/designer/digitization/extract`

**Summary:** Extract Protocol Digitization  
**Operation ID:** `extract_protocol_digitization_api_v1_designer_digitization_extract_post`  

**Clinical & GxP Rationale:**  
Extracts structured CDISC USDM v4.0 parameters from an uploaded clinical protocol file.

Accepts PDF, DOCX, or text files.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Mandatory 60s `sig_token` required for Part 11 sign-off

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `USDMProtocolExtractionResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/designer/digitization/extract', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/designer/api/v1/designer/digitization/extract',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/designer/api/v1/designer/digitization/extract' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/designer/digitization/sample`

**Summary:** Get Sample Protocol Extraction  
**Operation ID:** `get_sample_protocol_extraction_api_v1_designer_digitization_sample_get`  

**Clinical & GxP Rationale:**  
Returns a canonical pre-extracted USDM v4.0 study structure for instant review and sandboxing.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `USDMProtocolExtractionResponse` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/designer/digitization/sample', {
  method: 'GET',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/designer/api/v1/designer/digitization/sample',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/designer/api/v1/designer/digitization/sample' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/designer/export/m11/{study_id}`

**Summary:** Export Protocol M11 Endpoint  
**Operation ID:** `export_protocol_m11_endpoint_api_v1_designer_export_m11__study_id__get`  

**Clinical & GxP Rationale:**  
Download authored protocol specification in formatted ICH M11 Word (.docx) or USDM JSON format.

Requirements: PRD-SYS-001

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |
| `format` | `query` | `string` | No | Target export format: docx or json |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `object` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/designer/export/m11/{study_id}', {
  method: 'GET',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/designer/api/v1/designer/export/m11/{study_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/designer/api/v1/designer/export/m11/{study_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/designer/forms/{form_id}/comments`

**Summary:** Get Form Comments  
**Operation ID:** `get_form_comments_api_v1_designer_forms__form_id__comments_get`  

**Clinical & GxP Rationale:**  
Fetch all review comments for a given form.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `form_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<FormReviewCommentResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/designer/forms/{form_id}/comments', {
  method: 'GET',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/designer/api/v1/designer/forms/{form_id}/comments',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/designer/api/v1/designer/forms/{form_id}/comments' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/designer/forms/{form_id}/comments`

**Summary:** Post Form Comment  
**Operation ID:** `post_form_comment_api_v1_designer_forms__form_id__comments_post`  

**Clinical & GxP Rationale:**  
Post a new review comment anchored to a field_id.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Mandatory 60s `sig_token` required for Part 11 sign-off

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `form_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `CommentCreatePayload`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `field_id` | `string` | ✔ Yes | The ID of the eCRF field this comment anchors to |
| `comment_text` | `string` | ✔ Yes | The text content of the comment |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `FormReviewCommentResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/designer/forms/{form_id}/comments', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/designer/api/v1/designer/forms/{form_id}/comments',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/designer/api/v1/designer/forms/{form_id}/comments' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/designer/ingestion/candidates/{candidate_id}`

**Summary:** Get Ingestion Candidate  
**Operation ID:** `get_ingestion_candidate_api_v1_designer_ingestion_candidates__candidate_id__get`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `candidate_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `object` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/designer/ingestion/candidates/{candidate_id}', {
  method: 'GET',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/designer/api/v1/designer/ingestion/candidates/{candidate_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/designer/api/v1/designer/ingestion/candidates/{candidate_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/designer/ingestion/candidates/{candidate_id}/items/{item_id}/transition`

**Summary:** Transition Ingestion Item  
**Operation ID:** `transition_ingestion_item_api_v1_designer_ingestion_candidates__candidate_id__items__item_id__transition_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Mandatory 60s `sig_token` required for Part 11 sign-off

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `candidate_id` | `path` | `string` | ✔ Yes | — |
| `item_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `TransitionItemRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `status` | `string` | ✔ Yes | — |
| `reason` | `string` | ✔ Yes | — |
| `name` | `string` | `null` | No | — |
| `label` | `string` | `null` | No | — |
| `value` | `string` | `null` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `object` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/designer/ingestion/candidates/{candidate_id}/items/{item_id}/transition', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/designer/api/v1/designer/ingestion/candidates/{candidate_id}/items/{item_id}/transition',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/designer/api/v1/designer/ingestion/candidates/{candidate_id}/items/{item_id}/transition' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/designer/ingestion/candidates/{candidate_id}/promote`

**Summary:** Promote Ingestion Candidate  
**Operation ID:** `promote_ingestion_candidate_api_v1_designer_ingestion_candidates__candidate_id__promote_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Mandatory 60s `sig_token` required for Part 11 sign-off

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `candidate_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `PromoteRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `change_reason` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `object` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/designer/ingestion/candidates/{candidate_id}/promote', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/designer/api/v1/designer/ingestion/candidates/{candidate_id}/promote',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/designer/api/v1/designer/ingestion/candidates/{candidate_id}/promote' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/designer/ingestion/jobs/{job_id}`

**Summary:** Get Ingestion Job Status  
**Operation ID:** `get_ingestion_job_status_api_v1_designer_ingestion_jobs__job_id__get`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `job_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `object` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/designer/ingestion/jobs/{job_id}', {
  method: 'GET',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/designer/api/v1/designer/ingestion/jobs/{job_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/designer/api/v1/designer/ingestion/jobs/{job_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/designer/ingestion/upload`

**Summary:** Upload Protocol Ingestion  
**Operation ID:** `upload_protocol_ingestion_api_v1_designer_ingestion_upload_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Mandatory 60s `sig_token` required for Part 11 sign-off

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `object` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/designer/ingestion/upload', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/designer/api/v1/designer/ingestion/upload',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/designer/api/v1/designer/ingestion/upload' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/designer/library/smart-blocks`

**Summary:** List Smart Blocks  
**Operation ID:** `list_smart_blocks_api_v1_designer_library_smart_blocks_get`  

**Clinical & GxP Rationale:**  
List pre-configured CDISC clinical Smart Blocks.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<object>` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/designer/library/smart-blocks', {
  method: 'GET',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/designer/api/v1/designer/library/smart-blocks',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/designer/api/v1/designer/library/smart-blocks' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/designer/library/smart-blocks/{block_id}/instantiate`

**Summary:** Instantiate Smart Block  
**Operation ID:** `instantiate_smart_block_api_v1_designer_library_smart_blocks__block_id__instantiate_post`  

**Clinical & GxP Rationale:**  
Instantiate a CDISC Smart Block into a complete Tripartite Form Schema.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Mandatory 60s `sig_token` required for Part 11 sign-off

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `block_id` | `path` | `string` | ✔ Yes | — |
| `form_id` | `query` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `TripartiteFormDefinition` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/designer/library/smart-blocks/{block_id}/instantiate', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/designer/api/v1/designer/library/smart-blocks/{block_id}/instantiate',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/designer/api/v1/designer/library/smart-blocks/{block_id}/instantiate' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/designer/round-trip`

**Summary:** Run Round Trip Endpoint  
**Operation ID:** `run_round_trip_endpoint_api_v1_designer_round_trip_post`  

**Clinical & GxP Rationale:**  
Orchestrates USDM→internal→USDM and internal→USDM→internal round trips.
Returns classification, fidelity details, source format, detected/resolved version, and mapping diagnostics.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Mandatory 60s `sig_token` required for Part 11 sign-off

#### Request Payload Schema (`application/json`)

**Schema Type:** `object`

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `object` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/designer/round-trip', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/designer/api/v1/designer/round-trip',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/designer/api/v1/designer/round-trip' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/designer/sentinel/evaluate`

**Summary:** Evaluate Protocol Quality Endpoint  
**Operation ID:** `evaluate_protocol_quality_endpoint_api_v1_designer_sentinel_evaluate_post`  

**Clinical & GxP Rationale:**  
Evaluate authored protocol specification payload against quality and burden rules.

Requirements: PRD-SYS-001

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Mandatory 60s `sig_token` required for Part 11 sign-off

#### Request Payload Schema (`application/json`)

**Schema Type:** `object`

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `ProtocolQualityScore` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/designer/sentinel/evaluate', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/designer/api/v1/designer/sentinel/evaluate',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/designer/api/v1/designer/sentinel/evaluate' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/designer/startup-matrix/generate-template`

**Summary:** Generate Startup Template Endpoint  
**Operation ID:** `generate_startup_template_endpoint_api_v1_designer_startup_matrix_generate_template_post`  

**Clinical & GxP Rationale:**  
Generates country-specific startup prerequisite criteria matrix for study protocol design.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Mandatory 60s `sig_token` required for Part 11 sign-off

#### Request Payload Schema (`application/json`)

**Schema Type:** `GenerateStartupTemplateRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `study_id` | `string` | ✔ Yes | Study Identifier |
| `country_codes` | `Array<string>` | ✔ Yes | List of target country codes (e.g. ['US', 'EU', 'JP', 'GB']) |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `GenerateStartupTemplateResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/designer/startup-matrix/generate-template', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/designer/api/v1/designer/startup-matrix/generate-template',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/designer/api/v1/designer/startup-matrix/generate-template' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/designer/startup-matrix/jurisdictions`

**Summary:** Get Supported Jurisdictions Endpoint  
**Operation ID:** `get_supported_jurisdictions_endpoint_api_v1_designer_startup_matrix_jurisdictions_get`  

**Clinical & GxP Rationale:**  
Lists supported international jurisdictions with regulatory and ethics governance frameworks.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<JurisdictionInfo>` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/designer/startup-matrix/jurisdictions', {
  method: 'GET',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/designer/api/v1/designer/startup-matrix/jurisdictions',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/designer/api/v1/designer/startup-matrix/jurisdictions' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/designer/startup-matrix/presets`

**Summary:** Get Startup Presets Endpoint  
**Operation ID:** `get_startup_presets_endpoint_api_v1_designer_startup_matrix_presets_get`  

**Clinical & GxP Rationale:**  
Retrieves standard statutory startup criteria presets across international jurisdictions.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `object` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/designer/startup-matrix/presets', {
  method: 'GET',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/designer/api/v1/designer/startup-matrix/presets',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/designer/api/v1/designer/startup-matrix/presets' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/designer/studies/{study_id}/amendments/diff`

**Summary:** Calculate Amendment Diff  
**Operation ID:** `calculate_amendment_diff_api_v1_designer_studies__study_id__amendments_diff_post`  

**Clinical & GxP Rationale:**  
Calculate structural diff between base and amended form versions.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Mandatory 60s `sig_token` required for Part 11 sign-off

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `DiffRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `base_form` | `TripartiteFormDefinition` | ✔ Yes | — |
| `amended_form` | `TripartiteFormDefinition` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `PatientMigrationMatrix` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/designer/studies/{study_id}/amendments/diff', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/designer/api/v1/designer/studies/{study_id}/amendments/diff',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/designer/api/v1/designer/studies/{study_id}/amendments/diff' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/designer/studies/{study_id}/commit-usdm`

**Summary:** Commit Usdm To Study  
**Operation ID:** `commit_usdm_to_study_api_v1_designer_studies__study_id__commit_usdm_post`  

**Clinical & GxP Rationale:**  
Commits extracted USDM entities to the Neo4j graph and synthesizes CDASH eCRF forms.

Guarantees 21 CFR Part 11 audit attribution and GxP change justification.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Mandatory 60s `sig_token` required for Part 11 sign-off

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `CommitUSDMRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `study_id` | `string` | ✔ Yes | Unique study identifier |
| `data` | `USDMProtocolExtractionResponse` | ✔ Yes | USDM extraction data |
| `change_reason` | `string` | ✔ Yes | GxP change justification reason |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `CommitUSDMResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/designer/studies/{study_id}/commit-usdm', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/designer/api/v1/designer/studies/{study_id}/commit-usdm',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/designer/api/v1/designer/studies/{study_id}/commit-usdm' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/designer/studies/{study_id}/forms/compile`

**Summary:** Compile Form Schema  
**Operation ID:** `compile_form_schema_api_v1_designer_studies__study_id__forms_compile_post`  

**Clinical & GxP Rationale:**  
Compile and validate a Tripartite Form Definition.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Mandatory 60s `sig_token` required for Part 11 sign-off

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `TripartiteFormDefinition`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `form_id` | `string` | ✔ Yes | — |
| `protocol_id` | `string` | ✔ Yes | — |
| `version_index` | `integer` | No | — |
| `version_tag` | `string` | No | — |
| `data_schema` | `DataSchema` | ✔ Yes | — |
| `ui_schema` | `UiSchema` | ✔ Yes | — |
| `logic_schema` | `LogicSchema` | ✔ Yes | — |
| `created_at` | `string` | No | — |
| `created_by` | `string` | No | — |
| `reason_for_change` | `string` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `object` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/designer/studies/{study_id}/forms/compile', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/designer/api/v1/designer/studies/{study_id}/forms/compile',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/designer/api/v1/designer/studies/{study_id}/forms/compile' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/designer/studies/{study_id}/forms/{form_id}/publish`

**Summary:** Publish And Seal Form  
**Operation ID:** `publish_and_seal_form_api_v1_designer_studies__study_id__forms__form_id__publish_post`  

**Clinical & GxP Rationale:**  
Publish and cryptographically seal form definition with step-up electronic signature.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Mandatory 60s `sig_token` required for Part 11 sign-off

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |
| `form_id` | `path` | `string` | ✔ Yes | — |
| `X-User-Id` | `header` | `string` | No | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `PublicationRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `form_definition` | `TripartiteFormDefinition` | ✔ Yes | — |
| `reason_for_change` | `string` | ✔ Yes | — |
| `step_up_signature_token` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `object` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/designer/studies/{study_id}/forms/{form_id}/publish', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/designer/api/v1/designer/studies/{study_id}/forms/{form_id}/publish',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/designer/api/v1/designer/studies/{study_id}/forms/{form_id}/publish' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/designer/studies/{study_id}/forms/{form_id}/uat/execute`

**Summary:** Execute Uat Simulation  
**Operation ID:** `execute_uat_simulation_api_v1_designer_studies__study_id__forms__form_id__uat_execute_post`  

**Clinical & GxP Rationale:**  
Execute synthetic UAT simulation suite and generate 21 CFR Part 11 Evidence Report.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Mandatory 60s `sig_token` required for Part 11 sign-off

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |
| `form_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `UatExecutionRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `form_definition` | `TripartiteFormDefinition` | ✔ Yes | — |
| `payloads` | `Array<SyntheticPayload>` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `UatEvidenceReport` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/designer/studies/{study_id}/forms/{form_id}/uat/execute', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/designer/api/v1/designer/studies/{study_id}/forms/{form_id}/uat/execute',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/designer/api/v1/designer/studies/{study_id}/forms/{form_id}/uat/execute' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/designer/usdm/validate`

**Summary:** Validate Usdm Endpoint  
**Operation ID:** `validate_usdm_endpoint_api_v1_designer_usdm_validate_post`  

**Clinical & GxP Rationale:**  
Validates a USDM JSON or YAML payload, normalizes shape differences, and returns a detailed validation report.
If the payload is invalid, raises a structured HTTP 422 ProblemDetails response.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Mandatory 60s `sig_token` required for Part 11 sign-off

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `override` | `query` | `string` | `null` | No | Optional explicit version override ('v2' or 'v3') |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `object` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/designer/usdm/validate', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/designer/api/v1/designer/usdm/validate',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/designer/api/v1/designer/usdm/validate' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/mappings/upload`

**Summary:** Upload Mapping Csv  
**Operation ID:** `upload_mapping_csv_api_v1_mappings_upload_post`  

**Clinical & GxP Rationale:**  
Validates a CSV mapping configuration to ensure target names meet standard W3C XML naming specifications.

Raises:
    HTTPException: If the CSV format is invalid or if target XML names violate naming rules.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `object` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/mappings/upload', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/designer/api/v1/mappings/upload',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/designer/api/v1/mappings/upload' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/mdr/concepts`

**Summary:** Get Concepts  
**Operation ID:** `get_concepts_api_v1_mdr_concepts_get`  

**Clinical & GxP Rationale:**  
Fetches a paginated list of Biomedical Concepts.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `terminology` | `query` | `TerminologyEnum` | `null` | No | — |
| `domain` | `query` | `string` | `null` | No | — |
| `limit` | `query` | `integer` | No | — |
| `starting_after` | `query` | `string` | `null` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `ConceptListResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/mdr/concepts', {
  method: 'GET',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/designer/api/v1/mdr/concepts',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/designer/api/v1/mdr/concepts' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/mdr/concepts`

**Summary:** Create Concept  
**Operation ID:** `create_concept_api_v1_mdr_concepts_post`  

**Clinical & GxP Rationale:**  
Creates a new Biomedical Concept inside the MDR graph repository.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `CreateConceptRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `concept_code` | `string` | ✔ Yes | — |
| `terminology` | `string` | ✔ Yes | — |
| `display_name` | `string` | ✔ Yes | — |
| `definition` | `string` | ✔ Yes | — |
| `cdash_mapping` | `CDASHMapping` | `null` | No | — |
| `allowable_units` | `Array<AllowableUnit>` | `null` | No | — |
| `change_reason` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `ConceptDetail` |
| `400` | Bad Request | `ProblemDetails` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/mdr/concepts', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/designer/api/v1/mdr/concepts',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/designer/api/v1/mdr/concepts' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `PUT` `/api/v1/mdr/concepts/{id}`

**Summary:** Update Concept  
**Operation ID:** `update_concept_api_v1_mdr_concepts__id__put`  

**Clinical & GxP Rationale:**  
Updates an existing concept, creating a new audit history and incrementing version index.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `UpdateConceptRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `display_name` | `string` | ✔ Yes | — |
| `definition` | `string` | ✔ Yes | — |
| `cdash_mapping` | `CDASHMapping` | `null` | No | — |
| `allowable_units` | `Array<AllowableUnit>` | `null` | No | — |
| `reason_for_change` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `ConceptDetail` |
| `400` | Bad Request | `ProblemDetails` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/mdr/concepts/{id}', {
  method: 'PUT',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.put(
        '/api/v1/designer/api/v1/mdr/concepts/{id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X PUT 'http://localhost:8000/api/v1/designer/api/v1/mdr/concepts/{id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `DELETE` `/api/v1/mdr/concepts/{id}`

**Summary:** Delete Concept  
**Operation ID:** `delete_concept_api_v1_mdr_concepts__id__delete`  

**Clinical & GxP Rationale:**  
Deletes an existing Biomedical Concept if it is not referenced by an Active-Recruiting study.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `object` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/mdr/concepts/{id}', {
  method: 'DELETE',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.delete(
        '/api/v1/designer/api/v1/mdr/concepts/{id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X DELETE 'http://localhost:8000/api/v1/designer/api/v1/mdr/concepts/{id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/mdr/concepts/{id}/rename`

**Summary:** Rename Concept  
**Operation ID:** `rename_concept_api_v1_mdr_concepts__id__rename_post`  

**Clinical & GxP Rationale:**  
Renames an existing Biomedical Concept if it is not referenced by an Active-Recruiting study.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `RenameConceptRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `display_name` | `string` | ✔ Yes | — |
| `reason_for_change` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `ConceptDetail` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/mdr/concepts/{id}/rename', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/designer/api/v1/mdr/concepts/{id}/rename',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/designer/api/v1/mdr/concepts/{id}/rename' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/mdr/library`

**Summary:** Create Library Object Endpoint  
**Operation ID:** `create_library_object_endpoint_api_v1_mdr_library_post`  

**Clinical & GxP Rationale:**  
Creates a new Global Library object under the authenticated sponsor's scope.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `object`

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `object` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/mdr/library', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/designer/api/v1/mdr/library',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/designer/api/v1/mdr/library' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/mdr/library`

**Summary:** List Library Objects Endpoint  
**Operation ID:** `list_library_objects_endpoint_api_v1_mdr_library_get`  

**Clinical & GxP Rationale:**  
Lists latest global library objects under the authenticated sponsor.
Supports Stripe-style cursor-based pagination.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `object_type` | `query` | `ObjectType` | `null` | No | — |
| `limit` | `query` | `integer` | No | — |
| `starting_after` | `query` | `string` | `null` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `LibraryObjectListResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/mdr/library', {
  method: 'GET',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/designer/api/v1/mdr/library',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/designer/api/v1/mdr/library' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/mdr/library/{id}`

**Summary:** Get Library Object Endpoint  
**Operation ID:** `get_library_object_endpoint_api_v1_mdr_library__id__get`  

**Clinical & GxP Rationale:**  
Retrieves the latest version or a specific version of a global library object.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `id` | `path` | `string` | ✔ Yes | — |
| `version` | `query` | `integer` | `null` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `object` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/mdr/library/{id}', {
  method: 'GET',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/designer/api/v1/mdr/library/{id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/designer/api/v1/mdr/library/{id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `PUT` `/api/v1/mdr/library/{id}`

**Summary:** Update Library Object Endpoint  
**Operation ID:** `update_library_object_endpoint_api_v1_mdr_library__id__put`  

**Clinical & GxP Rationale:**  
Updates a global library object by creating a new version.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `object`

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `object` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/mdr/library/{id}', {
  method: 'PUT',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.put(
        '/api/v1/designer/api/v1/mdr/library/{id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X PUT 'http://localhost:8000/api/v1/designer/api/v1/mdr/library/{id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/mdr/library/{id}/amend`

**Summary:** Amend Library Object Endpoint  
**Operation ID:** `amend_library_object_endpoint_api_v1_mdr_library__id__amend_post`  

**Clinical & GxP Rationale:**  
Initiates an amendment on a library object that is in use by creating a successor draft version.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `LibraryObjectAmendRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `reason_for_change` | `string` | ✔ Yes | Mandatory reason for initiating the amendment. |
| `payload` | `object` | `null` | No | Optional updated payload for the amended version. If not provided, the latest payload is cloned. |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `object` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/mdr/library/{id}/amend', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/designer/api/v1/mdr/library/{id}/amend',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/designer/api/v1/mdr/library/{id}/amend' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/mdr/library/{id}/history`

**Summary:** Get Library Object History Endpoint  
**Operation ID:** `get_library_object_history_endpoint_api_v1_mdr_library__id__history_get`  

**Clinical & GxP Rationale:**  
Retrieves the complete version history of a global library object.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<object>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/mdr/library/{id}/history', {
  method: 'GET',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/designer/api/v1/mdr/library/{id}/history',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/designer/api/v1/mdr/library/{id}/history' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/mdr/library/{id}/transition`

**Summary:** Transition Library Object Endpoint  
**Operation ID:** `transition_library_object_endpoint_api_v1_mdr_library__id__transition_post`  

**Clinical & GxP Rationale:**  
Transitions the lifecycle status of a global library object.
Enforces a strict role-gated ALLOWED_LIBRARY_TRANSITIONS map.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `LibraryObjectTransitionRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `status` | `LibraryStatus` | ✔ Yes | Target status level for transition. |
| `change_reason` | `string` | ✔ Yes | Mandatory reason for change / audit trail justification. |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `object` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/mdr/library/{id}/transition', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/designer/api/v1/mdr/library/{id}/transition',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/designer/api/v1/mdr/library/{id}/transition' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/studies/{study_id}`

**Summary:** Get Legacy Study  
**Operation ID:** `get_legacy_study_api_v1_studies__study_id__get`  

**Clinical & GxP Rationale:**  
Returns the legacy internal projection with no USDM formatting.

Args:
    study_id = get_study_projection(study_id)

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `object` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}', {
  method: 'GET',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/designer/api/v1/studies/{study_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/studies/{study_id}/alignment-validation`

**Summary:** Validate Study Alignment  
**Operation ID:** `validate_study_alignment_api_v1_studies__study_id__alignment_validation_get`  

**Clinical & GxP Rationale:**  
Generate an alignment validation report for a specific clinical study.

Analyzes trace links dynamically to ensure the
Study Data Requirements (SDR) align with Metadata Requirements (MDR).

Args:
    study_id (str): The unique identifier of the study to validate.

Returns:
    StudyAlignmentReport: The structured validation report.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `StudyAlignmentReport` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/alignment-validation', {
  method: 'GET',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/designer/api/v1/studies/{study_id}/alignment-validation',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/alignment-validation' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/studies/{study_id}/blocks/{block_id}/suggestions`

**Summary:** Create Suggestion Endpoint  
**Operation ID:** `create_suggestion_endpoint_api_v1_studies__study_id__blocks__block_id__suggestions_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |
| `block_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `SuggestionCreate`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `suggested_text` | `string` | ✔ Yes | — |
| `reason` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `Suggestion` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/blocks/{block_id}/suggestions', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/designer/api/v1/studies/{study_id}/blocks/{block_id}/suggestions',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/blocks/{block_id}/suggestions' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/studies/{study_id}/blocks/{block_id}/suggestions`

**Summary:** Get Suggestions Endpoint  
**Operation ID:** `get_suggestions_endpoint_api_v1_studies__study_id__blocks__block_id__suggestions_get`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |
| `block_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<Suggestion>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/blocks/{block_id}/suggestions', {
  method: 'GET',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/designer/api/v1/studies/{study_id}/blocks/{block_id}/suggestions',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/blocks/{block_id}/suggestions' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/studies/{study_id}/ct-validation`

**Summary:** Validate Study Ct Endpoint  
**Operation ID:** `validate_study_ct_endpoint_api_v1_studies__study_id__ct_validation_get`  

**Clinical & GxP Rationale:**  
Generate a controlled terminology (CT) validation report for a specific clinical study.

Traverses study concept references and aggregates validation outcomes
such as identifying affected elements and references.

Args:
    study_id (str): The unique identifier of the study to validate.

Returns:
    StudyTerminologyValidationReport: The structured validation report.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `StudyTerminologyValidationReport` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/ct-validation', {
  method: 'GET',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/designer/api/v1/studies/{study_id}/ct-validation',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/ct-validation' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/studies/{study_id}/differences`

**Summary:** Study Differences  
**Operation ID:** `study_differences_api_v1_studies__study_id__differences_get`  

**Clinical & GxP Rationale:**  
Get human-readable field-level differences between two version actions of a study.

This endpoint uses a decoupled, API-first in-memory diffing architecture. Instead of
relying on a direct database connection (which led to 503 errors and tight coupling),
it fetches full study payloads from an external registry. The comparison logic runs
entirely in-memory by flattening nested dictionary structures to dynamically identify
added, modified, and deleted fields. This ensures high availability and fast execution
without maintaining direct database connections.

Args:
    study_id (str): The unique identifier of the study.
    action_id1 (str): The ID of the first action version.
    action_id2 (str): The ID of the second action version.

Returns:
    List[DifferenceResult]: A list of field-level differences.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |
| `action_id1` | `query` | `string` | ✔ Yes | — |
| `action_id2` | `query` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<DifferenceResult>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/differences', {
  method: 'GET',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/designer/api/v1/studies/{study_id}/differences',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/differences' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/studies/{study_id}/eligibility-criteria`

**Summary:** List Eligibility Criteria  
**Operation ID:** `list_eligibility_criteria_api_v1_studies__study_id__eligibility_criteria_get`  

**Clinical & GxP Rationale:**  
Retrieves all active eligibility criteria for a specific clinical study.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<EligibilityCriterion-Output>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/eligibility-criteria', {
  method: 'GET',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/designer/api/v1/studies/{study_id}/eligibility-criteria',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/eligibility-criteria' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/studies/{study_id}/eligibility-criteria`

**Summary:** Create Eligibility Criterion Endpoint  
**Operation ID:** `create_eligibility_criterion_endpoint_api_v1_studies__study_id__eligibility_criteria_post`  

**Clinical & GxP Rationale:**  
Creates a new eligibility criterion for a specific clinical study, parsing and validating the DSL.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `CreateEligibilityCriterionRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `criterion_id` | `string` | ✔ Yes | Unique identifier of this eligibility criterion, e.g., 'INC_01'. |
| `criterion_type` | `string` | ✔ Yes | Whether this is an inclusion or exclusion criterion. |
| `description` | `string` | ✔ Yes | Human-readable text description of the criterion. |
| `dsl_source` | `string` | ✔ Yes | The raw DSL statement source, e.g., 'eCRF.DM.AGE >= 18'. |
| `expected_outcome` | `boolean` | No | Expected Boolean outcome of evaluating the condition node. |
| `change_reason` | `string` | ✔ Yes | Reason for creating this criterion. |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `EligibilityCriterion-Output` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/eligibility-criteria', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/designer/api/v1/studies/{study_id}/eligibility-criteria',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/eligibility-criteria' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/studies/{study_id}/eligibility-criteria/{criterion_id}`

**Summary:** Get Eligibility Criterion Detail  
**Operation ID:** `get_eligibility_criterion_detail_api_v1_studies__study_id__eligibility_criteria__criterion_id__get`  

**Clinical & GxP Rationale:**  
Retrieves details for a specific eligibility criterion by ID.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |
| `criterion_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `EligibilityCriterion-Output` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/eligibility-criteria/{criterion_id}', {
  method: 'GET',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/designer/api/v1/studies/{study_id}/eligibility-criteria/{criterion_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/eligibility-criteria/{criterion_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `PUT` `/api/v1/studies/{study_id}/eligibility-criteria/{criterion_id}`

**Summary:** Update Eligibility Criterion Endpoint  
**Operation ID:** `update_eligibility_criterion_endpoint_api_v1_studies__study_id__eligibility_criteria__criterion_id__put`  

**Clinical & GxP Rationale:**  
Updates an eligibility criterion for a specific clinical study, parsing and validating the DSL.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |
| `criterion_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `UpdateEligibilityCriterionRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `criterion_type` | `string` | ✔ Yes | Whether this is an inclusion or exclusion criterion. |
| `description` | `string` | ✔ Yes | Human-readable text description of the criterion. |
| `dsl_source` | `string` | ✔ Yes | The raw DSL statement source, e.g., 'eCRF.DM.AGE >= 18'. |
| `expected_outcome` | `boolean` | No | Expected Boolean outcome of evaluating the condition node. |
| `change_reason` | `string` | ✔ Yes | Reason for updating this criterion. |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `EligibilityCriterion-Output` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/eligibility-criteria/{criterion_id}', {
  method: 'PUT',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.put(
        '/api/v1/designer/api/v1/studies/{study_id}/eligibility-criteria/{criterion_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X PUT 'http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/eligibility-criteria/{criterion_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/studies/{study_id}/export`

**Summary:** Export Protocol  
**Operation ID:** `export_protocol_api_v1_studies__study_id__export_get`  

**Clinical & GxP Rationale:**  
Assembles a study version's data, maps it to a canonical USDM content model,
and renders the resulting clinical protocol document as a structurally valid
PDF or DOCX document using shared layout templates.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |
| `format` | `query` | `string` | No | — |
| `output` | `query` | `string` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `object` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/export', {
  method: 'GET',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/designer/api/v1/studies/{study_id}/export',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/export' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/studies/{study_id}/library-instances`

**Summary:** Instantiate Library Object Endpoint  
**Operation ID:** `instantiate_library_object_endpoint_api_v1_studies__study_id__library_instances_post`  

**Clinical & GxP Rationale:**  
Instantiates a specific version (or latest) of a Global Library object into a study-scoped instance.
Enforces that the library object and study both belong to/are accessible by the authenticated sponsor.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `InstantiateLibraryObjectRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `library_object_id` | `string` | ✔ Yes | Stable, unique global library ID to instantiate. |
| `version` | `integer` | `null` | No | The specific version of the library object to instantiate. Defaults to latest if not specified. |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `LibraryInstanceResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/library-instances', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/designer/api/v1/studies/{study_id}/library-instances',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/library-instances' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `PUT` `/api/v1/studies/{study_id}/library-instances/{instance_id}`

**Summary:** Update Library Instance Endpoint  
**Operation ID:** `update_library_instance_endpoint_api_v1_studies__study_id__library_instances__instance_id__put`  

**Clinical & GxP Rationale:**  
Updates the payload of an instantiated library object inside a study.
Verifies that target study belongs to or is accessible by the authenticated sponsor,
leaving the global library source immutable.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |
| `instance_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `UpdateLibraryInstanceRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `payload` | `object` | ✔ Yes | The complete updated payload of the library instance. |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `LibraryInstanceResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/library-instances/{instance_id}', {
  method: 'PUT',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.put(
        '/api/v1/designer/api/v1/studies/{study_id}/library-instances/{instance_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X PUT 'http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/library-instances/{instance_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/studies/{study_id}/library-instances/{instance_id}/diff`

**Summary:** Get Library Instance Diff Endpoint  
**Operation ID:** `get_library_instance_diff_endpoint_api_v1_studies__study_id__library_instances__instance_id__diff_get`  

**Clinical & GxP Rationale:**  
Returns field-level dot-notated differences between the library instance payload and its linked source version.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |
| `instance_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<DifferenceResult>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/library-instances/{instance_id}/diff', {
  method: 'GET',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/designer/api/v1/studies/{study_id}/library-instances/{instance_id}/diff',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/library-instances/{instance_id}/diff' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/studies/{study_id}/rules`

**Summary:** Get Study Rules  
**Operation ID:** `get_study_rules_api_v1_studies__study_id__rules_get`  

**Clinical & GxP Rationale:**  
Retrieves all non-soft-deleted active rules for a specific clinical study.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<object>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/rules', {
  method: 'GET',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/designer/api/v1/studies/{study_id}/rules',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/rules' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/studies/{study_id}/rules`

**Summary:** Create Study Rule  
**Operation ID:** `create_study_rule_api_v1_studies__study_id__rules_post`  

**Clinical & GxP Rationale:**  
Creates a new rule for a clinical study, enforcing auth and X-Change-Reason.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `CreateRuleRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `type` | `string` | ✔ Yes | — |
| `condition` | `packages__ddf__rules__ast__ExpressionNode` | ✔ Yes | — |
| `action` | `string` | `null` | No | — |
| `target_field` | `string` | `null` | No | — |
| `target_form` | `string` | `null` | No | — |
| `target_group` | `string` | `null` | No | — |
| `query_message` | `string` | `null` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `object` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/rules', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/designer/api/v1/studies/{study_id}/rules',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/rules' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/studies/{study_id}/rules/preview`

**Summary:** Compile Preview Rule  
**Operation ID:** `compile_preview_rule_api_v1_studies__study_id__rules_preview_post`  

**Clinical & GxP Rationale:**  
Read-only compile and validation preview route.
Detects unknown field references and circular skip-logic dependencies.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `CreateRuleRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `type` | `string` | ✔ Yes | — |
| `condition` | `packages__ddf__rules__ast__ExpressionNode` | ✔ Yes | — |
| `action` | `string` | `null` | No | — |
| `target_field` | `string` | `null` | No | — |
| `target_form` | `string` | `null` | No | — |
| `target_group` | `string` | `null` | No | — |
| `query_message` | `string` | `null` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `RulePreviewResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/rules/preview', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/designer/api/v1/studies/{study_id}/rules/preview',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/rules/preview' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/studies/{study_id}/rules/validate`

**Summary:** Compile Validate Rule  
**Operation ID:** `compile_validate_rule_api_v1_studies__study_id__rules_validate_post`  

**Clinical & GxP Rationale:**  
Read-only compile and validation preview route.
Detects unknown field references and circular skip-logic dependencies.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `CreateRuleRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `type` | `string` | ✔ Yes | — |
| `condition` | `packages__ddf__rules__ast__ExpressionNode` | ✔ Yes | — |
| `action` | `string` | `null` | No | — |
| `target_field` | `string` | `null` | No | — |
| `target_form` | `string` | `null` | No | — |
| `target_group` | `string` | `null` | No | — |
| `query_message` | `string` | `null` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `RulePreviewResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/rules/validate', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/designer/api/v1/studies/{study_id}/rules/validate',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/rules/validate' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/studies/{study_id}/rules/{rule_id}`

**Summary:** Get Study Rule By Id  
**Operation ID:** `get_study_rule_by_id_api_v1_studies__study_id__rules__rule_id__get`  

**Clinical & GxP Rationale:**  
Retrieves a specific rule by ID.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |
| `rule_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `object` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/rules/{rule_id}', {
  method: 'GET',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/designer/api/v1/studies/{study_id}/rules/{rule_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/rules/{rule_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `PUT` `/api/v1/studies/{study_id}/rules/{rule_id}`

**Summary:** Update Study Rule By Id  
**Operation ID:** `update_study_rule_by_id_api_v1_studies__study_id__rules__rule_id__put`  

**Clinical & GxP Rationale:**  
Updates a rule's parameters, incrementing version index.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |
| `rule_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `CreateRuleRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `type` | `string` | ✔ Yes | — |
| `condition` | `packages__ddf__rules__ast__ExpressionNode` | ✔ Yes | — |
| `action` | `string` | `null` | No | — |
| `target_field` | `string` | `null` | No | — |
| `target_form` | `string` | `null` | No | — |
| `target_group` | `string` | `null` | No | — |
| `query_message` | `string` | `null` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `object` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/rules/{rule_id}', {
  method: 'PUT',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.put(
        '/api/v1/designer/api/v1/studies/{study_id}/rules/{rule_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X PUT 'http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/rules/{rule_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `DELETE` `/api/v1/studies/{study_id}/rules/{rule_id}`

**Summary:** Delete Study Rule By Id  
**Operation ID:** `delete_study_rule_by_id_api_v1_studies__study_id__rules__rule_id__delete`  

**Clinical & GxP Rationale:**  
Soft-deletes a rule, retaining its historical properties in audit.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |
| `rule_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `object` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/rules/{rule_id}', {
  method: 'DELETE',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.delete(
        '/api/v1/designer/api/v1/studies/{study_id}/rules/{rule_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X DELETE 'http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/rules/{rule_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/studies/{study_id}/sections/{section_id}/status`

**Summary:** Get Section Review Status  
**Operation ID:** `get_section_review_status_api_v1_studies__study_id__sections__section_id__status_get`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |
| `section_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `object` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/sections/{section_id}/status', {
  method: 'GET',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/designer/api/v1/studies/{study_id}/sections/{section_id}/status',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/sections/{section_id}/status' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/studies/{study_id}/sections/{section_id}/threads`

**Summary:** Create Thread Endpoint  
**Operation ID:** `create_thread_endpoint_api_v1_studies__study_id__sections__section_id__threads_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |
| `section_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `CommentThreadCreate`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `block_id` | `string` | ✔ Yes | — |
| `text` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `CommentThread` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/sections/{section_id}/threads', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/designer/api/v1/studies/{study_id}/sections/{section_id}/threads',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/sections/{section_id}/threads' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/studies/{study_id}/sections/{section_id}/threads`

**Summary:** Get Threads Endpoint  
**Operation ID:** `get_threads_endpoint_api_v1_studies__study_id__sections__section_id__threads_get`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |
| `section_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<CommentThread>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/sections/{section_id}/threads', {
  method: 'GET',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/designer/api/v1/studies/{study_id}/sections/{section_id}/threads',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/sections/{section_id}/threads' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/studies/{study_id}/sections/{section_id}/transition`

**Summary:** Transition Section  
**Operation ID:** `transition_section_api_v1_studies__study_id__sections__section_id__transition_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |
| `section_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `SectionTransitionRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `to_status` | `SectionReviewStatus` | ✔ Yes | — |
| `reason_for_change` | `string` | ✔ Yes | — |
| `username` | `string` | `null` | No | — |
| `password` | `string` | `null` | No | — |
| `signing_reason` | `SigningReason` | `null` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `SectionReviewTransition` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/sections/{section_id}/transition', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/designer/api/v1/studies/{study_id}/sections/{section_id}/transition',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/sections/{section_id}/transition' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/studies/{study_id}/suggestions/{suggestion_id}/decision`

**Summary:** Decide Suggestion Endpoint  
**Operation ID:** `decide_suggestion_endpoint_api_v1_studies__study_id__suggestions__suggestion_id__decision_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |
| `suggestion_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `SuggestionDecisionRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `decision` | `string` | ✔ Yes | — |
| `decision_reason` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Suggestion` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/suggestions/{suggestion_id}/decision', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/designer/api/v1/studies/{study_id}/suggestions/{suggestion_id}/decision',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/suggestions/{suggestion_id}/decision' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/studies/{study_id}/terminology-validation`

**Summary:** Validate Study Terminology Endpoint  
**Operation ID:** `validate_study_terminology_endpoint_api_v1_studies__study_id__terminology_validation_get`  

**Clinical & GxP Rationale:**  
Generate a terminology validation report for a specific clinical study.

Traverses study concept references and aggregates validation outcomes
such as identifying affected elements and references.

Args:
    study_id (str): The unique identifier of the study to validate.

Returns:
    StudyTerminologyValidationReport: The structured validation report.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `StudyTerminologyValidationReport` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/terminology-validation', {
  method: 'GET',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/designer/api/v1/studies/{study_id}/terminology-validation',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/terminology-validation' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/studies/{study_id}/threads/{thread_id}/comments`

**Summary:** Add Comment Endpoint  
**Operation ID:** `add_comment_endpoint_api_v1_studies__study_id__threads__thread_id__comments_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |
| `thread_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `CommentCreate`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `text` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `CommentThread` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/threads/{thread_id}/comments', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/designer/api/v1/studies/{study_id}/threads/{thread_id}/comments',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/threads/{thread_id}/comments' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/studies/{study_id}/threads/{thread_id}/resolve`

**Summary:** Resolve Thread Endpoint  
**Operation ID:** `resolve_thread_endpoint_api_v1_studies__study_id__threads__thread_id__resolve_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |
| `thread_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `CommentThread` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/threads/{thread_id}/resolve', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/designer/api/v1/studies/{study_id}/threads/{thread_id}/resolve',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/threads/{thread_id}/resolve' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/studies/{study_id}/versions`

**Summary:** Post Study Version  
**Operation ID:** `post_study_version_api_v1_studies__study_id__versions_post`  

**Clinical & GxP Rationale:**  
Establishes a new StudyVersion node under a clinical study.
Enforces that concurrent creation with duplicate index or tag fails with 409 Conflict.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `CreateStudyVersionRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `id` | `string` | ✔ Yes | — |
| `version_tag` | `string` | ✔ Yes | — |
| `status` | `string` | ✔ Yes | — |
| `version_index` | `integer` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `object` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/versions', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/designer/api/v1/studies/{study_id}/versions',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/versions' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/studies/{study_id}/versions/diff`

**Summary:** Get Versions Diff Endpoint  
**Operation ID:** `get_versions_diff_endpoint_api_v1_studies__study_id__versions_diff_get`  

**Clinical & GxP Rationale:**  
Exposes graph-native, form-level version-diff API.
Identifies additions, modifications, and deletions of forms.
Returns HTTP 400 Bad Request if either version is nonexistent or unrelated.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |
| `version_id1` | `query` | `string` | ✔ Yes | The old version ID |
| `version_id2` | `query` | `string` | ✔ Yes | The new version ID |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `VersionDiffResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/versions/diff', {
  method: 'GET',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/designer/api/v1/studies/{study_id}/versions/diff',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/versions/diff' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/studies/{study_id}/versions/{version_id}/approve`

**Summary:** Approve Study Version Endpoint  
**Operation ID:** `approve_study_version_endpoint_api_v1_studies__study_id__versions__version_id__approve_post`  

**Clinical & GxP Rationale:**  
Approve and cryptographically sign a Metadata Designer clinical protocol version, producing
a 21 CFR Part 11 compliant persisted signature manifestation, recording immutable Action history,
and locks the protocol version by transitioning its status to APPROVED.
Locked statuses (APPROVED, SIGNED) block any subsequent edits via immutability checks across both Neo4j and mock databases.
Successfully approved protocols are archived as PROTOCOL_SIGNOFF artifacts to the eTMF service asynchronously.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |
| `version_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `ApproveProtocolRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `signing_reason` | `SigningReason` | ✔ Yes | Reason for signing |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `object` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/approve', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/approve',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/approve' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/studies/{study_id}/versions/{version_id}/arms`

**Summary:** Create Arm Endpoint  
**Operation ID:** `create_arm_endpoint_api_v1_studies__study_id__versions__version_id__arms_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |
| `version_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `CreateStudyArmRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `id` | `string` | ✔ Yes | Unique identifier for the study arm. |
| `properties` | `StudyArmProperties` | ✔ Yes | — |
| `change_reason` | `string` | No | Change reason for audit trail |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `SoAEntityCreatedResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/arms', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/arms',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/arms' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/studies/{study_id}/versions/{version_id}/arms`

**Summary:** List Arms Endpoint  
**Operation ID:** `list_arms_endpoint_api_v1_studies__study_id__versions__version_id__arms_get`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |
| `version_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<SoAEntityDetail>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/arms', {
  method: 'GET',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/arms',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/arms' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/studies/{study_id}/versions/{version_id}/arms/reorder`

**Summary:** Reorder Arms Endpoint  
**Operation ID:** `reorder_arms_endpoint_api_v1_studies__study_id__versions__version_id__arms_reorder_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |
| `version_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `ArmReorderRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `arms` | `Array<ArmReorderItem>` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `object` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/arms/reorder', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/arms/reorder',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/arms/reorder' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `DELETE` `/api/v1/studies/{study_id}/versions/{version_id}/arms/{arm_id}`

**Summary:** Retire Arm Endpoint  
**Operation ID:** `retire_arm_endpoint_api_v1_studies__study_id__versions__version_id__arms__arm_id__delete`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |
| `version_id` | `path` | `string` | ✔ Yes | — |
| `arm_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `SoAEntityCreatedResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/arms/{arm_id}', {
  method: 'DELETE',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.delete(
        '/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/arms/{arm_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X DELETE 'http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/arms/{arm_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/studies/{study_id}/versions/{version_id}/arms/{arm_id}`

**Summary:** Get Arm Endpoint  
**Operation ID:** `get_arm_endpoint_api_v1_studies__study_id__versions__version_id__arms__arm_id__get`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |
| `version_id` | `path` | `string` | ✔ Yes | — |
| `arm_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `SoAEntityDetail` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/arms/{arm_id}', {
  method: 'GET',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/arms/{arm_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/arms/{arm_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `PUT` `/api/v1/studies/{study_id}/versions/{version_id}/arms/{arm_id}`

**Summary:** Update Arm Endpoint  
**Operation ID:** `update_arm_endpoint_api_v1_studies__study_id__versions__version_id__arms__arm_id__put`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |
| `version_id` | `path` | `string` | ✔ Yes | — |
| `arm_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `UpdateStudyArmRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `properties` | `StudyArmProperties` | ✔ Yes | — |
| `reason_for_change` | `string` | No | Reason for change for audit trail |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `SoAEntityCreatedResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/arms/{arm_id}', {
  method: 'PUT',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.put(
        '/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/arms/{arm_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X PUT 'http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/arms/{arm_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/studies/{study_id}/versions/{version_id}/assignments/activities`

**Summary:** Assign Activities To Visit Endpoint  
**Operation ID:** `assign_activities_to_visit_endpoint_api_v1_studies__study_id__versions__version_id__assignments_activities_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Mandatory 60s `sig_token` required for Part 11 sign-off

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |
| `version_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `ActivityAssignmentRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `visit_id` | `string` | ✔ Yes | The visit identifier. |
| `procedure_ids` | `Array<string>` | No | One or more procedure identifiers (non-empty). |
| `activity_ids` | `Array<string>` | No | One or more activity/procedure identifiers (non-empty). |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `object` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/assignments/activities', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/assignments/activities',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/assignments/activities' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/studies/{study_id}/versions/{version_id}/assignments/arms`

**Summary:** Assign Visits To Arm Endpoint  
**Operation ID:** `assign_visits_to_arm_endpoint_api_v1_studies__study_id__versions__version_id__assignments_arms_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Mandatory 60s `sig_token` required for Part 11 sign-off

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |
| `version_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `VisitToArmAssignmentRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `arm_id` | `string` | ✔ Yes | — |
| `visit_ids` | `Array<string>` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `object` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/assignments/arms', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/assignments/arms',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/assignments/arms' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/studies/{study_id}/versions/{version_id}/assignments/epochs`

**Summary:** Assign Visits To Epoch Endpoint  
**Operation ID:** `assign_visits_to_epoch_endpoint_api_v1_studies__study_id__versions__version_id__assignments_epochs_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Mandatory 60s `sig_token` required for Part 11 sign-off

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |
| `version_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `VisitToEpochAssignmentRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `epoch_id` | `string` | ✔ Yes | — |
| `visit_ids` | `Array<string>` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `object` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/assignments/epochs', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/assignments/epochs',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/assignments/epochs' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/studies/{study_id}/versions/{version_id}/blocks`

**Summary:** Create Block Endpoint  
**Operation ID:** `create_block_endpoint_api_v1_studies__study_id__versions__version_id__blocks_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |
| `version_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `CreateBlockRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `id` | `string` | ✔ Yes | — |
| `block_type` | `string` | ✔ Yes | — |
| `order` | `integer` | ✔ Yes | — |
| `properties` | `object` | ✔ Yes | — |
| `change_reason` | `string` | `null` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `BlockCreatedResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/blocks', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/blocks',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/blocks' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/studies/{study_id}/versions/{version_id}/blocks`

**Summary:** List Blocks Endpoint  
**Operation ID:** `list_blocks_endpoint_api_v1_studies__study_id__versions__version_id__blocks_get`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |
| `version_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<BlockDetailResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/blocks', {
  method: 'GET',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/blocks',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/blocks' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/studies/{study_id}/versions/{version_id}/blocks/reorder`

**Summary:** Reorder Blocks Endpoint  
**Operation ID:** `reorder_blocks_endpoint_api_v1_studies__study_id__versions__version_id__blocks_reorder_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |
| `version_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `ReorderBlocksRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `block_ids` | `Array<string>` | ✔ Yes | — |
| `change_reason` | `string` | `null` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `SoALinkResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/blocks/reorder', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/blocks/reorder',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/blocks/reorder' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `PUT` `/api/v1/studies/{study_id}/versions/{version_id}/blocks/{block_id}`

**Summary:** Update Block Endpoint  
**Operation ID:** `update_block_endpoint_api_v1_studies__study_id__versions__version_id__blocks__block_id__put`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |
| `version_id` | `path` | `string` | ✔ Yes | — |
| `block_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `UpdateBlockRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `properties` | `object` | ✔ Yes | — |
| `change_reason` | `string` | `null` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `BlockCreatedResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/blocks/{block_id}', {
  method: 'PUT',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.put(
        '/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/blocks/{block_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X PUT 'http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/blocks/{block_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `DELETE` `/api/v1/studies/{study_id}/versions/{version_id}/blocks/{block_id}`

**Summary:** Delete Block Endpoint  
**Operation ID:** `delete_block_endpoint_api_v1_studies__study_id__versions__version_id__blocks__block_id__delete`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |
| `version_id` | `path` | `string` | ✔ Yes | — |
| `block_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `BlockCreatedResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/blocks/{block_id}', {
  method: 'DELETE',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.delete(
        '/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/blocks/{block_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X DELETE 'http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/blocks/{block_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/studies/{study_id}/versions/{version_id}/blocks/{block_id}`

**Summary:** Get Block Endpoint  
**Operation ID:** `get_block_endpoint_api_v1_studies__study_id__versions__version_id__blocks__block_id__get`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |
| `version_id` | `path` | `string` | ✔ Yes | — |
| `block_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `BlockDetailResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/blocks/{block_id}', {
  method: 'GET',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/blocks/{block_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/blocks/{block_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/studies/{study_id}/versions/{version_id}/epochs`

**Summary:** Create Epoch Endpoint  
**Operation ID:** `create_epoch_endpoint_api_v1_studies__study_id__versions__version_id__epochs_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |
| `version_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `CreateEpochRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `id` | `string` | ✔ Yes | Unique identifier for the epoch. |
| `properties` | `EpochProperties` | ✔ Yes | — |
| `change_reason` | `string` | No | Change reason for audit trail |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `SoAEntityCreatedResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/epochs', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/epochs',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/epochs' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/studies/{study_id}/versions/{version_id}/epochs`

**Summary:** List Epochs Endpoint  
**Operation ID:** `list_epochs_endpoint_api_v1_studies__study_id__versions__version_id__epochs_get`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |
| `version_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<SoAEntityDetail>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/epochs', {
  method: 'GET',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/epochs',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/epochs' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/studies/{study_id}/versions/{version_id}/epochs/reorder`

**Summary:** Reorder Epochs Endpoint  
**Operation ID:** `reorder_epochs_endpoint_api_v1_studies__study_id__versions__version_id__epochs_reorder_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |
| `version_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `EpochReorderRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `epochs` | `Array<EpochReorderItem>` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `object` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/epochs/reorder', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/epochs/reorder',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/epochs/reorder' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `DELETE` `/api/v1/studies/{study_id}/versions/{version_id}/epochs/{epoch_id}`

**Summary:** Retire Epoch Endpoint  
**Operation ID:** `retire_epoch_endpoint_api_v1_studies__study_id__versions__version_id__epochs__epoch_id__delete`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |
| `version_id` | `path` | `string` | ✔ Yes | — |
| `epoch_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `SoAEntityCreatedResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/epochs/{epoch_id}', {
  method: 'DELETE',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.delete(
        '/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/epochs/{epoch_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X DELETE 'http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/epochs/{epoch_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/studies/{study_id}/versions/{version_id}/epochs/{epoch_id}`

**Summary:** Get Epoch Endpoint  
**Operation ID:** `get_epoch_endpoint_api_v1_studies__study_id__versions__version_id__epochs__epoch_id__get`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |
| `version_id` | `path` | `string` | ✔ Yes | — |
| `epoch_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `SoAEntityDetail` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/epochs/{epoch_id}', {
  method: 'GET',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/epochs/{epoch_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/epochs/{epoch_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `PUT` `/api/v1/studies/{study_id}/versions/{version_id}/epochs/{epoch_id}`

**Summary:** Update Epoch Endpoint  
**Operation ID:** `update_epoch_endpoint_api_v1_studies__study_id__versions__version_id__epochs__epoch_id__put`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |
| `version_id` | `path` | `string` | ✔ Yes | — |
| `epoch_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `UpdateEpochRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `properties` | `EpochProperties` | ✔ Yes | — |
| `reason_for_change` | `string` | No | Reason for change for audit trail |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `SoAEntityCreatedResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/epochs/{epoch_id}', {
  method: 'PUT',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.put(
        '/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/epochs/{epoch_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X PUT 'http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/epochs/{epoch_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `DELETE` `/api/v1/studies/{study_id}/versions/{version_id}/links/arm-applicability`

**Summary:** Retire Arm Applicability Endpoint  
**Operation ID:** `retire_arm_applicability_endpoint_api_v1_studies__study_id__versions__version_id__links_arm_applicability_delete`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |
| `version_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `LinkArmApplicabilityRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `arm_id` | `string` | ✔ Yes | — |
| `target_id` | `string` | ✔ Yes | — |
| `target_type` | `string` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `SoALinkResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/links/arm-applicability', {
  method: 'DELETE',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.delete(
        '/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/links/arm-applicability',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X DELETE 'http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/links/arm-applicability' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/studies/{study_id}/versions/{version_id}/links/arm-applicability`

**Summary:** Link Arm Applicability Endpoint  
**Operation ID:** `link_arm_applicability_endpoint_api_v1_studies__study_id__versions__version_id__links_arm_applicability_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |
| `version_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `LinkArmApplicabilityRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `arm_id` | `string` | ✔ Yes | — |
| `target_id` | `string` | ✔ Yes | — |
| `target_type` | `string` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `SoALinkResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/links/arm-applicability', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/links/arm-applicability',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/links/arm-applicability' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `DELETE` `/api/v1/studies/{study_id}/versions/{version_id}/links/epoch-visit`

**Summary:** Retire Epoch Visit Endpoint  
**Operation ID:** `retire_epoch_visit_endpoint_api_v1_studies__study_id__versions__version_id__links_epoch_visit_delete`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |
| `version_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `LinkEpochVisitRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `epoch_id` | `string` | ✔ Yes | — |
| `visit_id` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `SoALinkResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/links/epoch-visit', {
  method: 'DELETE',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.delete(
        '/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/links/epoch-visit',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X DELETE 'http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/links/epoch-visit' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/studies/{study_id}/versions/{version_id}/links/epoch-visit`

**Summary:** Link Epoch Visit Endpoint  
**Operation ID:** `link_epoch_visit_endpoint_api_v1_studies__study_id__versions__version_id__links_epoch_visit_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |
| `version_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `LinkEpochVisitRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `epoch_id` | `string` | ✔ Yes | — |
| `visit_id` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `SoALinkResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/links/epoch-visit', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/links/epoch-visit',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/links/epoch-visit' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `DELETE` `/api/v1/studies/{study_id}/versions/{version_id}/links/timing`

**Summary:** Retire Timing Endpoint  
**Operation ID:** `retire_timing_endpoint_api_v1_studies__study_id__versions__version_id__links_timing_delete`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |
| `version_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `LinkTimingRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `source_id` | `string` | ✔ Yes | — |
| `timing_id` | `string` | ✔ Yes | — |
| `source_type` | `string` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `SoALinkResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/links/timing', {
  method: 'DELETE',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.delete(
        '/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/links/timing',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X DELETE 'http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/links/timing' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/studies/{study_id}/versions/{version_id}/links/timing`

**Summary:** Link Timing Endpoint  
**Operation ID:** `link_timing_endpoint_api_v1_studies__study_id__versions__version_id__links_timing_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |
| `version_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `LinkTimingRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `source_id` | `string` | ✔ Yes | — |
| `timing_id` | `string` | ✔ Yes | — |
| `source_type` | `string` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `SoALinkResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/links/timing', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/links/timing',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/links/timing' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `DELETE` `/api/v1/studies/{study_id}/versions/{version_id}/links/visit-procedure`

**Summary:** Retire Visit Procedure Endpoint  
**Operation ID:** `retire_visit_procedure_endpoint_api_v1_studies__study_id__versions__version_id__links_visit_procedure_delete`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |
| `version_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `LinkVisitProcedureRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `visit_id` | `string` | ✔ Yes | — |
| `procedure_id` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `SoALinkResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/links/visit-procedure', {
  method: 'DELETE',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.delete(
        '/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/links/visit-procedure',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X DELETE 'http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/links/visit-procedure' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/studies/{study_id}/versions/{version_id}/links/visit-procedure`

**Summary:** Link Visit Procedure Endpoint  
**Operation ID:** `link_visit_procedure_endpoint_api_v1_studies__study_id__versions__version_id__links_visit_procedure_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |
| `version_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `LinkVisitProcedureRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `visit_id` | `string` | ✔ Yes | — |
| `procedure_id` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `SoALinkResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/links/visit-procedure', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/links/visit-procedure',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/links/visit-procedure' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/studies/{study_id}/versions/{version_id}/procedures`

**Summary:** Create Procedure Endpoint  
**Operation ID:** `create_procedure_endpoint_api_v1_studies__study_id__versions__version_id__procedures_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |
| `version_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `CreateProcedureRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `id` | `string` | ✔ Yes | Unique identifier for the procedure. |
| `properties` | `ProcedureProperties` | ✔ Yes | — |
| `change_reason` | `string` | No | Change reason for audit trail |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `SoAEntityCreatedResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/procedures', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/procedures',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/procedures' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/studies/{study_id}/versions/{version_id}/procedures`

**Summary:** List Procedures Endpoint  
**Operation ID:** `list_procedures_endpoint_api_v1_studies__study_id__versions__version_id__procedures_get`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |
| `version_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<SoAEntityDetail>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/procedures', {
  method: 'GET',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/procedures',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/procedures' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/studies/{study_id}/versions/{version_id}/procedures/reorder`

**Summary:** Reorder Procedures Endpoint  
**Operation ID:** `reorder_procedures_endpoint_api_v1_studies__study_id__versions__version_id__procedures_reorder_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |
| `version_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `ProcedureReorderRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `procedures` | `Array<ProcedureReorderItem>` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `object` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/procedures/reorder', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/procedures/reorder',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/procedures/reorder' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `DELETE` `/api/v1/studies/{study_id}/versions/{version_id}/procedures/{procedure_id}`

**Summary:** Retire Procedure Endpoint  
**Operation ID:** `retire_procedure_endpoint_api_v1_studies__study_id__versions__version_id__procedures__procedure_id__delete`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |
| `version_id` | `path` | `string` | ✔ Yes | — |
| `procedure_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `SoAEntityCreatedResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/procedures/{procedure_id}', {
  method: 'DELETE',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.delete(
        '/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/procedures/{procedure_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X DELETE 'http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/procedures/{procedure_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/studies/{study_id}/versions/{version_id}/procedures/{procedure_id}`

**Summary:** Get Procedure Endpoint  
**Operation ID:** `get_procedure_endpoint_api_v1_studies__study_id__versions__version_id__procedures__procedure_id__get`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |
| `version_id` | `path` | `string` | ✔ Yes | — |
| `procedure_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `SoAEntityDetail` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/procedures/{procedure_id}', {
  method: 'GET',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/procedures/{procedure_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/procedures/{procedure_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `PUT` `/api/v1/studies/{study_id}/versions/{version_id}/procedures/{procedure_id}`

**Summary:** Update Procedure Endpoint  
**Operation ID:** `update_procedure_endpoint_api_v1_studies__study_id__versions__version_id__procedures__procedure_id__put`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |
| `version_id` | `path` | `string` | ✔ Yes | — |
| `procedure_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `UpdateProcedureRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `properties` | `ProcedureProperties` | ✔ Yes | — |
| `reason_for_change` | `string` | No | Reason for change for audit trail |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `SoAEntityCreatedResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/procedures/{procedure_id}', {
  method: 'PUT',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.put(
        '/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/procedures/{procedure_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X PUT 'http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/procedures/{procedure_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/studies/{study_id}/versions/{version_id}/publish`

**Summary:** Publish Study Version Endpoint  
**Operation ID:** `publish_study_version_endpoint_api_v1_studies__study_id__versions__version_id__publish_post`  

**Clinical & GxP Rationale:**  
Two-Phase Study Version State Machine:
1. transition target version status to 'PENDING_PUBLISH'
2. synchronously call downstream execution setup / clinical migration
3. if downstream succeeds, transition target version status to 'ACTIVE'
4. if downstream fails or times out (15s window), rollback status to 'DRAFT',
   record exact execution error to audit trail, release lock resources,
   and return validation error to initiating researcher.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |
| `version_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `PublishStudyVersionRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `description` | `string` | `null` | No | Amendment description |
| `baseline_snapshot` | `object` | `null` | No | Optional baseline snapshot overrides |
| `amended_snapshot` | `object` | `null` | No | Optional amended snapshot overrides |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `PublishStudyVersionResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/publish', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/publish',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/publish' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/studies/{study_id}/versions/{version_id}/sign-off`

**Summary:** Approve Study Version Endpoint  
**Operation ID:** `approve_study_version_endpoint_api_v1_studies__study_id__versions__version_id__sign_off_post`  

**Clinical & GxP Rationale:**  
Approve and cryptographically sign a Metadata Designer clinical protocol version, producing
a 21 CFR Part 11 compliant persisted signature manifestation, recording immutable Action history,
and locks the protocol version by transitioning its status to APPROVED.
Locked statuses (APPROVED, SIGNED) block any subsequent edits via immutability checks across both Neo4j and mock databases.
Successfully approved protocols are archived as PROTOCOL_SIGNOFF artifacts to the eTMF service asynchronously.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Mandatory 60s `sig_token` required for Part 11 sign-off

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |
| `version_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `ApproveProtocolRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `signing_reason` | `SigningReason` | ✔ Yes | Reason for signing |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `object` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/sign-off', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/sign-off',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/sign-off' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/studies/{study_id}/versions/{version_id}/soa-projection`

**Summary:** Get Soa Projection Endpoint  
**Operation ID:** `get_soa_projection_endpoint_api_v1_studies__study_id__versions__version_id__soa_projection_get`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |
| `version_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `SoAMatrixView` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/soa-projection', {
  method: 'GET',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/soa-projection',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/soa-projection' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/studies/{study_id}/versions/{version_id}/timing-windows`

**Summary:** Create Timing Window Endpoint  
**Operation ID:** `create_timing_window_endpoint_api_v1_studies__study_id__versions__version_id__timing_windows_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |
| `version_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `CreateTimingWindowRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `id` | `string` | ✔ Yes | Unique identifier for the timing window. |
| `properties` | `TimingWindowProperties` | ✔ Yes | — |
| `change_reason` | `string` | No | Change reason for audit trail |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `SoAEntityCreatedResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/timing-windows', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/timing-windows',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/timing-windows' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/studies/{study_id}/versions/{version_id}/timing-windows`

**Summary:** List Timing Windows Endpoint  
**Operation ID:** `list_timing_windows_endpoint_api_v1_studies__study_id__versions__version_id__timing_windows_get`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |
| `version_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<SoAEntityDetail>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/timing-windows', {
  method: 'GET',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/timing-windows',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/timing-windows' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `DELETE` `/api/v1/studies/{study_id}/versions/{version_id}/timing-windows/{timing_id}`

**Summary:** Retire Timing Window Endpoint  
**Operation ID:** `retire_timing_window_endpoint_api_v1_studies__study_id__versions__version_id__timing_windows__timing_id__delete`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |
| `version_id` | `path` | `string` | ✔ Yes | — |
| `timing_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `SoAEntityCreatedResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/timing-windows/{timing_id}', {
  method: 'DELETE',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.delete(
        '/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/timing-windows/{timing_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X DELETE 'http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/timing-windows/{timing_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/studies/{study_id}/versions/{version_id}/timing-windows/{timing_id}`

**Summary:** Get Timing Window Endpoint  
**Operation ID:** `get_timing_window_endpoint_api_v1_studies__study_id__versions__version_id__timing_windows__timing_id__get`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |
| `version_id` | `path` | `string` | ✔ Yes | — |
| `timing_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `SoAEntityDetail` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/timing-windows/{timing_id}', {
  method: 'GET',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/timing-windows/{timing_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/timing-windows/{timing_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `PUT` `/api/v1/studies/{study_id}/versions/{version_id}/timing-windows/{timing_id}`

**Summary:** Update Timing Window Endpoint  
**Operation ID:** `update_timing_window_endpoint_api_v1_studies__study_id__versions__version_id__timing_windows__timing_id__put`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |
| `version_id` | `path` | `string` | ✔ Yes | — |
| `timing_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `UpdateTimingWindowRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `properties` | `TimingWindowProperties` | ✔ Yes | — |
| `reason_for_change` | `string` | No | Reason for change for audit trail |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `SoAEntityCreatedResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/timing-windows/{timing_id}', {
  method: 'PUT',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.put(
        '/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/timing-windows/{timing_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X PUT 'http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/timing-windows/{timing_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/studies/{study_id}/versions/{version_id}/visits`

**Summary:** Create Visit Endpoint  
**Operation ID:** `create_visit_endpoint_api_v1_studies__study_id__versions__version_id__visits_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |
| `version_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `apps__designer__domain__protocol_authoring__soa__CreateVisitRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `id` | `string` | ✔ Yes | Unique identifier for the visit. |
| `properties` | `VisitProperties` | ✔ Yes | — |
| `change_reason` | `string` | No | Change reason for audit trail |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `SoAEntityCreatedResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/visits', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/visits',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/visits' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/studies/{study_id}/versions/{version_id}/visits`

**Summary:** List Visits Endpoint  
**Operation ID:** `list_visits_endpoint_api_v1_studies__study_id__versions__version_id__visits_get`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |
| `version_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<SoAEntityDetail>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/visits', {
  method: 'GET',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/visits',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/visits' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/studies/{study_id}/versions/{version_id}/visits/reorder`

**Summary:** Reorder Visits Endpoint  
**Operation ID:** `reorder_visits_endpoint_api_v1_studies__study_id__versions__version_id__visits_reorder_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |
| `version_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `VisitReorderRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `visits` | `Array<VisitReorderItem>` | ✔ Yes | Ordered list of visit sequence updates. |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `object` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/visits/reorder', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/visits/reorder',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/visits/reorder' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `DELETE` `/api/v1/studies/{study_id}/versions/{version_id}/visits/{visit_id}`

**Summary:** Retire Visit Endpoint  
**Operation ID:** `retire_visit_endpoint_api_v1_studies__study_id__versions__version_id__visits__visit_id__delete`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |
| `version_id` | `path` | `string` | ✔ Yes | — |
| `visit_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `SoAEntityCreatedResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/visits/{visit_id}', {
  method: 'DELETE',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.delete(
        '/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/visits/{visit_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X DELETE 'http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/visits/{visit_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/studies/{study_id}/versions/{version_id}/visits/{visit_id}`

**Summary:** Get Visit Endpoint  
**Operation ID:** `get_visit_endpoint_api_v1_studies__study_id__versions__version_id__visits__visit_id__get`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |
| `version_id` | `path` | `string` | ✔ Yes | — |
| `visit_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `SoAEntityDetail` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/visits/{visit_id}', {
  method: 'GET',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/visits/{visit_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/visits/{visit_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `PUT` `/api/v1/studies/{study_id}/versions/{version_id}/visits/{visit_id}`

**Summary:** Update Visit Endpoint  
**Operation ID:** `update_visit_endpoint_api_v1_studies__study_id__versions__version_id__visits__visit_id__put`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |
| `version_id` | `path` | `string` | ✔ Yes | — |
| `visit_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `apps__designer__domain__protocol_authoring__soa__UpdateVisitRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `properties` | `VisitProperties` | ✔ Yes | — |
| `reason_for_change` | `string` | No | Reason for change for audit trail |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `SoAEntityCreatedResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/visits/{visit_id}', {
  method: 'PUT',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.put(
        '/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/visits/{visit_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X PUT 'http://localhost:8000/api/v1/designer/api/v1/studies/{study_id}/versions/{version_id}/visits/{visit_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/synopsis/export`

**Summary:** Export Synopsis Document  
**Operation ID:** `export_synopsis_document_api_v1_synopsis_export_post`  

**Clinical & GxP Rationale:**  
Export authored clinical protocol synopsis into PDF, DOCX, or HTML format.

Requirements: PRD-SYS-001

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `SynopsisExportRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `study_id` | `string` | ✔ Yes | Unique protocol study identifier |
| `format` | `string` | No | Target export format: 'pdf', 'docx', or 'html' |
| `creator` | `string` | `null` | No | Author or creator username |
| `change_reason` | `string` | `null` | No | GxP 21 CFR Part 11 change reason |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `SynopsisExportResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/synopsis/export', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/designer/api/v1/synopsis/export',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/designer/api/v1/synopsis/export' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/synopsis/render/{study_id}`

**Summary:** Render Synopsis Download  
**Operation ID:** `render_synopsis_download_api_v1_synopsis_render__study_id__get`  

**Clinical & GxP Rationale:**  
Direct file download endpoint for rendered protocol synopsis documents.

Requirements: PRD-SYS-001

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |
| `format` | `query` | `string` | No | Export format: pdf, docx, html |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `object` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/synopsis/render/{study_id}', {
  method: 'GET',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/designer/api/v1/synopsis/render/{study_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/designer/api/v1/synopsis/render/{study_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/terminology/search`

**Summary:** Search Terminology  
**Operation ID:** `search_terminology_api_v1_terminology_search_get`  

**Clinical & GxP Rationale:**  
Search or autocomplete terminology concepts by text query.

Args:
    term (str): Search term.
    from_record (int, optional): Record offset.
    page_size (int, optional): Page size.
    bypass_cache (bool, optional): Whether to bypass reading from cache. Defaults to False.
    refresh (bool, optional): Whether to refresh the cache. Defaults to False.

Returns:
    TerminologySearchResponse: Search results and status.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `term` | `query` | `string` | ✔ Yes | — |
| `from_record` | `query` | `integer` | `null` | No | — |
| `page_size` | `query` | `integer` | `null` | No | — |
| `bypass_cache` | `query` | `boolean` | No | — |
| `refresh` | `query` | `boolean` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `TerminologySearchResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/terminology/search', {
  method: 'GET',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/designer/api/v1/terminology/search',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/designer/api/v1/terminology/search' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/terminology/validate/{code}`

**Summary:** Validate Single Code  
**Operation ID:** `validate_single_code_api_v1_terminology_validate__code__get`  

**Clinical & GxP Rationale:**  
Validates a single terminology concept code.

Args:
    code (str): The concept code to validate.

Returns:
    ConceptValidationReport: Validation status and metadata.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `code` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `ConceptValidationReport` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v1/terminology/validate/{code}', {
  method: 'GET',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/designer/api/v1/terminology/validate/{code}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/designer/api/v1/terminology/validate/{code}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v2/studies/{study_id}/usdm`

**Summary:** Get Usdm Study  
**Operation ID:** `get_usdm_study_api_v2_studies__study_id__usdm_get`  

**Clinical & GxP Rationale:**  
Dynamically processes the internal projection and returns a compliant USDM structure.

Args:
    study_id (str): The unique identifier of the study.
    format (str, optional): Output format.

Returns:
    Any: Mapped USDM data (as dict or serialized Response).

Raises:
    HTTPException: If the study is not found or validation fails.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |
| `format` | `query` | `string` | `null` | No | Output format: json or yaml |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `object` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v2/studies/{study_id}/usdm', {
  method: 'GET',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/designer/api/v2/studies/{study_id}/usdm',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/designer/api/v2/studies/{study_id}/usdm' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v2/studies/{study_id}/usdm`

**Summary:** Import Usdm Study  
**Operation ID:** `import_usdm_study_api_v2_studies__study_id__usdm_post`  

**Clinical & GxP Rationale:**  
Ingests, validates, maps, and persists a USDM JSON/YAML payload for a specific study.

Requirements: Phase 2 Ingestion

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |
| `override` | `query` | `string` | `null` | No | Optional explicit version override ('v2' or 'v3') |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `object` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/api/v2/studies/{study_id}/usdm', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/designer/api/v2/studies/{study_id}/usdm',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/designer/api/v2/studies/{study_id}/usdm' \
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

Returns a basic JSON payload indicating the service is operational.

Returns:
    Dict[str, str]: The health status payload.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `object` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/designer/health', {
  method: 'GET',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/designer/health',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/designer/health' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

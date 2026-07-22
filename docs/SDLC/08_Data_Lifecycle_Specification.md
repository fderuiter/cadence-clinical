# 08_Data_Lifecycle_Specification

This document defines the central specification for clinical data flows, metadata graph versioning, database shadow triggers, ledger synchronization, and clinical data export standards.

## Bidirectional Links to SDLC Documentation
- [Product Requirements Document (PRD)](./01_Product_Requirements_Document_PRD.md)
- [Software Requirements Specification (SRS)](../SRS.md)
- [Architecture Overview](../../ARCHITECTURE.md)
- [Technical Design Document (TDD)](./02_Technical_Design_Document_TDD.md)
- [API & Interoperability Specification](./03_API_Integration_Specification.md)
- [Data Standards Interoperability Blueprint](./04_Data_Standards_Interoperability_Blueprint.md)
- [Security & Compliance Audit Spec](./05_Security_Compliance_Audit_Spec.md)
- [Quality Assurance Validation Plan](./06_QA_Validation_Plan.md)
- [Developer/Agent Guidelines](../../AGENTS.md)

## Section A: Metadata Graph Versioning Rules
- **Model / Database:** Managed in **Neo4j** (Designer Service) for upstream metadata structures (e.g., studies, arms, epochs, visits, branches, or elements).
- **Versioning Principles:** Overwriting existing nodes is strictly forbidden. Any updates or changes must spawn a new, versioned node.
- **Historical Continuity:** Version nodes are linked via directed temporal relationships (e.g., `PREVIOUS_VERSION`) to form an immutable chronological path. This enables deterministic playback or point-in-time reconstruction of study configurations.
- **Encryption:** Neo4j filesystems, block storage, and databases must be encrypted at rest using **AES-256-GCM** keys managed via AWS KMS or HashiCorp Vault.

## Section B: Database Audit Shadow Triggers & Ledger Synchronization
- **Model / Database:** Relational transaction storage in **PostgreSQL** (Execution Service).
- **Audit Schema (`public.audit_logs`):**
  - Clinical tables are registered with native `AFTER UPDATE` and `AFTER DELETE` database triggers.
  - Mutations are processed by the trigger function `capture_model_mutation()` and recorded in `public.audit_logs` with transactional payload fields (`old_values` and `new_values` as JSONB) along with audit details (`user_id` and `change_reason`).
- **Mutation & Soft-Deletes:**
  - Hard deletes are strictly forbidden on clinical entities at the trigger layer. Any attempt to run an actual SQL `DELETE` triggers a GxP exception: `"GxP Compliance Violation: Hard deletions are strictly forbidden for clinical entities. Use soft deletes by updating is_deleted=True."`
  - Soft deletes (updating `is_deleted = True`) are captured and flagged as `'DELETE'` in the audit trail.
  - The audit logs table itself is locked down by a trigger (`prevent_audit_log_mutation()`) which throws: `"GxP Compliance Violation: Modification or deletion of audit logs is strictly prohibited."`
- **Application-Layer Ledger Synchronization:**
  - **Thread-safe `contextvars`:** Thread-safe state tracking (`propagate_db_session_context`) propagates OIDC `user_id` and clinical `change_reason` from application contexts (e.g., FastAPI) to PostgreSQL local variables (`cadence.current_user_id` and `cadence.current_change_reason`).
  - **Cryptographic Chaining (`public.audit_ledger_seals`):** Background process runs every 60 seconds (or after 100 raw logs) to generate SHA-256 block hashes chaining previous blocks:
    $$\text{Block\_Hash}_N = \text{SHA-256}\left( \text{Block\_Hash}_{N-1} \parallel \sum \text{Record\_Hash} \right)$$
  - **Integrity Sweeps:** Daily cron jobs recalculate the Merkle root and block hashes, flagging alerts if a schema/database drift is detected.
- **Application-Level Security & Blinding Controls:**
  - **Database-Level Audit Protection:** Application-level soft-deletes are complemented by hard-delete blocks on ORM flush listeners, guaranteeing no administrative bypass.
  - **Multi-Party Cryptographic Key Splitting:** Treatment allocation blinding keys are distributed using threshold cryptography to prevent unilateral unblinding, coupled with automatic annual key rotation (365-day lifecycle).
  - **Automated Trial Locks:** Safety compromises automatically trigger a read-only lock (blocking `INSERT`, `UPDATE`, `DELETE`, while permitting `SELECT` for safety) globally or per trial, coupled with instant multi-channel alerts (SMS, Email, Webhook) under 60 seconds.

## Section C: Clinical Data Export Boundaries & Transmission States
- **CDISC Standards Integration:**
  - **CDISC USDM (v3.0/v4.0):** Standard models for representing clinical metadata designs in Neo4j.
  - **CDASH:** Compilation of eCRF templates from graph metadata, conforming to CDASH variable names (e.g., `BRTHDTC`, standard domain prefixes like `VS.` and `AE.`).
  - **CDISC ODM XML/JSON Bulk Export:** Accessible via `GET /api/v1/execution/studies/{study_id}/export` supporting formats like `ODM-XML` (versions 1.3.2 and 2.0).
  - **SDTM (Study Data Tabulation Model):** Translates operational transactional databases to core domains such as Demographics (`DM`), Adverse Events (`AE`), Vital Signs (`VS`), Lab Findings (`LB`), and Medical History (`MH`). Non-standard fields are compiled to Supplemental Qualifiers (`SUPP--`).
  - **ADaM (Analysis Data Model):** Standard analysis datasets derived directly from SDTM inputs (e.g., `ADSL` derived from `SDTM DM`, `SDTM EX`, etc., `ADAE`, `ADVS`).
- **Wire Formats & Transmission:**
  - Standard wire transport is compressed using `br` (Brotli), `gzip`, or `deflate` on payloads over 2 KB, negotiated via `Accept-Encoding`.
- **Export Security & Anonymization Policies:**
  - **Direct Identifier Hashing:** Direct PII fields are hashed using `HMAC-SHA256` with a secure vault-stored salt.
  - **Quasi-Identifier Obfuscation:** 
    - Age capping: Ages over 89 are capped to `"89 or older"`.
    - Date shifting: Transaction dates (except death) are offset per subject by a deterministic random delta ($\text{ShiftDelta}_{\text{subject}} \in [-30, +30]$ days).
    - $k$-Anonymity & $l$-Diversity: Ensure $k=5$ identical profiles and sufficient $l$ diversity on sensitive outputs.

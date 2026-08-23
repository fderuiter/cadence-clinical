# ADR-2203: Hermetic Containerized Statistical Computing Environment and Role-Gated Unblinded Data Vault

* **Status:** Accepted
* **Date:** 2026-08-22
* **Authors:** @fderuiter
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

In biostatistics and clinical data science, regulatory authorities (FDA, EMA, PMDA) require 100% computational reproducibility under 21 CFR Part 11 and GAMP 5 Category 5. Statistical analyses generating Study Data Tabulation Model (SDTM) and Analysis Data Model (ADaM) datasets, Summary Tables, Listings, and Figures (TLFs) must produce bit-for-bit identical results regardless of when or where the scripts are executed. Furthermore, unblinded treatment arm allocations must remain strictly quarantined from standard study biostatisticians and monitors until formal database lock or approved interim unblinding by the Independent Data Monitoring Committee (IDMC). We require a validated, hermetic Statistical Computing Environment (SCE) and unblinded data isolation vault (PRD-CDR-004, PRD-CDR-005, PRD-CDR-006).

## 2. Decision Drivers & Constraints

* **21 CFR Part 11 Auditability & Bit-for-Bit Reproducibility**: Execution environments must enforce immutable package dependency locks (`renv.lock` for R, `uv.lock` for Python) and version-pinned read-only dataset snapshots.
* **Strict Double-Blind Protection**: Unblinded treatment variables (`ARMCD`, `TRT01P`, `RANDDT`) must be encrypted at rest and accessible only to cryptographically authenticated unblinded roles.
* **Hermetic Sandbox Security**: Script execution must run in ephemeral, isolated Linux containers with zero egress network access, read-only root filesystems, and bounded CPU/memory limits.

## 3. Options Considered

1. **Hermetic Docker Containers with Version-Pinned Data Snapshots & Role-Gated Vault (Selected)**: Statistical execution is spawned in ephemeral, air-gapped Linux containers provisioned with pre-compiled, cryptographically hashed package caches. The CDR engine projects read-only dataset snapshots. Unblinded variables reside in an AES-256 encrypted vault, decrypted only during signed IDMC unblinded sessions. Every run generates a signed manifest capturing script hash, input snapshot hash, container digest, output dataset hashes, and biostatistician identity.
2. **Shared Persistent Virtual Machine**: Multiple statisticians execute scripts on a long-lived shared server with mutable system libraries and unpinned packages.
3. **External SAS/R Client Execution**: Statisticians download CSV files to local laptops and run scripts locally without central audit logging.

## 4. Decision Outcome

Chosen option: **Hermetic Docker Containers with Version-Pinned Data Snapshots & Role-Gated Vault**.
- Guarantees complete regulatory audit compliance and immutable execution history.
- Prevents package drift and OS-level environment contamination.
- Cryptographically protects study blinding integrity.

## 5. Consequences & Trade-offs

* **Positive**:
  - Uncompromising computational reproducibility across regulatory inspection audits.
  - Zero risk of accidental unblinding for blinded clinical teams.
  - Immutable SHA-256 signed execution manifests for all submission outputs.
* **Negative**:
  - Custom R/Python packages require pre-qualification and approval into the platform package repository.
  - Requires container runtime orchestration in the deployment cluster.

## 6. Implementation & Verification

* **Requirements:** Specified in `docs/SDLC/01_Product_Requirements_Document_PRD.md` under `PRD-CDR-003`.
* **Domain Model:** Captured and defined in `CONTEXT.md`.
* **GxP Traceability:** Synchronized via `uv run python scripts/sync_gxp.py`.


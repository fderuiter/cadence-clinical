# ADR-3991: Audit Auto-Populated Translation Fallbacks and Propagate Warnings Metadata

- **Status:** Accepted
- **Date:** 2026-08-18
- **Authors:** @google-labs-jules
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

In the Execution Engine translation workflow (`apps/execution/translator.py`), converting study protocols to CDISC ODM and OpenRosa XML formats previously substituted missing or permissive fields with fallbacks without persistent audit logging. Under 21 CFR Part 11 and GxP compliance standards (Trace-20, PRD-SYS-001), all auto-populated fields and permissive fallbacks during study translation must be explicitly recorded and accessible via API responses without interrupting export pipelines.

## 2. Decision Drivers & Constraints

- **21 CFR Part 11 & GxP Auditability (PRD-SYS-001, Trace-20):** Field mutations or auto-populated fallbacks must be recorded in structured metadata attached to translation jobs.
- **Pipeline Robustness:** XML translation (CDISC ODM & OpenRosa XML) must complete successfully when missing fields are encountered, using permissive fallbacks while accurately flagging warnings.
- **API Transparency:** Clients querying translation job details (`GET /api/v1/execution/translation/jobs/{job_id}`) must receive structured warning details explaining any fallback substitutions.

## 3. Options Considered

### Option 1: Fail Translation Jobs on Missing Data

- **Overview:** Throw validation errors immediately whenever required XML metadata fields (e.g., study IDs, narrative content IDs) are missing.
- **Pros:**
  - Prevents fallback values from entering export payloads.
- **Cons:**
  - ❌ Blocks translation jobs for draft or incomplete study designs, reducing operational flexibility.

### Option 2: Persist Structured Warnings Metadata in TranslationJob Model (Selected)

- **Overview:** Add a `warnings` JSON column (`Mapped[list | dict]`) to `TranslationJob` in `apps/execution/database/models/designer.py`. Record all auto-populated fallback mutations in the background translator and expose them through `TranslationJobResponse`.
- **Pros:**
  - ✅ Ensures non-blocking XML export for draft designs while maintaining full auditability.
  - ✅ Fully transparent API metadata propagation for GxP compliance.
- **Cons:**
  - ❌ Adds JSON storage overhead on `TranslationJob` records.

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Option 2 preserves non-blocking operational workflows while satisfying regulatory auditability requirements (Trace-20, PRD-SYS-001).

## 5. Consequences & Trade-offs

- **Positive Impact:** Full auditability for auto-populated translation fallbacks without disrupting XML export pipelines.
- **Negative Impact / Technical Debt:** Slight JSON column overhead on the PostgreSQL `translation_jobs` table.
- **Mitigation Strategy:** Index translation job queries by `job_id` and limit warning payloads to structured key-value dictionaries.

## 6. Implementation & Verification

- **Affected Repositories / Services:**
  - `apps/execution/database/models/designer.py` (Added `warnings` column)
  - `apps/execution/translator.py` (Audited fallback recording and array aggregation)
  - `apps/execution/main.py` (`TranslationJobResponse` DTO and API routes)
- **Verification Plan:**
  - Verified via integration tests in `scripts/tests/test_translation_recovery.py` and `uv run python scripts/validate_adrs.py`.

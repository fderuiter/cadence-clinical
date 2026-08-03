# ADR-254: Fail-Safe GxP Document Generation and Draft Warning Banners

* **Status:** Accepted
* **Date:** 2026-08-03
* **Authors:** @jules
* **Deciders:** @engineering_leads, @qa_lead

---

## 1. Context & Problem Statement

In GxP-regulated systems, the generation of qualification artifacts (such as the Requirements Traceability Matrix and IQ/OQ/PQ Qualification Execution Report) must adhere to strict data integrity standards. Previously, the document generation pipeline silently masked errors when the JUnit test report (`report.xml`) was missing or empty, replacing actual outcomes with mock "PASSED" records and synthetic durations. In a clinical and regulatory audit setting, generating unverified and fabricated results introduces significant compliance and data integrity risks.

We need a solution that prevents silent generation of unverified final records, while preserving developer productivity and facilitating document layout/format previews in fast PR check-runs where running full integration test suites may be undesirable or skipped.

This decision directly implements requirements under **PRD-SYS-001** and **PRD-SYS-003**.

## 2. Decision Drivers & Constraints

* **Driver 1:** Absolute compliance with FDA 21 CFR Part 11 and GxP data integrity rules (no synthetic or falsified evidence of test pass).
* **Driver 2:** Developer ergonomics and build-time safety (ability to preview/generate documents to verify layout and links without full test execution).
* **Driver 3:** High visual visibility of unverified compliance documents to ensure they are never mistaken for final, production-ready records.

## 3. Options Considered

### Option 1: Legacy Mock Fallback
* **Overview:** Maintain the previous behavior where missing JUnit results are automatically assumed to have passed with mock duration.
* **Pros:** Does not break any local generation scripts.
* **Cons:** Violates core regulatory requirements by fabricating test proof, presenting a massive audit liability.

### Option 2: Strict Unconditional Fail-Fast
* **Overview:** Immediately terminate execution of `generate_rtm.py` and fail the pipeline whenever `report.xml` is missing or empty.
* **Pros:** Completely prevents unverified compliance document generation.
* **Cons:** Blocks developer previews and fast PR validation workflows, requiring full, time-consuming test executions even for trivial layout or documentation changes.

### Option 3: Strict Default Fail-Fast with Visual Draft Bypass (Chosen Option)
* **Overview:** Introduce a strict fail-fast mechanism as the default. If `report.xml` is missing, the script terminates immediately with exit code 1. However, introduce an explicit `--draft` CLI parameter. When running in `--draft` mode:
  - Generation completes successfully.
  - A prominent, warning markdown banner (`DRAFT_BANNER`) is prepended to the top of all generated documents.
  - All test outcomes missing from the report are mapped to `UNVERIFIED` (marked as `⚪ (UNVERIFIED)` or `⚪ UNVERIFIED`) and overall requirement status to `⚠️ **Unverified**`.
  - Durations are strictly set to `N/A`.
* **Pros:** 
  - Prevents accidental generation of final verified files.
  - Highly visible draft/unverified badges.
  - Seamless PR integration (the synchronization orchestrator automatically appends `--draft` for dry-run validation checks, verifying layout and links without full execution).
* **Cons:** Draft files could theoretically be committed if not blocked (mitigated by dry-run blocking of uncommitted files).

## 4. Decision Outcome

* **Chosen Option:** Option 3
* **Justification:** Option 3 provides a robust, fail-safe mechanism that preserves data integrity while maintaining excellent developer productivity. By enforcing a visual draft banner and explicit "UNVERIFIED" states, we eliminate any risk of unverified compliance reports being accepted as final records.

## 5. Consequences & Trade-offs

* **Positive Impact:** 
  - Direct alignment with GxP and Part 11 data integrity standards.
  - Prompt alert feedback in standard CLI runs when a test execution has been skipped or missed.
  - Easy visual recognition of draft artifacts for reviewers and compliance auditors.
* **Negative Impact / Technical Debt:** 
  - Developers must use `--draft` to preview documents if they have not run tests, or run the full orchestration helper.
  - Ensure orchestration and dry-run flows reject the staging of documents containing the draft warnings.

## 6. Implementation & Verification

* **Affected Components:**
  - `scripts/generate_rtm.py` (fail-fast logic, `--draft` parser option, banner insertion, `UNVERIFIED` and `N/A` value mapping).
  - `scripts/sync_gxp.py` (dry-run mode maps to `--draft` for validation, blocking staging of draft files).
* **Verification Plan:**
  - Standardized automated test coverage under `tests/test_gxp_fail_fast.py` to assert correct fail-fast behavior, `--draft` banner prepending, mapping to `UNVERIFIED` and `N/A`, and dry-run validation.

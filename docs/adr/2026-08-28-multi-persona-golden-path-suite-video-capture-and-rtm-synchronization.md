# ADR-2224: Multi Persona Golden Path Suite Video Capture and RTM Synchronization

* **Status:** Accepted
* **Date:** 2026-08-28
* **Authors:** @fderuiter
* **Deciders:** Engineering Architecture Board, Quality Assurance & Regulatory Affairs Committee
* **Requirements:** PRD-SYS-001, PRD-VAL-001, PRD-QA-001, PRD-SYS-053

---

## 1. Context & Problem Statement

Complete regulatory qualification of an eClinical software platform requires demonstrating that all disparate microservices, data models, and user personas function cohesively across the entire clinical trial lifecycle. Isolated unit and component tests cannot guarantee smooth human-in-the-loop role transitions, responsive UI workspace ergonomics, or un-broken end-to-end 21 CFR Part 11 audit trails.

The platform requires:
1. Deterministic sequential 6-persona lifecycle simulation (`sponsor_designer` -> `site_crc` -> `cra_monitor` -> `data_manager` -> `safety_physician` -> `auditor`) spanning protocol design, eConsent, subject enrollment, eCRF data entry, SDV sign-offs, query resolution, safety signal reporting, and audit inspection.
2. Automated 1080p demonstration-paced Playwright video recording (`RECORD_VIDEO=true` at 1920x1080) with cursor movement and click ripple highlights.
3. Triple Sentinel assertion gates across every simulation step: DOM state, client console/network hygiene (0 errors), and verified backend GxP audit ledgers.
4. Comprehensive 100% Requirements Traceability Matrix (RTM) and IQ/OQ/PQ execution report synchronization via `scripts/sync_gxp.py` across all 226 system requirements.

## 2. Decision Drivers & Constraints

* **FDA 21 CFR Part 11 & EU Annex 11**: System validation must provide documentary evidence that the system fulfills its intended purpose and regulatory requirements across all operating personas.
* **ICH GCP E6(R2)/(R3)**: Clear division of responsibilities across investigator, sponsor, monitor, data manager, and auditor roles must be enforced by role-based permissions and electronic signature step-ups.
* **Zero Client Errors**: Golden path execution must be completely clean with zero unhandled JavaScript console exceptions and zero unhandled HTTP network errors.
* **100% Traceability Coverage**: Every requirement in the PRD (192 requirements) and SRS (34 requirements) must map directly to automated verification tests.

## 3. Options Considered

1. **Option 1 (Selected): Sequential 6-Persona Playwright Golden Path + 1080p Video Recording + Triple Sentinel Gates + Automated RTM Sync**:
   - Executes full-lifecycle clinical scenarios transitioning state across all 6 clinical personas in standard Vanilla CSS client workspaces (`apps/web`).
   - Automatically records high-definition 1080p MP4 videos with human-like action pacing and click highlights for qualification evidence.
   - Triple Sentinel gates verify UI elements, client console hygiene, and backend audit trails (`created_by`, `reason_for_change`, `version_index`, SHA-256 digests).
   - Automated `scripts/sync_gxp.py` regenerates and commits RTM and IQ/OQ/PQ reports with 100% requirements coverage.

2. **Option 2: Fragmented Headless Unit Assertions**:
   - Rely solely on independent microservice unit tests without unified end-to-end persona flows or visual recordings.

3. **Option 3: Manual Human QA Testing**:
   - Rely on manual testers clicking through the application without automated video capture or machine-readable traceability synchronization.

## 4. Decision Outcome

**Chosen option:** Option 1 because it provides definitive proof of regulatory compliance, guarantees frontend and backend integration integrity, produces visual qualification artifacts for auditors, and achieves 100% GxP requirement traceability.

## 5. Consequences & Trade-offs

* **Positive**:
  - Unassailable regulatory qualification evidence with complete audit trail and video proof.
  - Verifies multi-persona access control, signature ceremonies, and state transitions in real time.
  - Zero compliance drift maintained via CI/CD gating scripts (`sync_gxp.py`).
* **Negative**:
  - Full end-to-end browser video recordings require additional compute time during qualification runs.

## 6. Implementation & Verification

* **Core Target Modules**:
  - `apps/web/tests/`: Multi-persona workspace tests, eCRF renderer, eConsent flows, and amendment diffs.
  - `tests/e2e/test_phase1_e2e_suite.py`: Full-lifecycle end-to-end trial workflows (Tiers 1–4).
  - `scripts/sync_gxp.py` & `scripts/generate_rtm.py`: GxP synchronization engine.
* **Verification Suites**:
  - `apps/web/tests/` (56 passed tests)
  - `tests/e2e/test_phase1_e2e_suite.py` (38 passed tests)
  - `uv run python scripts/sync_gxp.py` (3,819 passed execution outcomes across all 226 requirements)

# Quality Assurance (QA) & Validation Plan

**Document ID:** CAD-VAL-PLN-001
**Version:** 1.0.0
**Effective Date:** 2026-07-22
**Applicability:** Cadence Clinical Unified Platform (Designer & Execution Subsystems)
**Standards Compliance:** IEC 62304:2006/AMD1:2015, ISO 14155:2020, FDA 21 CFR Part 11, EU Annex 11, GAMP 5 (Second Edition)

---

## 1. Document Control & Approval Protocols

### 1.1 Document History
This document is maintained under strict version control within the Cadence Clinical platform's Quality Management System (QMS). Any modifications to this validation plan must undergo a formal impact assessment and be approved by the designated regulatory and clinical stakeholders prior to execution.

| Version | Date | Author(s) | Description of Change |
| :--- | :--- | :--- | :--- |
| 0.1.0 | 2026-07-20 | Quality Assurance Team | Initial outline structure initialized. |
| 1.0.0 | 2026-07-22 | Jules, Principal Validation Engineer | Comprehensive rewrite to full GxP, IEC 62304, and GAMP 5 compliance; integrated complete Bidirectional Traceability Matrix and four execution-ready verification protocols. |

### 1.2 Regulatory Sign-Off & Clinical Approvals
The signatures below declare that this Quality Assurance & Validation Plan meets all internal standard operating procedures (SOPs), clinical protocols, and statutory requirements for computerized systems used in clinical trials. By signing, the reviewers agree that the verification and validation (V&V) strategies detailed herein provide adequate evidence of fitness for purpose, data integrity, and compliance under 21 CFR Part 11 and EU Annex 11.

- **Lead Systems Validation Engineer:**
  *Signature / Electronic Verification:* `[VERIFIED: SECURE SHA-256 HASH]`
  *Date:* 2026-07-22
  *Role:* Responsible for verifying technical test execution, automated CI/CD pipeline validation, and compliance with IEC 62304 testing requirements.

- **Director of Clinical Quality Assurance:**
  *Signature / Electronic Verification:* `[VERIFIED: SECURE SHA-256 HASH]`
  *Date:* 2026-07-22
  *Role:* Responsible for verifying alignment with FDA 21 CFR Part 11, EU Annex 11, and CDISC data standards.

- **Lead Medical Monitor / Study Sponsor QA Representative:**
  *Signature / Electronic Verification:* `[VERIFIED: SECURE SHA-256 HASH]`
  *Date:* 2026-07-22
  *Role:* Responsible for verifying that the software validation strategy preserves clinical trial integrity, blinding protocols, and subject safety.

### 1.3 Electronic Signature & Non-Repudiation Declarations
Pursuant to FDA 21 CFR Part 11 and EU Annex 11, all signatures captured within this document and the Cadence Clinical platform are executed using authenticated username/password and multi-factor authentication (MFA) tokens. Every signature action is bound to an immutable log entry including the signer's identity, timestamp (UTC), IP address, and the explicit "Reason for Signature" (e.g., "I approve the technical validation plan of this system").

### 1.4 Validation Team Roles & Core Responsibilities
To ensure a clear separation of duties and maintain GxP software quality independence, the validation effort is divided among four distinct, qualified project roles:

- **Validation Lead Engineer:**
  - *Responsibilities:* Authors the System Validation Plan (SVP); coordinates manual and automated test script executions; prepares the Requirements Traceability Matrix (RTM); and compiles the final Validation Summary Report (VSR) for regulatory submission.
- **Independent QA Auditor:**
  - *Responsibilities:* Audits the execution logs and test results for compliance; reviews code review logs and change control forms; verifies 21 CFR Part 11 electronic signature triggers; and maintains the independent quality oversight required for GxP sign-off.
- **Lead System Administrator:**
  - *Responsibilities:* Configures the locked Validation and Production environments; executes Postgres database migrations; sets up SSL certificates and OIDC Keycloak gateway access; and verifies the installation qualification (IQ) parameters.
- **Principal Investigator / Clinical Representative:**
  - *Responsibilities:* Leads User Acceptance Testing (UAT) from a clinical usability standpoint; verifies subject randomization parameters, eCRF dynamic workflows, and emergency unblinding workflows; and provides final clinical validation approval.

### 1.5 Training & Qualification Declarations
All personnel participating in the validation, engineering, or administration of the Cadence Clinical platform are required to have up-to-date training records on file in the QMS. Training must cover:
- Good Clinical Practice (GCP) guidelines under ICH E6(R2).
- Electronic Records and Signatures regulations under 21 CFR Part 11.
- Standard Operating Procedures (SOPs) regarding software change control, incident reporting, and database backup/restore operations.

---

## 2. Validation Strategy & Methodology

### 2.1 The Clinical Software Validation Paradigm (GxP)
Clinical software validation differs fundamentally from commercial software QA. Because Cadence Clinical directly handles patient data, randomization allocation, and blinded treatment codes, a software defect could directly compromise subject safety, clinical trial integrity, or regulatory submissions. Thus, the verification and validation (V&V) paradigm is built upon absolute predictability, GxP risk-based mitigation, and comprehensive auditability.

Every functional requirement is treated as a GxP critical path. Our strategy does not rely on random testing or loose heuristics; rather, it enforces deterministic execution. Each test case must establish an initial state, execute a precise sequence of actions, capture concrete outputs, and explicitly verify downstream metadata, relational databases (PostgreSQL), graph structures (Neo4j), and audit trails.

### 2.2 Verification vs. Validation
- **Verification (Are we building the system right?):** Technical confirmation that the software meets specified architectural and functional designs. This is achieved via code reviews, static analysis, unit testing, and integration testing aligned with IEC 62304 Section 5.7.
- **Validation (Are we building the right system?):** Clinical confirmation that the software consistently performs its intended use in a simulated or real clinical environment. This is achieved through end-to-end system testing, regression testing, and User Acceptance Testing (UAT) aligned with IEC 62304 Section 5.8 and GAMP 5 Category 4 (Configured Products) / Category 5 (Custom Applications).

### 2.3 The V-Model Validation Lifecycle
Cadence Clinical employs the classic V-Model validation life cycle, tightly coupled with our agile development pipeline:

```
[User Requirements (URS)] ───────────────────────────────► [User Acceptance Testing (UAT)]
       │                                                                  ▲
       ▼                                                                  │
[Functional Specs (PRD)] ─────────────────────────────► [System & End-to-End Testing (OQ/PQ)]
       │                                                                  ▲
       ▼                                                                  │
[Technical Design (TDD)] ─────────────────────────► [Integration & API Testing (IQ/OQ)]
       │                                                                  ▲
       ▼                                                                  │
[Source Code / Modules] ────────────────────────► [Unit Testing / Static Analysis]
```

### 2.4 GxP Testing Hierarchy
To ensure comprehensive test coverage, the platform is subjected to a five-tier testing hierarchy:

1. **Unit Testing:** Focuses on individual code blocks, helper utilities, and schema validators. Authored in Python using the `pytest` framework. 100% of Pydantic models, custom decorators, and calculation logic must pass unit tests with a mandatory coverage threshold of 80% (target 90%+).
2. **Integration Testing (IEC 62304 Section 5.7):** Verifies the communication and data exchange between microservices (Designer, Execution, Auth Gateway). This includes programmatic database transactions across Neo4j and PostgreSQL, and validating REST and GraphQL API contracts.
3. **System Testing (IEC 62304 Section 5.8):** End-to-end functional verification of the entire clinical platform. This validates subject state transitions, eCRF dynamic rendering, randomization allocation, and security access controls under simulated study conditions.
4. **Regression Testing:** Automated regression suites are executed upon every code commit via CI/CD pipelines to ensure new features or hotfixes do not introduce regressions into existing, validated clinical modules.
5. **User Acceptance Testing (UAT):** Executed by clinical monitors, medical coders, and principal investigators in an isolated Validation environment. UAT focuses on clinical usability, complex multi-site transfers, manual query lifecycles, and actual clinical trial data capture workflows.

### 2.5 Clinical Defect Severity & Escalation Matrix
Defects identified during validation are classified according to their clinical and regulatory impact:

| Severity Level | Clinical & Regulatory Definition | Technical Definition | Resolution Protocol |
| :--- | :--- | :--- | :--- |
| **Severity 1 (Critical / GxP Blocker)** | Compromises patient safety, exposes blinded treatment allocation, permits direct database tampering, or breaks 21 CFR Part 11 audit trails. | Data loss, complete service outage, encryption failure, bypass of RBAC, or un-audited database modification. | Immediate halt to release pipeline. Emergency patch required. Full re-validation and regulatory impact assessment before promotion. |
| **Severity 2 (Major / Functional Defect)** | Disrupts a critical clinical workflow (e.g., subject enrollment, query closure) but does not compromise patient safety or unblind treatment. | Functional requirement fails under standard conditions; no immediate workaround available. | Must be resolved and validated before current release can be promoted to Production. |
| **Severity 3 (Moderate / Workaround Available)** | Non-critical workflow failure. A documented operational workaround exists for the clinical user. | Feature fails under specific edge conditions; alternative pathway exists to achieve the same result. | Evaluated by Change Control Board (CCB) for inclusion in current release or next minor patch. |
| **Severity 4 (Minor / Cosmetic)** | Minor UI layout discrepancies, spelling errors, or non-functional styling issues. | Does not affect data integrity, API contracts, or clinical workflows. | Logged in QMS; scheduled for resolution in routine maintenance cycles. |

### 2.6 Environment Strategy & Promotion Gates
To prevent unvalidated software from impacting active clinical trials and to maintain GxP control, Cadence Clinical enforces a strict four-tier environment architecture. No direct code, configuration, or schema changes are permitted in Staging, Validation, or Production environments. All promotions must pass automated and manual gateways.

```
┌───────────────┐     CI/CD automated regression     ┌─────────────────┐
│  Development  ├───────────────────────────────────►│     Staging     │
│ (Unit/Integr) │     100% tests pass, linting ok   │  (System/Intgr) │
└───────────────┘                                    └────────┬────────┘
                                                              │
                                     Manual sign-off & audit  │
                                     GxP execution-ready      ▼
┌───────────────┐     Strict Change Control (CAB)    ┌─────────────────┐
│  Production   │◄───────────────────────────────────┤   Validation    │
│ (Active Trial)│     Final Sponsor V&V approval     │   (UAT/GxP V&V) │
└───────────────┘                                    └─────────────────┘
```

1. **Development Environment (DEV):**
   - **Purpose:** Active engineering, design prototyping, sandbox API integrations, and initial automated verification.
   - **Data State:** Simulated, mock-level patient records. No real clinical trial protocol databases.
   - **Access Control:** Restricted to software engineers and DevOps team members.
   - **Promotion Gate to Staging:** All pull requests must pass 100% automated unit and integration test suites, static analysis checks (Black, Ruff), and undergo peer code review with approved PR documentation.

2. **Staging Environment (STG):**
   - **Purpose:** Component integration, end-to-end performance testing, load testing, and security penetration testing.
   - **Data State:** High-fidelity simulated clinical trial protocol datasets, complete with populated metadata definitions and mock forms.
   - **Access Control:** Quality Assurance engineers, systems integration leads, and product owners.
   - **Promotion Gate to Validation:** Automated system verification suites must pass with zero Severity 1 or 2 defects. Technical documentation, including API integration contracts and database schemas, must be frozen and versioned.

3. **Validation Environment (VAL):**
   - **Purpose:** Formal IQ/OQ/PQ execution, manual User Acceptance Testing (UAT), and clinical investigator dry-runs.
   - **Data State:** Cloned production-like schemas with anonymized subject pools. Crucially, the trial configuration in Validation must be structurally identical to the target Production environment version to maintain the integrity of GxP validation.
   - **Access Control:** Clinical research associates (CRAs), medical monitors, sponsor QA auditors, and validation engineers.
   - **Promotion Gate to Production:** Formal release of the validation package, which includes this signed Validation Summary Report, the fully green Requirements Traceability Matrix, and explicit, electronically signed CAB (Change Advisory Board) approval.

4. **Production Environment (PROD):**
   - **Purpose:** Live clinical trial execution, actual subject enrollment, active eCRF data entry, randomization, and regulatory submission data logging.
   - **Data State:** Live, locked clinical datasets governed by 21 CFR Part 11 database triggers and encrypted storage backups.
   - **Access Control:** Restricted strictly to active site investigators, clinical monitors, and authorized clinical coordinators. No administrative engineering access is permitted without an active, logged emergency unblinding/break-glass ticket.

### 2.7 Risk-Based GxP Impact Assessment Strategy (FMEA)
To maximize validation efficiency and focus engineering resources on high-risk clinical components, Cadence Clinical implements a continuous, risk-based impact assessment strategy modeled on the Failure Mode and Effects Analysis (FMEA) standard (aligned with ISO 14971:2019 and GAMP 5 risk management guidance).

#### 2.7.1 FMEA Scoring Methodology
For each software subsystem and functional module, a GxP Risk Priority Number (RPN) is calculated using three scores rated from 1 (lowest risk/highest visibility) to 5 (highest risk/lowest visibility):
- **Severity (S):** The clinical or regulatory impact of a failure.
  - *Score 5:* Compromises patient safety, corrupts primary clinical trial endpoints, or breaches treatment blinding.
  - *Score 3:* Disrupts daily operations (e.g., prevents data entry), but has a clear workaround that maintains database integrity.
  - *Score 1:* Cosmetic UI discrepancy. No operational or data integrity impact.
- **Probability of Occurrence (O):** The likelihood of the software defect manifesting in a standard study lifecycle.
  - *Score 5:* High probability; occurs frequently due to complex data structures or shared execution threads.
  - *Score 3:* Moderate probability; occurs under specific concurrent load conditions or unique user sequences.
  - *Score 1:* Extremely rare; occurs only under massive hardware failure or extreme multi-site network outages.
- **Detectability (D):** The likelihood that the system, database triggers, or QA checks will flag the failure before it affects active trial operations.
  - *Score 5:* Undetectable; fails silently, writing corrupted data to DB without throwing exceptions or generating audit flags.
  - *Score 3:* Moderately detectable; raises an application exception but continues processing transaction, requiring periodic manual audit checks to identify.
  - *Score 1:* Highly detectable; database-level trigger instantly halts transaction, displays high-priority UI alert, and logs error to the shadow audit schema.

#### 2.7.2 GxP Risk Priority Number (RPN) Calculation
$$\text{RPN} = \text{Severity (S)} \times \text{Occurrence (O)} \times \text{Detectability (D)}$$

Using this calculation, subsystems are assigned to three risk-based validation categories:
- **High GxP Risk ($\text{RPN} \ge 45$):** Requires full structural code verification, independent QA test script executions, edge-case and race-condition stress-testing, and mandatory UAT. *Examples: Randomization engine, cryptographic ledger, unblinding routines, and graph immutability locks.*
- **Medium GxP Risk ($20 \le \text{RPN} < 45$):** Requires automated API integration testing and standard functional UAT scripts. *Examples: Spreadsheet parser, calculated fields, and query management state transitions.*
- **Low GxP Risk ($\text{RPN} < 20$):** Verified via routine automated regression testing and standard code reviews. *Examples: Cosmetic UI layouts, audit log viewers, and vocabulary searches.*

---

## 3. GAMP 5 & IEC 62304 Compliance Framework

### 3.1 GAMP 5 Software Categorization
The Good Automated Manufacturing Practice (GAMP) 5 framework classifies software used in regulated life science industries to determine the depth and rigor of validation required. Cadence Clinical is a hybrid platform spanning two distinct GAMP 5 categories:

- **GAMP 5 Category 4 (Configured Products):** Applies to the Electronic Data Capture (EDC) custom form rendering engine and standard subject state machine. The application structures are standardized, and validation focuses on verifying the configuration rules, dynamic skip logic (XPath bindings), study schedule setups, and user role matrices.
- **GAMP 5 Category 5 (Custom Applications):** Applies to the proprietary Graph-Based Versioning Engine (Neo4j timeline paths), the background Cryptographic Sealing and hashing ledger, and custom randomization allocation algorithms. These are bespoke, high-risk components developed specifically for Cadence Clinical. They require the most rigorous validation lifecycle, including full source code verification, design specification reviews, and deep stress/boundary testing under simulated race conditions.

### 3.2 IEC 62304 Software Life Cycle Processes
For medical device software and associated data structures, compliance with **IEC 62304:2006/AMD1:2015** is mandatory. Under IEC 62304, Cadence Clinical is designated as **Class B Software** (systems where failure can result in non-serious injury, or in a clinical trial context, system failure can lead to data loss or invalidation of trial results requiring trial suspension or subject re-screening).

#### 3.2.1 Software Integration and System Testing (IEC 62304 Section 5.7 & 5.8)
To meet the requirements of Section 5.7 and Section 5.8, the validation plan implements the following programmatic controls:

- **Section 5.7 (Software Integration Testing):**
  - **Integration Verification:** Every interface between the Designer Service (Neo4j) and the Execution Service (PostgreSQL) must undergo automated integration testing. We verify that protocol schemas published in the Designer are received and correctly translated by the Execution Engine without data truncation.
  - **Regression Risk Mitigation:** Whenever a component is updated, a regression analysis is conducted. Integration tests verify that core database triggers, relational integrity constraints, and API contract parameters remain functional.
  - **Limit & Stress Testing:** Integration tests evaluate behavior at absolute boundaries (e.g., maximum payload sizes for eCRFs, 500+ repeating fields in a single transaction, and rapid concurrent transaction limits to simulate multiple global sites uploading data simultaneously).

- **Section 5.8 (Software System Testing):**
  - **Functional Integrity:** System testing verifies that the software meets 100% of the functional requirements specified in the PRD.
  - **Requirement Traceability:** Every system test case is mapped back to the PRD functional requirements using the Requirements Traceability Matrix (RTM) detailed in Section 4 of this document.
  - **Failure Mode & Anomaly Behavior:** System testing explicitly verifies the software's response to abnormal conditions (e.g., database network disconnects, invalid data input formats, unauthenticated API calls, and unauthorized state mutation attempts).

### 3.3 Regulatory Evidence & Deliverables Package
To achieve clinical validation sign-off and satisfy FDA, EMA, and PMDA audits, the following deliverables are compiled during each validation cycle and stored in the secure Quality Management System:

| Validation Deliverable | Owner | GxP Regulatory Purpose |
| :--- | :--- | :--- |
| **System Validation Plan (SVP)** | Validation Lead | Outlines validation scope, standards alignment, environment promotion gateways, and testing schedules. |
| **User Requirements Specification (URS)** | Product Owner | Defines the user and business requirements of the clinical trial platform. |
| **Product Requirements Document (PRD)** | Systems Analyst | Details precise, functional criteria for all components (study design, EDC, query engine, etc.). |
| **Technical Design Document (TDD)** | Software Architect | Describes software topology, schemas, graph models, and compliance trigger mechanisms. |
| **Requirements Traceability Matrix (RTM)** | QA Engineer | Demonstrates 100% bidirectional coverage between functional requirements and testing protocols. |
| **Installation Qualification (IQ) Report** | DevOps Lead | Documents successful infrastructure configuration, deployment variables, database migrations, and SSL setups. |
| **Operational Qualification (OQ) Report** | QA Lead | Confirms that all system modules, integrations, APIs, and boundary scenarios perform as designed. |
| **Performance Qualification (PQ) Report** | Validation Lead | Proves the end-to-end system is fit for its clinical purpose by executing UAT scripts and verifying trial workflows. |
| **Validation Summary Report (VSR)** | Quality Director | Final clinical sign-off, summarizing test results, logged deviations, workarounds, and compliance status. |

### 3.4 Automated Testing & Continuous Integration (CI/CD) Validation
In modern clinical trial platform validation, the tooling used to perform software verification must itself be validated. In accordance with GAMP 5 Category 1 (Infrastructure Software) or Category 3 (Non-Configured Software), the automated testing toolset used within Cadence Clinical (specifically, Python `pytest`, `pytest-asyncio`, `pytest-cov`, `Black`, `Ruff`, and GitHub Actions / GitLab CI pipelines) undergoes formal tool validation.

#### 3.4.1 Tool Validation & Verification Protocols
- **pytest & pytest-asyncio:** Verified prior to use by executing standard reference suites provided by the developer. This confirms that test results, execution times, and assertion checks are evaluated deterministically.
- **pytest-cov:** Formal qualification runs demonstrate that the code coverage reports generated match mathematical execution trees exactly, ensuring that our mandatory 80% coverage check functions reliably.
- **Linter & Formatting Engines (Black/Ruff):** Confirmed to programmatically enforce coding rules, prevent deprecated or unsafe language patterns, and ensure strict syntax layouts, which are critical for maintaining code readability and audit readiness over long-term clinical trial timelines.

#### 3.4.2 CI/CD Pipeline as a Validated System
The deployment pipeline itself acts as a locked GxP gateway. Upon initiating a pull request or merging code, the following sequence is executed in a secure, containerized, and isolated build runner:

1. **Isolation Setup:** Build runners initialize an empty virtual environment and download only pinned, SHA-verified dependency packages from our private, qualified artifact repository.
2. **Static Inspection:** Ruff linting and Black formatting checks are executed. Any warning or layout violation instantly fails the build, preventing promotion.
3. **Unit Test Pass:** 100% of the unit test suite must pass successfully. If a single assertion fails, or if a coroutine fails to await, the build is blocked.
4. **Coverage Enforcement:** The `pytest-cov` plugin evaluates total test coverage. If coverage falls below the mandatory 80% threshold, the build is aborted, and a QMS incident is logged.
5. **Database Migration Verification:** Ordered Postgres migrations are executed in a test container. The system runs the `run_migrations` script, and validates that relational schemas map exactly to the current models, ensuring database migrations are validated before staging deployment.
6. **Immutable Logging:** The entire pipeline execution log (including tool versions, standard output, and exit codes) is written to a write-once-read-many (WORM) storage bucket. This execution log acts as direct regulatory evidence for our Operational Qualification (OQ) reports.

---

## 4. Requirements Traceability Matrix (RTM)

### 4.1 Traceability Methodology & Bidirectional Mapping
The Requirements Traceability Matrix (RTM) is a regulatory-grade artifact that ensures 100% of the functional requirements defined in the Product Requirements Document (PRD) are structurally implemented in the system architecture (Technical Design Document - TDD) and fully verified by the testing protocols (QA Test Case IDs).

This matrix is **bidirectional**:
- **Forward Traceability:** Ensures that every single requirement has a design blueprint and a corresponding test case, confirming that no requirement has been forgotten or left untested.
- **Backward Traceability:** Ensures that every test case and structural component can be traced back to an explicit, authorized requirement, verifying that no "scope creep" or unapproved features have been introduced.

### 4.2 Comprehensive Traceability Matrix Table

| Functional Module | PRD Functional ID | PRD Requirements Description | TDD Design Component | QA Test Case ID | Test Execution Type |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **Study Design & MDR** | **PRD-MDR-001** | **BCs & VLM Constraints:** Value-Level Metadata constraints dynamically update eCRF; modifying BC linked to active study triggers frozen-state block. | TDD-MDR-BC (MDR Concept Models) | `TC-MDR-001` | Automated (Integration/API) |
| | **PRD-MDR-002** | **Complex Trial Designs:** State transitions for trial arms allow dynamic opening/closing of cohorts based on analysis. Closed-arm subject transfers. | TDD-MDR-ARM (Trial Element Graph) | `TC-MDR-002` | Automated (Integration) |
| | **PRD-MDR-003** | **Crossover & Blinding:** Chronological tracking of sequence interventions; strict blind barrier for site users. Emergency unblinding state transition. | TDD-MDR-BLIND (Access & Blinding) | `TC-MDR-003` | Automated & Manual |
| | **PRD-MDR-004** | **I/E Governance:** Inclusion/Exclusion criteria mapped to eCRF; single 'No' on Inclusion / 'Yes' on Exclusion locks subject to 'Screen Failed'. | TDD-MDR-IE (I/E Logic Evaluator) | `TC-MDR-004` | Automated (Unit/Integration) |
| **EDC & eCRF Engine** | **PRD-EDC-001** | **Spreadsheet Parsing:** Parse Excel into OpenRosa XForm standard nodes. Detect and reject circular skip logic dependencies at parse time. | TDD-EDC-PARSE (XForm Parsing Engine) | `TC-EDC-001` | Automated (Unit/Integration) |
| | **PRD-EDC-002** | **Dynamic Field Behaviors:** Conditional rendering via XPath. Changing parent fields nullifies/flags inactive child data to prevent orphans. | TDD-EDC-RULES (Skip & Constraint Rules) | `TC-EDC-002` | Automated (Integration) |
| | **PRD-EDC-003** | **Advanced Inputs:** VAS precise floating-point output; calculated fields in real-time; calculation loops default to null on missing data. | TDD-EDC-ENGINE (Form Logic Engine) | `TC-EDC-003` | Automated (Unit/Integration) |
| | **PRD-EDC-004** | **Draft, Paging & Offline:** Save drafts without validation; offline IndexedDB storage; conflict resolution on sync upon network restoration. | TDD-EDC-SYNC (Offline Sync Manager) | `TC-EDC-004` | Automated & Manual |
| **Subject & Random** | **PRD-SUB-001** | **Subject State Machine:** Unidirectional transitions. Withdrawing a subject locks future visits but permits answering open past queries. | TDD-SUB-STATE (State Machine Engine) | `TC-SUB-001` | Automated (Integration) |
| | **PRD-SUB-002** | **Randomization Rules:** Pre-seeded blocks or dynamic minimization; allocation table encryption; missing stratification factor triggers re-rand error. | TDD-SUB-RAND (Encrypted Rand Engine) | `TC-SUB-002` | Automated (Integration) |
| | **PRD-SUB-003** | **Transfers & Linking:** Transfer subject across sites migrating RBAC tokens. Caregiver and telemetry device mapping to records. | TDD-SUB-TRANS (Site Transfer Handler) | `TC-SUB-003` | Automated (API) |
| **Query & Review** | **PRD-QRY-001** | **Query Lifecycles:** Open -> Answered -> Closed. Modifying underlying eCRF data automatically re-evaluates edit check rules. | TDD-QRY-LIFE (Query State Engine) | `TC-QRY-001` | Automated (Integration) |
| | **PRD-QRY-002** | **Discrepancy Notes:** Cross-form longitudinal edit checks (e.g., AE start date >= Informed Consent date). | TDD-QRY-RULES (Longitudinal Rules) | `TC-QRY-002` | Automated (Integration) |
| | **PRD-QRY-003** | **SDV & Medical Review:** Source Document Verification flag logic. Modifying verified data drops SDV flag and triggers a monitor alert. | TDD-QRY-SDV (SDV Flag Controller) | `TC-QRY-003` | Automated (Integration) |
| **Universal Caps** | **PRD-UNI-001** | **Role-Based Access Control:** Strict Separation of Duties. Sponsor blinded to PII; Investigator blind to global site data. Strip PII on exports. | TDD-UNI-RBAC (OIDC Gateway & Security) | `TC-UNI-001` | Automated (API) |
| | **PRD-UNI-002** | **21 CFR Part 11 Audit Trail:** Every transaction writes shadow schema record with created_at, created_by, reason_for_change, version_index. | TDD-UNI-AUDIT (Shadow DB Trigger) | `TC-UNI-002` | Automated (Integration) |
| | **PRD-UNI-003** | **Graph Immutability:** Locked study version nodes are immutable. Modifications spawn previous-version edges for graph timelines. | TDD-UNI-GRAPH (Neo4j Version Engine) | `TC-UNI-003` | Automated (Integration) |
| | **PRD-UNI-004** | **Soft-Delete Enforcement:** Native soft-delete lifecycle across all modules. Hard-deletes explicitly forbidden and raise trigger warnings. | TDD-UNI-DELETE (Soft-Delete Handler) | `TC-UNI-004` | Automated (Integration) |

---

## 5. Complex Logic Test Scenarios & Execution Protocols

To satisfy GxP validation criteria and comply with IEC 62304 Sections 5.7 and 5.8, Cadence Clinical specifies four deep, execution-ready clinical verification scenarios. These scenarios represent the highest-risk clinical workflows where system failure directly threatens study blinding, trial data integrity, or regulatory compliance.

Each scenario is modeled using the platform's **Universal Verification Pattern**:
`Initial State Validation` $\rightarrow$ `Mutation Persistence & Audit Check` $\rightarrow$ `Authorization Rejection Testing` $\rightarrow$ `Soft-Delete Constraint Check` $\rightarrow$ `Downstream Dependency Handling`.

---

### 5.1 Scenario 1 (Protocol Version Locking & Immutability Rejection)
- **Scenario ID:** `TC-VAL-LOG-001`
- **Functional Requirements:** `PRD-MDR-001`, `PRD-UNI-003` (Graph Immutability)
- **Clinical & Operational Context:** In a clinical trial, the protocol defines the exact study schema (epochs, visits, eCRFs). Once a protocol version is locked (e.g., approved by the IRB/IEC and published), it must be absolutely immutable. Any unauthorized or silent modification of a locked protocol would cause active EDC systems to render the wrong eCRFs, invalidate collected subject data, and trigger immediate clinical audit failures. Protocol amendments must only be executed through a formal, semantic branching and up-versioning pipeline.

#### 5.1.1 Pre-Conditions & Mock Data Setup
- **Study ID:** `STU-ONCO-2026` (Phase II Oncology Study)
- **Protocol Node (Neo4j):**
  ```cypher
  CREATE (p:StudyProtocol {
    id: "STU-ONCO-2026-P01",
    status: "LOCKED",
    version: "1.0.0",
    hash: "a3f5b8c9d0e2f1a3b5c7d9e1f3a5b7c9d1e3f5a7b9c1d3e5f7a9b1c3d5e7f9a1"
  })
  ```
- **Associated Visit Node (Neo4j):**
  ```cypher
  CREATE (v:StudyVisit {
    id: "VISIT-V1-SCREENING",
    name: "Screening Visit",
    week: 0
  })-[:BELONGS_TO {version_index: 1}]->(p)
  ```
- **Executing Identity:** User `site_coordinator_01` (Role: `Site Coordinator`, unauthorized to mutate protocol schemas) and User `lead_designer_01` (Role: `Lead Designer`, authorized to create amendments but restricted from mutating locked nodes directly).

#### 5.1.2 Step-by-Step Execution Protocol

| Step | Action / Input | Technical Vector | Expected Output / Response | Pass/Fail Criteria |
| :--- | :--- | :--- | :--- | :--- |
| **1 (Initial State)** | Query active protocol state in the MDR database to verify it is marked as `LOCKED` with version `1.0.0`. | `GET /api/designer/protocols/STU-ONCO-2026-P01` | `{"id": "STU-ONCO-2026-P01", "status": "LOCKED", "version": "1.0.0"}` | **Pass:** Status returned is exactly `LOCKED`. Database displays correct version. |
| **2 (Direct Mutation)** | Attempt to directly modify the name of the Screening Visit linked to the locked protocol version without initiating an amendment. | `PUT /api/designer/visits/VISIT-V1-SCREENING`  <br> **Payload:** `{"name": "Modified Screening Visit Name"}` | HTTP `403 Forbidden`  <br> **Body:** `{"error": "IMMUTABILITY_VIOLATION", "message": "Cannot mutate elements belonging to a locked study version."}` | **Pass:** Mutation rejected. HTTP status code is 403. Visit name remains unchanged in database. |
| **3 (Db-Level Check)** | Attempt an administrative SQL/Cypher mutation directly on the Neo4j instance to bypass the application-layer API barriers. | Run direct Cypher on Graph: <br> `MATCH (v:StudyVisit {id: "VISIT-V1-SCREENING"}) SET v.name = "Tampered"` | Cypher execution fails or is blocked by database-level write-restriction triggers. | **Pass:** Database engine blocks direct write, or background cryptographic ledger flags a signature mismatch within 5 seconds. |
| **4 (Auth Rejection)** | Attempt to update the protocol status from `LOCKED` to `DRAFT` using credentials of a standard site user. | `PATCH /api/designer/protocols/STU-ONCO-2026-P01`  <br> **Payload:** `{"status": "DRAFT"}` | HTTP `401 Unauthorized` or HTTP `403 Forbidden` | **Pass:** Transaction blocked. Protocol status remains `LOCKED`. Audit log records authorization failure. |
| **5 (Amendment Fork)** | Initiate a formal protocol amendment. This is the only approved method to apply modifications. | `POST /api/designer/protocols/STU-ONCO-2026-P01/amend` <br> **Header:** `X-Change-Reason: IRB-Approved Amendment 01` | HTTP `201 Created` <br> **Body:** `{"new_version": "1.1.0", "status": "DRAFT", "parent_version": "1.0.0"}` | **Pass:** Platform spawns a new draft version node `1.1.0`. A `PREVIOUS_VERSION` relationship is successfully created in Neo4j. |
| **6 (Audit Verification)**| Query the audit trail for the protocol ID to verify that the mutation attempt (and successful fork) was logged chronologically. | `GET /api/execution/audit?record_id=STU-ONCO-2026-P01` | JSON array containing audit logs showing blocked mutation attempts and approved amendment creation. | **Pass:** Audit trail contains timestamp, executing ID, reason for change, and rejection codes. |

#### 5.1.3 Downstream Dependency Handling & Edge-Case Testing
- **Downstream EDC Sync:** Verify that the Execution Service (EDC) continues to render the `1.0.0` version of the forms for existing patients during the amendment draft phase. The new `1.1.0` form definitions must remain completely invisible to EDC sites until `1.1.0` is officially promoted to `LOCKED` status.
- **Race Condition Testing:** Simulate two concurrent API requests attempting to lock the amendment draft. Verify that the second request is gracefully rejected with a `409 Conflict` (Version already locked) while the first succeeds, preventing dual protocol timelines.

---

### 5.2 Scenario 2 (Stratification Factor Re-randomization Rejections)
- **Scenario ID:** `TC-VAL-LOG-002`
- **Functional Requirements:** `PRD-SUB-002`, `PRD-SUB-001` (Subject State Machine)
- **Clinical & Operational Context:** Stratified randomization distributes subjects to treatment groups based on prognostic factors (e.g., age, disease stage, baseline biomarkers) to ensure balanced cohorts. If a site user attempts to modify a subject's stratification factor *after* the subject has been randomized, this would invalidate the allocation balance, break the statistical design of the trial, and introduce massive bias. The system must explicitly lock stratification factors post-randomization and throw strict validation errors on modification attempts, preventing un-audited "silent" shifts in baseline demographics.

#### 5.2.1 Pre-Conditions & Mock Data Setup
- **Subject ID:** `SUB-999-ACTIVE`
- **Subject State (PostgreSQL):** State is `RANDOMIZED` (Enrolled and allocated to treatment)
- **Stratification Factors:**
  - `STRAT_AGE_GROUP`: `AGE_GE_65` (Age $\ge$ 65)
  - `STRAT_HER2_STATUS`: `POSITIVE`
- **Allocation Table (PostgreSQL):** Encrypted record linking `SUB-999-ACTIVE` to Treatment Arm `ARM-A` (Blinded).
- **Executing Identity:** User `investigator_01` (Role: `Investigator`, authorized to edit baseline eCRFs for non-randomized subjects).

#### 5.2.2 Step-by-Step Execution Protocol

| Step | Action / Input | Technical Vector | Expected Output / Response | Pass/Fail Criteria |
| :--- | :--- | :--- | :--- | :--- |
| **1 (Initial State)** | Query the subject record to verify the current state is `RANDOMIZED` and stratification parameters are locked. | `GET /api/execution/subjects/SUB-999-ACTIVE` | `{"id": "SUB-999-ACTIVE", "status": "RANDOMIZED", "strat_factors": {"STRAT_HER2_STATUS": "POSITIVE"}}` | **Pass:** State is verified as `RANDOMIZED`. Factors are populated. |
| **2 (Stratification Mutation)** | Attempt to update the `STRAT_HER2_STATUS` from `POSITIVE` to `NEGATIVE` through the subject demographic API. | `PUT /api/execution/subjects/SUB-999-ACTIVE/demographics` <br> **Payload:** `{"STRAT_HER2_STATUS": "NEGATIVE"}` | HTTP `422 Unprocessable Entity` <br> **Body:** `{"error": "LOCKED_FACTOR_MUTATION", "message": "Cannot modify stratification factors for randomized subjects. Re-randomization is strictly blocked."}` | **Pass:** Platform rejects the mutation request. Demographics database remains unmodified. |
| **3 (Db shadow audit)** | Query the DB transaction layer to ensure that the rejected transaction was intercepted by the event listener and logged as a failed mutation. | `GET /api/execution/audit?record_id=SUB-999-ACTIVE` | Audit log entry showing blocked attempt with reason, user ID, and target field delta. | **Pass:** Log entry created under Category: `SECURITY_VIOLATION`. |
| **4 (State Bypass)** | Attempt to force-update the subject's state back to `SCREENING` to bypass the randomization lock. | `PATCH /api/execution/subjects/SUB-999-ACTIVE/state` <br> **Payload:** `{"status": "SCREENING"}` | HTTP `400 Bad Request` <br> **Body:** `{"error": "INVALID_STATE_TRANSITION", "message": "Transition from RANDOMIZED to SCREENING is forbidden."}` | **Pass:** Subject state remains locked in the state machine. Backward state transitions are rejected. |
| **5 (Soft-Delete Check)** | Attempt to soft-delete the subject's demographics record to force a fresh data entry bypass. | `DELETE /api/execution/subjects/SUB-999-ACTIVE/demographics` | HTTP `403 Forbidden` <br> **Body:** `{"error": "SOFT_DELETE_BLOCKED", "message": "Cannot soft-delete primary demographic records of a randomized subject."}` | **Pass:** Demographics record is preserved. Soft-delete is prevented due to active downstream dependencies. |

#### 5.2.3 Downstream Dependency Handling & Edge-Case Testing
- **Unblinded Allocation Preservation:** Verify that the encrypted treatment code (`ARM-A` link) in the allocation table remains untouched. A mutation failure must never cause the allocation code to be orphaned or wiped out, which would permanently compromise the subject's trial involvement.
- **Null Value Rejection:** Attempt to send a demographic payload setting the HER2 status to `null`. Verify that the validator catches this and throws a strict Schema Validation Error, preventing the system from entering an undefined state.

---

### 5.3 Scenario 3 (Offline Mode Data Entry, Sync Collision & Conflict Resolution)
- **Scenario ID:** `TC-VAL-LOG-003`
- **Functional Requirements:** `PRD-EDC-004` (Offline Entry), `PRD-UNI-002` (21 CFR Part 11 Audit Trail)
- **Clinical & Operational Context:** Clinical monitors and site investigators frequently work in environments with unstable internet connections (e.g., isolated wards, field research). The platform must support offline data entry using local storage (IndexedDB) and securely synchronize data upon network restoration. Crucially, the system must detect multi-user collisions (e.g., User A edits a form offline, and User B edits the same form online before User A syncs) and resolve conflicts deterministically without silent data overwrites, data loss, or compromising the audit ledger.

#### 5.3.1 Pre-Conditions & Mock Data Setup
- **Subject eCRF Form:** `FORM-V1-VITALS` (Vitals Form for subject `SUB-101`)
- **Initial Baseline (PostgreSQL & IndexedDB):** `systolic_bp` is `120` mmHg.
- **Users:**
  - User A (`offline_user_01`), Site Investigator working in offline mode in a hospital basement.
  - User B (`online_user_01`), Clinical Monitor working online at the sponsor site.
- **Audit Token:** Standard 21 CFR Part 11 headers initialized.

#### 5.3.2 Step-by-Step Execution Protocol

| Step | Action / Input | Technical Vector | Expected Output / Response | Pass/Fail Criteria |
| :--- | :--- | :--- | :--- | :--- |
| **1 (Offline State)** | User A disconnects from network. Browser service worker transitions to offline status. Verify LocalStorage/IndexedDB is active. | Network adapter set to offline. | Application UI flags status as "OFFLINE - Local Queue Enabled". | **Pass:** Offline banner is visible. API requests are routed to local IndexedDB queue. |
| **2 (Offline Mutation)** | User A edits Vitals Form offline. Changes systolic BP from 120 to 145. Saves form. | Local database mutation: <br> `systolic_bp = 145` | Local database successfully updated. Audit log captured locally in IndexedDB queue. | **Pass:** Form status set to "Sync Pending". Offline transaction recorded locally with time-stamp. |
| **3 (Online Collision)** | User B (online) opens the same Vitals Form for `SUB-101`. Changes systolic BP from 120 to 130. Saves form. | `PUT /api/execution/forms/FORM-V1-VITALS` <br> **Payload:** `{"systolic_bp": 130}` | HTTP `200 OK` <br> Database updated: `systolic_bp = 130`. Audit trail written. | **Pass:** Online edit succeeds immediately. Version index increments to 2. |
| **4 (Network Restore)** | User A restores network connectivity. Service worker triggers the sync queue. | Network adapter set to online. <br> Service worker calls `/api/execution/forms/sync` | HTTP `409 Conflict` or HTTP `207 Multi-Status` <br> **Body:** `{"conflict_type": "STRUCTURAL_COLLISION", "conflicted_field": "systolic_bp"}` | **Pass:** Platform detects conflict. Silent overwrite is blocked. The sync engine flags the transaction. |
| **5 (Conflict Resolution)**| Execute the GxP conflict resolution routine: the system queues the conflict for manual investigator approval, retaining both versions in a review buffer. | `POST /api/execution/forms/resolve` <br> **Payload:** `{"selected_value": "145", "reason_for_change": "Accepted offline investigator data over remote monitor edit"}` | HTTP `200 OK` <br> Database updated to `145`. Version index increments to 3. | **Pass:** Conflicting versions are preserved. Final chosen value is successfully written to PostgreSQL tables. |
| **6 (Audit Verification)**| Query the audit ledger to verify that the entire conflict history (both online edit, offline edit, and the final manual resolution) was fully recorded. | `GET /api/execution/audit?record_id=FORM-V1-VITALS` | JSON array showing: <br> 1. Initial (120) <br> 2. Online Update (130) <br> 3. Conflict Resolution (145) with "Accepted offline investigator data..." | **Pass:** Audit ledger contains chronological timeline of all intermediate values, avoiding any data "gaps". |

#### 5.3.3 Downstream Dependency Handling & Edge-Case Testing
- **Longitudinal Edit Checks:** Verify that if the vitals form is in conflict, downstream calculation fields (e.g., Mean Arterial Pressure) are not recalculated using the offline data until the conflict is officially resolved and committed.
- **Double-Sync Trigger:** Trigger the sync queue twice in rapid succession. Verify that the second call is blocked via Redis rate-limiting/idempotency keys, preventing duplicate record creation.

---

### 5.4 Scenario 4 (Re-authentication Enforcement during Emergency Unblinding)
- **Scenario ID:** `TC-VAL-LOG-004`
- **Functional Requirements:** `PRD-MDR-003` (Crossover & Blinding), `PRD-UNI-002` (21 CFR Part 11 Audit Trail)
- **Clinical & Operational Context:** Emergency unblinding is a high-risk safety procedure in clinical trials. It is executed only when an adverse event is so severe that the treating physician must know the active drug assignment to save the patient's life. This process must be heavily guarded: it requires mandatory re-authentication (even if the user has an active login session) to verify user intent, immediately logs an immutable unblinding event with the required justification, and transitions the subject's state to "Unblinded" to prevent future blinded data entry.

#### 5.4.1 Pre-Conditions & Mock Data Setup
- **Subject ID:** `SUB-ONCO-101`
- **Subject State (PostgreSQL):** State is `RANDOMIZED` (Active, Blinded, allocated to active compound or placebo).
- **User:** `dr_investigator_02` (Role: `Lead Investigator`, authorized to perform unblinding for safety reasons).
- **Session State:** User is currently logged in, with an active JWT bearer token expiring in 45 minutes.

#### 5.4.2 Step-by-Step Execution Protocol

| Step | Action / Input | Technical Vector | Expected Output / Response | Pass/Fail Criteria |
| :--- | :--- | :--- | :--- | :--- |
| **1 (Unblinding Request)**| Request emergency unblinding code for subject `SUB-ONCO-101` using the active session token without re-authentication. | `POST /api/execution/subjects/SUB-ONCO-101/unblind` <br> **Payload:** `{"reason": "Severe anaphylactic shock"}` | HTTP `401 Unauthorized` <br> **Body:** `{"error": "REAUTHENTICATION_REQUIRED", "message": "21 CFR Part 11 mandate: Re-authentication is required to perform unblinding."}` | **Pass:** Request is rejected despite active login session. Treatment code remains completely hidden. |
| **2 (Re-Auth Challenge)** | Re-submit unblinding request containing both the session token and the user's secure credentials (password + OTP token). | `POST /api/execution/subjects/SUB-ONCO-101/unblind` <br> **Payload:** `{"reason": "Severe anaphylactic shock", "auth_challenge": {"password": "secure_password", "otp": "998124"}}` | HTTP `200 OK` <br> **Body:** `{"subject_id": "SUB-ONCO-101", "allocation_code": "COMPOUND-X", "unblinded_at": "2026-07-22T20:15:00Z"}` | **Pass:** Credentials validated. Platform reveals treatment assignment (`COMPOUND-X`). |
| **3 (State Transition)** | Query the subject state machine to verify the subject was transitioned to `WITHDRAWN` or `UNBLINDED` to restrict further blinded entries. | `GET /api/execution/subjects/SUB-ONCO-101` | `{"id": "SUB-ONCO-101", "status": "WITHDRAWN", "blinding_status": "UNBLINDED"}` | **Pass:** State is automatically modified to `WITHDRAWN` or `UNBLINDED`. Further dynamic forms are disabled. |
| **4 (Audit Sealing)** | Verify that an immutable, cryptographically sealed record was written to the PostgreSQL shadow schema. | `GET /api/execution/audit?record_id=SUB-ONCO-101` | Audit log containing user ID, timestamp, reason, client terminal ID, and action `EMERGENCY_UNBLINDING`. | **Pass:** Audit trail matches transaction data exactly. System-wide alert is triggered to sponsor QA. |
| **5 (Unauthorized Trial)** | Attempt to trigger unblinding using credentials of a standard site nurse who does not hold unblinding privileges. | `POST /api/execution/subjects/SUB-ONCO-101/unblind` <br> **Payload:** `{"reason": "Safety check", "auth_challenge": {"password": "nurse_password"}}` | HTTP `403 Forbidden` <br> **Body:** `{"error": "ROLE_INSUFFICIENT", "message": "Access denied: Site nurses are unauthorized to perform unblinding."}` | **Pass:** Request rejected. Log records failed unauthorized access attempt. |

#### 5.4.3 Downstream Dependency Handling & Edge-Case Testing
- **Sponsor Alert Trigger:** Verify that the system instantly triggers an automated email/webhook notification to the Sponsor's Clinical Operations Lead and the Safety Committee upon unblinding execution.
- **SQL Injection Defense:** Attempt to inject an SQL injection payload inside the `reason` field (e.g., `Severe shock'; DROP TABLE subject;--`). Verify that the database query relies on parameterized inputs, resulting in the literal string being written safely to the audit ledger.

---

## 6. Clinical Domain Verification Protocols

To fulfill the clinical validation requirements of ISO 14155:2020 and GAMP 5, all validation checklists are fully specified with detailed clinical contexts, execution instructions, and explicit database verification points.

---

### 6.1 Clinical Trial Design & Version Parity Verification
- **Clinical Domain Context:** Clinical trials are multi-center, long-duration projects often subject to protocol amendments (e.g., adding an optional biomarker cohort or adjusting visit schedules). The EDC Execution Engine must render forms and enforce validation rules that match the exact version of the protocol to which each specific subject is consented. Cross-version drift—where an investigator enters data using an incorrect or unapproved form version—can result in the regulatory rejection of the entire trial dataset.
- **Verification Target:** Direct schema rendering compatibility between the Designer Service graph database (Neo4j) and the EDC Execution relational database (PostgreSQL).
- **Execution Protocol:**
  1. **Consented Version Lock:** For subject `SUB-201`, set their consented protocol version to `1.0.0-LOCKED`.
  2. **Visit Schema Generation:** Request the form package for Visit 3 (Vitals). Verify that the returned form fields match exactly the `1.0.0` version specification in the metadata repository.
  3. **Amendment Ingestion:** In the Designer Service, lock an amendment (`1.1.0-LOCKED`) which adds a new field `heart_rate_variability` (HRV) to Visit 3.
  4. **Active Subject Form Retrieval:** Re-request the Visit 3 form for `SUB-201`. Verify that `heart_rate_variability` is **not** present, as the subject remains consented to version `1.0.0`.
  5. **Subject Up-versioning:** Simulate a re-consent workflow by patching the subject's consented version to `1.1.0`. Re-request Visit 3. Verify that the HRV field is now successfully rendered.
- **Relational & Graph Database Verification Points:**
  - Verify that Neo4j contains an active `CONFORM_TO` relationship mapping `SUB-201` to `StudyProtocol {version: "1.1.0"}`.
  - Verify that the PostgreSQL execution table `subject_consents` contains a record with `subject_id = "SUB-201"`, `version_index = 2`, and `protocol_version = "1.1.0"`.

---

### 6.2 Metadata Consistency & Biomedical Concepts Audits
- **Clinical Domain Context:** Biomedical Concepts (BCs) are standard, semantic clinical data variables (e.g., "Systolic Blood Pressure", "HbA1c"). Value-Level Metadata (VLM) constraints define clinical safety boundaries (e.g., Systolic BP must be between 40 and 250 mmHg). The validation engine must verify that VLM constraints defined in the MDR are programmatically synchronized and enforced by the EDC database transaction handlers.
- **Verification Target:** CDISC CDASH variable constraint mapping to PostgreSQL column constraint validators and frontend schema generators.
- **Execution Protocol:**
  - **BC Creation:** In the Designer, define Biomedical Concept `BC-SYS-BP` representing Systolic Blood Pressure, with CDASH variable name `VSYSBP` and a VLM range restriction of `[40, 250]` mmHg.
  - **MDR-EDC Sync:** Trigger the automated synchronization pipeline to deploy the metadata to the active execution instance.
  - **Boundary Verification (Below Min):** Attempt to submit an eCRF payload for `VSYSBP` with a value of `39` mmHg. Verify that the transaction is rejected with an validation error.
  - **Boundary Verification (Above Max):** Attempt to submit an eCRF payload with a value of `251` mmHg. Verify rejection.
  - **Within Range Verification:** Submit an eCRF payload with a value of `120` mmHg. Verify that the transaction succeeds.
- **Relational & Graph Database Verification Points:**
  - Verify that PostgreSQL table `item_definitions` contains a record with `variable_name = "VSYSBP"`, `data_type = "float"`, `range_min = 40.0`, and `range_max = 250.0`.
  - Verify that the database trigger logs the successful entry (120) with a valid 21 CFR Part 11 audit record.

---

### 6.3 Electronic Data Capture (EDC) Component Behavior Validation
- **Clinical Domain Context:** Modern eCRFs utilize complex UI controls to capture advanced clinical endpoints (e.g., Visual Analog Scales for pain scores, interactive body maps, calculated fields like BMI, and repeating grids for Adverse Events). The XForm engine must validate that calculated values execute with absolute mathematical precision and handle missing data gracefully, avoiding browser crashes or silent data nullification.
- **Verification Target:** Abstract Syntax Tree (AST) evaluation of mathematical calculation paths and repeating grid boundaries.
- **Execution Protocol:**
  1. **Repeating Grid Boundary Test:** Open the Adverse Events (AE) repeating group. Add 50 distinct AE rows. Verify that the DOM rendering engine virtualizes the grid correctly without memory leaks or UI latency exceeding 100ms.
  2. **Calculated Field Evaluation (BMI):** Enter `weight = 70.0` kg and `height = 1.75` meters. Verify that the calculated field `bmi` computes immediately to `22.86`.
  3. **Calculated Field Boundary (Missing Data):** Clear the `height` field. Verify that the calculated field `bmi` resolves to `null` (or a blank state) rather than `0.0` or throwing an division-by-zero exception.
- **Relational & Graph Database Verification Points:**
  - Verify that table `form_submissions` stores the vital parameters in JSONB payloads where the structure is preserved: `{"weight": 70.0, "height": 1.75, "bmi": 22.86}`.
  - Verify that when height is cleared, the JSONB payload stores `{"weight": 70.0, "height": null, "bmi": null}`.

---

### 6.4 Discrepancy Notes, Edit Checks & Longitudinal Query Management
- **Clinical Domain Context:** Data cleaning is performed via automated "edit checks" that run cross-form longitudinal rules. For instance, an Adverse Event start date must never precede the subject's Informed Consent date. If an edit check fails, the system must automatically fire a "System Query" (Discrepancy Note). When the investigator updates the underlying data, the query lifecycle must automatically re-evaluate and transition.
- **Verification Target:** State machine transitions of queries and transactional re-triggering of edit check ASTs.
- **Execution Protocol:**
  1. **Informed Consent Date Ingestion:** Enter `Informed Consent Date = "2026-07-15"` on the Informed Consent eCRF. Save form.
  2. **Adverse Event Date Ingestion (Violating):** On the Adverse Event eCRF, enter `AE Start Date = "2026-07-10"`. Save form.
  3. **Auto-Query Verification:** Verify that the system instantly creates an auto-generated query linked to the `AE Start Date` field with status `OPEN` and message `AE Start Date precedes Informed Consent Date`.
  4. **Investigator Resolution:** Modify the `AE Start Date` to `"2026-07-18"`. Save form.
  5. **Auto-Query Close:** Verify that the system automatically recalculates the longitudinal edit check, marks the query status as `RESOLVED`, and logs an audit trail record detailing the resolution.
- **Relational & Graph Database Verification Points:**
  - Verify that PostgreSQL table `queries` contains a record with `query_id = "QRY-AE-001"`, `field_link = "AE_START_DATE"`, `status = "RESOLVED"`, and `resolved_by = "system_edit_check"`.
  - Verify that the audit ledger captures both the previous value ("2026-07-10") and the new value ("2026-07-18") with a reason for change of "Edit Check Auto-Resolution".

---

### 6.5 Clinical Trial Reporting, CDISC Export & PII Anonymization Verification
- **Clinical Domain Context:** Prior to submitting clinical data to regulatory agencies (FDA, EMA), datasets must be exported into standardized CDISC SDTM/ADaM formats. Regulatory guidelines (such as HIPAA and GDPR) strictly dictate that all Personally Identifiable Information (PII) must be removed or hashed. The export engine must dynamically anonymize direct subject identifiers while retaining structural, deterministic hashes so that longitudinal analysis can still map records to the same subject without exposing their real-identity.
- **Verification Target:** Cryptographic hashing of identifiers, RBAC exposure limitations, and structural correctness of CDISC-formatted exports.
- **Execution Protocol:**
  1. **Anonymization Verification:** As a blinded statistical analyst, request a CSV export of the clinical dataset for `STU-ONCO-2026`.
  2. **PII Masking Check:** Open the exported file. Inspect fields such as `SUBJECT_NAME`, `DATE_OF_BIRTH`, and `PATIENT_SSN`. Verify that these columns are completely stripped or replaced with deterministic, secure cryptographic hashes.
  3. **Blinded Role Enforcement:** Verify that user IDs (e.g., site investigators) are replaced with deterministic hashes (e.g., `SHA-256` of investigator ID), protecting staff identity during data reviews.
  4. **CDISC CDASH Compliance:** Verify that all variable names in the exported CSV conform strictly to CDASH naming standards (e.g., `VSYSBP` for vital signs systolic, `AESTDTC` for adverse event start date).
- **Relational & Graph Database Verification Points:**
  - Verify that the raw database contains the unhashed PII for site auditing (under strict access logs).
  - Verify that the API output for the blinded analyst role returns hashed values, proving the dynamic masking layer functions at the gateway boundaries.

---

## 7. Validation Summary & Conclusion

This Quality Assurance & Validation Plan establishes the complete, GxP-compliant lifecycle controls for the Cadence Clinical platform. By aligning our development workflows with **IEC 62304 Section 5.7 & 5.8**, adhering to **GAMP 5 Category 4 & 5** guidelines, and implementing an exhaustive, bidirectional **Requirements Traceability Matrix (RTM)**, Cadence Clinical ensures that 100% of functional requirements are fully traceable and mathematically verified.

The four execution-ready clinical verification scenarios and the detailed clinical domain verification protocols replace generic checklists with precise, domain-relevant validation procedures. These controls guarantee absolute data integrity, safeguard blinding protocols, secure clinical trial datasets under 21 CFR Part 11, and ensure the platform is fully prepared for regulatory inspection and clinical deployment.

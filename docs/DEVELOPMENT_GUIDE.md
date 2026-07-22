# Centralized Competitor Feature Specifications

## 1. Context & Objectives
This guide provides centralized functional specifications and manual verification checklists for competitor workflows, specifically Excel Case Report Form (CRF) importing and CDISC Unified Study Definition Model (USDM) versioning. By using these checklists, developers can quickly verify parity with competitor clinical systems during local development, reducing lookup times and implementation errors.

---

## 2. Excel CRF Import Parity

### 2.1 Specification: Excel CRF Parsing
The system must correctly parse clinical trial spreadsheets to translate workbook elements into corresponding clinical entities, ensuring full parity with competitor systems:
- **Sheets:** Each sheet within the Excel workbook represents a distinct CRF or form.
- **Sections:** Rows defined as section headers within the sheet are mapped to distinct layout containers or UI sections within the target form.
- **Groups:** Repeating sets of questions (e.g., grids or repeating groups) designated by grouping columns must be mapped to `ItemGroupDef` clinical entities.
- **Items:** Individual question rows are mapped to `ItemDef` clinical entities, translating their associated data types, constraints, and validation rules accordingly.

### 2.2 Manual Verification Checklist: Spreadsheet Import
Follow this step-by-step checklist to verify that Excel CRF import functionality correctly parses sheets, groups, and items into local clinical entities.

- [ ] **Step 1:** Start the local development environment.
- [ ] **Step 2:** Prepare a test Excel CRF spreadsheet containing multiple sheets, distinct section headers, grouped variables, and individual items.
- [ ] **Step 3:** Upload the spreadsheet using the local eCRF import interface or API endpoint.
- [ ] **Step 4:** Retrieve the mapped target entities from the local database or Designer UI.
- [ ] **Step 5:** **Verify Sheets:** Check that each sheet in the spreadsheet has correctly translated into a distinct form entity.
- [ ] **Step 6:** **Verify Sections:** Confirm that the section headers have correctly translated into corresponding layout sections within each form.
- [ ] **Step 7:** **Verify Groups:** Ensure that grouped columns are accurately structured as `ItemGroupDef` entities with the correct repeating attributes.
- [ ] **Step 8:** **Verify Items:** Compare the item rows in the spreadsheet against the created `ItemDef` entities, confirming that data types, constraints, and validation rules match exactly.

---

## 3. USDM Study Versioning Parity

### 3.1 Specification: USDM Versioning Translation
The system must translate study metadata into CDISC USDM-compatible versioning models.
- **Study Metadata Mapping:** Core study metadata (e.g., Title, Phase, Status) must map strictly to the USDM-defined fields.
- **Version Extraction Rules:** Any mutation to the study design or metadata must trigger a version increment. The updated metadata must map to a new version indicator or `StudyVersion` entity in the underlying models.
- **Graph Immutability:** Instead of overriding existing nodes, the current study status and version details must map to newly versioned nodes in the Neo4j graph, maintaining a clear `PREVIOUS_VERSION` relationship to the prior state.

### 3.2 Manual Verification Checklist: USDM Versioning Model
Follow this step-by-step checklist to verify that USDM study model versions accurately match the core definitions and version extraction rules.

- [ ] **Step 1:** Start the local development environment and log into the Designer Service.
- [ ] **Step 2:** Create a new Study Protocol with initial metadata and save it to establish the baseline version (Version 1).
- [ ] **Step 3:** Trigger a USDM study metadata export and verify that the output correctly reflects the initial version indicators.
- [ ] **Step 4:** Perform an update to the core study metadata (e.g., update the Phase or Status) and save the changes.
- [ ] **Step 5:** Export the updated study metadata via the local data model output.
- [ ] **Step 6:** **Verify Version Indicator:** Inspect the output and confirm that the version number has correctly incremented and matches the USDM version extraction rules.
- [ ] **Step 7:** **Verify Metadata Translation:** Check that the updated status and metadata accurately map to the core USDM definitions in the new export.
- [ ] **Step 8:** **Verify Graph History:** Query the local graph database to ensure the new version node links to the previous version node via a `PREVIOUS_VERSION` relationship, leaving the historical version intact.

---

## 4. Schedule of Activities (SoA) Parity

### 4.1 Specification: SoA Definitions
The system must correctly map visits, epochs, and scheduled activities in alignment with CDISC USDM standards.
- **Epochs:** Trial periods (e.g., Screening, Treatment, Follow-up) must map correctly to USDM `Epoch` entities.
- **Visits:** Study visits and encounters must be defined within their respective epochs as `StudyEventDef` entities.
- **Activities:** Clinical procedures and assessments must map to scheduled activities and link to the relevant forms/CRFs.

### 4.2 Manual Verification Checklist: SoA Definitions
- [ ] **Step 1:** Define a study protocol with multiple epochs, visits, and assigned activities.
- [ ] **Step 2:** Export the study definition to USDM format.
- [ ] **Step 3:** **Verify Epochs:** Confirm all defined epochs are correctly mapped.
- [ ] **Step 4:** **Verify Visits:** Ensure study events align with their parent epochs.
- [ ] **Step 5:** **Verify Activities:** Check that scheduled procedures map to the correct CRFs and encounters.

---

## 5. OpenRosa/Enketo XForm Rendering Parity

### 5.1 Specification: XForm Rendering Rules
The system must render clinical data capture forms compliant with OpenRosa standards.
- **Form UI Controls:** Data constraints and item definitions must map to valid XForm input controls.
- **Relevance & Skip Logic:** Branching logic must correctly map to XForm `relevant` attributes.
- **Calculations:** Computed fields must correctly translate to XForm `calculate` bindings.

### 5.2 Manual Verification Checklist: XForm Rendering
- [ ] **Step 1:** Define a CRF containing conditional logic and calculated fields.
- [ ] **Step 2:** Generate the OpenRosa XForm XML payload.
- [ ] **Step 3:** **Verify UI Controls:** Check that inputs match their defined data types.
- [ ] **Step 4:** **Verify Skip Logic:** Confirm that `relevant` attributes hide/show fields properly.
- [ ] **Step 5:** **Verify Calculations:** Test computed bindings in an Enketo-compatible renderer.

---

## 6. Subject State Machine Parity

### 6.1 Specification: Subject State Transitions
The system must govern participant statuses (e.g., Enrolled, Active, Completed, Withdrawn) via strict state machines.
- **State Integrity:** Participants can only transition between allowed states.
- **Transition Logs:** Every state change must record the timestamp and responsible user.

### 6.2 Manual Verification Checklist: Subject State Transitions
- [ ] **Step 1:** Create a test subject in the "Enrolled" state.
- [ ] **Step 2:** Attempt valid and invalid status transitions.
- [ ] **Step 3:** **Verify Integrity:** Confirm invalid transitions are rejected by the system.
- [ ] **Step 4:** **Verify Transition Logs:** Ensure valid state changes are recorded accurately.

---

## 7. Query Management Parity

### 7.1 Specification: Query Workflows
The system must support the complete lifecycle of clinical data queries (Open, Answered, Closed).
- **Query Creation:** Automated rules and manual reviewers can flag discrepancies.
- **Query Resolution:** Sites can provide answers, which data managers can subsequently close or re-open.

### 7.2 Manual Verification Checklist: Query Management
- [ ] **Step 1:** Trigger a validation discrepancy on an eCRF to generate a query.
- [ ] **Step 2:** Submit an answer to the query.
- [ ] **Step 3:** **Verify Workflow:** Ensure the query state moves from "Open" to "Answered".
- [ ] **Step 4:** **Verify Resolution:** Close the query and confirm it is locked.

---

## 8. Audit Log Parity

### 8.1 Specification: 21 CFR Part 11 Audit Logs
The system must capture comprehensive, immutable audit trails for all critical data modifications.
- **Required Fields:** `created_at`, `created_by`, `reason_for_change`, and `version_index`.
- **Immutability:** Audit records must never be updated or deleted.

### 8.2 Manual Verification Checklist: Audit Logs
- [ ] **Step 1:** Perform a data mutation (e.g., update a clinical observation).
- [ ] **Step 2:** Require a "reason for change" during the modification.
- [ ] **Step 3:** **Verify Fields:** Check the database to ensure the audit row contains the correct user, timestamp, and reason.
- [ ] **Step 4:** **Verify Immutability:** Confirm that standard API endpoints reject any attempts to modify the audit record.

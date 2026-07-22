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

# ADR-2159: Expose Query Lifecycle Event Emitters in Shared Clinical UI Components

- **Status:** Accepted
- **Date:** 2026-08-05
- **Authors:** @google-labs-jules
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Our clinical eCRF and data entry workspaces (such as the Rules Editor and Consent Authoring) rely on shared Vue 3 UI components in the `packages/ui` library to handle standard input types (`ClinicalInput`, `ClinicalLookupInput`, and `ClinicalRadioGroup`). However, these shared components lacked direct support for bubble-up notification events required to handle manual and automated clinical query lifecycles, as specified in **PRD-QRY-001** and **Trace-16**.

In order to support rich, inline data review capabilities—such as creating, responding to, closing, or reopening queries directly from the data entry inputs—we need a clean, standardized way for these inputs to emit query actions to their parent container workspaces.

## 2. Decision Drivers & Constraints

- Ensure compliance with regulatory requirements for clinical queries under 21 CFR Part 11 and GCP guidelines (**PRD-QRY-001**).
- Maintain modular separation of concerns: input controls should render queries but delegate actual database persistence and business rules to parent controllers.
- Avoid duplicate selector patterns or coupling input styling to query logic.

## 3. Options Considered

### Option 1: Direct API Integrations inside UI Components

- **Overview:** Have the shared inputs directly call the backend query APIs when actions occur.
- **Pros:** Completely self-contained components.
- **Cons:** ❌ Couples UI package to runtime authentication, gateway routes, and backend API structures, violating package isolation rules.

### Option 2: bubble-up Vue Custom Events (Selected)

- **Overview:** Expose standardized query interaction events upward from the shared inputs.
- **Pros:**
  - ✅ Keeps `packages/ui` clean, presentation-only, and fully isolated from business logic.
  - ✅ Parent page containers handle state, authentication, and gateway persistence seamlessly.
- **Cons:**
  - ❌ Requires parent containers to bind event handlers (`@create-query`, `@respond-query`, etc.) for every clinical input instance.

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Delegating API interactions to parent containers via event bubblers allows us to maintain strict hexagonal architecture principles. The shared clinical components remain lightweight and pure, while parent workspaces orchestrate clinical query transitions under **PRD-QRY-001**.

## 5. Consequences & Trade-offs

- **Positive Impact:** Workspaces can implement robust GxP-compliant inline query lifecycles directly on forms.
- **Negative Impact / Technical Debt:** Parent Vue templates must explicitly declare event listeners to handle the bubbled events.
- **Mitigation Strategy:** Leverage the standardized POM testing harness in E2E validation suites to ensure all inputs correctly bind and handle query actions without leakage.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/ui` (specifically `ClinicalInput.vue`, `ClinicalLookupInput.vue`, `ClinicalRadioGroup.vue`).
- **Verification Plan:**
  - Verified by running `python3 scripts/validate_adrs.py` to confirm ADR structure and trace mapping.
  - Bubbled events are validated by the Playwright integration test suite under sequential execution in the POM workspace test harness.

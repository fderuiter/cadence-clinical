# ADR-2593: Soft-Inactivation and Observation Superseding Model on eCRF Form Payload Updates

- **Status:** Accepted
- **Date:** 2026-10-01
- **Authors:** @cadence-team
- **Deciders:** @engineering-lead, @quality-compliance

@req:PRD-QRY-003

---

## 1. Context & Problem Statement

In clinical trial data management (eCRF / EDC), updating a form submission's payload extracts new discrete clinical observations (e.g., vital signs, lab results). Under 21 CFR Part 11 and ICH E6(R2) regulatory guidelines, historical observation data must remain immutable and audit-trailed. However, active queries and edit check evaluations must evaluate only current observations to avoid duplicate observation counting.

## 2. Decision Drivers & Constraints

- **21 CFR Part 11 Immutability:** Historical observation records cannot be physically deleted.
- **Query & Edit Check Accuracy:** Edit check evaluations and query exports must query only active, current observations.
- **Database Performance & Migration:** EF Core 10 database migration with indexed `form_submission_id` and `is_active` fields on `clinical_observations`.

## 3. Options Considered

### Option 1: Hard Delete / Hard Overwrite
- **Overview:** Delete old observations before inserting new ones.
- **Pros:**
  - Simple query logic.
- **Cons:**
  - ❌ Violates 21 CFR Part 11 auditability and data preservation requirements.

### Option 2: Soft-Inactivation & Superseding Model (Selected)
- **Overview:** Add `IsActive` (boolean) and `FormSubmissionId` fields to `ClinicalObservation`. When `FormSubmissionApplicationService.UpdatePayloadAsync` executes, soft-inactivate existing active observations for the form submission before persisting new observations from the updated payload.
- **Pros:**
  - ✅ Fully compliant with 21 CFR Part 11 immutable audit trail requirements.
  - ✅ Ensures default queries (`ListBySubjectAsync`, `ListByCoordinatesAsync`) filter out inactive observations.
  - ✅ Preserves active observation count across payload edits.
- **Cons:**
  - ❌ Incremental row count growth in `clinical_observations` table over time.

## 4. Decision Outcome

- **Chosen Option:** Option 2 (Soft-Inactivation & Superseding Model)
- **Justification:** Guarantees GxP regulatory compliance and statistical accuracy without data loss.

## 5. Consequences & Trade-offs

- **Positive Impact:** All historical observation states are preserved in the database with audit reasons, while application queries only see current active observations by default.
- **Mitigation Strategy:** Index on `form_submission_id` and `is_active` ensures fast filtering and soft-inactivation lookups.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `Cadence.Apps.Execution` (`ClinicalObservation`, `IObservationRepository`, `ObservationRepository`, `FormSubmissionApplicationService`, `ClinicalObservationConfiguration`, EF Core migration).
- **Verification Plan:** Unit tests in `FormSubmissionStateTests` and PostgreSQL integration tests in `ObservationSoftInactivationPostgresTests`.

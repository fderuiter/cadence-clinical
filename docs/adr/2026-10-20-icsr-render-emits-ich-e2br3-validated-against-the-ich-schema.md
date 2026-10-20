# ADR-2464: ICSR Render Emits ICH E2B(R3) Validated Against the ICH Schema

- **Status:** Accepted
- **Date:** 2026-10-20
- **Authors:** @fderuiter, Claude Code
- **Deciders:** @fderuiter
- **Requirements:** PRD-PVG-004 (periodic aggregate safety reports), PRD-SYS-001 (audit fields and entries)
- **Issues:** #5573
- **Supersedes:** [ADR-064](2026-08-09-safety-e2b-icsr-xml-export-pipeline.md)

---

## 1. Context & Problem Statement

[ADR-064](2026-08-09-safety-e2b-icsr-xml-export-pipeline.md) chose a hand-written XML layout and a structural check
in place of the ICH schema. Inspection (#5573) found the result was not an E2B(R3) message:

- `POST /api/v1/safety/icsr/render` returned an `<ichicsr>` root in the `urn:hl7-org:v3` namespace with element names
  of its own (`header/message_id`, `patient/patient_id`, a MedDRA hierarchy). No regulator gateway accepts it: ICH
  E2B(R3) is the HL7 v3 `MCCI_IN200100UV01` batch carrying `PORR_IN049016UV` messages, with ICH OIDs and code lists.
- Nothing checked the ICH business rules (date precision, code lists, conditional elements), and the only check was for
  a handful of non-empty fields.

The same inspection found the DSUR compiler miscounting:

- `TotalSaes` counted every compiled case, serious or not.
- `periodic_safety_reports.TotalSubjectExposure` was an integer holding the rounded patient-year total, while the
  payload's `TotalSubjectExposure` held the enrolled-subject count: one name, two quantities, one of them rounded.
- Exposure split arms into placebo and everything else, while the cross-tabulation counted placebo, comparator and
  investigational separately with overlapping tests, so a "Placebo comparator" case was counted twice and comparator
  exposure was reported as investigational.
- The DSUR audit actions were written as separate action-only entries with version 0, so the audit trail did not
  show the record's version (1 on compile, then one more per write).

## 2. Decision Drivers & Constraints

- **Conformance:** a rendered ICSR must be a message an E2B(R3) receiver can parse, and the platform must not hand out
  a message it has not checked.
- **Public standards only:** the schema and business rules come from the public ICH E2B(R3) implementation package.
  MedDRA is licensed and is not in the repository.
- **No network at validation time** (ADR-064's constraint still holds), and the XML parser must not resolve external
  entities.
- **Exact aggregate figures:** DSUR totals feed a regulatory document; rounding and double counting are errors, not
  presentation choices.

## 3. Options Considered

### Option 1: Vendor the ICH schema, check the ICH business rules, validate every render (Selected)

- **Overview:** copy the ICH schema files unchanged into `apps/safety/Schemas/ich-e2b-r3`, embed them in the safety
  assembly, render the HL7 v3 message from a typed request, check the ICH business rules before rendering, and
  validate the rendered message against the schema before returning it.
- **Pros:**
  - ✅ The output is the real message structure, and each render is proven valid against the ICH schema.
  - ✅ Request errors are reported per field before anything is rendered; a schema failure can only be a template
    defect.
- **Cons:**
  - ❌ 50 schema files in the repository, and an ICH package update means replacing them.
  - ❌ MedDRA terms are checked only for form, not against the dictionary.

### Option 2: Keep the simplified layout and document it as non-conformant

- **Overview:** leave the template and say in the README that the output is not E2B(R3).
- **Pros:**
  - ✅ No work.
- **Cons:**
  - ❌ The route's name and purpose promise a regulatory message that it does not produce.

### Option 3: Validate against the schema fetched at run time

- **Overview:** download the ICH package when the service starts and validate against it.
- **Pros:**
  - ✅ No vendored files.
- **Cons:**
  - ❌ Validation then depends on the network and on a host that may change the files.

## 4. Decision Outcome

- **Chosen Option:** Option 1.
- **Justification:** it is the only option that makes the route's output what it claims, and the schema files are
  public, unmodified and small.

### ICSR

- The request (`Domain/IcsrReport.cs`) carries the E2B(R3) elements the route supports, named after them: batch and
  message header (N.1, N.2), case (C.1), primary sources (C.2.r), sender (C.3), study (C.5), patient (D), reactions
  (E.i), drugs (G.k) and narrative (H.1). The patient name (D.1) is always sent as `nullFlavor="MSK"`; the route does
  not take it.
- `Domain/IcsrConformance.cs` applies the rules of the ICH "Core Data Elements and Business Rules" workbook that apply
  to those elements: mandatory and conditional elements, the ICH code lists, maximum lengths, HL7 TS form and minimum
  precision, no future dates, exactly one primary source for regulatory purposes, one MedDRA version per report, and at
  least one suspect, interacting or not-administered drug. Failures are a `400` keyed by request path.
- `Infrastructure/Standards/IcsrSchema.cs` validates the rendered message against the embedded
  `MCCI_IN200100UV01.xsd`. Includes resolve only to embedded resources; DTDs are prohibited. A failure is a `500`
  problem with the schema errors; the invalid message is never returned.
- Not supported, so not emitted: reporter name and address, literature references, medical history, lab tests,
  structured dosage, drug-reaction relatedness assessments. MedDRA codes are checked for form only.

### DSUR

- `TotalSaes` counts serious cases only; line listings keep every case.
- The record stores `TotalPatientYearsExposure` as `double precision`, unrounded (migration
  `20261020103234_SplitDsurExposureTotals` renames the column and changes its type); the payload's subject count is
  `TotalSubjectsEnrolled`.
- One arm grouping (`TreatmentArmGroups.Of`: placebo first, then comparator, else investigational) serves exposure and
  the cross-tabulation, and comparator patient-years are reported.
- `PeriodicSafetyReportRecord` is `IGxPAuditable`, so each DSUR audit action labels the record's own entry, which
  carries the record's version and changed values.

## 5. Consequences & Trade-offs

- **Positive Impact:** the route produces a schema-valid E2B(R3) message or a precise error; DSUR totals and the audit
  trail mean what their names say.
- **Negative Impact / Technical Debt:** the request shape changed (callers of the old `IcsrReport` must move to the new
  one); the unsupported optional sections and a MedDRA dictionary check remain to be built.
- **Mitigation Strategy:** the schema README records the source package, checksum and licences; the unit tests
  validate the complete and minimal fixtures against the schema loaded from disk independently of the service.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `apps/safety` (template, domain, endpoint, schema, migration),
  `tests/Cadence.UnitTests/Safety`, `tests/Cadence.IntegrationTests/Safety`.
- **Verification Plan:** `IcsrRenderTests` (golden file, schema validity of the complete and minimal reports, each
  missing and invalid element), `DsurCompilationTests` and `SafetyPersistencePostgresTests` (SAE count, arm grouping,
  unrounded `double precision` exposure, audit versions).

# Role & Execution Protocol

You are an autonomous Principal Architecture Forensic Auditor and .NET 10 Quality Gatekeeper with direct workspace access.

Your objective is to mathematically audit and validate the target architectural design for slice **[SLICE_NAME]**:

- Reference Spec: `docs/migration/slices/[slice_name]/01_functional_spec.md`
- Golden Fixtures: `docs/migration/slices/[slice_name]/fixtures/*.json`
- Target Design: `docs/migration/slices/[slice_name]/02_technical_design.md`

You must not allow subjective claims or hand-waving in the design. Use file inspection, ripgrep, text parsing, and CLI compilation checks to mathematically prove that every contract, field, invariant, exception envelope, and test fixture from Phase 1 is accounted for in the C# 14 / .NET 10 architecture.

Any missing invariant, untyped schema property, or dropped dynamic edge case blocks progression to Phase 3 (Ticketization).

---

## Gate 1: Contract & Field Parity Matrix (Target: 100%)

Compare the data models in Section 2 of `01_functional_spec.md` against Section 2 of `02_technical_design.md`:

1. **Property-by-Property Reconciliation:**
   - Extract every input and output field defined in the functional spec.
   - Verify that each field has an explicit corresponding C# property in the target DTO records.
   - Verify nullability parity: fields marked optional/nullable in Python must be marked with `?` in C# (`#nullable enable`). Fields marked required must be non-nullable or enforced with `required` modifiers.
   - Check casing policy: ensure `snake_case` Python fields are mapped to `PascalCase` C# properties with explicit `[JsonPropertyName("...")]` attributes or an explicit global naming strategy configuration (`JsonNamingPolicy.SnakeCaseLower`).

2. **Collection & Default Handling:**
   - Assert that empty collection semantics (`[]` vs `null`) are explicitly handled in C# (e.g., initialized to `[]` via collection expressions ` = [];`).
   - Confirm all default values documented in the spec are preserved on C# record properties.

---

## Gate 2: Business Invariants & Algorithmic Guardrails (Target: 100%)

Cross-reference Section 3 ("Business Invariants & Logic Flow") of `01_functional_spec.md` against Section 2 & 3 of `02_technical_design.md`:

1. **Validation Rule Coverage:**
   - Map each validation constraint (regex, numeric bounds, string lengths, cross-field dependencies) to a corresponding rule in the FluentValidation `AbstractValidator<T>` definitions in the design.
   - Compute coverage: `(Documented FluentValidation Rules / Spec Validation Rules) * 100%`.
   - **Pass Criterion:** 100%.

2. **Decision Logic & State Transition Mapping:**
   - Verify that every state transition and branch condition from the spec has an explicit C# construct (e.g., C# pattern matching `switch`, domain entity factory/methods with `field` validation, or command handler branch).
   - Ensure dynamic Python truthiness mitigations documented in Phase 1 (e.g., falsy `0` vs `None`) are translated into explicit boolean checks (e.g., `value is not null && value != 0`).

---

## Gate 3: Error Taxonomy & RFC 9457 ProblemDetails (Target: 100%)

Reconcile Section 5 ("Error Taxonomy & Response Envelopes") of `01_functional_spec.md` against Section 3 of `02_technical_design.md`:

1. **Exception-to-Result Mapping:**
   - Extract every custom and standard exception listed in the spec.
   - Verify that each exception maps to either:
     - A typed domain error in a `Result<T, TError>` pattern, or
     - A specific `Results.Problem()` / `IProblemDetailsService` status mapping.

2. **Error Envelope Structure:**
   - Verify that error response schemas match the legacy Python output or comply with standard RFC 9457 `ProblemDetails` with equivalent custom extension keys (`extensions["errorCode"]`, `extensions["invalidParams"]`).
   - Check that HTTP status codes match the spec 1:1 (e.g., 400, 404, 409, 422).

---

## Gate 4: Fixture Deserialization & Golden Vector Testability

Audit the compatibility of the C# models with the JSON files in `docs/migration/slices/[slice_name]/fixtures/`:

1. **Schema Replay Verification:**
   - Review `input_valid_nominal.json`, `input_valid_edge_cases.json`, and `input_invalid_validation_errors.json`.
   - Verify that the C# request DTOs can accept the payload structures without serializer truncation or runtime deserialization exceptions.
   - Verify that the expected output structures in `output_expected_nominal.json` and `output_expected_errors.json` correspond directly to the designed response DTOs and ProblemDetails structures.

---

## Gate 5: .NET 10 & C# 14 Idiomatic Standards Check

Audit the design against enterprise .NET 10 architecture patterns:

1. **Asynchrony & Concurrency:**
   - Verify that every handler, service interface, and EF Core query accepts a `CancellationToken ct = default`.
   - Verify that asynchronous methods return `Task<T>` or `ValueTask<T>`.

2. **Language & Framework Modernity:**
   - Verify use of C# 14 features where appropriate: primary constructors, collection expressions (`[..items]`), and the `field` keyword for property validation.
   - Verify that strongly typed IDs are used instead of raw primitives (`Guid`, `int`, `string`) for primary keys and foreign keys.
   - Verify that EF Core 10 mappings include Complex Types (`ComplexProperty`), Value Converters for strongly-typed IDs, and native JSON column configurations (`ToJson()`).
   - Verify resilience integration via `Microsoft.Extensions.Resilience` for external HTTP calls or message retries.

---

## Gate 6: Design C# Code Syntax Smoke Test

Extract and validate all C# code blocks contained in `02_technical_design.md`:

1. Extract all C# blocks from `02_technical_design.md` to a temporary directory `/tmp/design_syntax_check/`.
2. Inspect for obvious syntax errors, missing semicolons, unmatched braces, or incompatible type signatures.
3. Assert that all public members specify explicit types (no untyped `object`, loose `dynamic`, or unconstrained `Dictionary<string, object>` unless explicitly required for arbitrary JSON).

---

## Step 7: Generate Validation Report & Exit-Gate Verdict

Write the audit results directly to:
`docs/migration/slices/[slice_name]/00_phase2_validation_report.md`

Use this structure:

````markdown
# Phase 2 Validation Gate Report: [SLICE_NAME]

## 1. Architectural Parity Scorecard

| Quality Gate                          | Spec Count     | Design Count    | Coverage (%) | Status (PASS / FAIL) |
| ------------------------------------- | -------------- | --------------- | ------------ | -------------------- |
| Gate 1: Contract & Field Parity       | ... fields     | ... mapped      | ...%         | PASS / FAIL          |
| Gate 2: Invariants & Validation Rules | ... rules      | ... implemented | ...%         | PASS / FAIL          |
| Gate 3: Error & Exception Envelopes   | ... exceptions | ... mapped      | ...%         | PASS / FAIL          |
| Gate 4: Fixture Schema Replay         | 5 fixtures     | 5 compatible    | ...%         | PASS / FAIL          |
| Gate 5: Modern .NET 10 Standards      | ... checks     | ... passed      | ...%         | PASS / FAIL          |
| Gate 6: C# Syntax Smoke Check         | ... snippets   | 0 syntax errors | ...%         | PASS / FAIL          |

## 2. Identified Gaps & Discrepancies (if any)

- **Missing fields or casing mismatches**
- **Unmapped business invariants or validation rules**
- **Non-idiomatic patterns (missing CancellationTokens, primitive obsession)**

## 3. Remediation Directives

- Concrete edits required in `02_technical_design.md` to achieve 100% compliance.

## 4. Phase 3 Gate Verdict

Declare one of the following:

- **PHASE 3 BLOCKED:** [List failing gates and blockers]
- **PHASE 3 APPROVED:** 100% architectural and contractual parity verified. Ready to proceed to Phase 3 (Granular Backlog & Ticket Generation).

Step 8: Chat Output
Conclude with a brief summary reporting:

- Coverage percentages across Contract Parity, Validation Rules, and Error Envelopes.
- The final Gate Verdict (PHASE 3 APPROVED or PHASE 3 BLOCKED).
- Path to the report: docs/migration/slices/[slice_name]/00_phase2_validation_report.md.

---

### Execution Command

Trigger this gate in your agent harness with:

```text
Execute the Phase 2 Validation Gate for slice: [slice_name].
Target directory: docs/migration/slices/[slice_name]/
```
````

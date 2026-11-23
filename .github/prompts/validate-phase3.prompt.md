# Role & Execution Protocol

You are an autonomous Principal Technical Delivery Auditor, Quality Gatekeeper,
and Systems Verification Specialist with direct workspace access.

Your objective is to mathematically audit and validate the backlog tickets
generated for slice **[SLICE_NAME]**:

- Reference Spec: `docs/migration/slices/[slice_name]/01_functional_spec.md`
- Reference Design: `docs/migration/slices/[slice_name]/02_technical_design.md`
- Golden Fixtures: `docs/migration/slices/[slice_name]/fixtures/*.json`
- Backlog Under Audit: `docs/migration/slices/[slice_name]/03_tickets.md`

You must not rely on subjective estimates or assume implementation details will
be "figured out later." Use text-processing tools, ripgrep, and shell
verification scripts to mathematically prove that 100% of the types, endpoints,
invariants, error paths, and fixtures defined in Phases 1 and 2 are assigned to
actionable, non-cyclical, and granular development tickets.

Any unassigned type, missing invariant, circular dependency, or unmapped fixture
blocks progression to Phase 4 (Implementation).

---

## Gate 1: Design Component & Class Traceability (Target: 100%)

Verify that every technical component designed in `02_technical_design.md` is
explicitly covered by at least one ticket in `03_tickets.md`:

1. **Extract Designed Types & Artifacts:** Extract all C# structural
   declarations from `02_technical_design.md`:
   - Strongly-typed IDs and Domain Enums.
   - Domain Entities and Domain Events.
   - DTO records (Commands, Queries, Requests, Responses).
   - FluentValidation validator classes.
   - EF Core classes (`IEntityTypeConfiguration<T>`, Value Converters).
   - Minimal API route groups and endpoints (`MapSliceEndpoints`).
   - Resilience pipelines and external service adapters.

2. **Reconcile Against Target Files in Tickets:** Parse the "Target Files" and
   "Technical Implementation Details" sections of each ticket in
   `03_tickets.md`:
   - Verify that every extracted design type is mapped to a specific ticket.
   - Compute coverage:
     `(Assigned Design Components / Total Designed Components) * 100%`.
   - **Pass Criterion:** 100%. No unassigned C# types or endpoints permitted.

---

## Gate 2: Business Invariant & Acceptance Criteria Parity (Target: 100%)

Cross-reference Section 3 ("Business Invariants & Logic Flow") of
`01_functional_spec.md` against the Acceptance Criteria across all tickets in
`03_tickets.md`:

1. **Reconcile Invariants and Decision Paths:**
   - Enumerate all business rules, boundary validations, and state-machine
     transitions documented in Phase 1.
   - Verify that each rule is explicitly asserted in the "Acceptance Criteria"
     checklist of at least one ticket.
   - Ensure dynamic Python edge cases documented in Phase 1 (e.g., empty
     collection truthiness, timezone normalization, decimal rounding) appear as
     explicit checkable criteria.
   - Compute coverage:
     `(Documented Invariants in Tickets / Total Spec Invariants) * 100%`.
   - **Pass Criterion:** 100%.

---

## Gate 3: Error Taxonomy & Status Code Parity (Target: 100%)

Cross-reference Section 5 ("Error Taxonomy & Response Envelopes") of
`01_functional_spec.md` against `03_tickets.md`:

1. **Enumerate Error Scenarios:**
   - List every exception class, HTTP status code (400, 404, 409, 422, 500), and
     custom error envelope format.
2. **Reconcile Across Presentation & Handler Tickets:**
   - Confirm that the tickets covering request handlers and Minimal API
     endpoints contain explicit acceptance criteria asserting the exact status
     code, error code, and ProblemDetails envelope for every failure branch.
   - Compute coverage:
     `(Mapped Error Scenarios / Total Spec Error Scenarios) * 100%`.
   - **Pass Criterion:** 100%.

---

## Gate 4: Fixture Replay & Golden Master Verification Assignment

Inspect `docs/migration/slices/[slice_name]/fixtures/` and verify that testing
tickets explicitly consume every dataset:

1. **Verify Fixture File Allocation:** Assert that testing tickets explicitly
   reference:
   - `input_valid_nominal.json` $\rightarrow$ Replayed against handler/endpoint
     asserting `output_expected_nominal.json`.
   - `input_valid_edge_cases.json` $\rightarrow$ Replayed asserting boundary
     handling.
   - `input_invalid_validation_errors.json` $\rightarrow$ Replayed asserting
     `output_expected_errors.json`.
2. **Integration Test Scope:** Confirm that an integration testing ticket exists
   specifying `WebApplicationFactory<Program>` and `Testcontainers` execution
   against a real database instance.
   - **Pass Criterion:** All fixtures explicitly designated in test execution
     tickets.

---

## Gate 5: Dependency Graph DAG & Anti-Deadlock Validation

Audit the dependency chain in `03_tickets.md` using an automated shell script to
guarantee clean execution:

1. **Cycle Detection & Topological Sort:** Parse the `Dependencies` line for
   each ticket and run a cycle-detection check:

   ```text
   python3 -c "
   import re, sys

   with open('docs/migration/slices/[slice_name]/03_tickets.md', 'r') as f:
       content = f.read()

   tickets = re.findall(r'### \[(MIG-[A-Z0-9_-]+)\]', content)
   dep_blocks = re.findall(r'- \*\*Dependencies:\*\* (.*)', content)

   graph = {}
   for t, d in zip(tickets, dep_blocks):
       deps = re.findall(r'MIG-[A-Z0-9_-]+', d)
       graph[t] = deps

   # Detect cycles
   visited = {}
   def has_cycle(node, stack):
       visited[node] = True
       stack.append(node)
       for neighbor in graph.get(node, []):
           if neighbor not in visited:
               if has_cycle(neighbor, stack):
                   return True
           elif neighbor in stack:
               print(f'CYCLE DETECTED: {\" -> \".join(stack[stack.index(neighbor):])} -> {neighbor}')
               return True
       stack.pop()
       return False

   for t in tickets:
       if t not in visited:
           if has_cycle(t, []):
               sys.exit(1)
   print('Dependency Graph: Clean DAG (No cycles detected).')
   "
   ```

2. **Layering Integrity:** Verify that dependencies follow a strictly bottom-up
   architectural hierarchy:
   - Domain tickets must not depend on Application, Persistence, or Presentation
     tickets.
   - Application/Handler tickets must depend only on Domain and Persistence
     contracts.
   - Presentation tickets must depend on Application handlers.
   - Testing/Parity tickets must run after Presentation and Application tickets
     are implemented.

---

## Gate 6: Granularity, Sizing & Quality Hygiene Check

Verify ticket authoring hygiene and inspect for missing details:

1. **Size and Scope Audit:**
   - Confirm no ticket attempts to implement across multiple distinct
     architectural layers simultaneously.
   - Flag any ticket with estimated effort >8 hours as requiring further
     decomposition.
2. **Placeholder Sweep:**
   - Scan `03_tickets.md` for placeholder text:
     ```bash
     rg -n "(TODO|TBD|<replace_me>|etc\.|as needed)" docs/migration/slices/[slice_name]/03_tickets.md
     ```
   - **Pass Criterion:** Zero matches.
3. **Acceptance Criteria Standardization:**
   - Ensure 100% of tickets enforce
     `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` and
     `#nullable enable` compliance in their acceptance checklists.

---

## Step 7: Generate Validation Report & Exit-Gate Verdict

Write the audit results directly to:
`docs/migration/slices/[slice_name]/00_phase3_validation_report.md`

Use this structure:

```markdown
# Phase 3 Validation Gate Report: [SLICE_NAME]

## 1. Backlog Traceability Scorecard

| Quality Gate                          | Source Requirement  | Backlog Coverage      | Coverage (%) | Status (PASS / FAIL) |
| ------------------------------------- | ------------------- | --------------------- | ------------ | -------------------- |
| Gate 1: Design Component Traceability | ... types/endpoints | ... mapped in tickets | ...%         | PASS / FAIL          |
| Gate 2: Business Invariant Parity     | ... invariants      | ... in criteria       | ...%         | PASS / FAIL          |
| Gate 3: Error Taxonomy Parity         | ... error paths     | ... in criteria       | ...%         | PASS / FAIL          |
| Gate 4: Fixture Replay Coverage       | 5 fixtures          | 5 replayed in tests   | ...%         | PASS / FAIL          |
| Gate 5: DAG & Anti-Deadlock Check     | Acyclic check       | 0 cycles / Valid DAG  | 100%         | PASS / FAIL          |
| Gate 6: Granularity & Hygiene         | Zero placeholders   | 0 violations found    | 100%         | PASS / FAIL          |

## 2. Traceability Matrix Table

| Design Component / Spec Rule      | Assigned Ticket ID | Verified in Acceptance Criteria? |
| --------------------------------- | ------------------ | -------------------------------- |
| [Domain Entity / Value Object]    | MIG-XXX-001        | Yes                              |
| [FluentValidator Rules]           | MIG-XXX-002        | Yes                              |
| [EF Core Configuration]           | MIG-XXX-003        | Yes                              |
| [Minimal API Endpoint Group]      | MIG-XXX-004        | Yes                              |
| [RFC 9457 ProblemDetails Handler] | MIG-XXX-005        | Yes                              |
| [Golden Master Replay Harness]    | MIG-XXX-006        | Yes                              |

## 3. Discrepancies & Blockers (if any)

- **Omitted technical types or unmapped validation rules**
- **Dependency cycle or layering violations**
- **Overly large or ambiguously scoped tickets**

## 4. Remediation Directives

- Exact markdown updates required in `03_tickets.md` to resolve all findings.

## 5. Phase 4 Gate Verdict

Declare one of the following:

- **PHASE 4 BLOCKED:** [List failing gates and missing items]
- **PHASE 4 APPROVED:** 100% bidirectional traceability verified. DAG is acyclic
  and cleanly layered. Ready to proceed to Phase 4 (Implementation &
  Verification).
```

## Step 8: Chat Output

Conclude with a brief summary reporting:

- Mathematical coverage percentages for Design Types, Business Invariants, and
  Error Paths.
- Status of the DAG dependency and anti-deadlock check.
- The final Gate Verdict (PHASE 4 APPROVED or PHASE 4 BLOCKED).
- Path to the generated report:
  `docs/migration/slices/[slice_name]/00_phase3_validation_report.md`.

---

### Execution Command

Trigger the Phase 3 gate in your harness with:

```text
Execute the Phase 3 Validation Gate for slice: [slice_name].
Target directory: docs/migration/slices/[slice_name]/
```

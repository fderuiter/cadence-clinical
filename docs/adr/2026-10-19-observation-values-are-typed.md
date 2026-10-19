# ADR-2623: Observation Values Are Typed

- **Status:** Accepted
- **Date:** 2026-10-19
- **Authors:** @fderuiter
- **Deciders:** @fderuiter (accepted the recommended options on 2026-10-19)
- **Issue:** #5363 (gate 3 of #5310, under #4719)
- **Requirements:** PRD-SYS-001 (audit fields and audit entries), 21 CFR 11.10(e)
- **Amends:** kernel specification §8.1 (`ObservationValue`, K-FACT-2, K-FACT-3) and §17 (K036); extends ADR-2620 and
  ADR-2463

---

## 1. Context & Problem Statement

Spec §8.1 names an `ObservationValue` (`Quantity(value, ucum) | Coded | Text | Boolean | Instant | Missing(reason)`),
but the code stored `string Value` on the kernel `Observation` and its revisions. Nothing in the kernel told a missing
value from an empty string, kept the unit a value was collected in, or kept a partial date such as `2026-03`. Every
module would invent its own encoding, and the SDTM projection (`IMappingEngine`, ADR-2463) had to be handed a value
and a unit as two loose strings and could not write `--STAT`/`--REASND` at all.

## 2. Decision Drivers & Constraints

- **K-FACT-2:** units are UCUM; the unit belongs to the value, not to a separate column a caller may forget.
- **K-FACT-3:** a missing value is a first-class value with a reason, never an empty string or a sentinel.
- **K-TRUTH-1:** the value is clinical truth; SDTM `--ORRES`/`--ORRESU`/`--STAT`/`--REASND` are derived from it.
- **Pre-deployment:** there are no production records, so the column can change type in one migration without
  back-compat machinery.
- **One shape everywhere:** the command, the event, the stored row, the HTTP API and the web client carry the same
  value, so nothing is re-parsed at a boundary.

## 3. Options Considered

### Option 1: One polymorphic value record stored as jsonb (Selected)

- **Overview:** an abstract `ObservationValue` record in `Cadence.Kernel.Abstractions` with one variant per kind,
  serialised with a `type` discriminator, stored in one `jsonb` column on `observations` and `observation_revisions`.
- **Pros:**
  - ✅ Invalid values are hard to express: each variant validates itself (`Problem()`), and the domain refuses a value
    with a problem.
  - ✅ The command, the event payload, the row and the API share one JSON shape.
  - ✅ A new kind is a new variant, not a new column.
- **Cons:**
  - ❌ Values are not indexable as typed columns; queries over numeric values need a jsonb expression or a derived
    projection.

### Option 2: Typed columns (`value_number`, `value_unit`, `value_code`, `value_text`, `null_flavor`, …)

- **Pros:**
  - ✅ Plain SQL over numeric values.
- **Cons:**
  - ❌ Every column is nullable and the valid combinations live in check constraints and code twice.
  - ❌ The event and API shapes still need a discriminated union, so there are two models.

### Option 3: Keep the string and encode the kind in it

- **Cons:**
  - ❌ The problem this issue exists to fix.

## 4. Decision Outcome

- **Chosen Option:** Option 1.

### 4.1 The variants

| `type`     | Fields                                                                       | Rule                                                                                                                    |
| ---------- | ---------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------- |
| `quantity` | `value` (decimal), `unit` (UCUM), `comparator` (`<` `<=` `>=` `>`, optional) | The unit passes a UCUM syntax check; the decimal keeps the precision entered (`1.50` stays `1.50`)                      |
| `coded`    | `codeSystemVersionId`, `code`                                                | A code in a pinned code-system version (§6.2), so the meaning cannot drift when the code system is revised              |
| `text`     | `value`                                                                      | Not blank, at most 4000 characters                                                                                      |
| `boolean`  | `value`                                                                      |                                                                                                                         |
| `dateTime` | `value`, derived `precision`                                                 | ISO 8601 at year, month, day, minute or second precision (`2026`, `2026-03`, `2026-03-14T09:30Z`), a real calendar date |
| `missing`  | `reason` (null flavor), `detail` (optional)                                  | `NotDone`, `Unknown`, `AskedButUnknown`, `NotAsked`, `NotApplicable`, `Masked`                                          |

The spec's `Coded(ConceptRef)` becomes `Coded(CodeSystemVersionId, Code)`: a collected answer is a term in a
versioned code system (CDISC CT, MedDRA, LOINC answers), while the Concept the observation is about is already the
Observation's `Concept`. Binding a code to a Concept stays in the terminology layer. The spec's `Instant` becomes
`dateTime` with a precision, because clinical data collects partial dates.

### 4.2 Fit with the protocol

`CollectionItem` gains an optional `ValueKind`; without it the expected kind is a quantity when the item names a unit
and coded when it names a terminology. `CollectionItem.Admits(value)` checks the kind, the unit and the code-system
version; a `missing` value fits every item (K-FACT-3). The eCRF save (ADR-2622, #5459) uses it as a structural check.

### 4.3 Storage and events

`observations.value` and `observation_revisions.value` become `jsonb` (migration `TypedObservationValues`; existing
strings become `text` values). PostgreSQL reorders jsonb keys, so every reader sets
`AllowOutOfOrderMetadataProperties`. `ObservationRecorded` and `ObservationCorrected` move to schema version 2 and carry
the typed value. The kernel HTTP API carries it in snake case (`code_system_version_id`).

### 4.4 SDTM projection

`MappingInput.From(concept, value)` builds the mapping input from a typed value. A `missing` value writes no result,
`--STAT = NOT DONE`, and `--REASND` from the detail or the null flavor; a mapping that names no status variable is a
problem, never a guess. A quantity with a comparator is written as a bound (`<1.0`), a `dateTime` as its ISO 8601 text.
Mapping specifications gain the optional `status` and `reasonNotDone` result variables.

## 5. Consequences & Trade-offs

- **Positive Impact:** one value model for the kernel, its events, its API and the web; null flavors, units and partial
  dates survive from entry to SDTM.
- **Negative Impact / Technical Debt:** numeric queries over values need jsonb expressions; the UCUM check is
  syntactic, not a full UCUM parser, so `mm[Hg]` and `kg/m2` pass and the unit's meaning is checked by the
  CollectionItem and the mapping's conversions.
- **Mitigation Strategy:** analytical reads go through derived projections, not the evidence tables.
- **Out of scope:** partial observation timing (`--DTC` from a partial `observed_at`) stays with the observation's
  valid period (#4729 and the eCRF slice); this ADR types the value only.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `Cadence.Kernel.Abstractions` (`Evidence/ObservationValue.cs`, commands,
  events, `CollectionItem`, mapping), `Cadence.Kernel.Domain` (`Observation`), `Cadence.Kernel.Persistence`
  (conversion and migration), `Cadence.Kernel.Api`, `Cadence.Standards` (`MappingEngine`), `apps/web` (the observation
  correction screen keeps the value's kind and unit).
- **Verification Plan:** `ObservationValueTests` (JSON shape and round trips, validation, `Admits`, mapping),
  `KernelObservationPostgresTests` (jsonb round trip and correcting a missing value) and
  `KernelObservationHostPostgresTests` (HTTP and browser-to-database correction of a quantity).

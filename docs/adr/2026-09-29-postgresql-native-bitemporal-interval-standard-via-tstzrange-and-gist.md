# ADR-2246: PostgreSQL Native Bitemporal Interval Standard via tstzrange and GiST

- **Status:** Accepted
- **Date:** 2026-09-29
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Clinical research software under 21 CFR Part 11 and ICH GCP E6(R2) operates on contestable clinical facts where the system must answer two distinct questions:
1. **What was true in the physical clinical trial at time $t$?** (Valid Time, e.g., when was an amendment effective at Site A? What was the subject's blood pressure during Visit 2?)
2. **What did Cadence know at transaction time $t_{\text{rec}}$?** (Recorded Time, e.g., when an investigator corrected a typo in an observation three weeks later, what was the data state when the DSMB interim report was generated?)

Standard relational models use simple `created_at` / `updated_at` columns or overwrite records, destroying historical contestability. While event sourcing replays streams, it incurs severe read-path complexity for regulated tabular extractions (SDTM/ADaM). A native bitemporal model is required to satisfy regulatory audit reproduction requirements under 21 CFR Part 11 (PRD-SYS-053).

## 2. Decision Drivers & Constraints

- **Driver 1 (21 CFR Part 11 Audit Reproduction):** Ability to reproduce exact point-in-time clinical datasets and protocol applicability as of any historical timestamp (`known_at`).
- **Driver 2 (Database-Enforced Invariants):** Prevent overlapping valid intervals for the same entity without relying on error-prone application validation loops.
- **Driver 3 (Zero Proprietary Extensions):** Standardize on pure open-source PostgreSQL 14+ capabilities supported out of the box by AWS Aurora, Azure Flexible Server, and standard Docker images.
- **Constraint:** Native AOT compatibility and direct EF Core 10 LINQ expression mapping.

## 3. Options Considered

### Option 1: Native PostgreSQL `tstzrange` with `btree_gist` Exclusion Constraints (Selected)

- **Overview:** Model bitemporal state using native PostgreSQL range types: `valid_period tstzrange NOT NULL` and `recorded_period tstzrange NOT NULL`. Closed records have upper bounds set; active records have unbounded upper intervals (`upper_inf`). Exclusion constraints using `btree_gist` guarantee non-overlapping intervals at the database engine level (e.g. `EXCLUDE USING gist (participant_id WITH =, valid_period WITH &&) WHERE (upper(recorded_period) IS NULL)`). Point-in-time queries leverage the index-backed `@>` (contains) operator.
- **Pros:**
  - ✅ Database-enforced mathematical guarantee against interval corruption.
  - ✅ Supported natively by Npgsql and EF Core 10 (`NpgsqlRange<DateTimeOffset>`).
  - ✅ Zero external extensions required beyond standard PostgreSQL contrib `btree_gist`.
  - ✅ High-speed GiST and BRIN indexing for massive observation tables.
- **Cons:**
  - ❌ Developers must learn range syntax and interval query patterns.

### Option 2: Dual DateTimeOffset Columns Managed in Application Code

- **Overview:** Add `ValidFrom`, `ValidTo`, `RecordedAt`, and `SupersededAt` columns, validated in application services.
- **Pros:**
  - ✅ Familiar column representation.
- **Cons:**
  - ❌ Race conditions can allow overlapping validity intervals under concurrent writes.
  - ❌ Requires complex multi-column index definitions and verbose LINQ `WHERE` clauses.

## 4. Decision Outcome

- **Chosen Option:** Option 1: Native PostgreSQL `tstzrange` with `btree_gist` Exclusion Constraints.
- **Justification:** Guarantees absolute regulatory data integrity under 21 CFR Part 11 at the database engine level, eliminating race conditions while enabling ultra-fast historical reconstruction queries.

## 5. Consequences & Trade-offs

- **Positive Impact:**
  - Guaranteed interval integrity without complex locking.
  - Seamless time-travel queries for interim analysis and audits (`WHERE valid_period @> @t AND recorded_period @> @known_at`).
- **Negative Impact / Technical Debt:** Requires EF Core custom value converters and Npgsql range mapping configurations in `Cadence.Kernel.Persistence`.
- **Mitigation Strategy:** Encapsulate bitemporal query operators in reusable extension methods (`AsOf(validTime, knownAt)`) in `Cadence.Kernel.Domain`.

## 6. Implementation & Verification

- **Affected Assemblies:** `Cadence.Kernel.Domain`, `Cadence.Kernel.Persistence`, `Cadence.Apps.Execution`.
- **Verification Plan:**
  - Property-based tests verifying concurrent correction insertions never create overlapping valid intervals.
  - Acceptance tests validating historical observation reconstructions and amendment effectivity as of `(t, known_at)`.

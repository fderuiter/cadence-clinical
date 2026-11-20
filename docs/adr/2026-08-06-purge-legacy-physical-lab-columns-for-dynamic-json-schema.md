# ADR-2345: Purge Legacy Physical Lab Columns for Dynamic JSON Schema

Also known as: ADR-2159 (former number)

- **Status:** Accepted
- **Date:** 2026-08-06
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

To prevent database schema bloating and align with the design in ADR-2332, we need to purge the physical laboratory columns (such as `lab_source`, `lab_site_id`, `lab_indicator`, etc.) from the PostgreSQL `clinical_observations` table. To do this safely under GxP guidelines, we must run an inline data migration that preserves all existing records by serializing their values into the dynamic `additional_properties` JSON column before the physical columns are dropped. This satisfies requirement PRD-LAB-001.

## 2. Decision Drivers & Constraints

- Ensure 100% data preservation of existing physical column records during schema upgrade.
- Automate the inline migration to avoid downstream pipeline breakage and manual migrations.
- Business/GxP requirement (PRD-LAB-001)

## 3. Options Considered

### Option 1: Option A (Selected): Inline pre-migration of legacy columns to JSON before performing the DROP COLUMN operation.

- **Overview:** Option A (Selected): Inline pre-migration of legacy columns to JSON before performing the DROP COLUMN operation.
- _**Pros:**_
  - ✅ Satisfies architectural requirements
- _**Cons:**_
  - ❌ Requires implementation and maintenance overhead

### Option 2: Option B (Alternative): Manual migration script executed separately by operators before the database upgrade.

- **Overview:** Option B (Alternative): Manual migration script executed separately by operators before the database upgrade.
- _**Pros:**_
  - ✅ Satisfies architectural requirements
- _**Cons:**_
  - ❌ Requires implementation and maintenance overhead

## 4. Decision Outcome

- **Chosen Option:** Option A because it integrates the pre-migration directly into the startup migration utility (`upgrade_existing_tables` in `migrate.py`), ensuring that all tables are self-healed and satisfying PRD-LAB-001 without manual intervention.
- **Justification:** Selected as primary architectural approach.

## 5. Consequences & Trade-offs

- Positive: Zero data loss, self-healing database upgrades, and clean physical relational schema.
- Negative: Slightly higher database startup migration time during initial deployment.

## 6. Implementation & Verification

- Affected files: `apps/execution/database/migrate.py`
- Verification: Verified that reference range tests pass and schema remains clean.

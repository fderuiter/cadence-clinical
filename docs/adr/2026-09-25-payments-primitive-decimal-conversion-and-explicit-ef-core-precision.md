# ADR-2445: Payments Domain Primitive Decimal Conversion & EF Core Explicit Precision Mapping

Also known as: ADR-2241 (former number)

- **Status:** Accepted
- **Date:** 2026-09-25
- **Requirements:** PRD-SYS-053
- **Authors:** @jules
- **Deciders:** @fderuiter, @cadence-core-team

---

## 1. Context & Problem Statement

The payments microservice previously modeled monetary attributes, rates, and totals using double precision floating-point types (`double`). Floating-point arithmetic introduces representation errors and sub-cent rounding drift in financial ledgers, violating regulatory audit requirements under 21 CFR Part 11 and IRS 1099 standards.

## 2. Decision Drivers & Constraints

- **Driver 1:** Eliminate floating-point representation errors across site payments, fee schedules, subject stipends, and self-billing invoices.
- **Driver 2:** Guarantee strict financial ledger integrity under 21 CFR Part 11 and IRS 1099 tax reporting.
- **Driver 3:** Standardize on C# `decimal` (`System.Decimal`) explicitly mapped to PostgreSQL `numeric(18,4)` columns across all EF Core entity configurations in `Cadence.Apps.Payments`.

## 3. Options Considered

### Option 1: Retain double floating-point types with runtime rounding
- **Overview:** Keep `double` domain properties and apply `Math.Round` at runtime.
- **Pros:**
  - ✅ No database schema alterations required.
- **Cons:**
  - ❌ Does not eliminate IEEE 754 binary floating-point representation errors.
  - ❌ Fails GxP audit and IRS 1099 tax accuracy requirements.

### Option 2: Convert properties to decimal and configure explicit EF Core numeric(18,4) column mappings (Selected)
- **Overview:** Refactor all monetary and rate properties across domain entities in `Cadence.Apps.Payments` to C# `decimal`, configure explicit `.HasColumnType("numeric(18,4)")` mappings in `PaymentConfigurations.cs`, and generate EF Core schema migration script.
- **Pros:**
  - ✅ Fixed-point exact decimal arithmetic across all financial operations.
  - ✅ Banker's rounding (`MidpointRounding.ToEven`) ensures sub-cent precision consistency.
  - ✅ 100% compliance with 21 CFR Part 11 and IRS 1099 financial standards.
- **Cons:**
  - ❌ Requires database migration altering column types from `double precision` to `numeric(18,4)`.

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** C# `decimal` mapped to PostgreSQL `numeric(18,4)` satisfies regulatory requirements and guarantees zero rounding drift across all payment calculations.

## 5. Consequences & Trade-offs

- **Positive Impact:** Exact financial calculations for site payouts, split payee allocations, holdback reserves, and participant wallets with zero rounding drift.
- **Negative Impact / Technical Debt:** Database column alteration required.
- **Mitigation Strategy:** EF Core migration script (`20260925183437_ConvertPaymentPropertiesToDecimal`) alters column types while preserving numeric values.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `Cadence.Apps.Payments`, `Cadence.UnitTests`.
- **Verification Plan:** Verified via `PaymentDecimalCalculationTests` unit test suite, `dotnet build`, `dotnet format`, and Cadence CLI quality checks.

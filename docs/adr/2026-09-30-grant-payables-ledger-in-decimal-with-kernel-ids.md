# ADR-2521: The Grant Payables Ledger Is Ported to Payments in Decimal with Kernel IDs

- **Status:** Accepted
- **Date:** 2026-09-30
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-PAY-001 (fee matrix and overhead), PRD-PAY-002 (trigger-to-payable), PRD-PAY-003 (split payees and holdback)
- **Issue:** #5047 (part of #4789)

---

## 1. Context & Problem Statement

`packages/engine_core/financial/` is the Python behavioural reference for the site grant ledger. It holds four files:

- `contracts.py`: tiered overhead, payee splits and a line-item digest.
- `ledger.py`: the ledger, which turns clinical events into payable line items and keeps a holdback reserve.
- `engine.py`: a facade over the ledger.
- `models.py`: the models.

`apps/payments` had only EF Core entities for these concepts. `PaymentDecimalCalculationTests` computed its amounts
inline in the test rather than calling production code.

The Python code had three problems for a port:

- it held money as `float` and converted to `Decimal` at each step;
- it read the wall clock and drew random UUIDs;
- it identified the study, site and subject with strings.

## 2. Decision Drivers & Constraints

- Money is exact: `decimal` throughout, rounded to cents half away from zero (the Python `ROUND_HALF_UP`).
- Applications never use string study, site or participant IDs (spec §2.3, K-XS-1).
- K-TIME-3: time comes from `IClock`.
- The port must reproduce the Python amounts and digests so it can be checked against the reference.
- Layer purity: `Domain/` depends on no infrastructure.

## 3. Options Considered

### Option 1: Port into `packages/engine_core` next to the other engines

- **Overview:** Put the ledger in the shared domain library, as the forecasting and DTP ports are.
- **Pros:**
  - ✅ It sits next to the Python source.
- **Cons:**
  - ❌ Grant payments concern one application (K-SCOPE-1). The shared library would carry payments vocabulary that
    only `apps/payments` uses.

### Option 2: Port into `apps/payments/src/Domain/Ledger` (selected)

- **Overview:** Four parts:
  - `ContractCalculations` holds the arithmetic and the digests.
  - `PayableLedger` is the stateful ledger. It takes an `IClock` and an ID source.
  - `GrantContractValidator` enforces the limits the Pydantic model enforced.
  - Records for the contract, event, line item and statement, keyed by `StudyId`, `SiteId` and `ParticipantId`.
- **Pros:**
  - ✅ The calculation lives with the service that will expose it, and it tests without a database.
  - ✅ Nothing forbidden is redefined.
- **Cons:**
  - ❌ Two models describe the same ideas until the EF Core entities are mapped onto the ledger. The records are the
    calculation's input and output; the entities stay the persisted shape.

## 4. Decision Outcome

- **Chosen Option:** Option 2.
- **Justification:** K-SCOPE-1 keeps an application's calculations in the application.
- **Not carried forward:**
  - The facade (`EventTriggeredPayableEngine`) only constructed a `PayableLedgerEngine`, so callers create a
    `PayableLedger` directly.
  - The free-form event `metadata` dictionary is gone. Only its `identifier` key was ever read, and that becomes
    `PaymentEvent.Identifier`.
- **Differences from the Python, pinned by tests:**
  - Amounts are exact. The Python kept binary-float artefacts in some payee shares, for example 64.78999999999999
    where the port has 64.79. Digests were computed from the two-decimal text, so they agree.
  - `ReleaseHoldback` rejects an amount below one cent with `ArgumentOutOfRangeException` and an empty reserve with
    `InvalidOperationException`. The Python raised the same `ValueError` for both.
  - Line item and statement IDs come from an injected source, a UUIDv7 by default, instead of `uuid4`.
  - The validator also rejects empty contract, payee and fee item identifiers.
  - Digests are SHA-256 over compact, key-sorted JSON written with the relaxed encoder. For ASCII values the bytes
    match `json.dumps(sort_keys=True)`. Non-ASCII text differs, because the Python escaped it as `\uXXXX`. The
    digests are tamper evidence for a line item, not signatures, and never replace the GxP audit trail.
- **Kept as the Python had them:**
  - The effective overhead rate is rounded to two decimals.
  - Spend in a gap between overhead brackets is charged at the next bracket's rate.
  - Spend above every bracket takes the highest bracket's rate.
  - A holdback release goes to the first payee.
  - A zero-amount fee still counts toward its cap.

## 5. Consequences & Trade-offs

- **Positive Impact:** The four `financial/` files move to "Ported, deletion pending". The payments service has
  tested ledger arithmetic to build endpoints on.
- **Negative Impact / Technical Debt:**
  - No endpoint or persistence uses the ledger yet.
  - The EF Core entities (`Payable`, `HoldbackReserve`, …) still carry string IDs from the K-XS-1 baseline.
  - `ReleaseHoldback` does the arithmetic only. The Data Management and Quality sign-off that PRD-PAY-003 requires
    before a release, and the SDV gate in PRD-PAY-002, belong to the command that calls it.
- **Mitigation Strategy:** Mapping the entities onto the ledger belongs to the payments disposition under #4787. That
  work moves them to kernel IDs and shrinks the baseline.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `apps/payments` (`Domain/Ledger/`), `docs/PYTHON_REFERENCE_INVENTORY.md`.
- **Verification Plan:** `ContractCalculationsTests` and `PayableLedgerTests` pin every amount, rate and digest to
  the Python reference, run with the same inputs, IDs and clock.

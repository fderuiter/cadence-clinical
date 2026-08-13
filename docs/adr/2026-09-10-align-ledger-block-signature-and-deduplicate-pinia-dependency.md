# ADR-193: Align Ledger Block Signature and Deduplicate Pinia Dependency

- **Status:** Accepted
- **Date:** 2026-09-10
- **Authors:** @google-labs-jules[bot]
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

During package builds and TS verification, mismatch errors were found where `buildLedgerBlock` was declared as having 4 arguments but called with 6. Additionally, unit tests in `clinical_components.test.js` failed to find the active Pinia store inside Vue components declared under `packages/ui/` because of double Pinia package resolution inside Vitest context in our monorepo structure.

This decision implements and hardens requirements under PRD-CRF-006 and PRD-SYS-001.

## 2. Decision Drivers & Constraints

- **Driver 1:** 100% accurate alignment between TypeScript definitions (`packages/ui/index.d.ts`) and actual module implementation exports.
- **Driver 2:** Robust test isolation and single Pinia/Vue instance resolution across workspaces in Vitest.

## 3. Options Considered

### Option 1: Mock/Inline store configurations manually per test

- **Overview:** Override test helper structures individually.
- **Pros:**
  - ✅ Quick workaround.
- **Cons:**
  - ❌ Does not resolve root cause of double package allocation in monorepos.

### Option 2: Resolve type contracts and implement workspace aliases for Pinia/Vue (Selected)

- **Overview:** Update `buildLedgerBlock` interface to 6 parameters and map local aliases for common singletons inside `apps/web/vite.config.js`.
- **Pros:**
  - ✅ Solves root cause of double allocation/singleton resolution memory leak.
  - ✅ Guarantees TypeScript type-safety across compile gates.
- **Cons:**
  - None.

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Choosing Option 2 guarantees compile-time correctness and clean, isolated, single-instance dependency resolution for tests across monorepo packages.

## 5. Consequences & Trade-offs

- **Positive Impact:** Strong typing contracts are fully respected, and monorepo test runners resolve the single active Pinia state perfectly.
- **Negative Impact / Technical Debt:** Requires keeping aliases configured in our Vite configuration.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/ui/`, `apps/web/`
- **Verification Plan:** Run `pnpm check` and `pnpm --filter web test` to verify complete success.

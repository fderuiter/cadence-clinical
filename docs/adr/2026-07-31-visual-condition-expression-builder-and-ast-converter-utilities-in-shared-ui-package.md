# ADR-142: Visual Condition Expression Builder and AST Converter Utilities in Shared UI Package

* **Status:** Accepted
* **Date:** 2026-07-31
* **Authors:** @fderuiter
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Building no-code clinical edit rules requires bi-directional translation between user-friendly form fields (field name, operator, target value, and group match operator) and the structured JSON abstract syntax tree (AST) consumed by downstream execution and designer engines. Providing standard AST serialization/deserialization helpers in `packages/ui/index.js` ensures consistent rule parsing across web applications.

## 2. Decision Drivers & Constraints

* Expose reusable `buildConditionsTree` and `deserializeConditionsTree` functions in `@cadence/ui`.
* Ensure lossless bi-directional conversion between UI condition rows and logical/comparison AST nodes.
* System requirement compliance: PRD-SYS-001.

## 3. Options Considered

1. **Shared UI AST Converter Utilities (Selected)**: Implement AST build and parse helpers in `@cadence/ui` and integrate with `RulesView.vue`.
2. Duplicate AST parsing logic inside individual view components.

## 4. Decision Outcome

Chosen option 1 to eliminate duplicated AST transformation code and guarantee consistent evaluation logic across designer and execution surfaces.

## 5. Consequences & Trade-offs

* **Positive**: Centralized AST serialization logic for condition trees.
* **Positive**: Verified with unit tests in `packages/ui/tests/index.test.js` and `apps/web/tests/views/RulesView.test.js`.
* **Negative**: Requires maintaining parity between UI AST helpers and backend rule evaluators.

## 6. Implementation & Verification

* Updated `packages/ui/index.js`, `apps/web/src/views/RulesView.vue`.
* Verified with `pnpm test` and `python3 scripts/validate_adrs.py`.

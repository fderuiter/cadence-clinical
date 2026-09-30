# ADR-2506: The engine_core Rule AST Is Superseded by the One Rule Language

- **Status:** Accepted
- **Date:** 2026-09-30
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-DDF-001 (static circular dependency checks on skip logic)
- **Relates to:** [ADR-2461](2026-09-30-rule-expression-language-rule-pack-format-and-evaluator-port.md) (the one rule
  language), [ADR-2502](2026-09-30-ddf-item-rules-use-the-one-rule-language.md) (DDF item rules)
- **Issue:** #4789

---

## 1. Context & Problem Statement

`packages/engine_core` still holds two Python reference files for its old rule engine:

- `rules/ast.py`: an expression tree with `field_ref`, `variable` and operator nodes, a `RuleDefinition` with skip
  logic, edit check and derived-variable types, and `validate_node` shape checks.
- `rules/sentinels.py`: static checks before a rule is published. It collects field references, measures depth and
  node count, reports references to unknown fields or visits, and finds cycles in skip logic and derived variables.

The inventory lists both as "Port missing". Their evaluator was ported earlier as
`Cadence.Core.Domain.Rules.RuleEvaluator`, a JsonLogic-style engine with its own AST (`RuleAst.cs`). Only its own
tests call it.

ADR-2461 made one rule language for edit checks, skip logic, conformance and indicators (K-RULE-3), and rejected
JsonLogic. ADR-2502 moved DDF item rules onto it.

## 2. Decision Drivers & Constraints

- K-RULE-3: one rule language. A second AST with its own evaluator is a second language.
- Each check the sentinels made must still happen somewhere, or be recorded as not needed.
- A Python reference file leaves the inventory when a tested port exists or an ADR records that the behaviour is not
  carried forward (ADR-2492).

## 3. Options Considered

### Option 1: Port `validate_node` and the sentinels onto the engine_core AST

- **Cons:**
  - ❌ It builds out a second rule language that nothing in production uses.

### Option 2: Map each check onto the one rule language, and retire the engine_core rule engine (Selected)

- **Pros:**
  - ✅ One language, one parser and one set of static checks.
- **Cons:**
  - ❌ The engine_core `RuleEvaluator` tests go with the engine they test.

## 4. Decision Outcome

- **Chosen Option:** Option 2. `rules/ast.py` and `rules/sentinels.py` are not ported. Each check maps as follows:

| engine_core check | Where it lives now |
| --- | --- |
| `validate_node` (operand counts, required fields) | `RuleExpressionJson` parsing is strict: an unknown operator or property, or a wrong number of arguments, is an error when a pack or item rule is read |
| `calculate_expression_complexity` | `RuleExpressionJson.MaxDepth` (64) and `MaxNodes` (4096) refuse an expression that is too deep or too large |
| `extract_field_references`, `detect_unresolved_references` for form items | DDF compile: a rule that names an item the study does not have fails the compile (ADR-2502) |
| `detect_unresolved_references` for rule packs | Binding collection-stage packs to the `CollectionSpec` versions they check (#4728) |
| `detect_circular_dependencies`, skip logic | `DdfDependencyGraph.FindCycles` in the DDF compile (ADR-2502) |
| `detect_circular_dependencies`, derived variables | Derived variables (#5043) |
| `RuleDefinition` versioning, checksum and audit fields | Rule packs are versioned library content with the kernel audit log (ADR-2461) |

- `Cadence.Core.Domain.Rules` (`RuleAst.cs`, `RuleEvaluator.cs`) is a second rule language with no production
  caller. It is removed, with `RuleEvaluatorTests`, `RuleChecksumTests` and the rule case in
  `DomainEngineUnitTests`, in the same maintainer-approved deletion as the Python files.

## 5. Consequences & Trade-offs

- **Positive Impact:**
  - One rule language in the codebase once the deletion lands.
  - `rules/ast.py`, `rules/sentinels.py` and `rules/__init__.py` can be deleted once a maintainer approves.
- **Negative Impact / Technical Debt:**
  - Until then, the engine_core rule engine stays in the build, unused.
- **Mitigation Strategy:** nothing new may call `Cadence.Core.Domain.Rules`; rules go through `IRuleEvaluator`.

## 6. Implementation & Verification

- **Affected Repositories / Services:** documentation only: this ADR, `docs/PYTHON_REFERENCE_INVENTORY.md` and the
  engine_core README.
- **Verification Plan:** `pnpm cadence check adrs` and the documentation gates. Each check's new home is covered by
  its own tests: `RuleExpressionTests` for parsing and the limits, and `DdfRulesTests` for unknown items and cycles.

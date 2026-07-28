# ADR-104: Unified Python-based Documentation Validator

* **Status:** Accepted
* **Date:** 2026-07-27
* **Authors:** @google-labs-jules[bot]
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement
Our documentation validation pipeline was previously fragmented across two separate environments: a Node.js tool (`check-links.js`) and a Python tool (`validate_markdown.py`). This fragmentation led to inconsistent parsing behaviors, high maintenance overhead, and frequent false-positive build failures. Specifically, developers experienced pipeline blockers when using dummy file paths in code examples or placeholder links within commented-out sections, as the legacy tools lacked the context-aware parsing necessary to ignore them.

## 2. Decision Drivers & Constraints
* **Driver 1:** Eliminate false positives: Code blocks and multi-line HTML comments are parsed out and bypassed during path validation.
* **Driver 2:** Increase velocity: Pre-flight checks and pipeline runs execute entirely offline and complete in under 5 seconds, providing immediate feedback during local development.
* **Driver 3:** Simplify maintenance: Retiring the duplicate Node.js script cuts down on project dependencies and unifies our validation rules.

## 3. Options Considered
### Option 1: Fragmented Node.js and Python Validators
* **Overview:** Retain both `check-links.js` and `validate_markdown.py`.
* **Pros:**
  * ✅ No immediate migration effort needed.
* **Cons:**
  * ❌ Inconsistent parsing behavior and duplicate code maintenance.
  * ❌ Frequent false positives in CI.

### Option 2: Unified Python-based Documentation Validator
* **Overview:** Retire `check-links.js` and consolidate all markdown validation, link checks, and command execution dry-runs into an upgraded `validate_markdown.py`.
* **Pros:**
  * ✅ Single parser and ruleset, offline-ready, handles block-level comments and code block escaping.
  * ✅ Consistent and reliable local and CI execution.
* **Cons:**
  * ❌ Migration cost of updating workflows and pre-commit hooks.

## 4. Decision Outcome
* **Chosen Option:** Option 2
* **Justification:** Consolidating all documentation validation rules into a single Python linter improves developer velocity, removes maintenance overhead of Node dependencies, and provides stable offline validation.

## 5. Consequences & Trade-offs
* **Positive Impact:** Offline execution under 1.5 seconds, cleaner codebase, no more legacy Node dependency for links checking.
* **Negative Impact / Technical Debt:** Requires keeping `validate_markdown.py` synchronized with new subcommands and structures.
* **Mitigation Strategy:** Added comprehensive testing to `tests/test_markdown_validator.py` to ensure parser robustness.

## 6. Implementation & Verification
* **Affected Repositories / Services:** `.github/workflows/ci.yml`, `package.json`, `.pre-commit-config.yaml`
* **Verification Plan:** Verified via `python3 scripts/validate_markdown.py` and unit tests in `tests/test_markdown_validator.py`.

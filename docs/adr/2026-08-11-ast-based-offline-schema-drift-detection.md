# ADR-2167: AST-based Offline Schema Drift Detection and Babel Parser Dependency

- **Status:** Accepted
- **Date:** 2026-08-11
- **Authors:** @google-labs-jules-bot
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Changes to server-side relational database models or client-side storage structures could break clinical trial synchronization without triggering build errors. This discrepancy between the frontend IndexedDB schema and the backend relational database schema can lead to data loss or desynchronization in offline clinical data collection. To solve this, we must implement Abstract Syntax Tree (AST)-based offline schema drift tests to statically assert compatibility between server-side Pydantic/SQLAlchemy models and client-side structures.

Statically parsing JavaScript/TypeScript files to extract the schema definition requires a robust parser that does not rely on fragile regular expressions or string matching. We chose the Babel parser (`@babel/parser`) with the TypeScript plugin for this task. Thus, we need to introduce `@babel/parser` as a devDependency in the workspace root `package.json` to be available during both local testing and CI/CD pipelines.

This decision addresses requirements under PRD-SYS-001 and Trace-14.

## 2. Decision Drivers & Constraints

- **Driver 1:** Highly accurate extraction of frontend schema structures from TypeScript/JavaScript files without false positives or negatives from formatting, comments, or whitespace.
- **Driver 2:** Safe and fast build-time/test-time static assertions to fail early and protect clinical data flows.
- **Driver 3:** Clean modular dependencies with standard npm resolution to prevent path-resolution regressions in virtualized or containerized runner environments.
- **Driver 4:** Fully satisfy the platform's GxP and regulatory validation quality gates.

## 3. Options Considered

### Option 1: Regex or Text Pattern Matching

- **Overview:** Use regular expressions or simple text/line scanning to parse TypeScript files.
- **Pros:**
  - ✅ Zero extra third-party Node.js dependencies in the workspace root.
- **Cons:**
  - ❌ Extremely fragile and prone to false positives or failures when formatting, comments, or indentation change.
  - ❌ Fails to parse complex structures or nested typescript interfaces accurately.

### Option 2: AST-Based Extraction via Babel Parser

- **Overview:** Use `@babel/parser` in a Node.js utility script (`scripts/parse_frontend_ast.js`) to parse the TypeScript source files into an AST and walk the nodes to extract schema declarations.
- **Pros:**
  - ✅ 100% immune to whitespace, formatting, or comments.
  - ✅ Highly precise, resolving nested properties and typescript interfaces with full structural fidelity.
- **Cons:**
  - ❌ Introduces `@babel/parser` as a devDependency in the root `package.json`.

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Choosing Option 2 guarantees correctness and resilience against formatting changes. Adding `@babel/parser` as a standard devDependency is the standard and robust approach to enabling JS/TS AST parsing inside our multi-language verification suite.

## 5. Consequences & Trade-offs

- **Positive Impact:**
  - Highly robust schema drift detection that prevents breaking sync engines.
  - Standard module resolution for `@babel/parser` in both local environments and CI.
- **Negative Impact / Technical Debt:**
  - Introduces one extra development dependency (`@babel/parser`) in the root `package.json`.
  - Requires maintaining the helper node script `scripts/parse_frontend_ast.js`.

## 6. Implementation & Verification

- **Affected Repositories / Services:**
  - `package.json`
  - `scripts/parse_frontend_ast.js`
  - `tests/validation/test_offline_schema_drift.py`
- **Verification Plan:**
  - Verified via `uv run pytest tests/validation/test_offline_schema_drift.py`.

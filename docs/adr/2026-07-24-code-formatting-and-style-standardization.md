# ADR 2026-07-24: Code Formatting and Style Standardization

## Status
Accepted

## Context
As part of the continuous integration (CI) and GxP validation pipeline alignment for the Cadence Clinical Platform, we identified inconsistencies in code style, unused imports, and multi-line formatting across several modules, including database helpers under `apps/execution/database/`.

## Decision
We decided to enforce repository-wide linting and code style formatting using `ruff`. This includes organizing imports and standardizing quote types and indentation across both transactional and eTMF microservices to ensure code-base maintainability and clean validation sweeps.

## Alternatives Considered
- **Maintaining unformatted legacy configurations:** This leads to CI linting and formatting step failures, which blocks merge pipelines.
- **Ignoring python style rules in database modules:** This creates inconsistent readability standards and violates GxP quality gates.

## Trade-offs
- **Positive:** Standardized clean codebase with 100% ruff compliance and improved readability.
- **Negative:** Minor line diffs in database files that trigger architectural check rules, which are resolved by documenting this decision.

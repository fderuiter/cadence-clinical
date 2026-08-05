# ADR 2026-08-05: Global Prettier Formatting and Pre-commit Checks

## Status

Accepted

## Context

To ensure code style consistency across the entire monorepo, formatting rules must be strictly and automatically enforced. Previously, frontend files, styles, JSON schemas, and markdown documents were not consistently formatted, leading to unnecessary formatting diffs in pull requests. We need a reliable mechanism to enforce global formatting rules before code is committed.

## Decision

We decided to add automated Prettier validation hooks targeting `.json`, `.css`, and `.md` files alongside Ruff formatting in `.pre-commit-config.yaml` and a global formatting script in `package.json`. Additionally, we shield third-party assets and dynamically generated OpenAPI specs via `.prettierignore`.

This decision implements requirements under Trace-7.

## Alternatives Considered

- Rely on developers to format their files using IDE configurations or manual CLI commands, leading to inconsistent results and frequent formatting violations in pull requests.

## Trade-offs

- **Positive:** All JSON, CSS, and Markdown documentation are globally compliant and automatically validated on commit.
- **Negative:** Minor line diffs and a slight increase in local commit time.

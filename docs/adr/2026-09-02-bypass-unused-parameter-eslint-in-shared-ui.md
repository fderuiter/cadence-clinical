# ADR 2026-09-02: Bypass Unused Parameter ESLint in Shared UI

## Status
Accepted

## Context
The shared UI package `packages/ui/index.js` defines standard UI helper functions. A function `createRuleEditorHTML` was defined with a parameter `forms` which is currently not used in its body but is part of the exported function signature. ESLint enforces a strict `no-unused-vars` rule that flags this as a build/lint failure. To ensure backward-compatibility of function signatures without breaking potential consumers who pass the `forms` parameter, the parameter must be preserved, and the ESLint unused variable check must be bypassed on it.

This decision supports system configuration standard compliance under PRD-SYS-001.

## Decision
We decided to bypass the ESLint `no-unused-vars` rule inline for the unused parameter `forms` in `packages/ui/index.js` using the comment `// eslint-disable-next-line no-unused-vars`.

This preserves the function's signature and interface contract while keeping the linting rules clean and passing in the CI environment.

## Alternatives Considered
- **Remove the `forms` parameter**: This was rejected because `createRuleEditorHTML` is an exported library function, and changing its signature might break downstream consumers who rely on passing `forms` as the first argument.
- **Implement a dummy reference to `forms` inside the function**: This was rejected because it introduces dead code/boilerplate that adds no semantic value.

## Trade-offs
- **Positive**:
  - Maintained backward-compatibility of the library's public API signature.
  - Successfully satisfies strict ESLint requirements.
- **Negative**:
  - The function signature retains a parameter that is currently unused in the HTML builder template, which is slightly redundant but harmless.

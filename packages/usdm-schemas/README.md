# usdm-schemas

TypeScript Zod schemas for the CDISC USDM study structure and ePRO payloads, plus a graph validator for rule expressions. Consumed as TypeScript source by the frontends.

## Contents

- `src/index.ts`: Zod schemas and inferred types for `Code`, `SyntaxTemplate`, `EligibilityCriterion`, `BiomedicalConcept` and its properties, `Activity`, `Encounter`, `StudyArm`, `StudyEpoch`, `StudyDesign`, `StudyVersion` and `USDMStudy`; `EPROAnswersSchema`, `EPROSubmissionSchema` and `validateEproPayload`.
- `src/graph-validator.ts`: `ALLOWED_OPERATORS`, `inspectExpressionAST`, `detectCycles` and `validateUsdmGraph` for dependency and expression checks.
- `tests/graph-validator.test.ts`: Vitest tests.

## Scripts

| Command                            | Purpose                                                                                                                                                          |
| ---------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `pnpm --filter usdm-schemas test`  | Vitest.                                                                                                                                                          |
| `pnpm --filter usdm-schemas build` | Runs `cadence cdisc export` through `dotnet run`; that CLI command is currently a placeholder that only prints a message (see [packages/cli](../cli/README.md)). |

The package's `main` and `types` point at `src/index.ts`, so no build output is needed to consume it.

## Dependencies

- Runtime: `zod`.
- Development: `vitest`.

## Used by

[apps/web](../../apps/web/README.md), [apps/subject-portal](../../apps/subject-portal/README.md) and [packages/gateway-rewrite](../gateway-rewrite/README.md) (`"usdm-schemas": "workspace:*"`).

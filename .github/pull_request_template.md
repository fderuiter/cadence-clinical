# Pull Request

## Description

What changes, why, and how. Link the issue this closes (for example `Closes #123`).

## Checklist

### Requirements & Architecture

- [ ] **Issue linked:** the body closes or references its issue (`Closes #123`).
- [ ] **Kernel alignment:** new domain concepts follow `docs/kernel/CLINICAL_KERNEL_SPECIFICATION.md`, or the PR says why they don't.
- [ ] **ADR:** architectural decisions have an ADR (`dotnet cadence scaffold adr`), and `dotnet cadence check adrs` passes.
- [ ] **Traceability:** tests for regulatory requirements carry `[GxPRequirement("PRD-SYS-xxx")]` and `@req:` tags, and the RTM is regenerated (`dotnet cadence gxp sync`) when they change.

### Code Quality

- [ ] **Formatting:** `dotnet format Cadence.sln --verify-no-changes` passes, and `pnpm format` was run for frontend changes.
- [ ] **Quality gates:** `dotnet cadence check` passes (the pre-commit hook runs it).
- [ ] **No suppressions:** no new `NoWarn`, `#pragma warning disable`, skipped tests or `|| true` without a written justification.
- [ ] **Shared code:** UI components and signing/hashing helpers come from the shared `ui` package and `Cadence.Security.Signing`, not new copies.

### Testing

- [ ] `dotnet test Cadence.sln` passes locally.
- [ ] `pnpm -r test` passes locally for frontend changes.
- [ ] New behavior has tests (xUnit in `tests/`, Vitest next to the frontend package).

### GxP & Security

- [ ] Mutating entities keep their audit fields (`CreatedAt`, `CreatedBy`, `ReasonForChange`, `VersionIndex`).
- [ ] No secrets, PHI or real participant data in code, fixtures or logs.

### Documentation

- [ ] **Docs updated in this PR:** service/package README, `ARCHITECTURE.md`, `CONTEXT.md`, `docs/ENVIRONMENT_VARIABLES.md` and `.env.example` for new settings, `docs/cli.md` for CLI changes, and an ADR for new decisions (or N/A with the reason).

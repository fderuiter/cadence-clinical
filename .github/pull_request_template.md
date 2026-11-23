# Pull Request

## Description

What changes, why, and how. Link the issue this closes (for example `Closes #123`).

## Checklist

### Requirements & Architecture

- [ ] **Issue linked:** the body closes or references its issue (`Closes #123`).
- [ ] **Kernel alignment:** new domain concepts follow `docs/kernel/CLINICAL_KERNEL_SPECIFICATION.md`, or the PR says why they don't.
- [ ] **ADR:** architectural decisions have an ADR (`pnpm cadence scaffold adr "<title>"`), indexed in `docs/adr/index.md`.
- [ ] **Traceability:** tests for regulatory requirements carry `[GxPRequirement("PRD-SYS-xxx")]` and `@req:` tags, `pnpm cadence gxp sync` passes when they change, and this PR does not change the generated RTM, IQ/OQ/PQ report or `docs/SDLC/runs/` (ADR-2532).

### Code Quality

- [ ] **Formatting:** `dotnet format Cadence.slnx --verify-no-changes` passes, and `pnpm format` was run for frontend changes.
- [ ] **Quality gates:** `pnpm cadence check` passes (the pre-commit hook runs it). See `docs/CI.md` for each CI check.
- [ ] **No suppressions:** no new `NoWarn`, `#pragma warning disable`, skipped tests or `|| true` without a written justification.
- [ ] **Shared code:** UI components and signing/hashing helpers come from the shared `ui` package and `Cadence.Security.Signing`, not new copies.

### Testing

- [ ] `dotnet build Cadence.slnx`, `dotnet test tests/Cadence.UnitTests/Cadence.UnitTests.csproj` and `dotnet test tests/Cadence.ArchitectureTests/Cadence.ArchitectureTests.csproj` pass locally.
- [ ] `pnpm -r test` passes locally for frontend changes.
- [ ] New behavior has tests (xUnit in `tests/`, Vitest next to the frontend package).

### GxP & Security

- [ ] Mutating entities keep their audit fields (`CreatedAt`, `CreatedBy`, `ReasonForChange`, `VersionIndex`).
- [ ] No secrets, PHI or real participant data in code, fixtures or logs.

### Documentation

- [ ] **Docs updated in this PR.** The README of each changed service or package, `ARCHITECTURE.md`, `CONTEXT.md`, `docs/ENVIRONMENT_VARIABLES.md` and `.env.example` for new settings, `docs/cli.md` for CLI changes, and an ADR (indexed in `docs/adr/index.md`) for new decisions. See `CONTRIBUTING.md`. Otherwise, "No docs affected" with a reason.

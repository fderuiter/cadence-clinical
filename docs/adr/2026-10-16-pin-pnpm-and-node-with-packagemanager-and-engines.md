# ADR-2508: Pin pnpm and Node with packageManager and engines

- **Status:** Accepted
- **Date:** 2026-10-16
- **Authors:** @fderuiter, Claude Code
- **Deciders:** @fderuiter (backlog pass, 2026-10-16)
- **Requirements:** PRD-SYS-053 (development environment and agent guardrails: every documented command must run on a fresh
  machine and in agent sessions)
- **Issues:** #5331

---

## 1. Context & Problem Statement

No `package.json` declared a `packageManager`, so corepack resolved the newest pnpm (12.x), which could not start on
Node 22 (`Cannot find module …/pnpm/12.x/bin/pnpm.cjs`). Every `pnpm cadence` command, every frontend build and test,
and the setup wizard's `pnpm install` failed on a fresh machine and in agent sessions; the wizard only printed an
`[INFO]` line, and the pre-commit hook reported the crash as "Prettier formatting violations". The versions also
disagreed: the workflows pinned pnpm 9 and Node 20, `docker-compose.yml` installed whatever pnpm was newest, and
`pnpm-workspace.yaml` already used pnpm 10 keys (`allowBuilds`, `minimumReleaseAgeExclude`). Vitest 5 needs Node
22.12 or later and ESLint 10 needs 22.13, so Node 20 could not run the web tests either.

## 2. Decision Drivers & Constraints

- One toolchain version everywhere: workstations, agent sessions, Docker and the (paused) GitHub workflows.
- A tooling failure must be reported as one, not as a code problem.
- Supply chain: pin an exact pnpm release with its integrity hash, and prefer one that has been public for at least two
  weeks.

## 3. Options Considered

### Option 1: `packageManager` with an integrity hash, plus `engines.node` (Selected)

- **Overview:** the root `package.json` pins `pnpm@10.34.6+sha512…` and `engines.node: ^22.13.0 || >=24`. Corepack runs
  exactly that pnpm. The workflows drop `version: 9` so `pnpm/action-setup` reads the pin, and use Node 22.
- **Pros:** one source of truth; corepack verifies the hash; matches the pnpm 10 keys and the v9 lockfile already in use.
- **Cons:** Node 25 and later ship without corepack, so the Node 26 subject-portal container installs the same version
  with npm.

### Option 2: Document `corepack prepare pnpm@10 --activate`

- **Overview:** the status quo workaround.
- **Cons:** every machine and session must remember it, and nothing detects drift.

## 4. Decision Outcome

- **Chosen Option:** Option 1.
- **Justification:** it removes the failure instead of documenting around it, and `cadence doctor` can check it.

## 5. Consequences & Trade-offs

- **Positive Impact:** `pnpm cadence …` works on a fresh machine; the setup wizard exits non-zero, in red, when pnpm is
  missing, crashes or cannot install; the pre-commit hook says "pnpm cannot run" instead of blaming formatting;
  `cadence doctor` compares the installed pnpm with the pin and exits non-zero on a mismatch.
- **Negative Impact / Technical Debt:** upgrading pnpm is now a deliberate change to `package.json`, and the ADR gate
  asks for an ADR or amendment each time.
- **Mitigation Strategy:** upgrade with `corepack use pnpm@<version>`, which rewrites the pin and its hash.

## 6. Implementation & Verification

- **Affected files:** `package.json`, `.github/workflows/{ci,deploy-docs,production-pipeline}.yml`,
  `docker/docker-compose.yml`, `scripts/setup_wizard.{sh,ps1}`, `.githooks/pre-commit`,
  `packages/cli/src/Commands/DoctorCommand.cs`, `packages/cli/src/Services/PnpmPin.cs`.
- **Verification Plan:** `tests/Cadence.UnitTests/Packages/PnpmPinTests.cs` covers reading the pin and each comparison
  outcome, and asserts the repository pins pnpm 10.

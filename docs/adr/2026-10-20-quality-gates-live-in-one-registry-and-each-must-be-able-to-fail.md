# ADR-2534: Quality Gates Live in One Registry and Each Must Be Able to Fail

- **Status:** Accepted
- **Date:** 2026-10-20
- **Authors:** @fderuiter, Claude Code
- **Deciders:** @fderuiter
- **Requirements:** PRD-SYS-053 (development guardrails: the checks that guard every commit must be real)
- **Related:** [ADR-2532](2026-10-16-feature-branches-never-commit-the-gxp-snapshot.md), #5332

---

## 1. Context & Problem Statement

`cadence check` is the pre-commit hook and the core of CI, yet some of its gates could not fail (#5332). The
`prettier-check` alias returned success without running anything, the `contracts` gate called a verifier that never
recorded an error, and the requirement-tag `sanitizer` always returned `0`. The gates were written down three times:
a hand-numbered sequence in `QualityGateOrchestrator.RunQualityGates`, a separate `switch` for single gates (with
aliases the full run did not have), and hand-written tables and counts in `docs/CI.md`, `docs/cli.md`, the Makefile,
the pre-commit hook and the documentation index. The orchestrator said 22 gates and ran 25; the documents said 21 or 22. Around them sat stub services that printed success and returned `0` and nine-line shims nothing called.

## 2. Decision Drivers & Constraints

- **A gate that cannot fail is worse than none:** a green run must mean every listed check ran and passed.
- **One source:** the gate list, the single-gate lookup, the count, `check --help` and the docs come from one place.
- **No duplication of the architecture tests:** layering and port rules belong to `tests/Cadence.ArchitectureTests`.
- **Hermetic tests:** the proof that a gate can fail must run in the unit tests, without a database or network.

## 3. Options Considered

### Option 1: A registry of gates with a failing fixture for each (Selected)

- **Overview:** `packages/cli/src/Services/QualityGates.cs` lists every gate once (name, aliases, what it checks, its
  check against a `GateContext` repository root, and its pre-commit behaviour). The orchestrator runs and looks up
  gates from it; `GateDocs` generates the gate tables in `docs/cli.md` and `docs/CI.md`; `QualityGateRegistryTests`
  runs each gate against a fixture repository that breaks it.
- **Pros:**
  - ✅ A new gate without a failing fixture fails the unit tests, so a gate that always passes cannot be added.
  - ✅ The count and the docs cannot drift: the `cli-docs` gate fails when a generated table is stale.
- **Cons:**
  - ❌ Every gate service must take a repository root, which needed small signature changes (`ValidateAdrs`,
    `ValidateGxpRtm`).

### Option 2: Keep the hand-written orchestrator and fix the counts

- **Pros:**
  - ✅ Smallest change.
- **Cons:**
  - ❌ The counts and tables drift again on the next gate, and nothing proves a gate can fail.

## 4. Decision Outcome

- **Chosen Option:** Option 1.
- `prettier-check` is removed: Prettier already runs on the staged files in the pre-commit hook and in CI's frontend
  style step. The `contracts` gate, the `verify-contracts` command and CI's "Run hexagonal contract verification" step
  are removed: a real check would duplicate the layering and boundary rules the architecture tests enforce.
- The `sanitizer` gate fails when it had to rewrite a requirement tag, so the fix is staged instead of left behind.
- Gates whose real check needs an external toolchain (`api-clients`: pnpm and `tsc` over the web workspace;
  `schema-doc`: the EF Core schema exporter) are proven to fail closed when that toolchain is missing, and the test
  names them and the reason.
- CLI commands that only printed "not implemented" (`db status`, `db seed`, `setup credentials`, `record list`,
  `record run`, `cdisc export`, `mcp server`, `gxp verify`, `verify-gxp-signatures`) are removed with their
  `package.json` and Makefile entries; stub and shim services nothing called are deleted.

## 5. Consequences & Trade-offs

- **Positive Impact:** a passing `cadence check` means every gate ran a check that can fail, and the documents always
  list the gates that run.
- **Negative Impact / Technical Debt:** the fixtures for `api-clients` and `schema-doc` prove only that the gates fail
  closed; their comparisons are the TypeScript compiler's and `SchemaDocumentationTests`'.
- **Mitigation Strategy:** the full `cadence check` runs both gates against the real repository in CI and `cadence ci`.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/cli` (registry, orchestrator, docs generator, command tree),
  `packages/tooling_core` (repository roots, sanitizer, deleted stubs), `.github/workflows/ci.yml`, `docs/cli.md`,
  `docs/CI.md`.
- **Verification Plan:** `tests/Cadence.UnitTests/Packages/QualityGateRegistryTests.cs` runs every gate against a
  breaking fixture, checks name uniqueness and that the generated tables match the registry; the `cli-docs` gate checks
  the tables on every commit.

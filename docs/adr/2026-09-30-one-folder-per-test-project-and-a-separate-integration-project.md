# ADR-2500: One Folder per Test Project and a Separate Integration Test Project

- **Status:** Accepted
- **Date:** 2026-09-30
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-SYS-053 (development environment and agent guardrails: the test suites must be fast to run
  and trustworthy)

---

## 1. Context & Problem Statement

The three .NET test projects shared the `tests/` folder. Each csproj moved its `obj/` and `bin/` folders and
imported the SDK by hand so the projects could coexist, then used `<Compile Remove>` lists to decide which files
it compiled. The lists drifted: the architecture project compiled most of the execution, eConsent and audit tests
too, so about 615 tests ran twice. The PostgreSQL integration tests sat inside the unit project, so the unit
project needed Testcontainers and a filter to run without a database (#4793).

## 2. Decision Drivers & Constraints

- Every test runs in exactly one project.
- A new test file lands in the right project because of the folder it is in, not because of a csproj list.
- `dotnet test --filter "Category!=Integration"` must never need a database.
- The RTM lists test file paths, so a move regenerates it (`pnpm cadence gxp sync`).

## 3. Options Considered

### Option 1: One folder per project, a separate integration project and a shared support library (Selected)

- **Overview:** `tests/Cadence.UnitTests`, `tests/Cadence.IntegrationTests`, `tests/Cadence.ArchitectureTests` and
  `tests/Cadence.E2ETests` each hold one project that compiles its own folder with the default SDK globs. Fakes and
  fixtures that the unit and integration projects share live in `tests/Cadence.TestSupport`, a class library with
  no tests. The integration project marks every test `Category=Integration` with an assembly-level trait.
- **Pros:**
  - ✅ No custom build paths, no hand SDK imports, no `Compile Remove` or linked sources.
  - ✅ The duplicate runs disappear; the unit project drops Testcontainers.
- **Cons:**
  - ❌ A one-time move of every test file and a regenerated RTM.

### Option 2: Keep the shared folder and fix the `Compile Remove` lists

- **Cons:**
  - ❌ The lists drift again with the next new folder; nothing makes a missing entry fail.

## 4. Decision Outcome

- **Chosen Option:** Option 1.
- **Justification:** It makes the folder the only thing that decides where a test runs, and a test enforces it.

## 5. Consequences & Trade-offs

- **Positive Impact:**
  - `TestLayoutTests` (architecture project) fails on a source file outside the five folders, on a `Compile
Remove` or `Compile Include` in a test project, and on a `Category=Integration` trait outside the integration
    project.
  - `pnpm cadence test --integration` runs `tests/Cadence.IntegrationTests`; `--fast` still filters the solution.
  - CI runs the .NET tests against a `postgres:16` service container, so the database tests run there too.
- **Negative Impact / Technical Debt:** Kernel projects grant `InternalsVisibleTo` to the integration project (and
  `Cadence.Kernel.Persistence` to `Cadence.TestSupport` for the bitemporal probe), as they already did to the unit
  project.
- **Mitigation Strategy:** K-XS-2 exempts only the named test assemblies.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `tests/`, `Cadence.sln`, `packages/cli` (`cadence test`),
  `.github/workflows/ci.yml`, `packages/kernel` (`InternalsVisibleTo`), docs.
- **Verification Plan:** The test inventory before and after the move matches (no test lost, none run twice); the
  full suite passes with `CADENCE_TEST_POSTGRES`; `TestLayoutTests` passes.

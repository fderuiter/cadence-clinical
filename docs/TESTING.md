# Testing and Requirement Qualification

This guide explains how to run the Cadence test suites, how to reach a real PostgreSQL database from tests,
how to link a test to a requirement, and when the Requirements Traceability Matrix (RTM) counts a
requirement as qualified.

## Test projects

| Project | What it holds | Run it with |
| --- | --- | --- |
| `tests/Cadence.UnitTests.csproj` | Unit and component tests for services and packages. The Postgres integration tests under `tests/Execution/Integration/` live here too. | `dotnet test tests/Cadence.UnitTests.csproj` |
| `tests/Cadence.ArchitectureTests.csproj` | Layer and boundary rules (ArchUnitNET), the kernel baseline, and the architecture sentinels. | `dotnet test tests/Cadence.ArchitectureTests.csproj` |
| `tests/Cadence.E2ETests.csproj` | Black-box HTTP tests against the execution service through `WebApplicationFactory`. | `dotnet test tests/Cadence.E2ETests.csproj` |
| `packages/ui/tests/` | Vitest suite for the shared JavaScript signing and UI helpers. | `pnpm --filter ui test` |

`dotnet test Cadence.sln` runs all three .NET projects. `dotnet cadence test --fast` is the quick loop, and
`make verify` runs the quality gates plus every suite before you open a PR.

GitHub Actions minutes are not always available, so run the suites locally before pushing rather than
relying on CI.

## Tests that need PostgreSQL

Some behaviour only exists on a real database: advisory locks, transactions, and EF Core migrations. Those
tests never fall back to an in-memory stand-in. Without a database they report as **skipped**, not passed.

Two opt-ins are checked in order:

1. `CADENCE_TEST_POSTGRES` is an Npgsql connection string to a server you already run. Each fixture
   creates a throwaway database on it (`cadence_it_*` or `cadence_e2e_*`) and drops it afterwards.
2. `CADENCE_TEST_DOCKER=1` starts a Testcontainers `postgres:16-alpine` container.

Where Docker is not available, for example in a cloud agent session, a local server works:

```bash
sudo apt-get install -y postgresql
sudo service postgresql start
sudo -u postgres psql -c "ALTER USER postgres PASSWORD 'postgres'"

export CADENCE_TEST_POSTGRES="Host=localhost;Username=postgres;Password=postgres;Database=postgres"
dotnet test Cadence.sln
```

These tests currently use it:

- `tests/Execution/Integration/` (`ExecutionPostgresFixture`): enrollment index concurrency and migrations.
- `tests/E2E/` (`ExecutionE2EFactory`): write paths that enroll a subject. Read and routing tests run on
  the in-memory provider when no database is configured. Tests that write call
  `factory.SkipUnlessPostgres()`.

## Shared test helpers (`Cadence.Testing`)

Use `packages/testing/src` rather than writing ad-hoc fakes:

- `InMemoryRepository<T>`: a repository fake for application-service tests.
- `TestSecurityContext`: `CreateHmacHeaders(...)`, `CreatePrincipal(...)` and `Identity(...)` for gateway identities.
- The `factory.CreateSignedClient()` and `builder.UseTestGatewaySecret()` extensions, for signed calls into a
  `WebApplicationFactory` host.
- `FakeClock`: a settable `IClock`. Production code must not call `DateTime.Now` or `DateTime.UtcNow`, and
  tests should not depend on wall-clock time either.
- `GoldenMasterFixtureLoader` and `GoldenMasterAssert`: golden-master scenarios ported from the Python
  reference.
- `GxPRequirementAttribute`: declares which requirement a test verifies. See below.

## Linking a test to a requirement

Requirement IDs are defined in `docs/SDLC/01_Product_Requirements_Document_PRD.md` (`PRD-*`) and
`docs/SRS.md` (`SRS-*` and `Trace-*`).

A test declares the requirement it verifies **on the test method**, in any of these forms:

```csharp
/// <summary>
/// Stamps CreatedBy and VersionIndex on the first save.
/// @req:PRD-SYS-001
/// </summary>
[Fact]
[GxPRequirement("PRD-SYS-001")]
public async Task SaveChangesAsync_GivenNonStringTIdAggregate_StampsCreatedFields()
```

`[Trait("Requirement", "PRD-SYS-001")]` is also recognised. One form is enough.

Rules:

- **Tag only what the test asserts.** A test verifies a requirement only when its assertions would fail if
  the requirement were broken. For example, a test that checks a form starts in Draft does not verify the
  audit-trail requirement just because the form has audit fields.
- **Not every test needs a tag.** Helper, regression and architecture tests that verify no product
  requirement stay untagged. A tag exists to make an RTM claim, not to satisfy a checklist.
- **Tags on a class do not count.** `@req:` in a class summary is documentation only. It never marks
  every test in the file as verifying that requirement.
- **Use IDs that exist.** A test that declares an ID no requirement document defines fails
  `dotnet cadence check` (the `rtm` gate), `dotnet cadence gxp validate`, and the
  `RepositoryTests_DeclareOnlyRequirementIdsThatExist` unit test. Add the requirement first, then tag the
  test.
- **An alias does not qualify its parent.** An `SRS-*` or `Trace-*` entry that points at a `PRD-*`
  requirement is listed with its parent's tests, but tagging the alias does not qualify the parent.
- **No placeholder tests.** A test that asserts on its own literals (`true.Should().BeTrue()`, or a status
  that accepts 201, 400 or 500) verifies nothing. Write a real assertion, or leave the requirement Planned.

## How the RTM decides a requirement's state

`dotnet cadence gxp sync` runs `dotnet test Cadence.sln` and writes one TRX file per test project into
`TestResults/gxp/` (gitignored). It then regenerates `docs/SDLC/Requirements_Traceability_Matrix.md`
and `docs/SDLC/IQ_OQ_PQ_Execution_Report.md` from those results and stages them. If any test fails, the
documents are still regenerated so the failures are visible, but nothing is staged and the command exits
non-zero.

Tests are found by scanning `tests/` for `[Fact]` and `[Theory]` methods and matched to TRX results by
their fully qualified name. Theory cases are merged: a single failing case fails the test.

| State | Meaning |
| --- | --- |
| **Qualified** | At least one test declaring the requirement ran and passed, and none failed. |
| **Failed** | At least one test declaring the requirement failed, errored, timed out or aborted. |
| **Not verified** | Tests declare the requirement, but none of them passed: they were skipped or have no result. |
| **Planned** | No test declares the requirement yet. |

A test with no result is reported as **NOT RUN**. It is never assumed to have passed. Without any TRX
results, generation stops with an error unless you run `dotnet cadence generate-rtm --draft` or set
`RTM_DRAFT=true`. A draft is
marked **DRAFT ONLY** and every test shows as **UNVERIFIED**.

Set `RTM_STRICT_ALL=true` to make validation fail while any requirement is not qualified.

Run `gxp sync` with `CADENCE_TEST_POSTGRES` set, so the database-backed tests produce real results instead
of skips:

```bash
export CADENCE_TEST_POSTGRES="Host=localhost;Username=postgres;Password=postgres;Database=postgres"
dotnet cadence gxp sync
```

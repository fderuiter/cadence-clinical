# Testing and Requirement Qualification

This guide explains how to run the Cadence test suites, how to reach a real PostgreSQL database from tests,
how to link a test to a requirement, and when the Requirements Traceability Matrix (RTM) counts a
requirement as qualified.

## Test projects

| Project | What it holds | Run it with |
| --- | --- | --- |
| `tests/Cadence.UnitTests/Cadence.UnitTests.csproj` | Unit and component tests for services and packages, on in-memory fakes, SQLite or the EF Core in-memory provider. Needs no database. | `dotnet test tests/Cadence.UnitTests/Cadence.UnitTests.csproj` |
| `tests/Cadence.IntegrationTests/Cadence.IntegrationTests.csproj` | Tests against a real PostgreSQL database (see below). Every test in it carries `Category=Integration`. | `dotnet cadence test --integration` |
| `tests/Cadence.ArchitectureTests/Cadence.ArchitectureTests.csproj` | Layer and boundary rules (ArchUnitNET), the kernel baseline, suppression and test-layout policies, and the architecture sentinels. | `dotnet test tests/Cadence.ArchitectureTests/Cadence.ArchitectureTests.csproj` |
| `tests/Cadence.E2ETests/Cadence.E2ETests.csproj` | Black-box HTTP tests against the execution service through `WebApplicationFactory`. | `dotnet test tests/Cadence.E2ETests/Cadence.E2ETests.csproj` |
| `packages/ui/tests/` | Vitest suite for the shared JavaScript signing and UI helpers. | `pnpm --filter ui test` |
| `apps/web/tests/` | Vitest suite for the web app's API clients, stores and views (jsdom). Playwright specs under `tests/e2e/` are excluded. | `pnpm --filter web test` |

`dotnet test Cadence.sln` runs all four .NET test projects. `dotnet cadence test --fast` is the quick loop (every
project, `Category!=Integration`), and `make verify` runs the quality gates plus every suite before you open a PR.

Each project owns its folder under `tests/` and compiles nothing outside it, so every test runs exactly once.
Fakes and fixtures that both the unit and integration projects need live in `tests/Cadence.TestSupport`, a class
library with no tests. Helpers that need no application reference belong in `packages/testing` instead.
`TestLayoutTests` in the architecture project enforces this.

GitHub Actions minutes are not always available, so run the suites locally before pushing rather than
relying on CI.

## Tests that need PostgreSQL

Some behaviour only exists on a real database: advisory locks, transactions, and EF Core migrations. Those
tests never fall back to an in-memory stand-in. Without a database they report as **skipped**, not passed.

Two opt-ins are checked in order:

1. `CADENCE_TEST_POSTGRES` is an Npgsql connection string to a server you already run. Each fixture
   creates a throwaway database on it (`cadence_it_*`, `cadence_kernel_it_*` or `cadence_e2e_*`) and drops it afterwards.
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

- `tests/Cadence.IntegrationTests/Execution/` (`ExecutionPostgresFixture`): enrollment index concurrency, the audit chain
  round trip, and migrations.
- `tests/Cadence.IntegrationTests/Execution/` (`ExecutionPostgresApiFactory`): the execution API through its real
  `Program.cs`, on a throwaway database that the service's own migration hosted service migrates. Only configuration is
  supplied (`ConnectionStrings:cadence`, `Database:MigrateOnStartup`); nothing replaces the `DbContext` provider. The
  subject golden masters, audit attribution, reason-for-change encoding, caller authority, subject route authority and
  form e-signature tests run here (#4990). The unit project's `ExecutionApiFactory` runs the same host on the EF Core
  in-memory provider, which has no transactions, advisory locks, schemas or timestamp rules; it is only for tests whose
  assertions never depend on what is stored, such as authentication rejections, routing and OpenAPI. A test that
  writes through the API and checks the result belongs on `ExecutionPostgresApiFactory`.
- `tests/Cadence.IntegrationTests/Kernel/` (`KernelPostgresFixture`): the kernel schema, bitemporal guards and the command
  pipeline. This fixture honours only `CADENCE_TEST_POSTGRES`.
- `tests/Cadence.IntegrationTests/Database/` (`ThrowawayPostgresDatabase`): every service's migrations applied the way a deployed
  service applies them, into one shared database, checking that each service's tables and migration history
  land in its own schema (ADR-2247).
- `tests/Cadence.E2ETests/E2E/` (`ExecutionE2EFactory`): write paths that enroll a subject. Read and routing tests run on
  the in-memory provider when no database is configured. Tests that write call
  `factory.SkipUnlessPostgres()`.

## Web app tests (`apps/web`)

`pnpm --filter web test` runs Vitest with coverage in jsdom. `pnpm --filter web typecheck` and
`pnpm --filter web lint` are the other two checks to run before pushing a web change.

Coverage is measured over every hand-written source file in `apps/web` and `apps/subject-portal` (ADR-2530). The
global thresholds in `apps/web/vitest.config.js` and `apps/subject-portal/vite.config.js` are a ratchet: when a
change raises coverage, raise the floor to just under the new figure in the same change, and never lower it. A few
files keep their own 80 percent thresholds.

These three scripts regenerate `apps/web/src/types/usdm.ts` from `docs/openapi/legacy` before they run.
If that leaves the file modified, restore it with `git checkout apps/web/src/types/usdm.ts` rather than
committing the regenerated copy.

Rules for web tests:

- **Never reach the network.** Stub `fetch` in every test that calls an API client. When the module under
  test captures `fetch` at import time, create the stub with `vi.hoisted` and install it with
  `vi.stubGlobal` so it is in place before the import runs.
- **Return real `Response` objects.** The API client is built on `openapi-fetch`, which calls
  `fetch(request)` with a single `Request` and reads the status, headers and body of a real `Response`. Use
  `jsonResponse(body, { status })` from `apps/web/tests/fetch_mock.js` instead of a hand-built object.
- **Assert on the request that was sent.** `recordedRequest(mockFetch, index)` from the same file returns the
  URL, method, headers and parsed JSON body, whether the caller passed a `Request` or a `(url, init)` pair.
  Check the headers the gateway depends on, such as `X-Change-Reason`, not only the URL.
- **Wait for conditions, not time.** Use `vi.waitFor(...)` or `await router.isReady()` rather than fixed
  sleeps, which fail when the suite runs under load.

## Shared test helpers (`Cadence.Testing`)

Use `packages/testing/src` rather than writing ad-hoc fakes:

- `InMemoryRepository<T>`: a repository fake for application-service tests.
- `TestSecurityContext`: `CreateHmacHeaders(...)`, `CreatePrincipal(...)` and `Identity(...)` for gateway identities.
- The `factory.CreateSignedClient()` and `builder.UseTestGatewaySecret()` extensions, for signed calls into a
  `WebApplicationFactory` host.
- `FakeClock` (`Cadence.Testing.Time`): a settable `IClock`, frozen at `FakeClock.DefaultStart`
  (2026-01-01T00:00:00Z) unless a test calls `Advance` or `Set`. See "Time in tests" below.
- `GoldenMasterFixtureLoader` and `GoldenMasterAssert`: golden-master scenarios ported from the Python
  reference.
- `GxPRequirementAttribute`: declares which requirement a test verifies. See below.

When a `WebApplicationFactory` swaps a host's `DbContext` for a test provider, remove
`IDbContextOptionsConfiguration<TContext>` as well as `DbContextOptions<TContext>` before calling `AddDbContext`.
Since EF Core 9 every registered options action runs, so the host's own action would add its interceptors a second
time and the GxP audit interceptor would write every entry twice (#4922).
`ExecutionAuditInterceptorRegistrationTests` checks that the execution composition root and `ExecutionApiFactory`
each carry the audit interceptor once.

## Time in tests

Tests follow K-TIME-3 like the kernel does: `tests/Directory.Build.props` enables the banned-API analyzer with
[`BannedSymbols.txt`](../BannedSymbols.txt), so `DateTime.Now/UtcNow` and `DateTimeOffset.Now/UtcNow` fail the
build (RS0030). Pick the replacement by what the test needs:

- **A fixed instant** for dates the test controls (observation dates, signed-at values, anything passed to the
  code under test): `new FakeClock()` or `FakeClock.DefaultStart`. Use `clock.Advance(...)` when the test needs
  time to move, instead of sleeping. Don't declare a private clock class; the shared one covers it.
- **The real clock** only where the code under test compares with it and offers no clock to inject, such as
  token expiry, HMAC freshness or certificate validity: `SystemClock.Instance.UtcNow`.
- **Elapsed time** for timeouts and polling deadlines: `Stopwatch.StartNew()`.

`Task.Delay` belongs only where a test deliberately holds a lock or widens a race window, or where a fake
downstream must stall. It never makes timestamps differ.

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
- **Tag the requirement's own rule, not its neighbourhood.** A test of lookup, error mapping, codecs or a
  different rule in the same feature (for example a site-compliance gate next to the subject state machine)
  does not verify the requirement just because it lives in the same test class or file.
- **A test that contradicts its requirement is a defect report, not a qualification.** If the test's
  assertions disagree with the requirement text (a different status code, a missing field), don't tag it:
  raise the difference so the code or the requirement is fixed.
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
`RTM_DRAFT=true`. A draft is marked **DRAFT ONLY** and every test shows as **UNVERIFIED**. `gxp sync` ignores
`RTM_DRAFT`, so a variable left set in a shell or CI job can't turn documents built from real results into
drafts.

Set `RTM_STRICT_ALL=true` to make validation fail while any requirement is not qualified.

### Keeping the RTM current

`dotnet cadence check rtm` validates without running any tests. It fails when:

- a test declares a requirement ID that no requirement document defines, or
- the checked-in RTM lists different tests for a requirement than the tags in `tests/` declare. That happens
  when a PR adds, removes or moves a tag without re-running `gxp sync`. The error names each test that is
  missing from the RTM or no longer declares the requirement, or
- the RTM or the IQ/OQ/PQ report no longer matches its integrity stamp, because it was edited by hand.

`gxp sync` ends both documents with an **integrity stamp**: the SHA-256 of the document body, marked as an
unsigned working copy. It replaces the self-signed signature the documents used to carry, which came from a new
throwaway key on every run and so changed the files every time. The same tests and results now give byte-identical
documents, and CI's docs diff can pass. The stamp is not an electronic signature. A release copy of the validation
package is signed by a named person with `GxPSigner.SignGxpMarkdown` and their own certificate; no CLI command
does this yet.

`check rtm` does not compare test results; only a full `gxp sync` produces those. The check is part of CI's static
analysis job, not the pre-commit hook, because fixing it means running the whole suite. If you change a tag,
run `gxp sync` before you open the PR.

Run `gxp sync` with `CADENCE_TEST_POSTGRES` set, so the database-backed tests produce real results instead
of skips:

```bash
export CADENCE_TEST_POSTGRES="Host=localhost;Username=postgres;Password=postgres;Database=postgres"
dotnet cadence gxp sync
```

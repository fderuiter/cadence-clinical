# Testing and Requirement Qualification

This guide explains how to run the Cadence test suites, how to reach a real PostgreSQL database from tests,
how to link a test to a requirement, and when the Requirements Traceability Matrix (RTM) counts a
requirement as qualified.

## Test projects

| Project                                                            | What it holds                                                                                                                                | Run it with                                                                    |
| ------------------------------------------------------------------ | -------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------ |
| `tests/Cadence.UnitTests/Cadence.UnitTests.csproj`                 | Unit and component tests for services and packages, on in-memory fakes, SQLite or the EF Core in-memory provider. Needs no database.         | `dotnet test tests/Cadence.UnitTests/Cadence.UnitTests.csproj`                 |
| `tests/Cadence.IntegrationTests/Cadence.IntegrationTests.csproj`   | Tests against a real PostgreSQL database (see below). Every test in it carries `Category=Integration`.                                       | `dotnet cadence test --integration`                                            |
| `tests/Cadence.ArchitectureTests/Cadence.ArchitectureTests.csproj` | Layer and boundary rules (ArchUnitNET), the kernel baseline, suppression and test-layout policies, and the architecture sentinels.           | `dotnet test tests/Cadence.ArchitectureTests/Cadence.ArchitectureTests.csproj` |
| `tests/Cadence.E2ETests/Cadence.E2ETests.csproj`                   | Black-box HTTP tests against the execution service through `WebApplicationFactory`.                                                          | `dotnet test tests/Cadence.E2ETests/Cadence.E2ETests.csproj`                   |
| `packages/ui/tests/`                                               | Vitest suite for the shared JavaScript signing and UI helpers.                                                                               | `pnpm --filter ui test`                                                        |
| `apps/web/tests/`                                                  | Vitest suite for the web app's API clients, stores and views (jsdom). Playwright specs under `tests/e2e/` and `tests/browser/` are excluded. | `pnpm --filter web test`                                                       |

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

Two opt-ins are checked in order, by every fixture below:

1. `CADENCE_TEST_POSTGRES` is an Npgsql connection string to a server you already run, as a superuser. Each fixture
   creates a throwaway database on it (`cadence_it_*`, `cadence_kernel_it_*` or `cadence_e2e_*`) and drops it afterwards.
2. `CADENCE_TEST_DOCKER=1` starts a Testcontainers container of `PostgresTestImage.Image`, whose default `postgres`
   user is a superuser. `PostgresTestServer` in `tests/Cadence.TestSupport/Database` makes this choice for the kernel
   fixtures.

The tests qualify **PostgreSQL 18**, the major docker compose runs
([ADR-2533](adr/2026-10-19-tests-ci-and-compose-run-one-pinned-postgresql-major.md)). `PostgresTestImage`
(`packages/testing`, `Cadence.Testing.Persistence`) holds the major and the digest-pinned image; every fixture, CI's
service container and `docker/docker-compose.yml` use that image, and `PostgresVersionPinTests` fails when one drifts.
A server of another major is refused: `cadence ci` stops before the tests when `CADENCE_TEST_POSTGRES` reaches one, and
the kernel fixture fails its tests on one. A local server for `CADENCE_TEST_POSTGRES` is PostgreSQL 18 (on Debian or
Ubuntu, `postgresql-18` from the PostgreSQL apt repository).

Once either is set, a fixture that cannot reach or start the server fails its tests; it never turns them into skips.
A run that skips database tests is not evidence: `cadence ci` fails when any test in `Cadence.IntegrationTests` or
`Cadence.E2ETests` was skipped or did not execute (unless `--allow-skip` is given), and `gxp sync` and `gxp snapshot`
fail on such a run. The only exemption is the opt-in lineage benchmark, named in
`DatabaseTestEvidence.OptInTests`.

The kernel fixture checks that its application role is neither a superuser nor `BYPASSRLS`, and that the server runs
the qualified major, before any test runs, since a pass under such a role says nothing about Row-Level Security. When `CADENCE_TEST_EVIDENCE_DIR` is set (`gxp sync`,
`gxp snapshot` and `cadence ci` set it to `TestResults/gxp`), it records the server version and source, the installed extensions, the latest
kernel migration and the role's attributes in `kernel.database.txt`, which the RTM and IQ/OQ/PQ report print.

Where Docker is not available, a local PostgreSQL 18 server works. Create its cluster as UTF8: a `SQL_ASCII` cluster,
which `initdb` makes under the C locale, fails the kernel's correction tests.

```bash
sudo install -d /usr/share/postgresql-common/pgdg
sudo curl -fsSL -o /usr/share/postgresql-common/pgdg/apt.postgresql.org.asc https://www.postgresql.org/media/keys/ACCC4CF8.asc
. /etc/os-release
echo "deb [signed-by=/usr/share/postgresql-common/pgdg/apt.postgresql.org.asc] https://apt.postgresql.org/pub/repos/apt ${VERSION_CODENAME}-pgdg main" \
  | sudo tee /etc/apt/sources.list.d/pgdg.list
sudo apt-get update && sudo apt-get install -y postgresql-18
sudo pg_createcluster 18 main --port 5432 --locale C.UTF-8 --encoding UTF8 -- --auth-local=trust --auth-host=trust
sudo pg_ctlcluster 18 main start

export CADENCE_TEST_POSTGRES="Host=127.0.0.1;Port=5432;Username=postgres;Database=postgres"
dotnet test Cadence.sln
```

Cloud agent sessions get this from the project's cloud environment: its setup script installs the .NET 10 SDK and
PostgreSQL 18, creates that cluster (dropping Ubuntu's PostgreSQL 16 one) and restores dependencies, and the
environment sets `CADENCE_TEST_POSTGRES`. The `scripts/claude_session_start.sh` hook restarts the cluster when a session
resumes and sets `CADENCE_TEST_BROWSER=1`, so `cadence ci` runs as it would locally.

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
  pipeline, all as the non-owner application role under Row-Level Security. The IdentityVault migration tests use the
  same server choice.
- `tests/Cadence.IntegrationTests/Kernel/KernelObservationHostPostgresTests.cs`: the observation correction through the
  real kernel host (ADR-2620). Its browser-to-database check also needs `CADENCE_TEST_BROWSER=1`, Node, pnpm and a
  Chromium: it starts a signing proxy on port 8000 in place of Keycloak and the gateway, runs
  `apps/web/playwright.correction-check.config.ts` (the real `ObservationView` on the Vite dev server), and reads the
  correction and its reason back from PostgreSQL. `PLAYWRIGHT_CHROMIUM_PATH` names a preinstalled Chromium; the test
  sets it to `/opt/pw-browsers/chromium` when that file exists. Without the opt-in it is skipped.
- `tests/Cadence.IntegrationTests/Database/` (`ThrowawayPostgresDatabase`): every service's migrations applied the way a deployed
  service applies them, into one shared database, checking that each service's tables and migration history
  land in its own schema (ADR-2247). `ThrowawayPostgresDatabase` creates a throwaway `NOSUPERUSER NOBYPASSRLS` role that
  owns the database, and its connection string logs in as that role, so the execution and interop hosts under test
  never run with superuser privileges. The server connection string only creates and drops the database and the role.
- `tests/Cadence.E2ETests/E2E/` (`ExecutionE2EFactory`): write paths that enroll a subject. Read and routing tests run on
  the in-memory provider when no database is configured. Tests that write call
  `factory.SkipUnlessPostgres()`.

## Web app tests (`apps/web`)

`pnpm --filter web test` runs Vitest with coverage in jsdom. `pnpm --filter web typecheck` and
`pnpm --filter web lint` are the other two checks to run before pushing a web change.

Coverage is measured over every hand-written source file in `apps/web`, `apps/subject-portal` and `packages/ui`
(ADR-2530). The global thresholds in `apps/web/vitest.config.js`, `apps/subject-portal/vite.config.js` and
`packages/ui/vite.config.js` are a ratchet: when a change raises coverage, raise the floor to just under the new figure
in the same change, and never lower it. A few files keep their own 80 percent thresholds, among them the Part 11
helpers `packages/ui/signing.js` and `packages/ui/econsent.js`. ADR-2530 §7 sets the milestones the `apps/web` floor
has to reach.

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

## Shared test helpers (`Cadence.Testing` and `Cadence.UnitTests.Fixtures`)

Use `packages/testing/src` or centralized unit test fixtures under `tests/Cadence.UnitTests/Fixtures/` rather than writing ad-hoc fakes:

- `InMemoryRepository<T>`: a repository fake for application-service tests.
- `Cadence.UnitTests.Fixtures.Execution`: centralized unit test doubles for Execution domain services (`FakeCurrentAuditContextAccessor`, `RecordingAuditLogWriter`, and `FakeDemographicsEncryptionService`).
- `TestSecurityContext`: `CreateHmacHeaders(...)`, `CreatePrincipal(...)` and `Identity(...)` for gateway identities. Test identities carry the tenant code `TestSecurityContext.TenantCode`; there is no fallback tenant (ADR-2514).
- The `factory.CreateSignedClient()` and `builder.UseTestGatewaySecret()` extensions, for signed calls into a
  `WebApplicationFactory` host.
- `FakeClock` (`Cadence.Testing.Time`): a settable `IClock`, frozen at `FakeClock.DefaultStart`
  (2026-01-01T00:00:00Z) unless a test calls `Advance` or `Set`. See "Time in tests" below.
- `GoldenMasterFixtureLoader` and `GoldenMasterAssert`: golden-master scenarios ported from the Python
  reference.
- `GxPRequirementAttribute`: declares which requirement a test verifies. See below.
- `CommandValidationCatalog` (`Cadence.Testing.Commands`): `Violations(services, commandAssembly)` lists the
  commands that have neither a registered FluentValidation validator nor `[NoValidator("why")]`, or both
  (ADR-2600 §4.4). The kernel's tests and each module's tests assert it is empty.
- `AddDeclaredKernelErrorsCheck()` (`Cadence.Testing.Commands`): wraps the registered `IKernelCommandSender` so a
  command carrying `[RaisesKernelErrors]` throws when it returns an error it does not declare (ADR-2600 §4.5). The
  kernel Postgres fixture and the kernel host observation tests use it; a module's tests add it after
  `AddKernelCommandPipeline()`.
- `StartupValidationFactory<TEntryPoint>` (`Cadence.Testing.Hosting`): builds a service's real `Program` host
  without starting it and runs its `ValidateOnStart` checks on the test thread (`ValidateStartup()` returns the
  exception, or `null`). Use it to test that a service refuses bad configuration. Asserting the failure through
  `CreateClient()` is racy: `Program` runs on its own thread and disposes the host when its start fails, so the
  test can see an `ObjectDisposedException` instead of the `OptionsValidationException`.

When a `WebApplicationFactory` swaps a host's `DbContext` for a test provider, remove
`IDbContextOptionsConfiguration<TContext>` as well as `DbContextOptions<TContext>` before calling `AddDbContext`.
Since EF Core 9 every registered options action runs, so the host's own action would add its interceptors a second
time and the GxP audit interceptor would write every entry twice (#4922).
`ExecutionAuditInterceptorRegistrationTests` checks that the execution composition root and `ExecutionApiFactory`
each carry the audit interceptor once.

## Time in tests

Tests follow K-TIME-3 like the rest of the solution: root `Directory.Build.props` enables the banned-API analyzer globally with
[`BannedSymbols.txt`](../BannedSymbols.txt), so `DateTime.Now/UtcNow` and `DateTimeOffset.Now/UtcNow` fail the
build (RS0030). Pick the replacement by what the test needs:

- **A fixed instant** for dates the test controls (observation dates, signed-at values, anything passed to the
  code under test): `new FakeClock()` or `FakeClock.DefaultStart`. Use `clock.Advance(...)` when the test needs
  time to move, instead of sleeping. Don't declare a private clock class; the shared one covers it.
- **The real clock** only where the code under test compares with it and offers no clock to inject, such as
  token expiry, HMAC freshness or certificate validity: `SystemClock.Instance.UtcNow`.
- **Elapsed time** for timeouts and polling deadlines: `Stopwatch.StartNew()`.

A unit test never asserts how long the code took (`elapsed.Should().BeLessThan(...)`). The answer depends on the
load on the machine, so a latency ceiling fails whenever something else runs beside the tests (#5415). Assert the
behaviour instead: the result, the status code a timeout produces, or a deadline driven by `FakeClock`.
`WallClockAssertionTests` in `Cadence.ArchitectureTests` enforces this for `Cadence.UnitTests`. A performance budget
that is a real requirement belongs in an opt-in benchmark, like `LineageBenchmarkPostgresTests`.

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
  `pnpm cadence check` (the `rtm` gate), `pnpm cadence gxp validate`, and the
  `RepositoryTests_DeclareOnlyRequirementIdsThatExist` unit test. Add the requirement first, then tag the
  test.
- **An alias and its parent qualify separately.** An `SRS-*` or `Trace-*` entry that points at a `PRD-*`
  requirement is judged by its own tests only: the parent's tests do not qualify it, and its tests do not qualify the
  parent.
- **No placeholder tests.** A test that asserts on its own literals (`true.Should().BeTrue()`, or a status
  that accepts 201, 400 or 500) verifies nothing. Write a real assertion, or leave the requirement Planned.

## How the RTM decides a requirement's state

`pnpm cadence gxp sync` runs `dotnet test Cadence.sln` and writes one TRX file per test project into
`TestResults/gxp/` (gitignored). It then generates a preview of the RTM and the IQ/OQ/PQ report from those results
in `TestResults/gxp/preview/` (also gitignored) and fails on unknown requirement IDs, failing tagged tests and
skipped database tests. It writes and stages nothing under `docs/SDLC`. If any test fails, the preview is still
generated so the failures are visible, and the command exits non-zero.

`docs/SDLC/Requirements_Traceability_Matrix.md` and `docs/SDLC/IQ_OQ_PQ_Execution_Report.md` are a snapshot of the
integrated candidate, not something a feature branch commits (ADR-2532). Only the integration owner refreshes them,
against PostgreSQL, with `CADENCE_GXP_SNAPSHOT=1 pnpm cadence gxp snapshot`. That command runs the same tests and
checks, writes both documents and `docs/SDLC/runs/`, stages them, and with `--commit` commits them with
`CADENCE_GXP_SNAPSHOT=1` set.

### The snapshot on a feature branch

- Never commit the RTM, the IQ/OQ/PQ report or `docs/SDLC/runs/` on a feature branch, and never edit them or resolve
  a conflict in them by hand.
- On a merge or rebase conflict in them, take `main`'s copy and do not regenerate them on the branch:
  `git checkout origin/main -- docs/SDLC/Requirements_Traceability_Matrix.md docs/SDLC/IQ_OQ_PQ_Execution_Report.md docs/SDLC/runs`.
  The same command restores them on a branch that already changed them.
- The `gxp-snapshot` gate enforces this. The pre-commit hook runs it on the staged files, and `pnpm cadence check`
  and CI run it on everything the branch changed since its merge base with `origin/main`. It admits the documents
  only when `CADENCE_GXP_SNAPSHOT=1`; outside the pre-commit hook it then compares the regenerated documents with the
  snapshot (`gxp diff`).
- No custom merge driver or `merge=union` is used for them: GitHub ignores custom drivers, and union merges corrupt
  Markdown tables.

Tests are found by scanning `tests/` for `[Fact]` and `[Theory]` methods and matched to TRX results by
their fully qualified name. Theory cases are merged: a single failing case fails the test.

| State            | Meaning                                                                                         |
| ---------------- | ----------------------------------------------------------------------------------------------- |
| **Qualified**    | Every test declaring the requirement ran and passed.                                            |
| **Failed**       | At least one test declaring the requirement failed, errored, timed out or aborted.              |
| **Not verified** | No test failed, but at least one test declaring the requirement was skipped or did not execute. |
| **Planned**      | No test declares the requirement yet.                                                           |

Every test that declares a requirement is mandatory (#5324). A requirement with three passing tests and one skipped
test is **Not verified**, not Qualified. Each test is reported with one of four results: **PASSED**, **FAILED**,
**SKIPPED** (xUnit skipped it, for example because no database was configured) or **NOT EXECUTED** (it has no result in
any TRX file, or its result is neither a pass, a failure nor a skip). Theory cases are merged per method, and a single
skipped case keeps the method from passing. A test that did not execute is never assumed to have passed.

An `SRS-*` or `Trace-*` alias is a child requirement with its own acceptance criteria, so it is judged only by the tests
that declare the alias itself. Its parent's tests never qualify it, and its own tests never qualify the parent. Section 4
of the RTM lists each alias with its own tests.

Both documents open with an evidence block: the tested revision (`HEAD`, and whether the working tree had other
changes), the generation time, the database the fixtures recorded and the runner. Commit the code first and then run
`gxp snapshot`, so the recorded revision is exactly what was tested. The snapshot-mode docs comparison
(`cadence gxp diff`) leaves the evidence block and the integrity stamp out, since they change on every run. Without any
TRX results, generation stops with an error unless you run `pnpm cadence generate-rtm --draft` or set
`RTM_DRAFT=true`. A draft is marked **DRAFT ONLY** and every test shows as **UNVERIFIED**. `gxp sync` and `gxp snapshot` ignore
`RTM_DRAFT`, so a variable left set in a shell or CI job can't turn documents built from real results into
drafts.

Set `RTM_STRICT_ALL=true` to make validation fail while any requirement is not qualified.

### Keeping the RTM current

`pnpm cadence check rtm` validates without running any tests, as do `pnpm cadence gxp validate` and the agent Stop
hook. They fail when:

- a test declares a requirement ID that no requirement document defines, or
- the RTM or the IQ/OQ/PQ report no longer matches its integrity stamp, because it was edited by hand, or
- with `CADENCE_GXP_SNAPSHOT=1` only, the checked-in RTM lists different tests for a requirement than the tags in
  `tests/` declare. The error names each test that is missing from the RTM or no longer declares the requirement.

On a feature branch the checked-in RTM is `main`'s snapshot, so a new or moved tag is expected to differ from it until
the integration owner takes the next snapshot.

`gxp snapshot` ends both documents (and `gxp sync` its preview) with an **integrity stamp**: the SHA-256 of the document body, marked as an
unsigned working copy. It replaces the self-signed signature the documents used to carry, which came from a new
throwaway key on every run and so changed the files every time. The same tests and results now give byte-identical
documents, and CI's docs diff can pass. The stamp is not an electronic signature. A release copy of the validation
package is signed by a named person with `GxPSigner.SignGxpMarkdown` and their own certificate; no CLI command
does this yet.

`check rtm` does not compare test results; only a full `gxp sync` produces those. The check is part of CI's static
analysis job, not the pre-commit hook. If you change a tag, run `gxp sync` before you open the PR, and do not commit
anything it generates.

Run `gxp sync` with `CADENCE_TEST_POSTGRES` set, so the database-backed tests produce real results instead
of skips:

```bash
export CADENCE_TEST_POSTGRES="Host=localhost;Username=postgres;Password=postgres;Database=postgres"
pnpm cadence gxp sync
```

# Testing and Requirement Qualification

This guide explains how to run the Cadence test suites, how to reach a real PostgreSQL database from tests,
how to link a test to a requirement, and when the Requirements Traceability Matrix (RTM) counts a
requirement as qualified.

## Test projects

| Project                                                            | What it holds                                                                                                                                    | Run it with                                                                    |
| ------------------------------------------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------ | ------------------------------------------------------------------------------ |
| `tests/Cadence.UnitTests/Cadence.UnitTests.csproj`                 | Unit and component tests for services and packages, on in-memory fakes, SQLite or the EF Core in-memory provider. Needs no database.             | `dotnet test tests/Cadence.UnitTests/Cadence.UnitTests.csproj`                 |
| `tests/Cadence.IntegrationTests/Cadence.IntegrationTests.csproj`   | Tests against a real PostgreSQL database (see below). Every test in it carries `Category=Integration`.                                           | `pnpm cadence test --integration`                                              |
| `tests/Cadence.ArchitectureTests/Cadence.ArchitectureTests.csproj` | Layer and boundary rules (ArchUnitNET), the kernel baselines, suppression and test-layout policies, and the architecture sentinels.              | `dotnet test tests/Cadence.ArchitectureTests/Cadence.ArchitectureTests.csproj` |
| `tests/Cadence.E2ETests/Cadence.E2ETests.csproj`                   | Black-box HTTP tests against the execution service, and the cross-module clinical journeys through the single host, via `WebApplicationFactory`. | `dotnet test tests/Cadence.E2ETests/Cadence.E2ETests.csproj`                   |
| `packages/ui/tests/`                                               | Vitest suite for the shared JavaScript signing and UI helpers.                                                                                   | `pnpm --filter ui test`                                                        |
| `apps/subject-portal/tests/`                                       | Vitest suite for the participant portal stores, sync queue and views (jsdom). Playwright specs under `tests/e2e/` are excluded.                  | `pnpm --filter subject-portal test`                                            |
| `apps/web/tests/`                                                  | Vitest suite for the web app's API clients, stores and views (jsdom). Playwright specs under `tests/e2e/` and `tests/browser/` are excluded.     | `pnpm --filter web test`                                                       |
| `docs/.vitepress/navigation.test.mjs`                              | Node Vitest suite for VitePress sidebar synthesis, route coverage, and dual GitHub/VitePress heading anchors (`vitest.config.mjs`).              | `pnpm exec vitest run --config vitest.config.mjs`                              |

`dotnet test Cadence.slnx` runs all four .NET test projects. `pnpm cadence test --fast` applies
`Category!=Integration` across the solution. `make verify` runs the quality gates and the .NET suites.
Use `pnpm test` (`pnpm --filter ui build && pnpm -r test && pnpm exec vitest run --config vitest.config.mjs`) for frontend and VitePress portal unit tests and see [CI and local verification](CI.md#workflows) for the full workflow.

Each project owns its folder under `tests/` and compiles nothing outside it, so every test runs exactly once.
Fakes and fixtures that both the unit and integration projects need live in `tests/Cadence.TestSupport`, a class
library with no tests. Helpers that need no application reference belong in `packages/testing` instead.
`TestLayoutTests` in the architecture project enforces this.

GitHub Actions runs the pull-request and merge validation workflows on every push; run the suites locally before
pushing so failures are caught before opening or updating a pull request.

## Ratchet baselines

Some checks record pre-existing debt in checked-in per-owner baseline shards instead of failing on it: the kernel boundary rules
(`tests/Cadence.ArchitectureTests/ArchitectureBaselines/kernel-boundaries/<Assembly>.baseline.txt`), the kernel rule coverage ratchet
(`tests/Cadence.ArchitectureTests/ArchitectureBaselines/kernel-rule-coverage/<AREA>.baseline.txt`), the `deid` gate
(`baselines/deid/<unit>.baseline.txt`), and the detect-secrets allowlist (`baselines/secrets/config.json` plus
`baselines/secrets/<unit>.secrets.baseline.json`). Any other `*.baseline.txt` or `*-baseline.txt` file is treated the same way.
Empty shards are deleted automatically, and each baseline gate may only shrink:

- Fix a new violation instead of recording it. `CADENCE_UPDATE_BASELINE=1` subtracts resolved entries from touched shards
  (including scoped `CADENCE_UPDATE_BASELINE=1 pnpm cadence check deid <files>`) and deletes empty shards; it never adds
  new violations.
- The `baseline-ratchet` gate (`pnpm cadence check baseline-ratchet`) rejects uncommitted working-tree growth, compares
  every baseline gate across each commit in `compareBase..HEAD` (`git merge-base <base> HEAD` with `--base <ref>`, or
  `<sha>..HEAD` with `--push-before <sha>`), and fails when any commit adds an entry without authorization. Shard entries
  for each gate (`kernel-boundaries`, `kernel-rule-coverage`, `baselines/deid`, `baselines/secrets`, or standalone baseline
  files) are aggregated as one multiset, so reordering, splitting across shards, comment lines, and removed entries pass.
- When debt really must be accepted, say why in the exact commit that adds it, with a trailer naming the gate prefix or
  grown shard path: `git commit --trailer "Baseline-Growth: <path> - <reason>"` (for example
  `Baseline-Growth: baselines/deid - legacy fixture pending #1234`). A trailer on an earlier commit never covers
  later commits or other baseline gates. In `--pre-commit`, staged baseline shards are compared against `HEAD` before the
  commit message exists, so for that commit also set `CADENCE_BASELINE_GROWTH="<reason>"`; the variable is honoured only
  in `--pre-commit`, and `pnpm cadence check` and CI still require the `Baseline-Growth: <path> - <reason>`
  trailer on every commit that grew a baseline gate.
- When `--base` defaults to `origin/main` and `origin/main` is absent locally, `baseline-ratchet` falls back to local
  `refs/heads/main`, and fails closed (exit code `1`) with fetch instructions when neither ref exists or shares history
  with `HEAD`.

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
fail on such a run. The only exemptions are the two opt-in tests named in `DatabaseTestEvidence.OptInTests`: the
10M-edge lineage benchmark (`CADENCE_LINEAGE_BENCHMARK=1`) and `BrowserCorrectionReachesTheDatabaseWithItsReason`
(`CADENCE_TEST_BROWSER=1`, which CI runs against PostgreSQL 18 in `accessibility-e2e` on pushes to `main`).

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
dotnet test Cadence.slnx
```

Cloud agent sessions get this from the project's cloud environment: its setup script installs the .NET 10 SDK and
PostgreSQL 18 and creates that cluster (dropping Ubuntu's PostgreSQL 16 one), and the environment sets
`CADENCE_TEST_POSTGRES`. A failed step there only warns, so it never stops a session from starting. The
`scripts/claude_session_start.sh` hook restores pnpm and NuGet dependencies, restarts the cluster when a session resumes
and sets `CADENCE_TEST_BROWSER=1`, so `cadence ci` runs as it would locally.

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
  real kernel host (ADR-2620). Its browser-to-database check (`BrowserCorrectionReachesTheDatabaseWithItsReason`) also
  needs `CADENCE_TEST_BROWSER=1`, Node, pnpm and a Chromium: it starts a signing proxy on port 8000 in place of Keycloak
  and the gateway, runs `apps/web/playwright.correction-check.config.ts` (the real `ObservationView` on the Vite dev
  server), and reads the correction and its reason back from PostgreSQL. `PLAYWRIGHT_CHROMIUM_PATH` names a preinstalled
  Chromium; the test sets it to `/opt/pw-browsers/chromium` when that file exists. Without `CADENCE_TEST_BROWSER=1` it
  is skipped (`DatabaseTestEvidence.OptInTests`), and CI executes it against PostgreSQL 18 in `accessibility-e2e` on
  pushes to `main`.
- `tests/Cadence.IntegrationTests/Database/` (`ThrowawayPostgresDatabase`): every service's migrations applied the way a deployed
  service applies them, into one shared database, checking that each service's tables and migration history
  land in its own schema (ADR-2247). `ThrowawayPostgresDatabase` creates a throwaway `NOSUPERUSER NOBYPASSRLS` role that
  owns the database, and its connection string logs in as that role, so the execution and interop hosts under test
  never run with superuser privileges. The server connection string only creates and drops the database and the role.
- `tests/Cadence.E2ETests/E2E/` (`ExecutionE2EFactory`): write paths that enroll a subject. Read and routing tests run on
  the in-memory provider when no database is configured. Tests that write call
  `factory.SkipUnlessPostgres()`.
- `tests/Cadence.E2ETests/Journeys/` (`KernelHostJourneyFixture`, #5471): the cross-module clinical journeys (consent to
  export, SAE to safety, withdrawal) through the single host (`apps/kernel-api`, ADR-2601, ADR-2610) on a throwaway
  database, connected as a `NOBYPASSRLS` login role granted the application role's rights, with the kernel's and every
  module's migrations (as the host registers them) applied as the owner. Forms go over the Execution module's routes, and the journeys wait for
  the module's consumer to open its form as `svc:execution-forms`. Each journey arranges its own
  tenant and study through the kernel pipeline as a setup person, then makes every step as a gateway-signed caller,
  authorized by the host against that caller's grants, and checks one kernel `ParticipantId` throughout and a kernel
  audit entry naming the caller for every write. A step whose kernel command exists but whose route the host does not
  map yet goes through `KernelHostCommandRelay`, a test-only route at the end of the host's pipeline that takes the
  signed identity, tenant, reason and idempotency key as the real routes do; a step whose feature does not exist yet is
  a dated `TODO` naming the issue that adds it. Without a database the journeys are skipped.

## Web app tests (`apps/web`)

`pnpm --filter web test` runs Vitest with coverage in jsdom. `pnpm --filter web typecheck` and
`pnpm --filter web lint` are the other two checks to run before pushing a web change.

Coverage is measured over every hand-written source file in `apps/web`, `apps/subject-portal` and `packages/ui`
(ADR-2530). The global thresholds in `apps/web/vitest.config.js`, `apps/subject-portal/vite.config.js` and
`packages/ui/vite.config.js` are a ratchet: when a change raises coverage, raise the floor to just under the new figure
in the same change, and never lower it. A few files keep their own 80 percent thresholds, among them the Part 11
helpers `packages/ui/signing.js` and `packages/ui/econsent.js`. ADR-2530 §7 sets the milestones the `apps/web` floor
has to reach.

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
- `KernelRuleAttribute`: declares which kernel rule (`K-…`) a test proves. See "Linking a test to a kernel rule".
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
The same holds for the Vitest suites: no `expect(duration).toBeLessThan(...)` on a `performance.now()` or `Date.now()`
difference; a slow step gets a longer test timeout instead (#5478). `WallClockAssertionTests` in
`Cadence.ArchitectureTests` enforces this for `Cadence.UnitTests` and for the frontend tests. A performance budget
that is a real requirement belongs in an opt-in benchmark, like `LineageBenchmarkPostgresTests`.

`Task.Delay` belongs only where a test deliberately holds a lock or widens a race window, or where a fake
downstream must stall. It never makes timestamps differ.

## Linking a test to a requirement

Requirement IDs are defined in `docs/SDLC/01_Product_Requirements_Document_PRD.md` (`PRD-*`) and
`docs/SRS.md` (`SRS-*` and `Trace-*`).

A test declares the requirement it verifies **on the test method**, in either of these forms:

```csharp
/// <summary>
/// Stamps CreatedBy and VersionIndex on the first save.
/// @req:PRD-SYS-001
/// </summary>
[Fact]
[GxPRequirement("PRD-SYS-001")]
public async Task SaveChangesAsync_GivenNonStringTIdAggregate_StampsCreatedFields()
```

One form is enough. `[Trait("Requirement", "…")]` is not a requirement tag: it is not counted, and the `rtm` gate
refuses it.

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
- **No tags on a class.** A tag on a class, record or struct would read as a claim for every test in it, so it
  is refused: the `rtm` gate fails on an `@req:` line in a type's doc comment, and `[GxPRequirement]` only compiles
  on a method. Put the tag on the methods whose assertions prove the requirement.
- **PRD-SYS-001 is audit fields and audit entries.** Tag it only where the assertions check `CreatedBy`,
  `CreatedAt`, `ReasonForChange` or `VersionIndex`, or check the audit entry a change writes (its actor, reason or
  action). Hash chaining and chain verification are PRD-SYS-003. Notification, event, idempotency and validation
  tests are neither.
- **PRD-SYS-053 is the agent lifecycle hooks.** Only the hook tests (`tests/**/Packages/AgentHook*.cs`) may declare
  it; the `rtm` gate fails on it anywhere else, product tests included.
- **Use IDs that exist.** A test that declares an ID no requirement document defines fails
  `pnpm cadence check` (the `rtm` gate), `pnpm cadence gxp validate`, and the
  `RepositoryTests_DeclareOnlyRequirementIdsThatExist` unit test. Add the requirement first, then tag the
  test.
- **An alias and its parent qualify separately.** An `SRS-*` or `Trace-*` entry that points at a `PRD-*`
  requirement is judged by its own tests only: the parent's tests do not qualify it, and its tests do not qualify the
  parent.
- **No placeholder tests.** A test that asserts on its own literals (`true.Should().BeTrue()`, or a status
  that accepts 201, 400 or 500) verifies nothing. Write a real assertion, or leave the requirement Planned.

## Linking a test to a kernel rule

Every rule in the [kernel specification](kernel/CLINICAL_KERNEL_SPECIFICATION.md) becomes a test (AGENTS.md). A rule
is defined where a list item or a quoted principle opens with its bold ID (`- **K-FACT-1** …`, `> **K-TRUTH-1** …`);
any other mention is a reference. A test names the rule it proves with `[KernelRule]` from `Cadence.Testing`, **on the
test method**, once per rule:

```csharp
/// <summary>A correction supersedes the recorded value without overwriting it.</summary>
[Fact]
[KernelRule("K-FACT-1")]
public async Task CorrectionCommitsValueRevisionAuditEventAndOutboxAsOneActivity()
```

The attribute is the only form that counts. A K-ID in a test's name, display name, doc comment or class comment is
documentation: it can explain the test, but it does not make the test cover the rule. The attribute is allowed on
methods only.

`KernelRuleCoverageTests` in `Cadence.ArchitectureTests` reads the tags with the same source scan the RTM uses and
fails when:

- a rule the specification defines has no tagged test and is not listed in
  `tests/Cadence.ArchitectureTests/ArchitectureBaselines/kernel-rule-coverage/<AREA>.baseline.txt`;
- a listed rule now has a tagged test, or the specification no longer defines it: delete its line (and delete the shard when empty), by hand or with
  `CADENCE_UPDATE_BASELINE=1 dotnet test tests/Cadence.ArchitectureTests/Cadence.ArchitectureTests.csproj --filter "FullyQualifiedName~KernelRuleCoverageTests"`,
  which only ever deletes lines;
- a test names a rule the specification does not define, for example after a typo or a renamed rule.

The baseline lists the rules with no test yet, mostly concepts the kernel has not built (consent, specimens,
ingestion, standards projections). It may only shrink: a new rule in the specification comes with its test, or with a
reviewed line in the baseline.

The rules for `[GxPRequirement]` apply here too. Tag a rule only when the test's assertions would fail if the rule were
broken, not because the test lives near it, and a test whose assertions contradict the rule is a defect report, not
coverage. A test may prove several rules. Principle rules that no single assertion can prove are mapped to the checks
that enforce them: K-TRUTH-1 and K-SCOPE-1 are carried by the K-XS-1 test (applications do not redeclare kernel
concepts), K-TRUTH-1 also by the K-MOD-1 test (application schemas do not duplicate kernel truth) and the K-PROJ-1 test
(kernel code names no standard dataset or variable).

## How the RTM decides a requirement's state

`pnpm cadence gxp sync` runs `dotnet test Cadence.slnx` and writes one TRX file per test project into
`TestResults/gxp/` (gitignored), or reuses existing TRX reports and `*.database.txt` fixture metadata when given
`--results-dir <dir>` (as CI's `compliance` job does after downloading `gxp-test-evidence-${{ github.run_id }}-${{ github.run_attempt }}` from `dotnet-tests`). It then
generates a preview of the RTM and the IQ/OQ/PQ report from those results in `TestResults/gxp/preview/` (also
gitignored) and fails on unknown requirement IDs, failing tagged tests, missing `*.database.txt` metadata and skipped database tests. It writes and stages
nothing under `docs/SDLC`. If any test fails, the preview is still generated so the failures are visible, and the
command exits non-zero.

`docs/SDLC/Requirements_Traceability_Matrix.md` and `docs/SDLC/IQ_OQ_PQ_Execution_Report.md` are a snapshot of the
integrated candidate, not something a feature branch commits (ADR-2532). Only the integration owner refreshes them,
against PostgreSQL, with `CADENCE_GXP_SNAPSHOT=1 pnpm cadence gxp snapshot` (or `--results-dir TestResults/gxp` in
`production-pipeline.yml` when consuming verified CI test evidence). That command runs the same checks, writes both
documents and `docs/SDLC/runs/`, stages them, and with `--commit` commits them with `CADENCE_GXP_SNAPSHOT=1` set.

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
- A copy identical to `origin/main`'s is the baseline's, not the branch's: merging `main` into a branch after a new
  snapshot passes the gate, and only a document that differs from `main` is refused.
- No custom merge driver or `merge=union` is used for them: GitHub ignores custom drivers, and union merges corrupt
  Markdown tables.

Tests are found by scanning `tests/` for `[Fact]` and `[Theory]` methods and matched to TRX results by
their fully qualified name. Theory cases are merged: a single failing case fails the test.

| State                                        | Meaning                                                                                                  |
| -------------------------------------------- | -------------------------------------------------------------------------------------------------------- |
| **Qualified**                                | Every test declaring the requirement ran and passed.                                                     |
| **Failed**                                   | At least one test declaring the requirement failed, errored, timed out or aborted.                       |
| **Not verified**                             | No test failed, but at least one test declaring the requirement was skipped or did not execute.          |
| **Not verified (needs PostgreSQL evidence)** | Every test passed, but the requirement lives in the database and none of its tests is a PostgreSQL test. |
| **Planned**                                  | No test declares the requirement yet.                                                                    |

Every test that declares a requirement is mandatory (#5324). A requirement with three passing tests and one skipped
test is **Not verified**, not Qualified. Each test is reported with one of four results: **PASSED**, **FAILED**,
**SKIPPED** (xUnit skipped it, for example because no database was configured) or **NOT EXECUTED** (it has no result in
any TRX file, or its result is neither a pass, a failure nor a skip). Theory cases are merged per method, and a single
skipped case keeps the method from passing. A test that did not execute is never assumed to have passed.

A requirement whose behaviour lives in the database (row-level security, a trigger, an append-only ledger, values the
server fills on commit) says so in the PRD with a `**Evidence:** PostgreSQL (…)` line under its heading. In-memory and
SQLite doubles can't exercise that behaviour, so such a requirement qualifies only when at least one of its tests is in
`tests/Cadence.IntegrationTests` or `tests/Cadence.E2ETests`, the PostgreSQL-backed projects (#5324). Add the line when
you write such a requirement, and add a PostgreSQL test before tagging unit tests for it.

An `SRS-*` or `Trace-*` alias is a child requirement with its own acceptance criteria, so it is judged only by the tests
that declare the alias itself. Its parent's tests never qualify it, and its own tests never qualify the parent. Section 4
of the RTM lists each alias with its own tests.

Both documents open with an evidence block: the tested revision (`HEAD`, and whether the working tree had other
changes), the generation time, the database the fixtures recorded and the runner. Commit the code first and then run
`gxp snapshot`, so the recorded revision is exactly what was tested. The snapshot-mode docs comparison
(`cadence gxp diff`) leaves the evidence block and the integrity stamp out, since they change on every run. Without any
TRX results, generation stops with an error unless you run `pnpm cadence generate-rtm --draft` or set
`RTM_DRAFT=true`. A draft is marked **DRAFT ONLY** and every test shows as **UNVERIFIED**. The `rtm` gate refuses a
committed RTM or IQ/OQ/PQ report that is a draft or names no tested revision, and `gxp snapshot` never takes a draft. `gxp sync` and `gxp snapshot` ignore
`RTM_DRAFT`, so a variable left set in a shell or CI job can't turn documents built from real results into
drafts.

Set `RTM_STRICT_ALL=true` to make validation fail while any requirement is not qualified.

### Keeping the RTM current

`pnpm cadence check rtm` validates without running any tests, as do `pnpm cadence gxp validate` and the agent Stop
hook. They fail when:

- a test declares a requirement ID that no requirement document defines, or
- a requirement tag is misapplied: an `@req:` on a class, record or struct, a `[Trait("Requirement", …)]`, or
  PRD-SYS-053 outside `tests/**/Packages/AgentHook*.cs` (#5326). The error names the file and line of each, and the
  `RepositoryTests_HaveNoMisappliedRequirementTags` unit test fails on the same tags, or
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

# ADR-2533: Tests, CI and Compose Run One Pinned PostgreSQL Major

- **Status:** Accepted
- **Date:** 2026-10-19
- **Authors:** @fderuiter, Claude Code
- **Deciders:** @fderuiter
- **Requirements:** PRD-SYS-053 (development guardrails: the generated GxP reports must report the environment that was
  actually tested)
- **Related:** [ADR-2531](2026-10-15-every-mapped-test-is-mandatory-evidence.md),
  [ADR-2447](2026-09-29-module-schemas-via-search-path-and-migration-deployment.md)

---

## 1. Context & Problem Statement

The database evidence and the stack developers run used different PostgreSQL majors (#5438). `docker/docker-compose.yml`
ran `postgres:18-alpine` pinned by digest, while the Testcontainers fixtures each repeated an unpinned
`postgres:16-alpine`, CI's service container ran `postgres:16`, the Aspire AppHost took whatever image its package
defaulted to, and cloud agent sessions ran Ubuntu's PostgreSQL 16. The RTM and IQ/OQ/PQ report therefore qualified a
major nobody deploys, and planner, default and extension differences between majors went unseen.

## 2. Decision Drivers & Constraints

- **Evidence qualifies what runs (ADR-2531):** a database test pass counts only on the major the stack runs.
- **One source:** the version is written once for code, and every other copy is checked against it.
- **Supported:** the newest major Npgsql 10 and EF Core 10 support, installable where the tests run (Docker, or the
  PostgreSQL apt repository in cloud sessions without Docker).
- **Pre-deployment:** no data to migrate, so moving the major has no upgrade cost.

## 3. Options Considered

### Option 1: PostgreSQL 18 everywhere, one digest-pinned image (Selected)

- **Pros:**
  - ✅ Matches compose and the newest supported major, with the longest support window before the 2027 target.
- **Cons:**
  - ❌ Environments that run the tests against an existing server must install PostgreSQL 18.

### Option 2: PostgreSQL 16 everywhere

- **Pros:**
  - ✅ Cloud sessions already have it.
- **Cons:**
  - ❌ Compose moves back two majors, and a later move to 18 repeats this change.

### Option 3: Keep "16+" and test both

- **Cons:**
  - ❌ Doubles the database test run and still leaves the evidence ambiguous about which major was qualified.

## 4. Decision Outcome

- **Chosen Option:** Option 1.
- `Cadence.Testing.Persistence.PostgresTestImage` holds `Major = 18` and the digest-pinned `Image`. Every
  Testcontainers fixture starts that image. CI's service container, `docker/docker-compose.yml` and the AppHost's
  `postgres` resource name the same image and digest, and `PostgresVersionPinTests` fails when any copy drifts or a
  fixture names its own image.
- A server of another major is refused: `cadence ci` checks the major `CADENCE_TEST_POSTGRES` reaches before the test
  run (`LocalCiEnvironment.PostgresMajor`), and the kernel fixture fails on one before it records `kernel.database.txt`,
  which keeps naming the exact server version.

## 5. Consequences & Trade-offs

- **Positive Impact:** the RTM's database environment and the developer stack name the same major.
- **Negative Impact:** cloud sessions with Ubuntu's PostgreSQL 16 cannot run the database tests until PostgreSQL 18 is
  installed from the PostgreSQL apt repository; the project's cloud environment setup script does that, and
  `scripts/local_stack_nodocker.sh` starts the PostgreSQL 18 binaries too.
- **Mitigation Strategy:** the refusal names both majors, so a mismatch is found before the test run, not inferred from
  failures.

## 6. Implementation & Verification

- **Affected:** `packages/testing` (`PostgresTestImage`), `tests/Cadence.TestSupport`, the integration and E2E
  fixtures, `packages/cli` (`LocalCiEnvironment`), `.github/workflows/ci.yml`, `apps/apphost`, and `docs/TESTING.md`,
  `docs/CI.md`, `AGENTS.md`, `ARCHITECTURE.md`.
- **Verification:** `tests/Cadence.UnitTests/Testing/PostgresVersionPinTests.cs`.

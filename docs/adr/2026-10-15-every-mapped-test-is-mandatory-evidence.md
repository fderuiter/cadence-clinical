# ADR-2531: Every Mapped Test Is Mandatory Evidence, and Reports Name What They Tested

- **Status:** Accepted
- **Date:** 2026-10-15
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-SYS-053 (development guardrails: the quality gates and generated GxP reports must report
  the truth about a change), 21 CFR 11.10(a), issues #5324 and #5325
- **Amends:** [ADR-2287](2026-07-24-minimizing-merge-friction-for-baselines-and-reports.md)
- **Amended by:** [ADR-2532](2026-10-16-feature-branches-never-commit-the-gxp-snapshot.md): only the integration owner
  regenerates the documents, with `gxp snapshot`; branches validate with `gxp sync` and commit neither document.

---

## 1. Context & Problem Statement

The Requirements Traceability Matrix called a requirement **Qualified** when at least one test that declares it passed
and none failed. A requirement whose real proof needed PostgreSQL could qualify through unit tests while its database
tests were skipped. An `SRS-*` or `Trace-*` alias inherited its parent's tests and showed "Verified via Parent", so a
child with different acceptance criteria qualified on its parent's evidence. Both documents printed a fixed date and no
revision or environment, and `CADENCE_TEST_DOCKER=1` silently skipped every kernel PostgreSQL test while `cadence ci`
reported green.

## 2. Decision Drivers & Constraints

- **A green report proves what it says:** missing, skipped or not-executed evidence never counts as a pass.
- **Results stay distinct:** passed, failed, skipped and not executed are four different facts.
- **Evidence is tied to a revision and an environment**, including the database and the role the tests ran as.
- **Branches can still merge** (ADR-2287): CI's comparison must not fail just because the revision moved.

## 3. Options Considered

### Option 1: Every mapped test mandatory, aliases judged alone, an evidence block (Selected)

- **Overview:** a requirement qualifies only when every test that declares it passed. An alias is judged by its own
  tests. Both documents open with a delimited evidence block (revision, time, database, runner) that `cadence gxp diff`
  leaves out of CI's comparison. `cadence ci` and `gxp sync` reject runs that skipped integration or E2E tests.
- **Pros:**
  - ✅ One skipped test is enough to stop qualification, which is what the reviewer asked for.
  - ✅ The database line comes from the fixtures themselves, so it proves a database was used and which role ran.
- **Cons:**
  - ❌ The documents change on every regeneration, so one integration owner regenerates them for a merged candidate.

### Option 2: Keep "at least one passed" and add a per-requirement list of mandatory tests

- **Overview:** mark some mapped tests as mandatory.
- **Cons:**
  - ❌ A second list to keep in step with the tags, and every unlisted test can still be skipped unnoticed.

## 4. Decision Outcome

**Chosen option: Option 1.** Every applicable mapped test is mandatory by default. A test is mapped by declaring the
requirement on the test method, so a test that does not apply to a requirement should not declare it.

## 5. Consequences & Trade-offs

- Requirements whose evidence was incomplete drop from Qualified to Not Verified, and aliases that only inherited their
  parent's tests become Planned. That is the intended correction.
- The opt-in lineage benchmark (`CADENCE_LINEAGE_BENCHMARK=1`) and the Chromium browser-to-database correction check
  (`BrowserCorrectionReachesTheDatabaseWithItsReason`, `CADENCE_TEST_BROWSER=1`, executed in CI's `accessibility-e2e`
  job on pushes to `main`) are the two named exemptions from the skipped-test check (`DatabaseTestEvidence.OptInTests`).

## 6. Implementation & Verification

- `packages/compliance/src/RtmGeneratorService.cs`, `RtmEvidenceContext.cs`, `DatabaseTestEvidence.cs`.
- `tests/Cadence.TestSupport/Database/PostgresTestServer.cs` and `PostgresTestEvidence.cs`, used by
  `KernelPostgresFixture`.
- `cadence gxp diff` (`packages/cli/src/Services/GxpDocsDiffService.cs`) and the skipped-test check in
  `LocalCiRunner`.
- Rules for contributors: [docs/TESTING.md](../TESTING.md#how-the-rtm-decides-a-requirements-state).

# ADR-2525: Dependabot Bumps Merge as One Locally Validated Batch

- **Status:** Accepted
- **Date:** 2026-10-20
- **Authors:** @fderuiter, Claude Code
- **Deciders:** @fderuiter
- **Requirements:** PRD-SYS-053 (development guardrails: a dependency update must not change what the platform needs
  to start)
- **Related:** [ADR-2524](2026-10-19-messaging-stays-on-the-apache-licensed-masstransit-8-line.md)

---

## 1. Context & Problem Statement

GitHub Actions is off until 2027, so Dependabot pull requests carry no CI result. On 2026-10-19 a batch of them merged
one by one from the GitHub UI. Main stopped building (AWSSDK.S3 4), stopped starting (MassTransit 9, ADR-2524), and
`pnpm-lock.yaml` gained a duplicate snapshot key that pnpm refuses to read, because each npm pull request rewrote the
lockfile against a base the previous merge had already changed. The pull requests still open then conflicted with
main or were about to (#5496, #5498, #5500, #5510, #5511, #5524 to #5529, #5531, #5533, #5534, #5536), and fo-dicom 5.2.6 (#5511) no longer compiled against the DICOM
de-identifier: it renamed the retired Ethnic Group tag (0010,2160) to `EthnicGroupRETIRED`.

## 2. Decision Drivers & Constraints

- **Main stays green:** nothing reaches main that `pnpm cadence ci` has not run against.
- **One lockfile write:** npm bumps that share `pnpm-lock.yaml` must resolve together, not in sequence.
- **De-identification keeps its coverage:** a library rename must not drop a PHI tag from the removal list.

## 3. Options Considered

### Option 1: Recreate the open bumps as one branch, validate locally, merge once (Selected)

- **Pros:**
  - ✅ One `pnpm install` writes the lockfile once, and one local CI run covers every bump.
  - ✅ A bump that needs code (fo-dicom) gets the fix in the same change.
- **Cons:**
  - ❌ A single bad bump holds back the rest until it is fixed or dropped from the batch.

### Option 2: Rebase and merge each Dependabot pull request on its own

- **Pros:**
  - ✅ Each bump keeps its own history and release notes.
- **Cons:**
  - ❌ Every npm merge invalidates the next pull request's lockfile, and each needs its own 20-minute CI run.

## 4. Decision Outcome

- **Chosen Option:** Option 1.
  - NuGet: fo-dicom 5.1.5 → 5.2.6, Fluid.Core 2.13.0 → 2.40.0, Microsoft.CodeAnalysis.BannedApiAnalyzers 5.0.0 →
    5.6.0, and MassTransit with MassTransit.EntityFrameworkCore 8.3.6 → 8.5.11 (still the Apache-licensed 8 line,
    ADR-2524).
  - The Microsoft.AspNetCore.\*, Microsoft.Extensions.\* and Microsoft.EntityFrameworkCore.\* pins move together from
    10.0.11 to 10.0.13. Microsoft.AspNetCore.OpenApi (#5534) and Microsoft.AspNetCore.Mvc.Testing (#5533) at 10.0.13
    depend on Microsoft.Extensions.\* 10.0.13, so bumping them alone fails restore with NU1109 (package downgrade).
  - npm: vue 3.5.42 → 3.5.44, pinia 4.0.3 → 4.1.0, eslint 10.11.0 → 10.13.0, @typescript-eslint/eslint-plugin
    8.68.0 → 8.71.2, @playwright/test 1.64.0 → 1.64.1, @axe-core/playwright 4.13.0 → 4.14.0, vite 8.3.3 → 8.3.4,
    @vitest/coverage-v8 5.0.2 → 5.0.5 and jsdom 30.1.2 → 30.1.3.
  - They land in one pull request, and the Dependabot pull requests close as superseded.
  - `DicomDeidentifier` names Ethnic Group by its tag number and also removes Ethnic Group Code Sequence (0010,2161)
    and Ethnic Groups (0010,2162), which fo-dicom 5.2 adds and which carry the same information.
- **Revisit:** when GitHub Actions runs again and Dependabot pull requests carry their own CI result.

## 5. Consequences & Trade-offs

- **Positive Impact:** main builds, starts and reads its lockfile after the dependency pass.
- **Negative Impact:** later Dependabot pull requests still conflict whenever an earlier one merges first.
- **Mitigation Strategy:** batch open Dependabot pull requests the same way, and run `pnpm install --frozen-lockfile`
  after any npm merge.

## 6. Implementation & Verification

- **Affected:** `Directory.Packages.props` (all NuGet bumps), `package.json`, `apps/web/package.json`, `apps/subject-portal/package.json`,
  `packages/ui/package.json`, `pnpm-lock.yaml`, `packages/deid/src/DicomDeidentifier.cs`.
- **Verification:** `pnpm install --frozen-lockfile` succeeds, and the full local `pnpm cadence ci` passes against
  PostgreSQL 18, including the de-identification unit tests.

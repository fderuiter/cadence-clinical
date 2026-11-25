# ADR-2499: Multi-Ecosystem OSV Auditing, Bounded FMEA Ledger, and Reconciled Container Scanning

- **Status:** Accepted
- **Date:** 2026-11-23 <!-- deid-ignore: ADR metadata date -->
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Supersedes:** [ADR-2285](2026-07-24-continuous-fmea-gxp-aligned-exemption-ledger.md)
- **Relates to:** [ADR-2327](2026-08-03-upgrade-environments-to-python-314.md), [ADR-2444](2026-09-25-decommission-python-scripts-to-native-dotnet-10-tooling.md), [ADR-2496](2026-09-30-parse-the-cli-with-system-commandline-and-publish-aot-without-suppressions.md), [ADR-2527](2026-11-21-enforce-dependency-alignment-with-msbuild-and-pnpm-catalogs.md), [ADR-2534](2026-10-20-quality-gates-live-in-one-registry-and-each-must-be-able-to-fail.md)

---

## 1. Context & Problem Statement

Following the decommission of legacy Python repository tooling in ADR-2444, `VulnerabilityAuditorService` in `packages/tooling_core` audited `Cadence.sln` (`dotnet list package --vulnerable --include-transitive`) and `pnpm-lock.yaml` (`pnpm audit --json`), while leaving five supply-chain and compliance gaps across GxP statistical containers, auxiliary `.NET` manifests, the FMEA exemption ledger, and Trivy container scans (`@req:PRD-SYS-001`):

1. **Unpinned Python statistical container closure and blind Python/auxiliary `.NET` advisories:** Even with Debian `apt-get` system packages pinned in `docker/sce/python/Dockerfile` and `docker/sce/r/Dockerfile`, `docker/sce/python/Dockerfile` installed top-level scientific packages with floating transitive dependencies and retained `pip` in the runtime layer, while `VulnerabilityAuditorService` audited neither the Python statistical closure nor `.config/dotnet-tools.json` (`dotnet-ef`) and `FloorNativeAotPacks` (`Microsoft.DotNet.ILCompiler`, `Microsoft.NETCore.App.Runtime.NativeAOT.*`) in `Directory.Build.targets`.
2. **Fail-open, unbounded, and non-expiring FMEA ledger records:** `LoadAndValidateLedger` returned valid when `docs/SDLC/vulnerability_exclusions_ledger.json` was missing, accepted arbitrary status strings (`Approved`), permitted duplicate active `(package_name, vulnerability_id)` pairs (`js-yaml` / `GHSA-2883-xcg3-v3hh`), enforced neither version bounds (`affected_versions`) nor re-evaluation deadlines (`review_by`), and allowed 22 historical exemptions with zero live findings to remain active indefinitely.
3. **Incomplete inline bypass scanning:** ADR-2285 specified blocking `--ignore-vuln` and `--ignore-vulnerability` flags, but the native C# port omitted bypass scanning across `.github/`, `scripts/`, `.npmrc` (`audit-level`), `package.json` / `pnpm-workspace.yaml` (`ignoreCves`), and `.trivyignore`.
4. **Unreconciled container scans hiding `MEDIUM` and unfixed advisories:** `container-security` in `.github/workflows/ci.yml` (`CiPlan.cs`) and `publish-container-images` in `.github/workflows/production-pipeline.yml` passed `--ignore-unfixed` (or `ignore-unfixed: true`) and restricted Trivy severity to `CRITICAL,HIGH`, masking `MEDIUM` and unfixed upstream CVEs instead of reconciling findings against the governed FMEA ledger. In addition, `.github/workflows/ci.yml` still contained a dead `validate_vulnerabilities` path check from the retired Python script.

## 2. Decision Drivers & Constraints

- **Zero Python developer tooling (`ADR-2444`):** Python exists only inside the isolated `docker/sce/python` statistical container; all lockfile parsing, OSV queries, ledger validation, bypass scanning, and Trivy report reconciliation must run natively in C# inside `Cadence.ToolingCore` and `Cadence.Cli`.
- **Native AOT & deterministic clock discipline (`K-XS-1`, `K-TIME-3`, `ADR-2496`):** JSON parsing and HTTP payloads must use `JsonDocument` / `Utf8JsonWriter` with zero reflection or `IL2xxx`/`IL3xxx` warnings, and ledger `review_by` expiration checks must read `Cadence.Kernel.Time.IClock` rather than `DateTime.UtcNow`.
- **Fail-closed governance (`ADR-2534`):** Missing ledgers, malformed or unpinned lockfiles, OSV HTTP errors or timeouts, expired or unbounded exemptions, stale active dependency exemptions, and unapproved `CRITICAL`/`HIGH`/`MEDIUM` container findings must all exit `1`.
- **Auditable historical retention:** Resolved historical exemptions in `docs/SDLC/vulnerability_exclusions_ledger.json` must remain preserved for regulatory inspection under `"status": "retired"` rather than being deleted or left `"active"`.

## 3. Options Considered

### Option 1: Native C# OSV batch auditing, hash-pinned SCE lockfile, bounded/time-boxed FMEA ledger, inline bypass scanner, and ledger-reconciled Trivy container scans (selected)

- **Overview:** Commit a SHA-256 hash-pinned `docker/sce/python/requirements.lock` (`--require-hashes --no-deps`, followed by `pip uninstall -y pip`) while retaining the pinned Debian `apt-get` versions in `docker/sce/python/Dockerfile` and `docker/sce/r/Dockerfile`; query `https://api.osv.dev/v1/querybatch` and `/v1/vulns/{id}` natively in `VulnerabilityAuditorService` for `PyPI` (`requirements.lock`) and auxiliary `NuGet` packages (`.config/dotnet-tools.json`, `FloorNativeAotPacks`); enforce fail-closed ledger schema, `active`/`retired` status, version bounds (`affected_versions`), `IClock`-evaluated `review_by` dates, and zero-finding stale exemption rejection; scan for inline scanner bypasses; and reconcile `CRITICAL,HIGH,MEDIUM` (including unfixed) Trivy SARIF/JSON reports via `cadence validate-vulnerabilities --trivy-report <path>` in CI, `cadence ci`, and `production-pipeline.yml`. <!-- deid-ignore: OSV public API URL -->
- **Pros:**
  - ✅ Deterministic, reproducible SCE container builds with full transitive SHA-256 verification and zero runtime `pip` attack surface.
  - ✅ Single Native AOT C# vulnerability auditor covering `.NET` projects, CLI tools, Native AOT packs, `pnpm`, `PyPI`, and Trivy container reports with zero host Python toolchain requirement.
  - ✅ Eliminates indefinite exemptions via bounded `affected_versions`, `review_by` deadlines, and stale active dependency exemption enforcement while preserving retired historical records.
- **Cons:**
  - ❌ Unfixed upstream Debian or base-image `MEDIUM`/`HIGH`/`CRITICAL` advisories require explicit bounded `"ecosystem": "container"` FMEA ledger entries (`RPN < 20`) until upstream fixes ship.

### Option 2: Invoke `pip-audit` or external `osv-scanner` CLI binaries from `VulnerabilityAuditorService`

- **Overview:** Shell out to `pip-audit` in a local Python virtual environment or require contributors to install a separate `osv-scanner` Go binary.
- **Pros:**
  - ✅ Reuses third-party CLI output formats.
- **Cons:**
  - ❌ Violates ADR-2444 by reintroducing Python packaging tooling on developer hosts or adding another external binary prerequisite to pre-commit and `cadence check`.

### Option 3: Keep `--ignore-unfixed` on Trivy and retain timeless `"status": "Approved"` ledger exemptions

- **Overview:** Pin `requirements.lock` and query OSV for Python, while leaving Trivy `--ignore-unfixed` (`CRITICAL,HIGH`) and legacy ledger semantics unchanged.
- **Pros:**
  - ✅ Smaller workflow diff.
- **Cons:**
  - ❌ Leaves unfixed and `MEDIUM` container CVEs invisible to governance and allows unbounded or obsolete dependency exemptions to silently mask regressions on newer package versions.

## 4. Decision Outcome

- **Chosen Option:** Option 1, superseding [ADR-2285](2026-07-24-continuous-fmea-gxp-aligned-exemption-ledger.md).
- **Deterministic SCE container pins:** `docker/sce/python/requirements.lock` pins all 30 direct and transitive runtime wheels with exact `==` specifiers and `--hash=sha256:...` digests; `docker/sce/python/Dockerfile` installs with `pip install --no-cache-dir --require-hashes --no-deps -r /tmp/requirements.lock` and removes `pip` (`pip uninstall -y pip`); the pinned Debian `apt-get` package versions in `docker/sce/python/Dockerfile` and `docker/sce/r/Dockerfile` are preserved; and `baselines/secrets/config.json` (`SecretsBaselineCleanerService`) excludes `requirements.lock` hex digests from secret false positives.
- **Native C# OSV multi-ecosystem auditing (`VulnerabilityAuditorService`):** `AuditPythonEcosystem` parses `docker/sce/python/requirements.lock` (failing closed if missing, unpinned, missing `--hash=sha256:`, or containing pip flags such as `--extra-index-url` / `--trusted-host`) and queries OSV (`PyPI`). `AuditDotNetEcosystem` augments `dotnet list package --vulnerable --include-transitive` with OSV (`NuGet`) queries for `.config/dotnet-tools.json` and `FloorNativeAotPacks` (`Directory.Build.targets` resolved against `Directory.Build.props`). Any OSV HTTP error, timeout, or malformed payload fails closed.
- **Fail-closed bounded FMEA ledger & stale exemption rejection:** `LoadAndValidateLedger` fails closed when `docs/SDLC/vulnerability_exclusions_ledger.json` is absent and requires non-blank fields, `"status"` in `{"active", "retired"}`, unique active `(package_name, vulnerability_id, ecosystem)` scopes (rejecting overlapping unscoped or same-ecosystem duplicates), bounded `affected_versions` (rejecting `*`, wildcards, or upper-unbounded ranges), `review_by` ISO dates (`DateOnly` via `IClock`, failing active entries past `review_by`), and `RPN = S * O * D < 20`. `CheckExclusion` enforces `affected_versions` against installed versions, `AuditVulnerabilities` fails when any active dependency exemption matches zero live findings, and the 22 resolved historical ledger entries are transitioned to `"status": "retired"` (with duplicate `js-yaml` consolidated).
- **Inline bypass scanner:** `ScanForInlineBypasses` rejects `--ignore-vuln`, `--ignore-vulnerability`, `audit-level`, `ignoreCves`, and `.trivyignore` across `.github/`, `scripts/`, `.npmrc`, `package.json`, and `pnpm-workspace.yaml` (including staged Git index blobs under `--pre-commit`).
- **Ledger-reconciled Trivy container scanning:** `cadence validate-vulnerabilities --trivy-report <path>` (`AuditTrivyReport`) reconciles Trivy SARIF and JSON reports (`CRITICAL`, `HIGH`, `MEDIUM`) against bounded active ledger entries. `.github/workflows/ci.yml` (`container-security`, mirrored in `CiPlan.cs` and `LocalCiRunner.cs`) and `.github/workflows/production-pipeline.yml` (`publish-container-images`, gated before `docker push`) scan `CRITICAL,HIGH,MEDIUM` (`os,library`) without `--ignore-unfixed`, upload SARIF where applicable, and run `validate-vulnerabilities --trivy-report` using the `native-aot-build` CLI artifact. Dead `validate_vulnerabilities` workflow path matching in `ci.yml` is removed.

## 5. Consequences & Trade-offs

- **Positive Impact:** Every runtime dependency across `.NET` apps, `.NET` CLI/AOT packs, `pnpm`, Python SCE wheels, and container OS/library layers is deterministically pinned and audited fail-closed against a single bounded, expiring FMEA ledger with zero Python host scripts.
- **Negative Impact / Technical Debt:** Active exemptions must be re-evaluated before `review_by` or retired as soon as upstream upgrades eliminate the finding, and container advisories without upstream patches require explicit `"ecosystem": "container"` FMEA entries.
- **Mitigation Strategy:** `VulnerabilityAuditorService` prints actionable remediation messages naming expired, unbounded, stale, or unapproved package/advisory pairs, and `VulnerabilityAuditorServiceTests` plus `LocalCiPlanTests` lock ledger rules, OSV handling, bypass detection, and CI workflow parity.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `docker/sce/python/{Dockerfile,requirements.lock}`, `packages/tooling_core`, `packages/cli`, `docs/SDLC/vulnerability_exclusions_ledger.json`, `baselines/secrets/config.json`, `.github/workflows/{ci,production-pipeline}.yml`, `tests/Cadence.UnitTests`, `AGENTS.md`, `docs/CI.md`, `docs/cli.md`, `docs/adr/`.
- **Verification Plan:**
  - `docker build` verification of `docker/sce/python/Dockerfile` (confirming `--require-hashes --no-deps`, `pip` removal, and scientific module imports) and `docker/sce/r/Dockerfile`, plus `pnpm cadence check hadolint`.
  - `dotnet publish packages/cli/src/Cadence.Cli.csproj -c Release -r linux-x64` confirming zero Native AOT / trim warnings (`IL2xxx`/`IL3xxx`).
  - `dotnet test tests/Cadence.UnitTests/Cadence.UnitTests.csproj` (`VulnerabilityAuditorServiceTests`, `LocalCiPlanTests`, `LocalCiRunnerTests`, `ContainerImagePinTests`, `QualityGateRegistryTests`), `tests/Cadence.ArchitectureTests`, and full `pnpm cadence check`.

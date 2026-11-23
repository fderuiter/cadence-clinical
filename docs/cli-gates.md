# Quality Gates Reference

Generated from `packages/cli/src/Services/QualityGates.cs` by `pnpm cadence generate-cli-docs`. Don't edit this file by hand. See [CLI Reference](cli.md#cadence-check) and [Continuous Integration](CI.md#quality-gates) for usage context.

## Gate Checks

| Gate | What it checks |
| --- | --- |
| `dotnet-format` | Whitespace formatting against `.editorconfig` (`dotnet format whitespace`) |
| `dotnet-format-style` | Roslyn code-style rules (`dotnet format style`) |
| `prettier` | Prettier formatting across JS, TS, Vue, CSS, JSON, Markdown and YAML files |
| `eslint` | ESLint and duplicate store identifier checks across JS, TS and Vue files |
| `actionlint` | GitHub Actions workflow and composite-action static analysis via `actionlint` from `.cadence/bin` or `PATH` (fails closed when missing) |
| `hadolint` | Dockerfile best-practice and syntax linting (`hadolint --failure-threshold error`) from `.cadence/bin` or `PATH` (fails closed when missing) |
| `yaml` | YAML 1.2 syntax, indentation, quoted scalars, flow brackets and duplicate mapping keys |
| `trailing-whitespace` | No trailing spaces or tabs at the end of lines in text files |
| `end-of-file` | Non-empty UTF-8 text files end with a newline character |
| `clean-secrets` | Verifies `baselines/secrets/config.json` and `baselines/secrets/*.secrets.baseline.json` shards are valid, normalized JSON with the canonical exclusion filter without mutating them |
| `detect-secrets` | Scans changed or tracked files against merged `baselines/secrets/` shards using `detect-secrets-hook` from `.cadence/venv`, `PATH` or `uvx` (fails closed when missing) |
| `duplication` | Duplicated code blocks over 15 normalized lines involving a C# or JavaScript file |
| `cli-docs` | `docs/cli-gates.md`, `docs/cli-commands.md`, `docs/cli.md` and synthesized doc regions match `QualityGates.cs`, `Program.cs`, `PortCheckerService` and code metadata |
| `adrs` | ADR schema, `docs/adr/index.md` consistency, and an ADR for architectural changes |
| `arch-drift` | Fail-closed parity of `docker/docker-compose.yml`, `ServiceTopology`, Vite ports, `core` hosts, EF Core module schemas, `RequireElectronicSignature` endpoints, durable signature token stores, and `GxPAuditInterceptor` hosts across `ARCHITECTURE.md`, `docs/OPERATIONAL_DATABASE.md`, and `docs/SDLC/Signature_Token_Cryptographic_Contract.md` |
| `sanitizer` | Requirement tags in Markdown are upper case (`@req:PRD-…`); fixes them in normal mode and reports errors read-only during pre-commit |
| `markdown` | Markdown structure, links, and paths into .NET test project folders (`tests/Cadence.*`) that no longer exist |
| `env-vars` | Bidirectional parity of environment variables across source code, `.env.example`, and `docs/ENVIRONMENT_VARIABLES.md`, plus existence of every file path cited in `docs/ENVIRONMENT_VARIABLES.md` |
| `dev-env` | Service ports, Vite ports, database schemas, `core` profile hosts, and standalone startup keys in `docs/LOCAL_DEV_ENVIRONMENT.md` match `ServiceTopology`, the gateway, and the Aspire AppHost |
| `readmes` | Each directory under `apps/` and `packages/` has a README with a title |
| `codeowners` | Path pattern existence and owner handle syntax in `.github/CODEOWNERS` |
| `workflow-security` | 40-hex commit SHAs with version comments, `sha256:` digests on `docker://` refs, `persist-credentials: false` on `actions/checkout`, typed non-interpolated `workflow_dispatch` inputs, ref-guarded dispatch triggers, and least-privilege permissions across `.github/workflows` and `.github/actions` |
| `rulesets` | Branch and signed `v*` tag rulesets in `.github/rulesets/*.json`, `pull_request`-only branch bypasses, release-admin-only tag bypasses, `merge_queue` policy, strict status checks, `CI Gate` upstream job coverage, and `pull_request` / `merge_group` workflow parity |
| `imports` | No `using` of another service's namespace across `apps/<service>` |
| `security` | Hard-coded secret patterns across source, Vue templates and build scripts (private keys, cloud keys, API tokens, compliance secrets) |
| `deid` | PHI/PII in changed or tracked files including authored `docs/`, root portal docs, `.vue` templates and `stores/` (vendored `docs/CDISC/`, `docs/NCI/`, `docs/SDLC/runs/` and `baselines/` excluded), ratcheted by per-unit `baselines/deid/<unit>.baseline.txt` shards; a line that is not PHI takes a `deid-ignore` comment |
| `openapi` | `docs/openapi` matches what the .NET services serve, and `docs/api` matches `docs/openapi` |
| `api-clients` | The web API types are generated from the current `docs/openapi`, and the web API clients typecheck against them |
| `schema-doc` | `docs/schema` matches the EF Core models, and all 18 persistence modules pass snapshot, `.Designer.cs`, attribute, and DDL uniqueness checks |
| `path-patterns` | No `tmp`, `temp`, `scratch` or `old_src` directories, `.vue` files only under `apps/`, `packages/` and `docs/.vitepress/`, `.sh` files only under `scripts/` |
| `query-linter` | No interpolated SQL string passed to `FromSqlRaw` or `ExecuteSqlRaw`, and no comparison with a boolean literal |
| `vulnerabilities` | NuGet and pnpm dependency vulnerabilities not exempted by a valid `docs/SDLC/vulnerability_exclusions_ledger.json` entry (fails closed on missing or broken scanners) |
| `dependencies` | Valid JSON across workspace `package.json` and `.config/dotnet-tools.json` manifests, no client-side crypto package (`node-forge`, `elliptic`, `jose`), and no test harness package (`axe-core`, `@axe-core/playwright`, `vitest`, `@vue/test-utils`, `jsdom`, `fake-indexeddb`, `msw`) in runtime `dependencies` |
| `doc-index` | Every Markdown document under `docs/` is listed in `docs/DOCUMENTATION_INDEX.md` |
| `demo-manifest` | `packages/demo-fixtures` matches the canonical demo seed in `packages/demo-fixtures/demo-seed.json`, `ExecutionDbSeeder.cs` and `docker/cadence-realm.json` |
| `rtm` | Requirement tags name known requirements and sit on test methods, and the generated documents match their integrity stamps and are not drafts |
| `gxp-snapshot` | The branch or push range does not change the generated RTM, IQ/OQ/PQ report or `docs/SDLC/runs/` unless `CADENCE_GXP_SNAPSHOT=1` (ADR-2532) |
| `baseline-ratchet` | No ratchet baseline gate (`*.baseline.txt`, `baselines/deid/*.baseline.txt`, `baselines/secrets/*.secrets.baseline.json`) gained an entry without a `Baseline-Growth: <path> - <reason>` trailer on the commit that grew it |
| `merge-readiness` | Verifies `origin/main` (or `--base`) is present, shares history with `HEAD`, and merges cleanly via `git merge-tree --write-tree` without conflicts |
| `public-bundle` | Zero-exemption post-build scan of `docs/.vitepress/dist`, `apps/web/dist`, and `apps/subject-portal/dist` for national IDs, unapproved emails, secrets, external assets, executable local URLs, CSP meta headers, `axe-core`/`keycloak-js` demo contamination, forbidden modulepreloads, and staged demo byte budgets (skipped in unscoped `cadence check` only when all three `dist/` folders are absent; fails closed on partial `dist/` or direct `check public-bundle`) |
| `branch-freshness` | Branch `HEAD` includes `origin/main` (or `--base <ref>`) so local and PR runs enforce strict branch-freshness policy |
| `architecture-tests` | Hexagonal layering and kernel boundary invariants (`tests/Cadence.ArchitectureTests/Cadence.ArchitectureTests.csproj`) |
| `unit-tests` | .NET unit test suite (`tests/Cadence.UnitTests/Cadence.UnitTests.csproj`) |

## CLI Aliases and Pre-Commit Modes

| Gate | Also accepted | Takes `[files]` | Pre-commit hook (`--pre-commit`) |
| --- | --- | --- | --- |
| `dotnet-format` | | yes | Quick: checks staged C# blobs; fails closed when `dotnet` is missing |
| `dotnet-format-style` | | yes | Quick: checks staged C# files and fails closed when `dotnet` is missing or unstaged edits overlap MSBuild inputs |
| `prettier` | | yes | Quick: streams staged JS, TS, Vue, CSS, JSON, Markdown and YAML blobs over stdin; fails closed when `pnpm` or `node_modules` is missing |
| `eslint` | | yes | Quick: lints staged JS, TS and Vue blobs over stdin; fails closed when `pnpm` or `node_modules` is missing |
| `actionlint` | `validate-workflows` | yes | Quick: validates staged `.github/workflows` and `.github/actions` blobs; fails closed when `actionlint` is missing |
| `hadolint` | `hadolint-docker`, `validate-dockerfiles` | yes | Quick: lints staged `Dockerfile*` blobs; fails closed when `hadolint` is missing |
| `yaml` | `check-yaml`, `validate-yaml` | yes | Runs on the staged files |
| `trailing-whitespace` | `check-trailing-whitespace` | yes | Runs on the staged files |
| `end-of-file` | `end-of-file-fixer`, `eof-newline` | yes | Runs on the staged files |
| `clean-secrets` | `clean-secrets-baseline` | | Quick: validates staged `baselines/secrets/` JSON shards without mutating the working tree |
| `detect-secrets` | `secrets-scan` | yes | Quick: scans materialized staged blobs instead of the working tree |
| `duplication` | `detect-code-duplication` | yes | Runs on the staged files |
| `cli-docs` | `validate-cli-docs` | | Runs |
| `adrs` | `validate-adrs` | | Quick: checks staged files for architectural changes, accepting ADRs already added on the branch |
| `arch-drift` | `validate-architecture-drift` | | Runs |
| `sanitizer` | `path-requirement-sanitizer` | yes | Quick: reports lowercase `@req:prd-…` tags in staged Markdown blobs without rewriting working-tree files |
| `markdown` | `validate-markdown` | yes | Runs on the staged files |
| `env-vars` | `validate-environment-variables` | | Runs |
| `dev-env` | `validate-dev-environment`, `dev-environment` | | Runs |
| `readmes` | `validate-readmes` | | Runs |
| `codeowners` | `validate-codeowners` | | Runs |
| `workflow-security` | `validate-workflow-security`, `action-pinning`, `check-action-pinning` | yes | Runs on the staged files |
| `rulesets` | `validate-rulesets` | | Quick: validates staged `.github/rulesets` and `.github/workflows` blobs |
| `imports` | `validate-imports` | | Runs |
| `security` | `audit-security` | yes | Runs on the staged files |
| `deid` | `deid-compliance-scan` | yes | Runs on the staged files |
| `openapi` | `auto-stage-openapi` | yes | Quick: exports specs only for the services the staged C# files touch, otherwise checks staged `docs/api` against staged `docs/openapi` |
| `api-clients` | `validate-api-clients` | yes | Quick: runs `generate:api --check` and `tsc --noEmit` when staged files touch `docs/openapi` or frontend inputs; fails closed when `pnpm` or `apps/web/tsconfig.json` is missing |
| `schema-doc` | `auto-stage-schema-doc` | yes | Quick: runs the drift check when staged files include Entity Framework entities, migrations, configurations, or `docs/schema` |
| `path-patterns` | `validate-path-patterns` | yes | Runs on the staged files |
| `query-linter` | `query-style-linter` | yes | Runs on the staged files |
| `vulnerabilities` | `validate-vulnerabilities` | yes | Quick: validates `docs/SDLC/vulnerability_exclusions_ledger.json` offline and scans only ecosystems with staged manifests, lockfiles or ledger edits |
| `dependencies` | `validate-dependencies` | | Runs |
| `doc-index` | `validate-doc-index` | | Runs |
| `demo-manifest` | `validate-demo-manifest` | yes | Quick: runs when staged files touch `packages/demo-fixtures`, `DevelopmentDemoData.cs`, `ExecutionDbSeeder.cs` or `docker/cadence-realm.json`, otherwise skips it |
| `rtm` | `gxp-rtm-validate` | | Runs |
| `gxp-snapshot` | `gxp-snapshot-guard` | | Quick: checks the staged files instead of the branch |
| `baseline-ratchet` | `baselines` | | Quick: checks staged files against `HEAD`; `CADENCE_BASELINE_GROWTH="<reason>"` admits growth |
| `merge-readiness` | `check-merge-readiness` | | Skipped: skipped so pre-commit stays offline and fast; runs in `cadence check`, `cadence ci`, pull requests, and `merge_group` |
| `public-bundle` | `validate-public-bundle` | | Skipped: post-build gate; skipped in unscoped `check` only when all three `dist/` folders are absent, and enforced fail-closed in `docs-build` and `deploy-docs` |
| `branch-freshness` | `check-branch-freshness`, `validate-branch-freshness` | | Skipped: skipped on commit so local work-in-progress commits stay offline; run by `cadence check` before push |
| `architecture-tests` | `arch-tests` | | Skipped: full xUnit architecture suite; run by `cadence check` and CI's `dotnet-quality` job |
| `unit-tests` | `dotnet-unit-tests` | | Skipped: full xUnit unit test suite; run by `cadence check` and CI's `dotnet-unit-tests` job |

# CLI Commands Inventory

Generated from `packages/cli/src/Program.cs` by `pnpm cadence generate-cli-docs`. Don't edit this file by hand. See [CLI Reference](cli.md) for usage examples and options.

| Command | Kind | Description |
| --- | --- | --- |
| `cadence doctor` | Command | Validates system dependencies and local datastores |
| `cadence check` | Command | Runs architecture sentinels and quality gates |
| `cadence test` | Command | Runs fast unit tests or testcontainer integration suites |
| `cadence ci` | Command | Runs the pull-request jobs of .github/workflows/ci.yml locally for pre-push CI parity |
| `cadence rulesets` | Command | Validates CI Gate coverage and .github/rulesets/*.json (when present) and syncs rulesets via gh api |
| `cadence dev` | Command | Starts the .NET Aspire AppHost with all local services and datastores |
| `cadence fix` | Command | Executes self-healing formatting, index sync, schema and OpenAPI pipeline |
| `cadence detect-duplication` | Command | Finds duplicated C# and TypeScript code blocks |
| `cadence validate-imports` | Command | Checks cross-service import boundaries |
| `cadence validate-dependencies` | Command | Checks project and package dependency rules |
| `cadence validate-path-patterns` | Command | Checks file path and directory conventions |
| `cadence validate-readmes` | Command | Checks that every app and package has a README |
| `cadence validate-codeowners` | Command | Checks the CODEOWNERS file |
| `cadence validate-workflow-security` | Command | Checks GitHub Actions SHA pinning, checkout credentials and least-privilege permissions |
| `cadence validate-markdown` | Command | Checks Markdown formatting and workspace links |
| `cadence query-linter` | Command | Lints database and ORM query statements |
| `cadence extract-env-vars` | Command | Verifies bidirectional parity and file citations between code, .env.example and docs/ENVIRONMENT_VARIABLES.md |
| `cadence audit-security` | Command | Scans for hardcoded secrets |
| `cadence validate-public-bundle` | Command | Verifies built VitePress and demo web bundles with zero comment exemptions |
| `cadence validate-vulnerabilities` | Command | Audits dependency vulnerabilities against the exclusions ledger |
| `cadence clean-secrets-baseline` | Command | Normalizes or checks the detect-secrets baseline |
| `cadence validate-architecture-drift` | Command | Verifies compose, topology, database schema, electronic signature and audit-host parity across architecture docs |
| `cadence sync-openapi-spec` | Command | Exports each service's OpenAPI document into docs/openapi |
| `cadence validate-openapi-drift` | Command | Compares docs/openapi with the services |
| `cadence generate-api-docs` | Command | Renders docs/api from docs/openapi |
| `cadence generate-schema-doc` | Command | Renders docs/schema from the EF Core models |
| `cadence pre-commit-openapi` | Command | Runs the quick OpenAPI check used by the pre-commit hook |
| `cadence validate-api-clients` | Command | Typechecks the web API clients against their OpenAPI types |
| `cadence generate-cli-docs` | Command | Writes docs/cli-gates.md and docs/cli-commands.md and synthesizes doc regions; --check verifies parity |
| `cadence generate-doc-index` | Command | Synchronizes docs/DOCUMENTATION_INDEX.md with all repository markdown documents |
| `cadence generate-demo-manifest` | Command | Exports the canonical synthetic demo study manifest into packages/demo-fixtures; --check verifies parity |
| `cadence generate-rtm` | Command | Regenerates the Requirements Traceability Matrix |
| `cadence compliance-utility` | Command | Sanitizes document paths and requirement prefixes |
| `cadence check-ports` | Command | Reports which local service ports are in use |
| `cadence run-gating-pipeline` | Command | Runs the full gating pipeline |
| `cadence gxp` | Branch | GxP requirement traceability |
| `cadence gxp sync` | Command | Runs the tests and validates requirement tags and results on a branch; writes a preview, never docs/SDLC |
| `cadence gxp snapshot` | Command | Integration owner only: runs the tests and writes and stages the RTM and IQ/OQ/PQ snapshot |
| `cadence gxp validate` | Command | Validates requirement tags and integrity stamps without running tests |
| `cadence gxp diff` | Command | Compares the regenerated RTM and IQ/OQ/PQ report with the committed copies, ignoring the evidence block |
| `cadence db` | Branch | Database management and migrations |
| `cadence db reset` | Command | Resets the local PostgreSQL database and applies all migrations |
| `cadence db migrate` | Command | Applies pending EF Core migrations across all PostgreSQL schemas |
| `cadence migrations` | Branch | Unified EF Core migration tooling across all 18 persistence modules |
| `cadence migrations add` | Command | Scaffolds a new EF Core migration for a persistence module |
| `cadence migrations script` | Command | Generates idempotent SQL review scripts in TestResults/migrations/ |
| `cadence migrations list` | Command | Lists migrations across one or all 18 persistence modules |
| `cadence migrations verify` | Command | Verifies snapshots, .Designer.cs companions, attributes and DDL uniqueness across modules |
| `cadence settings` | Branch | Manage microservice configuration settings models and template exports |
| `cadence settings export` | Command | Checks that environment variables read in code are listed in .env.example and docs |
| `cadence scaffold` | Branch | Architecture scaffolding tools |
| `cadence scaffold service` | Command | Scaffolds a new C# Clean Architecture microservice |
| `cadence scaffold adr` | Command | Scaffolds a new Architectural Decision Record (ADR) |
| `cadence scaffold post` | Command | Scaffolds a new engineering blog post in docs/blog/ |
| `cadence hook` | Branch | Agent lifecycle hooks: read the hook payload on stdin, exit 2 to block (PRD-SYS-053) |
| `cadence hook pre-tool` | Command | Blocks destructive git and shell commands and hand edits to generated GxP documents |
| `cadence hook post-write` | Command | Lints the query style of an edited C# file |
| `cadence hook pre-invocation` | Command | Prints the GxP working rules for the agent |
| `cadence hook stop` | Command | Blocks finishing while the traceability matrix is out of date |
| `cadence wizard` | Branch | Interactive onboarding and scaffolding wizards |
| `cadence wizard onboarding` | Command | Interactive full-stack developer setup wizard |
| `cadence wizard dotnet` | Command | Interactive .NET 10 onboarding wizard |
| `cadence wizard migrate` | Command | Interactive microservice transcription wizard |

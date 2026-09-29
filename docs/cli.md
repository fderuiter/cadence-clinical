# Cadence Unified Developer CLI Reference

The `cadence` CLI (`packages/cli`) provides a unified developer and AI agent interface for local microservice orchestration, quality sentinel validation, GxP compliance synchronization, multi-engine database management, code scaffolding, and Model Context Protocol (MCP) integrations.

## Global Options

All `cadence` commands support the following global flags:

| Option | Short | Description | Default |
| --- | --- | --- | --- |
| `--json` | `-j` | Emit output in structured, machine-readable JSON for agents | `false` |
| `--verbose` | `-v` | Enable verbose output | `false` |

```bash
cadence --json <command>
cadence --verbose <command>
```

## Command Reference

### `cadence doctor`

Run system diagnostics and auto-heal development environment issues.

#### Options & Arguments

| Option / Argument | Type | Description | Default |
| --- | --- | --- | --- |
| `--auto-fix`, `-f` | boolean | Automatically remediate missing databases and recoverable environment issues | `false` |

#### Usage Examples

```bash
cadence doctor
cadence doctor --auto-fix
cadence doctor --json
```

### `cadence dev`

Start the local services, datastores and Aspire dashboard by running the .NET Aspire AppHost (`apps/apphost/src/Cadence.AppHost.csproj`). Requires Docker for the datastore containers. Stop with Ctrl+C.

#### Usage Examples

```bash
cadence dev
```

### `cadence test`

Run backend and frontend test suites with filtering, coverage, and watch mode.

#### Options & Arguments

| Option / Argument | Type | Description | Default |
| --- | --- | --- | --- |
| `<target>` | str | Target test directory or test file | - |
| `--service`, `-s` | str | Filter tests by service name (e.g. execution, ctms) | - |
| `--unit` | boolean | Run unit tests only | `false` |
| `--integration` | boolean | Run integration tests only | `false` |
| `--frontend` | boolean | Run frontend Vitest suites | `false` |
| `--e2e` | boolean | Run decoupled HTTP E2E test suite | `false` |
| `--dotnet` | boolean | Run .NET 10 xUnit integration tests (Cadence.E2ETests) | `false` |
| `--failed-first`, `--lf` | boolean | Run failed tests first | `false` |
| `--cov`, `--no-cov` | boolean | Enable coverage checking | `true` |
| `--filter`, `-f` | str | Filter .NET tests using xUnit expression (e.g. 'Category=Unit') | - |
| `--verbosity` | str | Set .NET test verbosity level (quiet, minimal, normal, detailed, diagnostic) | - |
| `--verbose`, `-v` | boolean | Verbose .NET test output | `false` |
| `--watch`, `-w` | boolean | Watch file system and automatically rerun relevant tests on change | `false` |
| `--fast` | boolean | Fast authoring mode: disable coverage overhead and run unit tests only | `false` |

#### Usage Examples

```bash
cadence test
cadence test --service execution
cadence test --unit
cadence test --frontend
cadence test --watch
cadence test --fast
cadence test --failed-first
```

### `cadence check`

Run all pre-commit and pre-push quality gates.

#### Options & Arguments

| Option / Argument | Type | Description | Default |
| --- | --- | --- | --- |
| `--gate`, `-g` | str | Run a specific gate only (e.g. lint, security, imports) | - |
| `--parallel`, `--sequential` | boolean | Run quality gates concurrently | `true` |

#### Usage Examples

```bash
cadence check
cadence check --gate ruff-lint
cadence check --sequential
```

### `cadence fix`

Auto-fix lint errors, format code, index ADRs, and synchronize schemas.

#### Options & Arguments

| Option / Argument | Type | Description | Default |
| --- | --- | --- | --- |
| `--all`, `-a` | boolean | Execute all self-healing, schema synchronization, and formatting steps | `false` |
| `--sync-gxp` | boolean | Also synchronize GxP compliance and RTM documentation | `false` |

#### Usage Examples

```bash
cadence fix
cadence fix --all
cadence fix --sync-gxp
```

### `cadence db`

Manage multi-engine databases, migrations, clinical seeding, and snapshots.

#### Subcommands

##### `cadence db reset`

Reset all database instances (PostgreSQL, Neo4j, SQLite) to clean state.

| Option / Argument | Type | Description | Default |
| --- | --- | --- | --- |
| `--allow-offline` | boolean | Allow offline reset if remote databases are unreachable | `false` |

##### `cadence db migrate`

Execute pre-boot migrations across all relational microservices.

##### `cadence db seed`

Populate multi-engine databases with realistic end-to-end clinical trial datasets.

| Option / Argument | Type | Description | Default |
| --- | --- | --- | --- |
| `--tier`, `-t` | str | Seeding tier: protocol, subjects, operations, full | `full` |
| `--scenario`, `-s` | str | Named clinical scenario (e.g. CADENCE-101, oncology-phase3) | `CADENCE-101` |

##### `cadence db snapshot`

Capture a portable snapshot archive of all local SQLite databases.

| Option / Argument | Type | Description | Default |
| --- | --- | --- | --- |
| `<name>` | str | Name for the snapshot (e.g. baseline-clean, post-enrollment) | - |

##### `cadence db restore`

Restore local databases from a saved snapshot archive.

| Option / Argument | Type | Description | Default |
| --- | --- | --- | --- |
| `<name>` | str | Name of the snapshot to restore | - |

##### `cadence db status`

Display multi-engine database sizes and available snapshots.

#### Usage Examples

```bash
cadence db reset
cadence db migrate
cadence db seed --scenario CADENCE-101 --tier full
cadence db snapshot baseline-clean
cadence db restore baseline-clean
cadence db status
```

### `cadence scaffold`

Scaffold new hexagonal microservices, ports, adapters, or ADRs.

#### Subcommands

##### `cadence scaffold service`

Scaffold a new enterprise hexagonal microservice.

| Option / Argument | Type | Description | Default |
| --- | --- | --- | --- |
| `<name>` | str | Name of the microservice to scaffold (e.g. analytics, billing) | - |
| `--features`, `-f` | str | Comma-separated optional features | `""` |

##### `cadence scaffold adr`

Scaffold a new Architecture Decision Record (ADR).

| Option / Argument | Type | Description | Default |
| --- | --- | --- | --- |
| `<title>` | str | Title for the Architecture Decision Record | - |
| `--domain`, `-d` | str | Domain category | `core-platform` |
| `--req`, `-r` | str | Traceable requirement ID | `PRD-SYS-001` |

#### Usage Examples

```bash
cadence scaffold service analytics
cadence scaffold adr "Use Event Driven Architecture" --domain core-platform --req PRD-SYS-001
```

### `cadence settings`

Manage microservice configuration settings models and template exports.

#### Subcommands

##### `cadence settings export`

Export Pydantic settings models into .env.example and docs/ENVIRONMENT_VARIABLES.md.

| Option / Argument | Type | Description | Default |
| --- | --- | --- | --- |
| `--check` | boolean | Check for configuration drift without modifying files | `false` |
| `--json` | boolean | Emit output in structured JSON format | `false` |

### `cadence setup`

Environment setup, cryptographic key generation, and cloud credential bootstrapping.

#### Options & Arguments

| Option / Argument | Type | Description | Default |
| --- | --- | --- | --- |
| `--dev`, `-d` | boolean | Run in automated local development mode (auto-generate all cryptographic keys) | `false` |
| `--env-file`, `-e` | str | Target environment file path | `.env` |

#### Subcommands

##### `cadence setup credentials`

Launch the interactive credential bootstrapping wizard (/wizard standard).

| Option / Argument | Type | Description | Default |
| --- | --- | --- | --- |
| `--dev`, `-d` | boolean | Run in automated local development mode (auto-generate all cryptographic keys) | `false` |
| `--env-file`, `-e` | str | Target environment file path | `.env` |

#### Usage Examples

```bash
cadence setup
cadence setup credentials --dev
```

### `cadence record`

Capture high-definition UI video recordings across clinical personas and sync to Google Drive.

#### Subcommands

##### `cadence record setup-gdrive`

Validate Google Drive Service Account credentials and target folder permissions.

| Option / Argument | Type | Description | Default |
| --- | --- | --- | --- |
| `--credentials-path`, `-c` | str | Path to Google Cloud Service Account JSON key file | - |
| `--folder-id`, `-f` | str | Target Google Drive Folder ID | - |

##### `cadence record list`

List historical local UI video recording runs and manifests.

##### `cadence record run`

Execute end-to-end persona golden paths, record 1080p videos, and sync to Google Drive.

| Option / Argument | Type | Description | Default |
| --- | --- | --- | --- |
| `--suite`, `-s` | str | Target persona suite: 'designer', 'crc', 'cra', 'data_manager', 'safety', 'compliance', 'all' | `all` |
| `--upload-drive`, `--upload` | boolean | Upload recorded video artifacts to Google Drive upon completion | `false` |
| `--local-only` | boolean | Skip Google Drive upload and preserve artifacts locally only | `false` |
| `--demo`, `--fast` | boolean | Run in demonstration pacing mode with smooth pauses vs high-speed headless | `true` |
| `--headed` | boolean | Display browser window during test execution | `false` |
| `--mp4` | boolean | Transcode WebM recordings to MP4 if ffmpeg is installed | `false` |

#### Usage Examples

```bash
cadence record run --suite designer
cadence record list
cadence record setup-gdrive --credentials-path credentials.json
```

### `cadence gxp`

GxP compliance, RTM synchronization, and qualification report automation.

#### Subcommands

##### `cadence gxp sync`

Run tests, regenerate RTM documentation, and stage compliance reports.

| Option / Argument | Type | Description | Default |
| --- | --- | --- | --- |
| `--dry-run` | boolean | Validate only without staging changes | `false` |
| `--full` | boolean | Force full test suite execution bypassing incremental cache | `false` |

##### `cadence gxp validate`

Validate that checked-in RTM documentation is strictly up-to-date.

##### `cadence gxp verify`

Verify electronic signatures and document integrity of signed GxP reports.

| Option / Argument | Type | Description | Default |
| --- | --- | --- | --- |
| `--path`, `-p` | str | Target Markdown file or directory to verify | `docs/SDLC` |

##### `cadence gxp export-cdisc`

Generate a validated CDISC USDM compliance document from local database models.

| Option / Argument | Type | Description | Default |
| --- | --- | --- | --- |
| `--output`, `-o` | str | Path to save generated CDISC USDM compliance JSON document | `docs/CDISC/cdisc_usdm_compliance.json` |
| `--validate`, `--no-validate` | boolean | Validate generated document against CDISC USDM Pydantic schema | `true` |

#### Usage Examples

```bash
cadence gxp sync
cadence gxp validate
cadence gxp verify --path docs/SDLC
cadence gxp export-cdisc --output docs/CDISC/cdisc_usdm_compliance.json
```

### `cadence cdisc`

CDISC USDM standards and schema documentation exporter tools.

#### Subcommands

##### `cadence cdisc export`

Generate a validated CDISC USDM compliance document from local database models.

| Option / Argument | Type | Description | Default |
| --- | --- | --- | --- |
| `--output`, `-o` | str | Path to save generated CDISC USDM compliance JSON document | `docs/CDISC/cdisc_usdm_compliance.json` |
| `--validate`, `--no-validate` | boolean | Validate generated document against CDISC USDM Pydantic schema | `true` |

#### Usage Examples

```bash
cadence cdisc export --output docs/CDISC/cdisc_usdm_compliance.json
```

### `cadence mcp`

Run native JSON-RPC Model Context Protocol (MCP) Stdio server for AI agents.

#### Native JSON-RPC MCP Tools

The `mcp` command launches the `CadenceMcpServer` over stdio using the JSON-RPC 2.0 protocol (Model Context Protocol specification). It exposes the following 11 native MCP tools:

##### `doctor_diagnose`

Validates Python 3.14+ runtime, port allocations, and database connectivity.

| Input Parameter | Type | Required | Description | Default |
| --- | --- | --- | --- | --- |
| `auto_heal` | boolean | `no` | Auto-initialize missing SQLite schemas or configs | `False` |
| `summary` | boolean | `no` | Return concise summary instead of full logs | `True` |

##### `run_sentinels`

Concurrently executes repository architecture sentinels and quality gates.

| Input Parameter | Type | Required | Description | Default |
| --- | --- | --- | --- | --- |
| `gate` | string | `no` | Optional single gate to execute | - |
| `parallel` | boolean | `no` | Run gates concurrently | `True` |
| `summary` | boolean | `no` | Return concise summary output | `True` |

##### `run_fast_tests`

Runs sub-second unit and contract tests.

| Input Parameter | Type | Required | Description | Default |
| --- | --- | --- | --- | --- |
| `subsystem` | string | `no` | Optional subsystem path filter | - |
| `summary` | boolean | `no` | Return concise summary metrics | `True` |

##### `seed_clinical_scenario`

Seeds multi-engine clinical test scenarios across databases.

| Input Parameter | Type | Required | Description | Default |
| --- | --- | --- | --- | --- |
| `tier` | string | `no` | Seed tier ('smoke', 'standard', 'full') | `standard` |
| `scenario` | string | `no` | Scenario name preset | `default` |
| `dry_run` | boolean | `no` | Preview seeding plan without writing | `False` |

##### `sync_gxp_compliance`

Regenerates RTM traceability matrix and IQ/OQ/PQ docs.

| Input Parameter | Type | Required | Description | Default |
| --- | --- | --- | --- | --- |
| `dry_run` | boolean | `no` | Verify synchronization without modifying files | `False` |

##### `introspect_service_contracts`

Returns OpenAPI specifications and inter-service boundary schemas.

| Input Parameter | Type | Required | Description | Default |
| --- | --- | --- | --- | --- |
| `service` | string | `no` | Target microservice name (or None for all) | - |

##### `usdm_graph_inspect`

Inspects CDISC USDM v3/v4 protocol graph metadata from Neo4j (Study, StudyArm, StudyEpoch, Activity, ScheduleTimeline) with depth and entity filtering.

| Input Parameter | Type | Required | Description | Default |
| --- | --- | --- | --- | --- |
| `protocol_id` | string | `no` | Optional study protocol identifier | - |
| `entity_type` | string | `no` | Optional entity label filter (Study, StudyArm, StudyEpoch, Activity, ScheduleTimeline) | - |
| `depth` | integer | `no` | Traversal depth limit from root study node | `2` |
| `summary` | boolean | `no` | Return concise entity counts and structure summary | `True` |

##### `sdtm_pipeline_validate`

Validates eCRF-to-SDTM domain mapping rules against CDISC Conformance Rules.

| Input Parameter | Type | Required | Description | Default |
| --- | --- | --- | --- | --- |
| `domain` | string | `no` | Optional SDTM domain code filter (DM, AE, LB, VS, CM) | - |
| `strict_cdisc` | boolean | `no` | Enforce strict CDISC Conformance Rules | `True` |
| `summary` | boolean | `no` | Return concise conformance metric summary | `True` |

##### `clinical_event_stream`

Inspects and optionally replays reactive outbox clinical events across decoupled microservice boundaries.

| Input Parameter | Type | Required | Description | Default |
| --- | --- | --- | --- | --- |
| `event_type` | string | `no` | Optional clinical event filter (SubjectStateChanged, FormSubmitted, LabValueIngested, PaymentTriggered) | - |
| `limit` | integer | `no` | Maximum number of recent events to retrieve | `20` |
| `replay` | boolean | `no` | Replay queried events through registered mock listeners | `False` |

##### `synthetic_patient_generate`

Generates GxP-compliant synthetic clinical trial participants with longitudinal activities and lab profiles.

| Input Parameter | Type | Required | Description | Default |
| --- | --- | --- | --- | --- |
| `protocol_id` | string | `no` | Study protocol context for participant schedule | `default` |
| `scenario` | string | `no` | Clinical scenario (standard, oncology_dlt, gene_therapy_coc, cardiac_cec) | `standard` |
| `count` | integer | `no` | Number of synthetic subjects to synthesize | `1` |
| `dry_run` | boolean | `no` | Simulate generation without writing to database fixtures | `False` |

##### `inspect_zoom_target`

Progressively inspects granular logs, error traces, or test outputs for a given zoom token.

| Input Parameter | Type | Required | Description | Default |
| --- | --- | --- | --- | --- |
| `zoom_token` | string | `yes` | Zoom token returned in previous tool summary envelope | - |
| `offset` | integer | `no` | Starting line offset for pagination | `0` |
| `limit` | integer | `no` | Number of lines to retrieve | `100` |

##### JSON-RPC Stdio Protocol Specification

The MCP server reads JSON-RPC 2.0 requests from `sys.stdin` and writes JSON-RPC 2.0 responses to `sys.stdout`. Supported JSON-RPC methods:
- `initialize`: Handshakes and exchanges capabilities and server metadata.
- `tools/list`: Returns array of available tools and input schemas.
- `tools/call`: Executes specified tool with input arguments and returns JSON output envelope.

#### Usage Examples

```bash
cadence mcp
```

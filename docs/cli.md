# Cadence Unified Developer CLI Reference

The `cadence` CLI (`packages/cli`) provides a developer and agent command-line tool for managing local services, running concurrent quality checks, executing multi-engine database migrations and clinical scenario seeding, and maintaining GxP compliance documentation.

## Command Reference

### `cadence doctor`

Run full system and environment diagnostics.

```bash
cadence doctor
cadence doctor --json
```

### `cadence dev`

Start local microservices in development mode with live reload.

```bash
cadence dev                          # Launch default services
cadence dev --service designer       # Launch single service
cadence dev --services designer,execution,web
cadence dev --daemon                 # Run in background
cadence dev --stop                   # Terminate running background daemons
```

### `cadence test`

Execute pytest and vitest test suites with smart filtering.

```bash
cadence test                         # Run all tests
cadence test --service designer      # Run designer tests
cadence test --unit                  # Run unit tests only
cadence test --frontend              # Run Vitest web frontend tests
cadence test --failed-first          # Re-run failed tests first
cadence test --cov                   # Enforce 80% coverage check
```

### `cadence check`

Execute all pre-flight quality and architecture sentinels concurrently.

```bash
cadence check                        # Run all quality gates
cadence check -g format,lint         # Run specific gates
cadence check -g contract-verification
cadence check -g import-boundaries
```

### `cadence fix`

Automatically remediate formatting, linting, ADR indices, and OpenAPI schema exports.

```bash
cadence fix
```

### `cadence db`

Multi-engine clinical database management (SQLite, PostgreSQL, Neo4j).

```bash
cadence db reset                     # Reset all local databases
cadence db migrate                   # Run migrations
cadence db seed --scenario oncology-phase3 --tier full # Seed clinical data
cadence db snapshot <name>           # Create compressed snapshot
cadence db restore <name>            # Restore from snapshot
cadence db status                    # Inspect database sizes and snapshots
```

### `cadence scaffold`

Generate new hexagonal services and ADRs with automated indexing.

```bash
cadence scaffold service <service-name>
cadence scaffold adr "Title" --domain core-platform --req PRD-SYS-001
```

### `cadence gxp`

Synchronize GxP Requirements Traceability Matrix and test execution reports.

```bash
cadence gxp sync                     # Run tests, regenerate RTM, stage docs
cadence gxp validate                 # Dry-run validation
```

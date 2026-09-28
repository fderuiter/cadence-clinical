# Cadence Clinical Plugin Rules

These guidelines are automatically applied when the `cadence-core` plugin is active.

## Stack & Standards (.NET 10 Target Architecture)
- C# 14 / .NET 10 (`net10.0`) with strict nullable types (`<Nullable>enable</Nullable>`) and Native AOT readiness.
- ASP.NET Core 10 Minimal APIs with Endpoint Filters (`IEndpointFilter`) and HMAC gateway signatures (`Cadence.Security.Signing`).
- Single-Assembly Clean Architecture per microservice (`Domain/`, `Application/`, `Infrastructure/`, `Endpoints/`).
- Entity Framework Core 10 + Npgsql for PostgreSQL (`apps/execution`) with `SaveChangesInterceptor` for 21 CFR Part 11 GxP shadow auditing and `.AsNoTracking()` on read queries.
- Architecture sentinels via `ArchUnitNET` (`tests/Cadence.ArchitectureTests`) ensuring `Domain/` has zero dependencies on infrastructure or presentation.
- Cross-service communication via strongly-typed contracts in `Cadence.Contracts` with Polly resilience policies.
- Async Neo4j Driver for clinical graph authoring (`apps/designer`).
- Centralized test infrastructure via `Cadence.Testing` (factories, in-memory repository fakes, security headers).
- Interactive developer wizards via Cadence CLI (`dotnet cadence wizard ...`) and `scripts/wizards/`.
- Automated Git pre-commit hooks in `.githooks/` enforcing `dotnet format --verify-no-changes` and ADR integrity.
- Mandatory 21 CFR Part 11 traceability: XML doc comments (`/// @req:PRD-SYS-xxx`) and `[GxPRequirement("PRD-SYS-xxx")]` attributes on xUnit tests, synced via `python scripts/sync_gxp.py`.
- 6-step service transcription SOP and 4-step decommissioning sequence documented authoritatively in `docs/MIGRATION_WORKFLOW.md`.
- Pure .NET projects without a `pyproject.toml` (`packages/<name>`, `apps/<name>`) must be explicitly excluded in `pyproject.toml` under `[tool.uv.workspace] exclude`.
- Multi-runtime pre-commit enforcement via `.pre-commit-config.yaml` (`dotnet format Cadence.sln --verify-no-changes`, `validate_adrs.py`, `validate_architecture_drift.py`).
- Markdown & API Docs formatting: Wrap endpoint paths containing route parameters in code backticks in headings (`### `GET` `/api/v1/resource/{id}``) to prevent VitePress `markdown-it-anchor` custom ID collisions.

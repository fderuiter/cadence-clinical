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
- Interactive developer wizards via Cadence CLI (`dotnet cadence wizard ...`).
- Automated quality sentinels enforcing `dotnet format --verify-no-changes`, ArchUnitNET layer boundaries, and ADR integrity.
- Mandatory 21 CFR Part 11 traceability: XML doc comments (`/// @req:PRD-SYS-XXX`) and `[GxPRequirement("PRD-SYS-xxx")]` attributes on xUnit tests, validated on a branch via `pnpm cadence gxp sync` (preview only). The generated RTM and IQ/OQ/PQ report are an integration-owner snapshot (`pnpm cadence gxp snapshot`); never commit them on a feature branch or hand-resolve conflicts in them, and take `main`'s copy on a conflict.
- Architectural governance and service patterns documented authoritatively in `ARCHITECTURE.md` and `docs/adr/`.
- All microservices under `apps/` and packages under `packages/` are enrolled in `Cadence.sln` under Central Package Management (`Directory.Packages.props`).
- Native pre-commit and CI enforcement via `dotnet cadence check` running all 21 quality sentinels.
- Markdown & API Docs formatting: Wrap endpoint paths containing route parameters in code backticks in headings (`### `GET` `/api/v1/resource/{id}``) to prevent VitePress `markdown-it-anchor` custom ID collisions.

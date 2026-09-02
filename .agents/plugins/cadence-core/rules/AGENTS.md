# Cadence Clinical Plugin Rules

These guidelines are automatically applied when the `cadence-core` plugin is active.

## Stack & Standards (.NET 10 Target Architecture)
- C# 14 / .NET 10 (`net10.0`) with strict nullable types (`<Nullable>enable</Nullable>`) and Native AOT readiness.
- ASP.NET Core 10 Minimal APIs with Endpoint Filters (`IEndpointFilter`) and HMAC gateway signatures (`Cadence.Security.Signing`).
- Entity Framework Core 10 + Npgsql for PostgreSQL (`apps/execution`) with `SaveChangesInterceptor` for 21 CFR Part 11 GxP shadow auditing and `.AsNoTracking()` on read queries.
- Async Neo4j Driver for clinical graph authoring (`apps/designer`).
- Vue 3 with vanilla CSS semantic tokens (no Tailwind CSS).
- Centralized test infrastructure via `Cadence.Testing` (factories, in-memory repository fakes, security headers).
- Unified Cadence CLI (`dotnet cadence ...`) for all dev, test, and diagnostic tasks.
- Mandatory 21 CFR Part 11 traceability: XML doc comments (`/// @req:PRD-SYS-xxx`) and `[GxPRequirement("PRD-SYS-xxx")]` attributes on xUnit tests, synced via `python scripts/sync_gxp.py`.
- Markdown & API Docs formatting: Wrap endpoint paths containing route parameters in code backticks in headings (`### `GET` `/api/v1/resource/{id}``) to prevent VitePress `markdown-it-anchor` custom ID collisions.

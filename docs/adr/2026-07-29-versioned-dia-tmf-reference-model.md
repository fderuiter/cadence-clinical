# ADR 2026-07-29: Versioned DIA TMF Reference Model Catalog

## Status
Accepted

## Context
A robust, GxP-compliant electronic Trial Master File (eTMF) requires a shared and immutable source of truth for TMF Zones, Sections, Artifacts, and their valid hierarchical structures. The existing eTMF implementation relied on unstructured/informal string matching to assign TMF taxonomy. To ensure compliance with GxP, GAMP 5, and 21 CFR Part 11, we need a canonical, strongly-typed Reference Model catalog that supports multiple versions and avoids runtime database or remote dependency checks.

## Decision
Establish the canonical, versioned DIA TMF Reference Model catalog inside a shared Pydantic v2 package named `packages/core-models/tmf_reference_model`.
The implementation includes:
- Declaring strongly-typed, runtime-immutable Pydantic v2 models (`Zone`, `Section`, `Artifact`, and `Catalog`).
- Designing a catalog registry (`CatalogRegistry`) to index multiple versions and serve the current default active catalog (defaulting to `v3.2.0`).
- Seeding the active catalog with the 11 canonical zones and standard sections/artifacts representing the DIA TMF Reference Model.
- Modifying `packages/__init__.py` and `tests/conftest.py` to inject `packages/core-models` into `sys.path` to allow seamless and clean direct imports of the `tmf_reference_model` package despite the hyphenated parent directory name.

## Alternatives Considered
### Option 1: Database-backed Taxonomy Tables
Store the DIA TMF taxonomy in database tables, populated via database migrations or startup seeds.
- ❌ Requires database migrations and startup synchronization, increasing operational complexity.
- ❌ Risk of unauthorized runtime modification, violating GxP immutability goals for static reference models.

### Option 2: Hardcoded Substring Matching (Legacy)
Maintain the legacy inline fallback matching logic.
- ❌ Fragile and prone to classification drift across different microservices.
- ❌ Lacks typing validation and support for versioned catalogs co-existing.

## Trade-offs
### Pros
- ✅ Compile-time and runtime immutability via frozen Pydantic models ensures high GxP data integrity.
- ✅ Zero runtime database or network dependencies, achieving ultra-fast lookups and high reliability.
- ✅ Clear separation of concerns, providing a clean package that can be imported by any backend service.

### Cons
- ❌ Adding a new taxonomy catalog version requires a code update rather than a database insert. However, since TMF Reference Model updates occur extremely rarely (years apart), this is a highly acceptable trade-off for GxP rigor.

# ADR: Structured Expiration Metadata and Migration Runners for eTMF/eISF

## Status
Proposed

## Context
Regulated clinical documentation (both in eTMF and eISF repositories) often has formal date validity requirements, such as Issue Date and Expiration Date. These need to be indexed, first-class, queryable attributes rather than nested, un-typed metadata blobs to allow robust warnings, reporting, and automated alerts.

Additionally, to ensure zero-downtime rolling deployments (GAMP 5, GxP 21 CFR Part 11 compliant), schema evolution must occur idempotently without relying solely on SQLite in-memory generation or raw `create_all`.

## Decision
1. **First-Class Metadata Columns:** Add nullable `issue_date` (Date), `expiration_date` (Date), and `document_owner_id` (String(255)) fields directly on `TMFDocument` and `ISFDocument` models.
2. **Date Order Validation:** Implement Pydantic model validators on ingestion/creation and update schemas to enforce that `issue_date` is never later than `expiration_date` (yielding clear 422 HTTP responses on violation).
3. **Dedicated Migration Runners:** Introduce standalone migration runner scripts at `apps/etmf/database/migrate.py` and `apps/eisf/database/migrate.py` utilizing SQLAlchemy introspection. These runners will execute idempotently before web server start-up, executing `Base.metadata.create_all` and then patching pre-existing tables safely with the new columns and indexes.
4. **RBAC Control:** Restrict the setting and modification of expiration metadata to users possessing the new `etmf_document:manage_expiration` action privilege (which is granted to `sponsor_dm` and `admin` roles, and system/service accounts).

## Consequences
- Expiration warnings and date alerts can query indexed columns directly, improving performance.
- Both SQLite and PostgreSQL schemas can be evolved idempotently on production-like environments during pre-boot orchestration.
- Schema, API, and migration invariants are fully tested.

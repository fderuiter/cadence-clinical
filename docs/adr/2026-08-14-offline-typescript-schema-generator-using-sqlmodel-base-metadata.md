# ADR-2173: Offline TypeScript Schema Generator using SQLModel Base Metadata

- **Status:** Accepted
- **Date:** 2026-08-14
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

The active database connection requirement in `scripts/introspect_pg_schema.py` posed a significant barrier to local development, automated PR checks, and isolated CI/CD workflows. Introspecting live database tables required running containerized databases or active test environments, complicating what should be a clean, build-time static type-generation step.

To decouple schema type-generation from runtime dependencies, we require an offline introspection solution that derives database schemas statically from SQLModel model declarations, while maintaining GxP compliance boundaries and preventing unauthorized table exports.

This decision implements requirements under Trace-8.

## 2. Decision Drivers & Constraints

- **Zero Live Database Requirements:** Eliminate database initialization and migration steps during static code generation.
- **Hermetic and Secure CI/CD builds:** Keep the style and lint verification jobs fast, completely network-isolated, and dependency-free.
- **GxP Compliance Parity:** Exclude audit, seal, outbox, and internal configuration tables from the exported type definitions.

## 3. Options Considered

### Option 1: Live PostgreSQL/SQLite Database Introspection (Legacy)

- **Overview:** Initialize a database, apply migrations via Alembic, connect the introspection engine, inspect schemas from PG catalog, and output TS files.
- **Pros:**
  - ✅ Reflects the actual database state including any raw triggers or custom indexes.
- **Cons:**
  - ❌ Requires a running database, rendering CI/CD slow and fragile.
  - ❌ Does not support pure offline static validation workflows.

### Option 2: SQLModel Metadata-Driven Static Introspection (Chosen)

- **Overview:** Load SQLAlchemy's `Base.metadata.tables` directly using offline-configured python model imports, avoiding active database connections entirely.
- **Pros:**
  - ✅ 100% offline-capable, runs instantly without any service dependencies.
  - ✅ Precise type mapping from Python types to TypeScript.
  - ✅ High isolation, complying with GxP audit-trail requirements.
- **Cons:**
  - ❌ Requires maintaining mock/default environments during python model loading in the script.

## 4. Decision Outcome

Chosen option: Option 2 because it satisfies Trace-8 while ensuring system maintainability, eliminating live database dependencies, and preserving strict GxP compliance boundaries.

## 5. Consequences & Trade-offs

- **Positive:** Faster static checks, simplified developer setup, and completely secure type generation in isolated containers.
- **Negative:** Import blocks must be loaded carefully inside Python with appropriate environment variables (`TERMINOLOGY_OFFLINE`, etc.) configured.

## 6. Implementation & Verification

- Target files/packages modified: `scripts/introspect_pg_schema.py` and `apps/web/src/types/db_schemas.ts`.
- Verification: Run ADR validation and lint steps locally to confirm parity.

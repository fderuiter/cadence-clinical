# ADR-2237: Multi-Tenant Database Schema Provisioning and Automated Migration Pipeline

- **Status:** Superseded by [ADR-2250](2026-09-29-kernel-tenant-isolation-via-row-level-security.md)
- **Date:** 2026-08-29
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

> [!NOTE]
> **Superseded Notice (2026-09-29):** Schema-per-tenant isolation is superseded for the Clinical Kernel by [ADR-2250](2026-09-29-kernel-tenant-isolation-via-row-level-security.md): one `kernel` schema with `tenant_id`, composite foreign keys and forced Row-Level Security, with database-per-tenant as the upgrade path for regulated customers.

## 1. Context & Problem Statement

In enterprise eClinical deployments across diverse sponsors, clinical trial metadata and EDC operational datasets must maintain strict cryptographic isolation and multi-tenant boundary integrity. Previously, schema provisioning for new clinical tenants required ad-hoc manual scripts without automated transactional rollbacks or standardized 21 CFR Part 11 audit trigger attachment.

Under PRD-SYS-041 and PRD-SYS-053, the Cadence platform requires an automated schema provisioning pipeline in `apps/execution` that creates dedicated PostgreSQL schemas per tenant/sponsor (`tenant_{id}`) with session `search_path` routing, installs immutable 21 CFR Part 11 database triggers (`audit_schema.prevent_audit_mutation()` and `capture_model_mutation()`), establishes initial genesis cryptographic HMAC audit sequence headers, and executes transactional schema rollbacks on failure without cross-tenant data leakage.

## 2. Decision Drivers & Constraints

- **Strict GxP Multi-Tenant Isolation (PRD-SYS-041):** Data queries for Tenant A must never leak or mutate data belonging to Tenant B.
- **SQL Injection & Schema Name Sanitization:** Tenant identifiers must be strictly validated against regex `^[a-z0-9_]{3,32}$` and reserved schema keywords to prevent DDL injection vulnerabilities.
- **Dual-Defense 21 CFR Part 11 Audit Trail:** Every tenant schema must automatically install database-level write-protection and audit capture triggers for all EDC entity tables (`clinical_subjects`, `clinical_observations`, `form_submissions`, `study_authored_rules`, `clinical_queries`).
- **Cryptographic Audit Genesis Sealing:** Each newly provisioned tenant must initialize an authentic genesis HMAC audit sequence header block (`AuditLedgerSeal`) in `audit_schema`.
- **Transactional Rollback on Failure:** Any midway failure during DDL creation, migration, trigger deployment, or seed ingestion must safely clean up and drop the incomplete schema (`DROP SCHEMA IF EXISTS {tenant_id} CASCADE`).
- **Unified CLI Ergonomics:** Expose a unified CLI command `cadence db provision --tenant <tenant_id> [--seed]` in `packages/cli/commands/db.py`.

## 3. Options Considered

### Option 1: **Option A (Selected): Dedicated Schema-per-Tenant with Dynamic Session `search_path` and Transactional DDL Pipeline**

- **Overview:** - Each sponsor/tenant receives an isolated PostgreSQL schema (`tenant_xyz`).
   - Shared models in `apps/execution/database/models` are dynamically created inside the tenant schema using `Base.metadata.create_all` with `search_path` configuration.
   - Triggers and genesis cryptographic blocks are deployed automatically during provisioning.
   - Transactional rollback purges the schema if provisioning fails.
- ***Pros:***
  - ✅ Satisfies architectural requirements
- ***Cons:***
  - ❌ Requires implementation and maintenance overhead

### Option 2: **Option B: Shared Single-Schema with Row-Level Security (RLS) and Tenant ID Columns**

- **Overview:** - All tenant rows mixed in a single global table with `tenant_id` column.
   - Higher operational risk of data leakage via misconfigured ORM queries or missing `.where()` clauses. Harder to satisfy strict sponsor data separation audits.
- ***Pros:***
  - ✅ Satisfies architectural requirements
- ***Cons:***
  - ❌ Requires implementation and maintenance overhead

### Option 3: **Option C: Database-per-Tenant**

- **Overview:** - High infrastructure overhead, connection pool exhaustion, and slow provisioning times for dynamic multi-tenant SaaS.
- ***Pros:***
  - ✅ Satisfies architectural requirements
- ***Cons:***
  - ❌ Requires implementation and maintenance overhead


## 4. Decision Outcome

- **Chosen Option:** **Option A (Dedicated Schema-per-Tenant with Dynamic Session `search_path`)** because it provides mathematical data isolation at the PostgreSQL catalog level, simplifies backup and archival per sponsor, enforces 21 CFR Part 11 triggers natively per table, and integrates seamlessly with Cadence's SQLAlchemy async ORM infrastructure.
- **Justification:** Selected as primary architectural approach.

## 5. Consequences & Trade-offs

- **Positive:**
  - Absolute data isolation per sponsor tenant without risk of cross-tenant query contamination.
  - Automated trigger attachment guarantees Part 11 compliance from day zero.
  - Fast, scriptable provisioning with transactional rollback protection.
  - Zero schema drift between tenants due to centralized metadata discovery.
- **Negative:**
  - Multi-schema maintenance requires session `search_path` management in connection pooling.
  - PostgreSQL schema limit must be monitored in ultra-high tenant density configurations.

## 6. Implementation & Verification

- **Execution Engine:** Implemented `apps/execution/database/provision_tenant.py` providing `provision_tenant(tenant_id, connection_string, seed)`.
- **CLI Command:** Added `provision` command to `packages/cli/commands/db.py` (`cadence db provision --tenant <tenant_id> [--seed]`).
- **Automated Tests:** Implemented comprehensive test suite in `apps/execution/tests/test_tenant_provisioning.py` verifying schema creation, table initialization, multi-tenant isolation, regex validation, transactional rollback, and CLI invocation.
- **Requirements Traceability:** Linked to `@req:PRD-SYS-041` and `@req:PRD-SYS-053` and synced via `uv run python scripts/sync_gxp.py`.

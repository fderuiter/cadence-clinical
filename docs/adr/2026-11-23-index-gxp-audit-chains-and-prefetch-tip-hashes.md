# ADR-2649: Index GxP Merkle Audit Chains and Prefetch Chain Tip Hashes

- **Status:** Accepted
- **Date:** 2026-11-23
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-SYS-001, PRD-SYS-003

---

## 1. Context & Problem Statement

Every standalone regulated mutation routed through `GxPAuditInterceptor.SavingChangesAsync` (`packages/database/src/GxPAuditInterceptor.cs`) resolves the current Merkle chain tip for each modified `EntityName` using a correlated `NOT EXISTS` anti-join (`sub.EntityName == entityName && sub.PredecessorHash == e.BlockHash`) ordered by `Timestamp DESC, VersionIndex DESC, Id DESC`. Previously, `ExecutionDbContext` defined no `EntityName` or `PredecessorHash` index on `"AuditLogEntries"` beyond `IX_AuditLogEntries_tenant_id`, while `AuditLogEntryConfiguration` defined only `IX_audit_log_entries_EntityName_Timestamp` (`(EntityName, Timestamp)`) across the remaining 14 standalone service schemas. Both configurations forced $O(N^2)$ nested scans and temporary B-tree sorts as per-entity chains grew (`223,279.0 ms` at `50,000` rows on SQLite), and multi-entity `SaveChangesAsync` calls issued $K$ sequential tip round-trips across distinct `EntityName` chains.

## 2. Decision Drivers & Constraints

- **Tamper-Evident Merkle Chain Integrity (`PRD-SYS-001`, `PRD-SYS-003`, ADR-2446):** `GxPAuditInterceptor` must remain the sole writer of `AuditLogEntry` rows, preserving `AuditLogEntry.ComputeBlockHash`, `AuditLogEntry.NormalizeTimestamp`, `AuditLogChainVerifier`, fork detection without mutable head tables, and legacy table-name chain readability.
- **Sub-5ms Constant-Depth Tip Resolution:** Resolving any `EntityName` chain tip at `50,000+` rows must execute via B-tree index seeks without full-chain scans or explicit sort nodes on both PostgreSQL 18 and SQLite.
- **Single-Round-Trip Multi-Entity Saves:** A `SaveChangesAsync` call mutating $K$ distinct `EntityName` aggregates or `AuditActions` must issue one batched tip-prefetch query and chain subsequent mutations of the same `EntityName` in memory.
- **Cross-Provider Pure LINQ & Synchronized 18-Module Schemas (ADR-2536, ADR-2548):** Tip prefetching must translate cleanly across EF Core `InMemory`, `SQLite`, and `Npgsql` without raw SQL, and all 15 standalone module migrations and `docs/schema` snapshots must stay synchronized.

## 3. Options Considered

### Option 1: Mutable Chain-Head Pointer Table per Schema

- **Overview:** Introduce a separate mutable `audit_chain_heads(entity_name, tip_block_hash)` table updated on every `SaveChangesAsync` instead of resolving tips from `AuditLogEntry`.
- **Pros:**
  - Single-row primary-key lookup per `EntityName`.
- **Cons:**
  - Introduces a mutable state table alongside append-only `KA001`-guarded audit tables, obscures unreferenced branch tips when verifying tampered or forked chains under ADR-2446, and requires backfilling head rows across existing deployments.

### Option 2: Composite Sequence + Predecessor B-Tree Indexes and Batched `Concat` Tip Prefetching (Selected)

- **Overview:** Centralize `AuditLogEntryConfiguration.ConfigureChainIndexes` to declare `(EntityName ASC, Timestamp DESC, VersionIndex DESC, Id DESC)` and `(EntityName, PredecessorHash)` B-tree indexes across `AuditLogEntryConfiguration` and `ExecutionDbContext`, generate `IndexAuditLogEntryChains` migrations across all 15 standalone services, and update `GxPAuditInterceptor` to collect all pending `AuditLogEntryDraft`s up front, prefetch all distinct `EntityName` tips in one `AsNoTracking()` `Take(1)` `Concat` (`UNION ALL`) query, and chain subsequent entries for the same `EntityName` in memory.
- **Pros:**
  - Reduces `50,000`-row tip lookup latency from `223,279.0 ms` to `0.0052 ms` on SQLite and `0.529 ms` across 3 `UNION ALL` chains on PostgreSQL 18 using pure B-tree index seeks (`Index Scan` + anti-join `Index Scan` / covering index search) with zero sort nodes.
  - Collapses $K$ sequential tip reads per `SaveChangesAsync` into one batched query while preserving ADR-2446 append-only chain verification and `InMemory`/`SQLite`/`Npgsql` provider compatibility.
- **Cons:**
  - Maintains two composite B-tree indexes on `audit_log_entries` / `"AuditLogEntries"` across each standalone module schema, replacing the narrower `(EntityName, Timestamp)` index.

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Pairing the descending `(EntityName, Timestamp, VersionIndex, Id)` sequence index with `(EntityName, PredecessorHash)` directly matches the `ORDER BY ... DESC LIMIT 1` and `NOT EXISTS` anti-join predicates in `GxPAuditInterceptor`, eliminating quadratic scans and sequential $K$-query round-trips without introducing mutable chain-head tables or raw SQL.

## 5. Consequences & Trade-offs

- **Positive Impact:** Single-chain and multi-entity `SaveChangesAsync` latency stays flat at sub-millisecond B-tree probe depth regardless of audit history volume, and `ExecutionDbContext` preserves its `"AuditLogEntries"` table mapping, RLS policies, and `KA001` insert-only triggers while sharing `AuditLogEntryConfiguration.ConfigureChainIndexes`.
- **Negative Impact / Technical Debt:** Each insert into `audit_log_entries` / `"AuditLogEntries"` updates two composite B-tree indexes per standalone schema until standalone services consolidate onto the Clinical Kernel per-tenant `AuditEntry` store.
- **Mitigation Strategy:** `GxPAuditInterceptorMerkleChainTests`, `GxPAuditInterceptorMerkleChainIntegrationTests`, and `AuditChainPostgresRoundTripTests` assert single-command batched prefetching, `AuditLogChainVerifier.Verify` validity, and `EXPLAIN` index-seek plans without table scans or temporary sorts.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/database`, `docs/schema`, and standalone service migrations across `ctms`, `econsent`, `eisf`, `etmf`, `execution`, `fileshare`, `imaging`, `interop`, `knowledge`, `notifications`, `org`, `payments`, `quality`, `safety`, and `tickets`.
- **Verification Plan:** Verified via `MigrationUnitTests`, `SchemaDocumentationTests`, `GxPAuditTrailGenerationTests`, `GxPAuditInterceptorMerkleChainTests`, `GxPAuditInterceptorMerkleChainIntegrationTests`, `EfAuditLogWriterTests`, `AuditChainPostgresRoundTripTests`, `ServiceSchemaPostgresTests`, `pnpm cadence check`, and the `50,000`-row benchmark.

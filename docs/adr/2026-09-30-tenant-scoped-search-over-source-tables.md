# ADR-2484: Tenant-Scoped Search over the Source Tables

- **Status:** Accepted
- **Date:** 2026-09-30
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

The Clinical Canvas command palette and Search (Canvas doc PX-3) find any kernel object by code, title or concept,
and resolve codes such as MedDRA terms. Issue #4848 (decision D9) records the gaps:

- The kernel defines no search capability.
- A search index is a copy of identifiers and titles. A copy can leak: it can miss Row-Level Security, outlive a
  deletion or crypto-shredding, or index identity content the kernel otherwise keeps in the IdentityVault (K-IDN-1).
- Results have to be NodeRefs (ADR-2481), so the palette opens them like any other object.

## 2. Decision Drivers & Constraints

- **K-ID-2.** Every read of kernel data runs under the tenant's Row-Level Security.
- **K-IDN-1.** Identity layers other than the pseudonymous `ParticipantId` are revealed only with a
  `identity.reveal.<layer>` capability, and every reveal is audited.
- **Freshness.** A title or code that just changed must be found under its new value at once. The palette is
  where people go to find what they just created.
- **Codes in any language.** Terminologies carry codes (`10020772`, `C25298`) and labels in many languages, so
  stemming for one language would mis-match.
- **Requirements:** PRD-CRF-013 (site and tenant data isolation): a search never returns another tenant's object.

## 3. Options Considered

### Option 1: External search engine (OpenSearch, Elasticsearch)

- **Cons:**
  - ❌ A second copy of the data outside PostgreSQL, with its own tenant filter to get right and its own
    deletion path.
  - ❌ A new service to run and qualify.

### Option 2: A search projection table fed from domain events

- **Overview:** a `search_documents` table updated by a projector reading the outbox.
- **Cons:**
  - ❌ Still a copy: it lags the source, needs its own RLS, and must be purged when a source row is shredded.
  - ❌ Events carry the subject's NodeRef, not its title, so the projector reads the source anyway.
  - ❌ Code-system imports add thousands of entries in one command; projecting them doubles the write.

### Option 3: PostgreSQL full-text search over the source tables (selected)

- **Overview:** each searchable table gets a GIN expression index on its code and title. A search runs one
  `UNION ALL` query over the sources the actor may search, through a context bound to the tenant.
- **Pros:**
  - ✅ No copy: RLS, deletion and shredding apply as they do to the source, and results are never stale.
  - ✅ No new infrastructure.
- **Cons:**
  - ❌ Each new searchable kind adds a source definition and an index migration.
  - ❌ Ranking across kinds is simple (exact code first, then text rank).

## 4. Decision Outcome

- **Chosen Option:** Option 3.
- **Justification:** it keeps search inside the security boundary the kernel already enforces. The index "update
  path" is PostgreSQL's own index maintenance in the same transaction as the change, so there is no update path to
  get wrong.

### 4.1 Matching

- Text is reduced to its words, meaning runs of letters and digits in any script. It is lower-cased, capped at 8
  words, and each word matches as a prefix. The words are joined with AND and passed as a parameter to
  `to_tsquery('simple', …)`. Operators and punctuation never reach SQL.
- The document is `to_tsvector('simple', code || ' ' || title)`. The `simple` configuration does no stemming and
  no stop words, so codes and multilingual labels match as typed.
- A result whose code equals the whole search text (case-insensitive) ranks first. The rest are ordered by
  `ts_rank`, then title.
- `SearchRequest(text, kinds?, limit)` accepts a limit from 1 to 50 and can be restricted to NodeRef kinds.

### 4.2 Sources

Sources are an explicit allow-list in `KernelSearchStore`. A table is searchable only when it is listed.

| Source | NodeRef | Code | Title | Detail |
| --- | --- | --- | --- | --- |
| organizations | `organization` | code | name | |
| sites | `site` | site code | name | country |
| studies | `study` | study code | title | |
| concepts | `concept` (pinned to the concept version) | CDISC code | preferred label | |
| code_systems | `code_system` | | name | publisher |
| terminology codes (`code_system_entries`) | `code_system_version` the code belongs to | code | display | code system and version |
| standard_versions | `standard_version` | standard code | name | version |
| library_items | `library_item` | key | title | |

- Results are NodeRefs pinned to the version read (ADR-2481 §4.2). A terminology code refers to its code-system
  version and carries the code, because codes are not kernel objects of their own.
- **Identity content is excluded.** `people` and the IdentityVault (#4782) are not sources. A source that holds
  identifying content declares its `identity.reveal.<layer>` capability (`SearchSourceInfo.RequiredCapability`).
  It is searched only when a dry run (ADR-2483) permits that capability for the actor on the tenant. Reveal
  auditing stays with the vault's reveal operation (K-IDN-1).
- Participants, visits and observations become sources when their slices land (#4776, #4727). Their codes
  (`SubjectCode`) are pseudonymous, and their titles are never personal data.

### 4.3 Security

- The query runs through `KernelTenantDbContextFactory.Create(tenant)`, so the tenant's RLS applies to every
  source table. Each branch also filters `tenant_id` explicitly, as defence in depth and for the indexes.
- Per-reader masking of objects the reader may not see adopts the D1 contract (`INodeVisibility`, ADR-2471) when
  it lands, as the navigator does. Until read authorization exists (#4781), a tenant member can search the
  tenant's reference data, studies and sites, which is what the navigator already shows.

### 4.4 Out of scope

- A kernel HTTP endpoint waits for tenant resolution and read authorization in the kernel API host (#4780,
  #4781). Until then applications call `IKernelSearch` in-process.
- Fuzzy (typo-tolerant) matching needs `pg_trgm`. It can be added later as an extra index per source without
  changing the contract.

## 5. Consequences & Trade-offs

- **Positive Impact:** search that cannot leak across tenants or outlive its data, with no new infrastructure.
- **Negative Impact / Technical Debt:** each new source needs a migration for its index, and the `UNION ALL` grows
  with the number of sources. Kinds filters keep palette queries narrow.
- **Mitigation Strategy:** a test checks that every source's query expression is answered from its index, so a
  source cannot silently fall back to a sequential scan.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/kernel`:
  - `Cadence.Kernel.Abstractions/Search` (`IKernelSearch`, `SearchRequest`, `SearchHit`);
  - `Cadence.Kernel.Application/Search` (`KernelSearch`, port `IKernelSearchStore`, `SearchSourceInfo`,
    `SearchPlan`; `AddKernelSearch()`);
  - `Cadence.Kernel.Persistence/Search` (`KernelSearchStore`, `KernelSearchSql`; `AddKernelSearchStore()`);
  - migration `KernelSearchIndexes` (GIN indexes only).
- **Verification Plan:**
  - `KernelSearchTests`: text reduced to prefix words (operators stripped, any script); no words means no query;
    identity sources searched only with the reveal capability; kinds filter; limits validated; long text cut before
    codes and words; no store query when no source is left; the store refusing unknown sources.
  - `KernelSearchPostgresTests`:
    - studies, sites and organizations found by title words and exact codes;
    - no object from another tenant is ever returned (PRD-CRF-013);
    - terminology codes and concepts found with their NodeRefs;
    - code systems, standard versions and library items found with their detail, and exact codes matched in any case;
    - equal matches ordered by title, with the limit cutting the ordered list;
    - the migration creates every index, and every query expression uses its index.

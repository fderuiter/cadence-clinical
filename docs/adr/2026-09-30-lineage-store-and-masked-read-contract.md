# ADR-2471: Lineage Store and Masked Read Contract

- **Status:** Accepted
- **Date:** 2026-09-30
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

ADR-2255 decided that lineage lives in the kernel schema and is written in the command transaction. It left the
store, the write API and the read contract open (#4815). The Clinical Canvas Trace verb needs one way to read lineage
(#4840, Canvas decision D1). That read must say what a reader sees when an object on the path is blinded or in the
identity layer. If unreadable objects are dropped, a trace looks shorter than it is, and that misleads a reviewer or
an inspector.

Open questions:

1. How a node is named, and what "version" means for lineage.
2. How a handler declares inputs, and how the pipeline enforces K-LIN-1 and K-LIN-2.
3. The query shape, including depth, edge kinds and the bitemporal parameters.
4. What a masked object looks like, and who decides masking.

## 2. Decision Drivers & Constraints

- **Driver 1 (completeness):** an object and its lineage commit together (K-LIN-2). A derived object without inputs
  must be caught at write time, not by a later report.
- **Driver 2 (honest reads):** no read may make a trace look shorter or a graph look complete when it is not.
- **Driver 3 (time):** lineage reads take `(valid_at, known_at)` like every other kernel read (K-TIME-*).
- **Driver 4 (one reference format):** nodes are `NodeRef`s (ADR-2481), so a trace link, an event subject and a URL
  name an object the same way.
- **Requirements:** PRD-CRF-013 (site and tenant data isolation): lineage tables are tenant-scoped with forced
  Row-Level Security, and every read is bound to one tenant. PRD-MDR-006 (blinding constraints on data rendering): a
  reader who may not see an object gets a masked placeholder, never its identifier.

## 3. Options Considered

### Option 1: Edges keyed by pinned NodeRefs, written from the unit of work, read by recursive CTE with masking (Selected)

- **Overview:** `lineage_nodes` records each object version a command wrote. `lineage_edges` stores
  `(from, to, kind, activity_id, recorded_at, valid_from)`, where `from` and `to` are pinned NodeRefs. Handlers
  declare inputs with `IKernelUnitOfWork.LinkInput`. `IKernelLineage` walks the graph with one recursive CTE and asks
  the host's `INodeVisibility` which objects to mask.
- **Pros:**
  - ✅ Written in the command transaction with no extra round trip, and insert-only like the audit tables.
  - ✅ One indexed query per walk; no second datastore.
  - ✅ Masking is decided once per read, by the host that owns authority.
- **Cons:**
  - ❌ Very deep or very wide walks are bounded, not complete; the result says when it was cut off.

### Option 2: Derive lineage on read from provenance and object foreign keys

- **Cons:**
  - ❌ Rejected by ADR-2255: historical lineage would change when specifications change.

### Option 3: Omit unreadable objects and report a count

- **Cons:**
  - ❌ The path shape is lost. A reader cannot tell which edge the hidden object sat on, and a count still reveals
    less than a placeholder would.

## 4. Decision Outcome

- **Chosen Option:** Option 1.
- **Justification:** It meets K-LIN-1 and K-LIN-2 in the pipeline itself, reuses tenancy and insert-only
  enforcement, and gives the Canvas one read contract that never shortens a path.

### 4.1 Nodes and edges

- A lineage node is a pinned `NodeRef` (`kind:id@version`). The version is the one ADR-2481 §4.2 defines: the
  aggregate's `VersionIndex`, which the pipeline stamps. For a concept that is not deprecated, this equals its
  revision number, so a `ConceptRef` and its NodeRef name the same version.
- An edge runs from the upstream object (`from`) to the downstream object (`to`). It reads "`to` is &lt;kind&gt;
  `from`". The kinds are spec §9.6's `requiredBy`, `definedBy`, `collectedBy`, `derivedFrom`, `mappedBy`,
  `transformedBy`, `validatedBy`, `attestedBy`, `governedBy` and `supersedes` (`LineageEdgeKind`).
- `kernel.lineage_nodes` holds one row per object version a command wrote: kind, id, version, whether it is derived,
  and the activity. `kernel.lineage_edges` holds the edges, including `recorded_at` (transaction time) and an
  optional `valid_from` (valid time). Both tables are tenant-scoped with forced Row-Level Security, and both are
  insert-only (SQLSTATE `KA001`). An edge is stored once per pair and kind.
- Indexes: `(tenant_id, to_id, to_kind, to_version)` serves backward walks. The unique
  `(tenant_id, from_id, from_kind, from_version, to_id, to_kind, to_version, kind)` serves forward walks.

### 4.2 Writing (K-LIN-1, K-LIN-2)

- A handler declares each input with `context.Work.LinkInput(input, kind, validFrom?)`. The edge goes from `input`
  to the command's aggregate at the version the commit stamps. An input must name a version, and it may not be the
  aggregate itself or a later version of it.
- The pipeline writes the aggregate's node and edges in the command transaction, after provenance and before events
  (`CommitStage.LineageStaged`). A failure at any later stage rolls lineage back with the object.
- Every update also gets a `supersedes` edge from the previous version. The pipeline adds it, so version history is
  lineage without each handler repeating it.
- An aggregate class marked `[DerivedObject]` must have at least one inbound edge. When a handler writes one without
  inputs, the commit throws `K-LIN-1` and nothing is written. Declaring inputs when no object version is written
  (nothing changed, or a delete) also throws.
- When two commands update the same version, each writes that version's next node. A lineage duplicate on an update
  is therefore reported as a concurrency conflict, the same as the row-version check.
- The scheduled K-LIN-1 check (`KernelLineageCheckService`, hourly by default) visits each tenant. It reports derived
  object versions that have no inbound edge to `ILineageOrphanSink`. The kernel's default sink logs them. The Quality
  engine replaces it to raise ValidationFindings (#4812).
- First writers: `MapConcept` (a `ConceptMapping` is derived; it is `definedBy` the concept version and the
  code-system version) and every update (`supersedes`). Later slices add theirs, for example `collectedBy` for
  observations (#4729), `governedBy` for protocol context (#4775) and `mappedBy` for SDTM projections.

### 4.3 Reading

```
Lineage(node, direction, depth, edge_kinds, valid_at, known_at) → { start, nodes[], edges[], truncated }
```

- `IKernelLineage.TraceAsync(tenant, reader, LineageQuery)`. `direction` is `Backward` (toward inputs) or `Forward`
  (toward dependents). `depth` runs from 1 to 50 and defaults to 10. `edge_kinds` is optional. `known_at` defaults
  to now, and `valid_at` defaults to `known_at`.
- An edge is followed when `recorded_at <= known_at`, and `valid_from` is null or `valid_from <= valid_at`.
- A start with a version walks from that version. A start without one walks from every version of the object.
- Each node carries its fewest-edges distance from the start. Nodes are ordered by distance, then by the order in
  which the walk reached them.
- A walk returns at most 10,000 edges. `truncated` is true when the graph continues past `depth` or past that limit.
  The walk looks one edge further than it returns so that it can tell the difference.
- `DossierAsync(tenant, reader, node, depth, valid_at, known_at)` is the inspection-readiness query in spec §9.6. It
  runs a backward and a forward walk at the same time point, makes one visibility decision for both, and groups the
  objects by kind.

### 4.4 Masking (#4840, Canvas PX-20)

- Every lineage read names its reader (an `Actor`). The kernel asks the host's `INodeVisibility.MaskAsync(tenant,
  reader, nodes)` once per read. It returns the objects the reader may not see and a `MaskReason` for each:
  `NotPermitted`, `Blinded` (K-AUTH-4) or `IdentityVault` (K-IDN-1).
- A masked object stays in the result as `Masked(kind, reason)`. Its `Ref` is null, and its handle is `masked:n`,
  numbered in the order the read reaches masked objects. Edges connect handles, so the path keeps its length and the
  objects beyond a masked one are still reached.
- Masking hides the object, not one version of it: if any version is masked, every version in the result is masked.
- An edge that touches a masked object withholds its `activity_id`, because the activity names the object it wrote.
- The kernel registers no `INodeVisibility`. A host that resolves `IKernelLineage` without deciding visibility
  fails, so it can never serve unmasked lineage by default. Until read authorization (#4781) and the IdentityVault
  (#4782) land, a host implements the port from what it knows.
- The same shape (`MaskReason`, `INodeVisibility`) is the masking contract for every kernel read that can reach
  blinded or identity-layer content. The navigator (ADR-2481 §4.3) adopts it next.

### 4.5 Dependents and amendment impact (#4841, Canvas D2)

- `DependentsAsync(tenant, reader, DependentsQuery(node, depth, edge_kinds, valid_at, known_at))` is a forward walk
  that the kernel aggregates:
  - One entry per dependent object, at the latest version reached and the nearest distance.
  - Grouped by kind in ordinal order, each group with its count and its objects.
  - A total, and `truncated` from the walk.
- The start object is never its own dependent, including its later versions.
- Masked objects are counted and listed masked (§4.4). A screen that shows an impact count therefore shows the same
  number to every reader, and no screen counts dependents its own way.
- Amendment impact (K-PROT-2) is `IAmendmentImpact.ComputeAsync(tenant, reader, amended)`. It returns the dependents of
  every version of the amended version's parent, walked without passing through the fork itself, together with the
  amendment's declared impacted elements.
  - Other open amendments of the same parent are listed, because they are open proposals on the same content.
  - `ForkProtocolVersion` records the fork as `DerivedFrom` the parent version it copies.
  - Until protocol elements are nodes of their own (#4808), the parent version is the unit that changes. Impact then
    narrows to the objects that reference a changed element.

## 5. Consequences & Trade-offs

- **Positive Impact:**
  - Trace, dossier, amendment impact (#4841, K-PROT-2) and finding root cause (K-FIND-3) read one structure.
  - A writer cannot forget the inputs of a derived object.
  - A reader always knows when a result is masked or cut off.
- **Negative Impact / Technical Debt:**
  - One `lineage_nodes` row per command that writes an aggregate.
  - Wide graphs hit the 10,000-edge limit, and those reads come back truncated rather than complete.
  - Spec §14 test 9 and the full dossier contents need the objects the later slices add (CollectionSpec, SDTM and
    ADaM records, consent, delegation), so they are tested once those writers exist.
- **Mitigation Strategy:** a 10-million-edge benchmark (opt-in, below) tracks latency. A graph projection stays
  optional (spec §16) if measured need appears. First measurement, on a local PostgreSQL 16 with the graph described
  in the benchmark (21 traces each, as the application role):

  | Walk | Median | Max | Median edges returned |
  | --- | --- | --- | --- |
  | Backward, depth 5 | 9.6 ms | 424 ms (first, cold cache) | 62 |
  | Backward, depth 10 | 156 ms | 218 ms | 2,030 |
  | Forward, depth 5 | 6.4 ms | 36.5 ms | 62 |

## 6. Implementation & Verification

- **Affected Assemblies:**
  - `Cadence.Kernel.Abstractions`: `Lineage/` (`LineageEdgeKind`, `LineageDirection`, `DerivedObjectAttribute`,
    `LineageQuery`, `LineageGraph`, `LineageNode`, `LineageEdge`, `LineageDossier`, `DependentsQuery`,
    `DependentGroup`, `DependentsReport`, `IKernelLineage`, `ILineageOrphanSink`), `Protocol/IAmendmentImpact.cs` and
    `References/NodeVisibility.cs` (`MaskReason`, `INodeVisibility`).
  - `Cadence.Kernel.Application`: `IKernelUnitOfWork.LinkInput`, and the `MapConcept` and `ForkProtocolVersion`
    handlers.
  - `Cadence.Kernel.Domain`: `[DerivedObject]` on `ConceptMapping`.
  - `Cadence.Kernel.Persistence`: `Lineage/` (records, `KernelLineageStore`, `KernelLineageOrphanCheck`,
    `KernelAmendmentImpact`), the lineage
    stage in `KernelCommandTransaction`, `KernelLineageCheckService`, and migration `LineageStore`.
- **Tracked by:** #4815, #4840, #4841.
- **Verification Plan:**
  - `LineagePostgresTests`, as the application role:
    - Mapping edges share the command's activity, and updates write `supersedes`.
    - A failed commit writes no lineage, and a derived object without inputs is refused.
    - Backward and forward walks return the right distances.
    - A masked object in the middle keeps the path length and leaks no id, and masking one version masks the object.
    - Depth truncation, `(valid_at, known_at)`, the edge-kind filter, unversioned starts and the dossier each behave
      as described above.
    - Another tenant sees nothing, and the orphan check reports a derived version without inputs.
  - `KernelBackgroundJobsTests`: the lineage check is scheduled, and a host without `INodeVisibility` fails validation.
  - `KernelTenantIsolationPostgresTests` and `InsertOnlyTables_RejectUpdateDeleteAndTruncate` cover the new tables.
  - `DependentsPostgresTests`:
    - Dependents are grouped by kind and counted as objects, and masked dependents are counted and masked.
    - A versioned start excludes its own later versions, and depth and edge kinds limit the walk.
    - Amendment impact is the parent's dependents without the fork; a first version is refused.
  - `LineageBenchmarkPostgresTests` (opt-in, `CADENCE_LINEAGE_BENCHMARK=1`): 10,000,000 edges, reporting trace
    latency.

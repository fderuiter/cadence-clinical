# ADR-2474: Protocol Elements as Lineage Nodes

- **Status:** Accepted
- **Date:** 2026-09-30
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

ADR-2472 made a protocol version's content a typed design whose elements (objectives, endpoints, estimands, arms,
epochs, planned activities, collection specifications and items, rules) keep stable ids across versions. ADR-2471 §4.5
computes amendment impact (K-PROT-2) as the dependents of the amended version's parent. Until now the parent version
was the unit that changed, so an amendment that rewords one objective reported everything that depends on the
protocol, including data collected for endpoints the amendment does not touch. Simulation (ADR-2480 §4.6) reports its
dependents the same way.

The spec lists protocol elements among lineage nodes (§9.6) and gives the forward chain
`Objective → Endpoint → Estimand → PlannedActivity → CollectionSpec → Observation → …`. ADR-2472 §4.6 left
"protocol elements as lineage nodes" to a later slice of #4808.

Open questions:

1. What a protocol element's lineage identity is, so that every protocol version names the same object.
2. What its version is, so an element an amendment does not change keeps the lineage that hangs off it.
3. When the elements and their edges are written.
4. How amendment impact and simulation narrow to the changed elements.
5. Who may see an element, and where it sits in the zoom hierarchy.

## 2. Decision Drivers & Constraints

- **Driver 1 (one lineage):** elements are ordinary lineage nodes in `kernel.lineage_nodes` and `kernel.lineage_edges`
  (ADR-2471), read by the same trace, dossier and dependents reads. No second graph.
- **Driver 2 (stable across versions):** a downstream object that references `ep.1` in version 1 still references the
  same object in version 2 when the amendment leaves `ep.1` alone.
- **Driver 3 (K-LIN-2):** element nodes and edges are written in the approval's transaction.
- **Driver 4 (one count):** amendment impact and simulation keep calling the one dependents read, so they report the
  same objects (Canvas D2).
- **Requirements:** PRD-SYS-179 (differential, element-level changes on a protocol amendment): the impact report
  lists the elements an amendment adds, changes or removes, and what depends on them. PRD-CRF-013 (site and tenant
  data isolation) and PRD-MDR-006 (blinding constraints on data rendering): element nodes are tenant-scoped lineage
  rows, and a reader outside an element's study sees it masked.
- **Constraint:** no new table and no migration. `kind` is a string column, so a new node kind needs none.
- **Constraint:** the Canvas builds the expected schedule (K-TIME-7) from `ProtocolDesign`; its types stay as they are.

## 3. Options Considered

### Option 1: One node per study and element id, versioned by the protocol version that last changed it (Selected)

The node id is a name-based UUID of the study and the element id. An element's version is the number of the protocol
version that last added or changed it, by the kernel's structural diff.

- ✅ An unchanged element keeps its node and version, so its lineage carries over.
- ✅ Element versions are ordered and readable: version 4 of `obj.1` is the one protocol version 4 wrote.
- ✅ Nothing to store: the id is computed, and the version follows from the approved chain.
- ❌ The node id cannot be read back to the study; the owner comes from the lineage (§4.5).

### Option 2: One node per protocol version and element (`protocol-element:<version>/<id>`)

ADR-2472 §4.6 sketched this form.

- ✅ Simple to write.
- ❌ Every amendment writes every element again, and the lineage of an unchanged element stays on the old node.
  Impact would have to map old nodes onto new ones, which is the problem this ADR solves.

### Option 3: A `kernel.protocol_elements` table with its own rows

- ✅ Elements queryable by SQL with their text.
- ❌ Copies content the protocol version already holds under its hash, needs a migration, and the lineage still needs
  a node per element.

## 4. Decision Outcome

**Chosen Option:** Option 1.

### 4.1 Identity

- `NodeKinds.ProtocolElement = "protocol_element"`, bound to `ProtocolElementId`.
- `ProtocolElementId.For(study, elementId)` is a UUID version 8 (RFC 9562) from the first 16 bytes of
  SHA-256 of `cadence.protocol_element\n{study:D}\n{elementId}`, with the version and variant bits set. Element ids
  are unique within a version (K-PROT-1), so the pair is unique within a tenant.
- The design root is not an element.

### 4.2 Versions

- Along the approved chain from the first version to the one being approved, each element's version is the `Number`
  of the last version whose structural diff (ADR-2480 §4.6) added or changed it. A removed element is no longer part
  of the design. Numbers are unique per study, so two versions never write the same element version.
- A fork needs an approved parent (ADR-2452), so the chain is all approved.
- Sibling amendments of one parent each follow their own chain. Two of them that change the same element both
  supersede its version in the parent.

### 4.3 What approval writes

`ApproveProtocolVersion` computes the plan (`ProtocolElementLineage.Plan`) and declares it on the unit of work with
`IKernelUnitOfWork.IncludePart` and `LinkPart`. The pipeline writes it with the approved version's own node, in the
same transaction (K-LIN-2):

- A node for every element that this version adds or changes (`derived = false`).
- `GovernedBy` from the approved version (at its version index) to every element of its design, new or not.
- `Supersedes` from a changed element's previous version.
- The references between elements, wherever one side is new:
  - `RequiredBy`: objective → endpoint → estimand, planned activity → collection spec → item, and the endpoints,
    estimands and safety rules a data purpose names → the activity, spec or item.
  - `DefinedBy`: population, arms and interventions → estimand; criteria → population; intervention → arm;
    arm → cohort; epoch → planned activity.
- `DefinedBy` from the concept versions and code-system versions (lineage version 1, as the Concept Registry links
  them) that define a new element.

The pipeline refuses a part edge that touches no new part, parts without an object version to attach them to, and
a part that is the subject itself. Artifacts referenced by the design are not linked yet.

### 4.4 Amendment impact and simulation

- `IAmendmentImpact.ComputeAsync` diffs the amended content against its parent and reports the result as
  `AmendmentImpact.ChangedElements` (`changed_elements` over HTTP), sorted by element id. `ImpactedElements` stays the
  amendment's declaration, so a reviewer can compare the two.
- The dependents walk still starts at the parent version and still excludes the fork. From the start object it
  follows an edge into a protocol element only when the amendment changes or removes that element; an added element
  has no dependents in the parent. Every other edge is followed: other open forks through `DerivedFrom`, objects the
  parent governs as a whole, and everything downstream of a changed element, unchanged elements included.
- Simulation uses the same rule for its `Dependents` section. Its dependents engine is `lineage.dependents` version 2,
  because the output changes.

### 4.5 Visibility and zoom

- The authority evaluator scopes an element to the study of the protocol version that most recently governed it
  (`ProtocolElementOwner`), so grant-backed masking ([ADR-2457](2026-09-30-grant-backed-node-visibility.md)) shows it
  to readers of that study. An element with no governing version is on no scope path, so only a tenant-wide grant
  covers it.
- The navigator places an element under that protocol version, at its latest lineage version, labelled
  `Protocol element <element id>`.
- The web `NodeRef` mirror (`apps/web/src/canvas/nodeRef.ts`) lists the new kind.

## 5. Consequences & Trade-offs

- **Positive Impact:**
  - An amendment's impact names the objects that depend on what it changes, not everything under the protocol.
  - A forward trace from an objective reaches its endpoints and estimands, and later the data collected for them.
  - Readers compare the declared impacted elements with the ones actually changed.
- **Negative Impact / Technical Debt:**
  - Approval reads the version's ancestors and diffs each pair. Protocol chains are short, and approval is rare.
  - An element's node id cannot be reversed; visibility and zoom read the owner from the lineage.
  - An element removed and later re-added starts again without `Supersedes` from its old version.
- **Future Considerations:**
  - Downstream writers (collection specs in execution, mapping specifications, rules, consent forms) link to element
    nodes as they land, so impact reaches them.
  - The K-PROT-2 gate: approving an amendment needs its impact report attested as reviewed.
  - Artifacts referenced by the design as `DefinedBy` edges.

## 6. Implementation & Verification

- **Code:** `ProtocolElementId` and `NodeKinds.ProtocolElement` (`Cadence.Kernel.Abstractions`);
  `ProtocolElementLineage` and `ProtocolElementImpact` (`Cadence.Kernel.Application/Protocol`);
  `IKernelUnitOfWork.IncludePart` and `LinkPart`, written by `KernelCommandTransaction`; `KernelAmendmentImpact`,
  `KernelLineageStore`, `KernelSimulationInputs`, `KernelAuthorityStore`, `KernelScopeGraph` and
  `ProtocolElementOwner` (`Cadence.Kernel.Persistence`).
- **Tests:** `ProtocolElementLineageTests` (unit) cover ids, plans for a first version, an amendment and a chain, and
  the impact rule. `ProtocolElementLineagePostgresTests` cover what approval writes, narrowed amendment impact, and
  visibility and zoom. `KernelSimulationPostgresTests` and `SimulationTests` cover narrowed simulation dependents.
- **References:** spec §6.2 (K-PROT-1, K-PROT-2), §9.6; ADR-2471 §4.5; ADR-2472 §4.6; ADR-2480 §4.6; #4808.

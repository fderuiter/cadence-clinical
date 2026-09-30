# ADR-2590: Counts Limited to the Reader's Visible Scope

- **Status:** Accepted
- **Date:** 2026-09-30
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-CTMS-007 (study metrics), K-AUTH-4 (blinding), Canvas D1 and D2 (#4840, #4841)

---

## 1. Context & Problem Statement

ADR-2471 §4.4 keeps a masked object in a lineage result as `Masked(kind, reason)`, so a trace never silently shortens.
§4.5 applies the same rule to dependents: masked dependents are **counted and listed masked**, so every reader sees
the same impact number.

With grant-backed visibility (ADR-2457) that number leaks another site's data. A site's staff may read their study's
protocol versions, so they may ask for a protocol version's dependents or an amendment's impact. The report then
counts every enrollment and observation in the study, with the other sites' objects masked but still grouped under
their kind. A site coordinator learns the study-wide enrollment and data volume, which sponsors treat as confidential.

Once blinding masks objects (`MaskReason.Blinded`, K-AUTH-4), the same shape would leak allocation: dependents of an
arm or a treatment grouped by kind are per-arm counts. No component masks `Blinded` yet, so this is not exposed
today, but the contract must be right before one does.

ADR-2471 already rejected "omit and report a count" because a count reveals. Listing masked objects one by one
reveals the same count. Study Pulse indicators (ADR-2488 §4.7) already count only what the reader may see.

## 2. Decision Drivers & Constraints

- A number shown to a reader never depends on objects the reader may not see (K-AUTH-4, spec §9.3).
- A reader always knows when a result is incomplete, and why (Canvas D1: never silently shortened).
- One rule for every kernel aggregate: dependents, amendment impact, indicators and future projections.
- Traces keep their path shape where a path is what the reader asked for.

## 3. Options Considered

### Option 1: Keep counting masked objects (status quo)

- ✅ Every reader sees the same number.
- ❌ Leaks other sites' volume now and allocation once blinding lands.

### Option 2: Count the visible scope, report the rest only as "more exist" (selected)

- ✅ No number reveals a hidden object. Matches ADR-2488 §4.7.
- ❌ Two readers can see different counts for the same object. Each is correct for its reader.

### Option 3: Refuse aggregate reads that reach any masked object

- ✅ Nothing leaks.
- ❌ A site user could never see impact on their own site's objects.

## 4. Decision Outcome

Option 2. It amends ADR-2471 §4.4 and §4.5.

### 4.1 Aggregates

- `DependentsReport` groups and counts only the objects the reader may see. The masked remainder is reported as
  `hidden: { more_exist: true, reasons: [...] }`: the distinct `MaskReason`s and nothing else. There is no count, no
  kind and no handle.
- Traversal is unchanged: the walk still passes through masked objects, so visible objects beyond them are counted.
- `IAmendmentImpact` inherits this from dependents. `IndicatorView` (ADR-2488 §4.7) already follows it.
- Any new kernel read that returns a count or a group size follows it too.

### 4.2 Traces

- `TraceAsync` and `DossierAsync` keep masked placeholders so a path keeps its length (ADR-2471 §4.4).
- A masked placeholder whose reason is `Blinded` carries no kind (`kind: null`), so a blinded trace cannot be grouped
  by kind.
- Masked siblings reached from the same node over the same edge kind collapse into one placeholder that stands for
  "one or more", so a trace's fan-out does not reveal a count. Masked objects on the way to a visible object stay
  separate, because the path needs them.

### 4.3 Oversight reads

A study-wide number (an unblinded QTL review, a sponsor's impact assessment) comes from a reader whose grants cover the
whole study. It never comes from widening another reader's result.

## 5. Consequences & Trade-offs

- **Positive:** no kernel number leaks hidden objects, and the rule is the same everywhere.
- **Negative:** impact counts differ by reader. The Canvas labels a partial count as such.
- **Mitigation:** the `more_exist` flag and reasons tell the reader to ask someone with wider access.

## 6. Implementation & Verification

- **Affected:** `Cadence.Kernel.Abstractions/Lineage` (`DependentsReport`, `LineageNode.Kind` nullable),
  `Cadence.Kernel.Persistence/Lineage/KernelLineageStore`, the kernel API responses, the generated web client and
  Canvas Impact (PX-6). No migration.
- **Verification:** dependents and impact tests for a site-scoped reader:
  - other sites' objects are not counted;
  - `more_exist` and the reasons are reported;
  - a visible object beyond a masked one is still counted.

  Trace tests for collapsed masked siblings and kind-less blinded placeholders.

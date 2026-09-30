# ADR-2481: Public NodeRef and Kernel-Computed Zoom Path

- **Status:** Accepted
- **Date:** 2026-09-30
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Every selectable object in the Clinical Canvas needs one reference that works in a URL, the command palette, a
trace link and an event (Canvas doc PX-2). The study also has to zoom from the portfolio down to a single
observation (PX-1). Issue #4846 (decision D7) records the gaps:

- The kernel has strongly-typed UUIDv7 identifiers (spec §3.2), but no public form that says *what kind* of object
  an id names, or which version of it. A bare UUID in a URL cannot be resolved without guessing the table.
- The scope path in spec §9.3 stops at `study_site`. The UI also needs participant, visit, assessment and
  observation levels.
- A participant in a decentralised trial may have no responsible study site (ICH E6(R3) Annex 2). A zoom path
  that assumes every participant sits under a site would fail for them.

## 2. Decision Drivers & Constraints

- **One string per object version.** A reference that has two spellings breaks links, caching, deduplication
  and audit comparison.
- **Opaque identifiers (§3.2).** Clients must not parse meaning out of ids. Human codes (`StudyCode`,
  `SiteCode`, `SubjectCode`) are for display and resolution, never keys.
- **K-EVT-1.** Anything added to the event envelope is additive.
- **K-TRUTH-1 in the UI.** The zoom path is computed by the kernel, not reconstructed in the client (Canvas doc §8).
- **Authority is not structure.** Where an object appears in the navigator must not decide who may see or act on
  it (spec §9.3, UX-6).
- **Requirements:** PRD-CRF-013 (site and tenant data isolation): every navigation read is tenant-bound.

## 3. Options Considered

### Option 1: Bare UUIDs everywhere

- **Cons:**
  - ❌ The reader has to know the kind from context. Palette results, trace links and events mix kinds.
  - ❌ No way to pin a version.

### Option 2: URL-style paths (`/studies/{id}/sites/{id}/participants/{id}`)

- **Cons:**
  - ❌ Encodes the hierarchy into the reference. A participant moving to another site, or having none, changes or
    breaks its reference.
  - ❌ Objects outside the study hierarchy (organizations, people, standards content) need a second scheme.

### Option 3: Typed reference `kind:id@version` with a kernel-computed zoom path (selected)

- **Overview:** a flat reference naming the kind, the UUID and optionally the version. The hierarchy is not part
  of the reference; the kernel computes the zoom path from it on request.
- **Pros:**
  - ✅ Stable when the hierarchy changes, and works for every kind.
  - ✅ Reads naturally in logs, events and URLs (`:` and `@` are legal in URL path segments, RFC 3986 `pchar`).
- **Cons:**
  - ❌ Showing a breadcrumb needs one kernel call.

## 4. Decision Outcome

- **Chosen Option:** Option 3.
- **Justification:** it keeps references stable and uniform, and puts the hierarchy where the facts are.

### 4.1 Grammar

```
node-ref = kind ":" id [ "@" version ]
kind     = %x61-7A *( [ "_" ] ( %x61-7A / DIGIT ) )      ; lower snake_case, at most 48 characters, registered
id       = 8HEXLC "-" 4HEXLC "-" 4HEXLC "-" 4HEXLC "-" 12HEXLC   ; lower-case UUID, never all zeros
version  = %x31-39 *DIGIT                                 ; positive, no leading zero, fits a signed 64-bit integer
```

Example: `study_site:0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d6e@3`.

- Parsing is strict. Upper-case hex, braces, missing hyphens, unregistered kinds, zero or zero-padded versions and
  surrounding whitespace are rejected, so each object version has exactly one string.
- `kind` values are registered in `Cadence.Kernel.References.NodeKinds`, one per strongly-typed identifier. A test
  fails when an identifier type in `Cadence.Kernel.Abstractions` has no kind, so every new aggregate registers one.
  Current kinds: `tenant`, `organization`, `person`, `site`, `study`, `study_site`, `participant`,
  `protocol_version`, `observation`, `attestation`, `tenant_membership`, `code_system`,
  `code_system_version`, `concept`, `concept_mapping`. Lineage node kinds from spec §9.6 (`collection_spec`,
  `mapping_specification`, `validation_finding`, `proposal`, …) are registered by the slices that add them.

### 4.2 Versions

- `@version` is the object's version index: `VersionIndex` for current-state aggregates, which the command pipeline
  stamps, and the recorded-row sequence for bitemporal facts.
- Without `@version`, the reference means "this object as of the read's `(valid_at, known_at)`". A read returns the
  summary with its `Ref` pinned to the version it found, so the caller always learns which version it saw.
- Content-addressed objects (a `ProtocolVersion`) are immutable, so their id already names one content. A version
  suffix on them is allowed but carries no extra meaning.
- Current-state aggregates cannot be read at an earlier version. For them a versioned reference resolves to the
  current version, and the returned `Ref` shows the difference. History of such objects comes from the audit trail
  (Canvas PX-12).

### 4.3 Zoom path

The kernel computes the presentation path from a reference (`IKernelNavigator.GetZoomPathAsync`):

```
tenant (portfolio) → study → [country] → study_site → participant → clinical event (visit) → observation
tenant (portfolio) → organization → site
```

- A participant sits under its **responsible study site** when it has one, and **directly under its study** when
  it has none (`ZoomRules.ParticipantParent`). No level assumes that activity happens at a site.
- `country` is a grouping level, not an object: it groups a study's sites by the country recorded on the site.
  It has a label and no reference.
- The path is presentation only. It never grants, implies or narrows authority. What an actor may read or do comes
  from Grants and Delegations (§9.3), and an object that the actor may not read will be returned masked under the
  D1 contract (#4840), never silently dropped from the path.
- Levels below participant (clinical event, assessment, observation) are added by the slices that map those
  objects (#4776, #4727). The path algorithm does not change; each slice supplies the parent of its kind.

### 4.4 Summaries and children

- `IKernelNavigator.ResolveAsync(tenant, ref)` returns a `NodeSummary` (reference pinned to the version read, title,
  human code, status). This is the summary card for the inspector, palette and trace links.
- `IKernelNavigator.GetChildrenAsync(tenant, ref)` returns the object's zoom children, each with the number of its
  own children when that level is queryable, and `null` when it is not yet (for example participants per study
  site before #4776). The navigator never shows an invented count.
- All three reads are tenant-bound and read-only.

### 4.5 Where NodeRefs appear

| Place | Rule |
| --- | --- |
| Event envelope | `subject`: the aggregate the command changed, pinned to its new version. Additive (K-EVT-1). Null for events about no aggregate, such as `DecisionDenied`. |
| Lineage edges | `from` and `to` are NodeRefs (spec §9.6). The lineage store (#4815) persists them. |
| Web URLs | The canonical object URL is `/n/{node-ref}` in the web app. The app asks the kernel for the zoom path and renders the object in its study context. |
| Kernel HTTP API | `GET /api/v1/kernel/nodes/{node-ref}`, `…/path` and `…/children`. These are added when the kernel API host resolves the caller's tenant (#4780) and read authorization (#4781); until then the navigator is used in-process (ADR-2244). |
| Palette, search, activity stream, explanations | Results and entries carry NodeRefs (D3, D5, D9). |

## 5. Consequences & Trade-offs

- **Positive Impact:** one reference format across the UI, events and lineage. Stable links for participants whose
  site changes or who have no site. A breadcrumb that matches the kernel's facts.
- **Negative Impact / Technical Debt:**
  - UUIDv7 ids carry their creation time, so anyone holding a link to a participant can read when that record was
    created. The spec accepts UUIDv7 for all ids (§3.2), and links are only shown to people who can read the
    object.
  - Not-found and not-permitted are indistinguishable today because read authorization does not exist yet. The D1
    masking contract (#4840) makes an unreadable object resolve to "masked", which avoids a not-found versus
    forbidden oracle.
- **Mitigation Strategy:** `NodeKinds` coverage is enforced by a test. HTTP exposure waits for tenant resolution
  and read authorization rather than shipping without them.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/kernel`:
  - `Cadence.Kernel.Abstractions/References` (`NodeRef`, `NodeKinds`) and `/Navigation` (`IKernelNavigator`,
    `NodeSummary`, `ZoomStep`, `ScopeChild`);
  - `EventEnvelope.Subject`, filled by `KernelCommandTransaction`;
  - `Cadence.Kernel.Application/Navigation` (`KernelNavigator`, `ZoomRules`, port `IKernelScopeGraph`);
  - `Cadence.Kernel.Persistence/Navigation` (`KernelScopeGraph` over tenants, organizations, sites, studies and
    study sites).
- **Verification Plan:**
  - `NodeRefTests`: canonical round trip including JSON, rejection of every non-canonical form, typed binding, and
    a kind for every kernel identifier type.
  - `KernelNavigatorTests`: participant path through study site and country, a site-less participant directly
    under its study, unknown references, children, and broken or cyclic hierarchies reported.
  - `KernelNavigationPostgresTests`: summary, path, children with counts and no cross-tenant reads against
    PostgreSQL, and events carrying the changed aggregate's NodeRef in the outbox envelope.

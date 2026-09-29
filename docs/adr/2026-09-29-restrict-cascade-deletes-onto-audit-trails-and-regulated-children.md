# ADR-2258: Restrict Cascade Deletes onto Audit Trails and Regulated Child Records

- **Status:** Accepted
- **Date:** 2026-09-29
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Issue #4764 found that the EF Core configurations of the fileshare, etmf, ctms and quality services mapped
every child of a regulated parent with `OnDelete(DeleteBehavior.Cascade)`. Deleting a `FileRecord`, `TmfDocument`,
`QualityDeviation` or `CtmsInvestigatorGrant` row would therefore have made PostgreSQL silently delete its audit logs
(`FileAuditLog`, `PlaybackAuditLog`), its QC transition history (`TmfDocumentQcTransition`) and its regulated
children (RCA/CAPA records, payment milestones, payables, budget lines, share grants, guest links, expiration alert
state). 21 CFR Part 11 §11.10(e) requires audit trails to be retained at least as long as the records they describe
(PRD-SYS-001), and ALCOA+ requires records to be enduring. A database rule that erases them on a parent delete breaks
both, whether the delete is issued by application code, a DBA or a script.

## 2. Decision Drivers & Constraints

- **Driver 1 (audit durability):** no delete path, including raw SQL, may remove an audit or history row as a side effect.
- **Driver 2 (fail loudly):** an attempt to delete a regulated parent that still has children must fail, not succeed partially.
- **Driver 3 (regression safety):** new audit/history entities must not reintroduce the problem unnoticed.
- **Constraint:** EF Core 10 + Npgsql; schema changes ship as EF migrations per service.

## 3. Options Considered

### Option 1: `DeleteBehavior.Restrict` on every regulated child FK, plus an architecture sentinel (Selected)

- **Overview:** Every FK from an audit log, transition, history or regulated child to its parent uses `Restrict`
  (`ON DELETE RESTRICT`). An architecture test builds each service's DbContext model and fails if any entity whose CLR
  name ends in `AuditLog`, `Transition` or `History` is the dependent side of a cascade delete.
- **Pros:**
  - ✅ The database rejects the parent delete; audit rows cannot disappear as a side effect.
  - ✅ Enforced for every client, not only EF Core.
  - ✅ The sentinel catches regressions at test time.
- **Cons:**
  - ❌ Any future hard delete of a parent must deal with its children explicitly. In a regulated system that is intended: retire or soft-delete instead.

### Option 2: `DeleteBehavior.SetNull`

- **Overview:** Keep child rows but null their parent key.
- **Cons:**
  - ❌ Orphans the audit trail from the record it describes, which breaks attributability; the FKs are required columns.

### Option 3: Keep `Cascade` and rely on application code never hard-deleting

- **Overview:** Leave the schema unchanged and forbid hard deletes by convention.
- **Cons:**
  - ❌ The database can still erase audit trails; no protection against scripts or future code.

## 4. Decision Outcome

- **Chosen Option:** Option 1.
- **Justification:** It is the only option that makes audit-trail loss impossible at the database layer while
  keeping the audit rows attributable to their parent.

Rules that implement the decision:

1. Audit-log, transition, history and regulated-child relationships use `DeleteBehavior.Restrict`.
2. Regulated parents are retired or soft-deleted with a reason for change, never hard-deleted. When this decision
   was made, no repository, endpoint or service in fileshare, etmf, ctms or quality deleted these parents.
3. `SetNull` stays acceptable only for optional cross-links between sibling children (for example
   `QualityCapaRecord.RcaId`, `CtmsInvestigatorPayable.MilestoneId`), where the child row itself is kept.

## 5. Consequences & Trade-offs

- **Positive Impact:** Deleting a regulated parent that has children now fails with a foreign-key violation instead of
  silently erasing its audit trail.
- **Negative Impact / Technical Debt:** Retiring a regulated parent now needs an explicit status change (soft delete)
  rather than a row delete; none of these services deletes them today.
- **Mitigation Strategy:** The architecture sentinel guards the audit/history naming convention; code review guards
  other regulated children.

## 6. Implementation & Verification

- **Affected Assemblies:** `Cadence.Apps.Fileshare`, `Cadence.Apps.Etmf`, `Cadence.Apps.Ctms`, `Cadence.Apps.Quality`,
  `Cadence.ArchitectureTests`.
- **Tracked by:** #4764.
- **Verification Plan:**
  - Migrations `RestrictAuditTrailCascadeDeletes` for fileshare, etmf, ctms and quality drop and re-add the affected FKs with `ON DELETE RESTRICT`.
  - Architecture test `AuditTrailEntities_MustNotBe_DependentOfCascadeDelete` (PRD-SYS-001) fails on the previous configuration and passes on this one.

# ADR 2026-07-25: Data-Driven Expected Document Lists (EDLs) & Completeness Tracking

## Status
Accepted

## Context
The electronic Trial Master File (eTMF) requires a robust validation mechanism to ensure all essential regulatory documents are archived before transitioning to key study milestones (e.g., INITIATION, CONDUCT, CLOSEOUT).
Previously, the platform checked completeness using hardcoded rules and milestone mappings. This rigid approach made it extremely difficult to adapt expectations to specific sites or custom protocol timelines without code changes, posing compliance and validation overhead.

## Decision
Introduce an `ExpectedDocument` model in the relational/SQLite store with indexed fields and Part 11 audit trails, alongside list/CRUD APIs.
This establishes a compliant, scalable, and fully configurable backbone that completely replaces hardcoded logic. By indexing keys like `study_id`, `site_id`, and `milestone`, performance is maintained, and site-scope expectations are combined dynamically during completeness checking.

## Alternatives Considered
### Option 1: Retain Hardcoded Ladder
Hardcode the expectations as Python dictionaries within the main service file.
- ❌ Violates data-driven principles; requires code changes for any configuration modification.
- ❌ No support for site-specific requirements or dynamic user-defined expectations.

### Option 2: Data-Driven EDL Reference Database Model (Selected)
Introduce an `ExpectedDocument` model and API.

## Trade-offs
### Pros
- ✅ Greater developer velocity, easier clinical configuration management, and instant compliance checking on milestone transitions.
- ✅ Site-scope resolution combines study-scope and site-scope expectations seamlessly.
- ✅ Fully compliant with Part 11 auditing by tracking change justifications and incremental versions on expectations themselves.

### Cons
- ❌ Slightly higher query complexity during completeness evaluations. Added database table and CRUD routes requiring RBAC security to prevent unauthorized definition changes.
- ❌ Mitigated by inline RBAC checks blocking inspector roles from mutating EDL rows, and the system logging every EDL mutation (`EDL_UPDATE`) or view (`EDL_VIEW`, `COMPLETENESS`) action to the immutable TMFAuditLog.

# Feature Specification: Cadence Knowledge & Support Hub

## Problem Statement

Clinical research operations involve diverse cross-functional personas (`super_admin`, `sponsor_designer`, `site_crc`, `cra_monitor`, `data_manager`, `auditor`) executing complex regulatory, protocol authoring, eCRF data capture, and monitoring workflows across the Cadence platform. Users encounter workflow friction and procedural ambiguity when navigating complex clinical screens (e.g., Schedule of Activities matrix, dynamic visit enrollment, query discrepancy lifecycles).

Furthermore, operational guidance documents and SOPs in clinical trials are quality system records governed by **21 CFR Part 11**, **ICH GCP E6(R2)**, and **GxP standards**. Regulatory compliance strictly prohibits mutable in-place editing of approved guidance, requires non-destructive retention of historical versions, mandates four-eyes approval workflows, and requires tamper-evident audit trails. A unified, role-aware, and GxP-compliant knowledge repository and support hub is needed to provide in-app contextual assistance while ensuring regulatory integrity.

## Solution

The **Cadence Knowledge & Support Hub** (`apps/knowledge/`) is a dedicated microservice providing an in-app, GxP-compliant, role-aware knowledge base and contextual help system for the Cadence Clinical Research Platform. 

Key capabilities include:
1. **Two-Tier Controlled Document Model**: `KnowledgeArticle` manages operational metadata and active published pointers, while `KnowledgeArticleVersion` stores immutable snapshot content (Markdown source, cached HTML, and full-text search vectors).
2. **Strict Seven-State GxP Lifecycle**: Enforces `DRAFT` → `IN_REVIEW` → `APPROVED` → `PUBLISHED` → `SUPERSEDED` / `ARCHIVED` (with `REJECTED` reversion) and four-eyes review controls.
3. **Role-Aware Content & Category Taxonomy**: Hierarchical category tree with persona-level visibility filtering across platform roles.
4. **In-Page Contextual Help Mapping**: Route pattern and persona matching (`/ecrf/*`, `/mdr/*`) dynamically surfacing relevant articles in an interactive drawer.
5. **Decoupled Ticketing Integration**: Full interoperability with `apps/tickets/` for operational issue reporting and dual-target SLA resolution.
6. **21 CFR Part 11 Audit Trail**: Immutable audit ledger guarded by SQLAlchemy ORM session hooks and chained to SHA-256 cryptographic digest logs.

## User Stories

1. As a **super_admin**, I want to author new knowledge base articles in Markdown, so that platform SOPs and user manuals can be centrally managed.
2. As a **super_admin**, I want to organize articles into a multi-tier category hierarchy, so that users can navigate structured guidance by functional domain.
3. As a **super_admin**, I want to restrict category visibility to specific personas (e.g. `site_crc,cra_monitor`), so that operational teams only see relevant SOPs.
4. As a **super_admin**, I want to submit drafted articles for review, so that technical and quality reviewers are notified to evaluate content.
5. As a **sponsor_designer**, I want to review submitted articles authored by other team members, so that quality and clinical accuracy are verified before publication.
6. As a **super_admin**, I want the platform to enforce the four-eyes principle, so that the editor who last saved an article cannot also approve it.
7. As a **super_admin**, I want to provide a formal `reason_for_change` on approval and publication transitions, so that 21 CFR Part 11 audit trails are satisfied.
8. As a **super_admin**, I want to publish an approved article version, so that it becomes immediately active and visible to authorized platform personas.
9. As a **super_admin**, I want publishing a new version of an existing article to auto-supersede the currently published version, so that users never see conflicting active versions.
10. As a **site_crc**, I want to browse published knowledge base articles relevant to my site operations role, so that I can follow standardized eCRF entry procedures.
11. As a **site_crc**, I want to open an in-page contextual help panel on `/ecrf`, so that the system immediately surfaces the exact SOP for dynamic subject enrollment and visit entry.
12. As a **cra_monitor**, I want to access contextual monitoring guidance while reviewing query discrepancies, so that protocol deviation criteria are applied consistently.
13. As a **data_manager**, I want to search across knowledge articles by keyword or tag, so that I can quickly retrieve medical coding and edit-check guidelines.
14. As a **site_crc**, I want to submit a support ticket directly from the contextual help drawer when an article does not resolve my issue, so that site operational blockers are escalated to the sponsor.
15. As an **auditor**, I want to inspect historical, superseded, and archived versions of any guidance article, so that I can verify the exact SOP that was in effect during historical study visits.
16. As an **auditor**, I want my read access on controlled guidance documents to be recorded in the GxP audit trail (`READ_BY_AUDITOR`), so that regulatory inspection compliance is verifiable.
17. As an **auditor**, I want to verify the SHA-256 cryptographic digest chain on article audit records, so that tamper-evidence of lifecycle actions is guaranteed.
18. As a **developer**, I want PostgreSQL full-text search indexing to fall back gracefully to standard text matching under SQLite test harnesses, so that fast local tests run without failure.

## Implementation Decisions

### 1. Two-Tier Controlled Document Data Model
- **`KnowledgeCategory`**: Self-referential hierarchy via `parent_id` (`ondelete="SET NULL"`), unique `slug` and `name`, `persona_visibility` string/JSON array, and standard GxP audit fields.
- **`KnowledgeArticle`**: Entity head holding UUID, `slug`, `title`, `category_id` (`ondelete="RESTRICT"`), `status`, `version_index`, `version_label`, `current_published_version_id`, `tags` JSON array, authorship fields (`author_user_id`, `last_edited_by`, `approved_by`), and soft-delete flag `is_deleted`.
- **`KnowledgeArticleVersion`**: Immutable snapshot holding `article_id` (`ondelete="RESTRICT"`), `version_index`, `version_label`, `status_at_snapshot`, `body_markdown`, `body_html`, and `search_vector` (`tsvector`).
- **`KnowledgeArticleAuditLog`**: Immutable audit ledger with GxP fields and ORM `before_flush` hook blocking `dirty`/`deleted` operations.
- **`ContextualHelpMapping`**: Route mapping holding `route_pattern`, `persona`, `article_id` (`ondelete="CASCADE"`), `section_anchor`, `priority`, and `is_active`.

### 2. Microservice Decoupling
- `apps/knowledge/` owns articles, categories, versions, help mappings, and article audit logs.
- Operational support ticketing is delegated to `apps/tickets/` via authenticated internal gateway calls, maintaining strict microservice boundaries per `AGENTS.md`.

### 3. Draft Granularity & Version Immutability
- A single working draft record is updated in `KnowledgeArticleVersion` while the article is in `DRAFT` status.
- Upon transitioning to `APPROVED`, the version record is permanently locked.
- Editing an approved or published article increments `version_index` and initiates a new draft record.

### 4. Contextual Help Matching Algorithm & Specificity Scoring
- In-page help queries filter by route pattern match (exact match, prefix wildcard `/ecrf/*`, or parameter wildcard `/mdr/:studyId/*`) and `(persona = :role OR persona IS NULL)`.
- Hierarchical Specificity Resolution:
  1. Route pattern specificity (Exact > Parameterized > Longest prefix wildcard > Global `/*`).
  2. Persona specificity (Exact persona match > Universal wildcard `NULL`).
  3. Administrator-configured `priority` integer (ascending).
  4. Recency tie-breaker.
- Returns 1 primary spotlight article + up to 3 secondary related guides.

### 5. Frontend Help Panel UX & Fallback Escalation
- Global slide-in right drawer (`width: 420px`) anchored to `AppShell.vue` using Vanilla CSS design tokens.
- Lazy on-demand fetching when the drawer is toggled, with route+persona in-memory caching.
- Unmapped fallback pane with instant search, category directory links, and pre-populated ticket escalation to `apps/tickets/`.

### 6. PostgreSQL Full-Text Search with SQLite Dialect Guard
- `search_vector` on `KnowledgeArticleVersion` indexes `title`, `tags`, and `body_markdown` with a PostgreSQL `GIN` index.
- SQLAlchemy definitions use conditional dialect guards to allow execution against SQLite in unit test suites.

## Testing Decisions

### Test Strategy & Seams
1. **Repository & Service Layer Seam** (`apps/knowledge/application/article_service.py`):
   - Test complete 7-state lifecycle state machine (`DRAFT` → `IN_REVIEW` → `APPROVED` → `PUBLISHED` → `SUPERSEDED`/`ARCHIVED`/`REJECTED`).
   - Test four-eyes approval validation (`last_edited_by != actor_user_id`).
   - Test `reason_for_change` enforcement on regulated transitions.
   - Test auto-supersede trigger on publication of new article versions.
2. **Database ORM & Immutability Seam** (`apps/knowledge/infrastructure/models.py`):
   - Test `before_flush` hook raising `ValueError` on update or delete of `KnowledgeArticleAuditLog`.
   - Test foreign key constraints (`RESTRICT` on article/category deletion).
   - Test composite indexes and query execution.
3. **Presentation & Gateway Router Seam** (`apps/knowledge/presentation/routers/`):
   - Test role-based access control and persona visibility filtering.
   - Test contextual help resolution for various route patterns and persona contexts.
   - Test 21 CFR Part 11 eSignature validation via `GatewayAuthMiddleware`.

### Prior Art
- `apps/tickets/tests/test_tickets_service.py`
- `packages/testing/factories.py` and `packages/testing/security.py`

## Out of Scope

- Native ticketing database engine in `apps/knowledge/` (delegated to `apps/tickets/`).
- Interactive user helpfulness voting tables (`ArticleFeedback`) — deferred to post-MVP.
- Real-time collaborative Markdown editing via WebSockets — deferred.
- Machine-translation / multi-lingual localization of SOP text — deferred.

## Further Notes

- Traceable to system requirements `PRD-KNB-001` (Article Lifecycle) and `PRD-KNB-002` (Multi-Channel Notifications).
- Scaffolds ADR-2188 (`docs/adr/2026-08-21-knowledge-hub-two-tier-immutable-data-model.md`).
- All test functions must carry `@req:PRD-KNB-001` or `@req:PRD-KNB-002` docstrings for automated RTM sync via `scripts/sync_gxp.py`.


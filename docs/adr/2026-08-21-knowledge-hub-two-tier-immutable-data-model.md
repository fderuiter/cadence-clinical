# ADR-2188: Knowledge Hub Two-Tier Immutable Data Model

* **Status:** Accepted
* **Date:** 2026-08-21
* **Authors:** @fderuiter
* **Deciders:** @fderuiter
* **Requirement Reference:** PRD-KNB-001 | 21 CFR Part 11 | Trace-8

---

## 1. Context & Problem Statement

The Cadence Clinical Research Platform requires an in-app, GxP-compliant, role-aware Knowledge & Support Hub (`apps/knowledge/`) to serve standard operating procedures (SOPs), eClinical user manuals, and contextual workflow guidance to diverse clinical trial personas (`super_admin`, `sponsor_designer`, `site_crc`, `cra_monitor`, `data_manager`, `auditor`). 

Under 21 CFR Part 11 and ICH GCP E6(R2), operational guidance documents used by clinical site staff constitute quality system records. Consequently:
1. Approved regulatory article content must be permanently immutable;
2. Historical versions must be retained non-destructively for regulatory inspection;
3. Four-eyes authoring controls must be enforced;
4. The service must remain decoupled from operational ticketing (which resides in `apps/tickets/`).

This record formalizes the relational database schema, version snapshotting mechanics, foreign key deletion rules, and indexing strategy for `apps/knowledge/`. Reference requirements: `PRD-KNB-001` (Article Lifecycle & Two-Tier Immutability).

## 2. Decision Drivers & Constraints

* **21 CFR Part 11 & Non-Destructive Retention**: Approved and published SOP versions must never be overwritten, modified, or cascadingly deleted.
* **Read-Path Performance**: 99% of hub traffic is reading published articles and looking up in-page contextual help. Lookups must resolve in $O(1)$ without table scans across historical versions.
* **Microservice Decoupling**: Sibling database imports are prohibited per `AGENTS.md`. Knowledge base and ticketing domain data must remain strictly isolated.
* **Multi-Engine Dialect Compatibility**: PostgreSQL in production (with GIN full-text search) and SQLite for fast developer unit tests (`cadence test --fast`).

## 3. Options Considered

1. **Option A (Selected: Two-Tier Head + Immutable Version Snapshot)**: `KnowledgeArticle` acts as an operational entity head holding active lifecycle state, slug, category FK, current published version pointer (`current_published_version_id`), and authorship metadata, linked 1-to-many to `KnowledgeArticleVersion` holding frozen snapshot content (markdown, HTML, FTS vectors).
2. **Option B (Single-Table Version Rows)**: Every version edit creates a new independent row in `knowledge_articles` with a shared family ID.
3. **Option C (Colocated Knowledge & Tickets DB)**: Replicate or merge `apps/tickets/` schema directly inside `apps/knowledge/`.

## 4. Decision Outcome

Chosen option: **Option A (Two-Tier Head + Immutable Version Snapshot)** because it satisfies `PRD-KNB-001` with optimal read performance, strict immutability, and clean microservice boundaries.

### Key Architectural Specifications:
* **Microservice Boundary**: `apps/knowledge/` exclusively manages categories, articles, version snapshots, contextual help mappings, and audit logs. Support tickets are delegated to `apps/tickets/` (port 8009).
* **Two-Tier Schema Architecture**:
  - `KnowledgeCategory`: Adjacency list (`parent_id` FK with `ondelete="SET NULL"`), unique name/slug, and `persona_visibility` string.
  - `KnowledgeArticle`: Entity head tracking status, slug, category (`ondelete="RESTRICT"`), `version_index`, `version_label`, `current_published_version_id` pointer, `tags` JSON array, and four-eyes authoring fields (`author_user_id`, `last_edited_by`, `approved_by`).
  - `KnowledgeArticleVersion`: Immutable snapshots (`article_id` FK with `ondelete="RESTRICT"`), `status_at_snapshot`, `body_markdown`, `body_html`, and `search_vector` (`tsvector`).
  - `KnowledgeArticleAuditLog`: Immutable audit ledger with SQLAlchemy `before_flush` session event preventing updates/deletions, chained to `packages.security.audit_logger` SHA-256 digests.
  - `ContextualHelpMapping`: Maps `route_pattern` (wildcard/prefix) and `persona` to `article_id` (`ondelete="CASCADE"`) with priority-based resolution.
* **Draft Granularity**: Single working draft record per major version during `DRAFT` status; frozen permanently on `APPROVED` transition.

## 5. Consequences & Trade-offs

* **Positive**:
  - High-performance $O(1)$ reads for published articles via `current_published_version_id`.
  - Guaranteed 21 CFR Part 11 compliance with non-destructive version retention.
  - Strict database-level referential integrity preventing accidental deletion of regulatory assets.
  - Native full-text search capability with clean SQLite fallback for unit testing.
* **Negative**:
  - Requires service-layer orchestration to maintain atomic synchronization between `KnowledgeArticle.current_published_version_id` and new `KnowledgeArticleVersion` snapshots on publish events.

## 6. Implementation & Verification

* **Target Schema**: `apps/knowledge/infrastructure/models.py`
* **Target Domain Logic**: `apps/knowledge/domain/models.py` and `apps/knowledge/application/article_service.py`
* **Verification**: Test suite under `apps/knowledge/tests/` verifying lifecycle transitions, version snapshot immutability, 4-eyes enforcement, and search vector indexing. Traceable to `@req:PRD-KNB-001`.


# Cadence Clinical Plugin Rules

These guidelines are automatically applied when the `cadence-core` plugin is active.

## Stack & Standards
- Python 3.14+ with strict typing (Pydantic v2).
- FastAPI REST API-first architecture with HMAC gateway signatures (`packages.security.signing`).
- Async SQLAlchemy / SQLModel for PostgreSQL (`apps/execution`) with strict boolean filter pattern (`.is_(True)` / `.is_(False)` per E712).
- Async Neo4j Driver for clinical graph authoring (`apps/designer`).
- Vue 3 with vanilla CSS semantic tokens (no Tailwind CSS).
- Centralized test infrastructure via `packages.testing` (factories, in-memory repository fakes, security headers).
- Unified Cadence CLI (`uv run cadence ...`) for all dev, test, and diagnostic tasks.
- Mandatory 21 CFR Part 11 traceability: `@req:PRD-SYS-xxx` tags in test docstrings, synced via `uv run python scripts/sync_gxp.py`.
- Markdown & API Docs formatting: Wrap endpoint paths containing route parameters in code backticks in headings (`### `GET` `/api/v1/resource/{id}``) to prevent VitePress `markdown-it-anchor` custom ID collisions.

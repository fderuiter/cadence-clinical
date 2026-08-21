# 03: `apps/fileshare` Microservice Scaffold

**What to build:** A new FastAPI microservice, `apps/fileshare`, that will own all file-sharing concerns: file records (metadata, object store keys), share grants, and guest link tokens. This ticket delivers the skeleton: domain models, database schema, two foundational endpoints (presigned upload URL and presigned download URL), and gateway routing. All subsequent fileshare tickets build on top of this scaffold.

**Blocked by:** 01 — Object Storage Adapter (Expand). Can run in parallel with 02.

**Status:** ready-for-agent

- [ ] `apps/fileshare/` created with the standard Cadence microservice layout: `domain/`, `application/`, `infrastructure/`, `adapters/`, `ports/`, `presentation/`, `routers/`, `tests/`, `main.py`, `pyproject.toml`, `Dockerfile`.
- [ ] Domain models defined (Pydantic v2, strict typing, no `Any`):
  - `FileRecord` — `id`, `study_id`, `site_id`, `filename`, `mime_type`, `size_bytes`, `object_key`, `checksum_sha256`, `version_index`, `uploaded_by`, `uploaded_at`, `is_on_hold`, `created_at`, `created_by`, `reason_for_change`.
  - `ShareGrant` — `id`, `file_record_id`, `granted_to_user_id`, `granted_by_user_id`, `scope` (study/site/individual/folder), `permission_level` (enum: view/comment/download/upload_revision/reshare/approve/expire_revoke), `expires_at` (nullable), `revoked_at` (nullable), `created_at`, `created_by`, `reason_for_change`.
  - `GuestLink` — `id`, `file_record_id`, `token_hmac`, `expires_at`, `created_by`, `last_accessed_at`, `access_count`, `revoked_at`.
- [ ] SQLAlchemy/SQLModel models + Alembic migrations for the three tables above (PostgreSQL).
- [ ] `POST /api/v1/fileshare/files/upload-url` — authenticated endpoint returning a presigned multipart upload URL set from the object store. Requires `GatewayAuthMiddleware`.
- [ ] `GET /api/v1/fileshare/files/{file_id}/download-url` — returns a short-lived presigned GET URL; enforces that the caller has at least `view` permission on the file. Watermark applied server-side for `view`-only grants.
- [ ] Gateway (`apps/gateway`) routing table updated to proxy `/api/v1/fileshare/*` to the new service.
- [ ] Docker Compose service entry added for `fileshare`.
- [ ] Unit tests cover domain model validation; integration tests cover the two endpoints against an in-memory repository fake.
- [ ] `uv run ruff format .` and `uv run ruff check . --fix` pass with zero errors.

# 02: eTMF / eISF Storage Migration (Migrate + Contract)

**What to build:** Move all existing file content out of the PostgreSQL `tmf_documents._content` column (currently base64-encoded blobs) into the object store introduced in T-01. After migration, the `_content` column is dropped and the service reads all file bytes via the `StoragePort`. A dual-read fallback ensures zero downtime during the cutover: if a document's `object_key` is null, the service falls back to the legacy `_content` value.

**Blocked by:** 01 — Object Storage Adapter (Expand).

**Status:** ready-for-agent

- [ ] Alembic migration adds a nullable `object_key: str` column to `tmf_documents` (PostgreSQL) and the equivalent SQLite schema for dev/test.
- [ ] Data migration script (`scripts/migrate_etmf_blobs_to_s3.py`) iterates all rows where `object_key IS NULL`, decodes `_content` from base64, uploads to the object store via `StoragePort`, writes the returned key back to `object_key`, and clears `_content`. Script is idempotent and safe to re-run.
- [ ] `ingestion_service.py` updated to write new uploads to the object store (setting `object_key`) instead of `_content`.
- [ ] All read paths (export, watermark, preview, sign) updated to fetch bytes via `StoragePort.get_object(object_key)` if `object_key` is set, falling back to `_content` decode if not.
- [ ] A second Alembic migration (the "contract" step, in a separate PR) drops the `_content` column once the data migration script has been run in all environments.
- [ ] Integration tests verify round-trip upload → download via the object store.
- [ ] `uv run ruff format .` and `uv run ruff check . --fix` pass with zero errors.
- [ ] GxP sync run (`uv run python scripts/sync_gxp.py`) after any test changes.

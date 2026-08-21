# 01: Object Storage Adapter (Expand)

**What to build:** Introduce a `StoragePort` abstraction and a concrete MinIO/S3-compatible adapter into the platform so that any service can store and retrieve binary blobs via an object store, without breaking the existing eTMF database-blob flow. This is a pure "expand" step — nothing is migrated yet, nothing currently writing to the DB changes. The adapter sits ready to be used by the new `fileshare` microservice (T-03) and later consumed by the eTMF migration (T-02).

An Architecture Decision Record must be written documenting the move from in-DB blobs to object storage and the adapter/port pattern chosen.

**Blocked by:** None (can start immediately).

**Status:** ready-for-agent

- [ ] `packages/storage` (or `apps/fileshare/ports/storage_port.py`) defines a `StoragePort[T]` protocol with operations: `put_object`, `get_object`, `delete_object`, `generate_presigned_get_url`, `generate_presigned_put_url`, `generate_presigned_multipart_urls`, `complete_multipart_upload`.
- [ ] `S3StorageAdapter` implements `StoragePort` using `aiobotocore` or `boto3` with async wrappers; configurable via environment variables (`STORAGE_ENDPOINT_URL`, `STORAGE_BUCKET`, `STORAGE_ACCESS_KEY`, `STORAGE_SECRET_KEY`).
- [ ] `MinioStorageAdapter` (thin wrapper over `S3StorageAdapter` with path-style URLs) usable for local dev via Docker Compose.
- [ ] MinIO container added to `docker/docker-compose.yml` for local development.
- [ ] Adapter is covered by unit tests against a mock S3 client and an integration test against the MinIO container.
- [ ] ADR written at `docs/adr/` titled "Object Storage Adapter for Binary File Management" referencing the relevant PRD requirement ID.
- [ ] `uv run ruff format .` and `uv run ruff check . --fix` pass with zero errors.

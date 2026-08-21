# 08: Video Transcoding Pipeline (HLS/DASH)

**What to build:** When a video file finishes uploading (T-06), `fileshare` enqueues a background transcoding job. An FFmpeg worker transcodes the source video into an HLS (HTTP Live Streaming) package: a master manifest and multiple rendition playlists at different quality tiers (e.g. 360p, 720p, 1080p). The manifest and segments are stored back in the object store alongside the original. Playback audit events — play start, 25%/50%/75% completion checkpoints, and play end — are logged to the fileshare audit trail per viewer session.

**Blocked by:** 06 — S3 Multipart Presigned Client-Side Upload. Can run in parallel with 07.

**Status:** ready-for-agent

- [ ] `FileRecord` gains fields: `transcode_status` (enum: `NOT_REQUIRED` / `PENDING` / `IN_PROGRESS` / `READY` / `FAILED`), `hls_manifest_key` (nullable str — object store key of the `.m3u8` master manifest), `transcode_error` (nullable str).
- [ ] Alembic migration adds the new columns.
- [ ] On upload completion (`complete-upload` endpoint), if `mime_type` is a video type, `transcode_status` is set to `PENDING` and a transcoding task is enqueued (via an async task queue — Celery + Redis, or an `asyncio` background task for MVP).
- [ ] FFmpeg worker (`apps/fileshare/workers/transcode_worker.py`): downloads source from object store, runs FFmpeg to produce HLS at three quality tiers (360p/720p/1080p), uploads all segments and manifests back to object store under `hls/{file_record_id}/`, updates `FileRecord.hls_manifest_key` and `transcode_status`.
- [ ] FFmpeg and the worker run in a dedicated Docker container (`docker/fileshare-transcoder/`). `docker-compose.yml` updated.
- [ ] `GET /api/v1/fileshare/files/{file_record_id}/stream-url` — returns the presigned HLS manifest URL (short-lived, 5-minute TTL) when `transcode_status == READY`; returns `202 Accepted` with `transcode_status` when still pending.
- [ ] `POST /api/v1/fileshare/audit/playback` — accepts `{file_record_id, event: 'PLAY_START'|'PLAY_25'|'PLAY_50'|'PLAY_75'|'PLAY_END', source: 'internal'|'guest_link', session_id}`. Each call appended to the fileshare audit log with `user_id` (or guest link token hash), `timestamp`, and event type. Used by the frontend player (T-09) to emit checkpoints.
- [ ] Unit tests cover the worker state machine transitions; integration test runs a short test video (< 5s) through FFmpeg in CI.
- [ ] `uv run ruff format .` and `uv run ruff check . --fix` pass with zero errors.
- [ ] GxP sync run after test additions.

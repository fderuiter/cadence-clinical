# 06: S3 Multipart Presigned Client-Side Upload

**What to build:** Replace the existing in-memory file POST in `DocumentGrid.vue` with a direct-to-object-store multipart upload flow. The browser requests presigned part URLs from `fileshare`, uploads each chunk directly to the object store (bypassing the app server entirely), and signals completion. This enables large files — including videos that may be several gigabytes — to upload reliably with progress reporting and automatic retry on chunk failure.

**Blocked by:** 03 — `apps/fileshare` Microservice Scaffold.

**Status:** ready-for-agent

- [ ] `POST /api/v1/fileshare/files/upload-url` (already scaffolded in T-03) fully implemented: accepts `filename`, `mime_type`, `size_bytes`, `study_id`, `site_id`, `taxonomy_artifact_code` (optional); initiates an S3 multipart upload; returns `upload_id`, `file_record_id` (pre-created with status `UPLOADING`), and an array of presigned part URLs (one per chunk, default chunk size 10 MB).
- [ ] `POST /api/v1/fileshare/files/{file_record_id}/complete-upload` — accepts the list of `{part_number, etag}` objects returned by S3 for each completed chunk; calls `CompleteMultipartUpload`; transitions `FileRecord` status to `PENDING_SCAN` (to be picked up by T-11) or `READY` if scanning is not yet implemented.
- [ ] `POST /api/v1/fileshare/files/{file_record_id}/abort-upload` — aborts the S3 multipart upload and marks the `FileRecord` as `ABORTED`.
- [ ] Frontend upload composable `useChunkedUpload.js` in `apps/web/src/composables/`: orchestrates chunk splitting, parallel presigned URL requests, per-chunk XHR upload with progress events, retry (up to 3 attempts per chunk), and completion signal.
- [ ] `DocumentGrid.vue` upload modal updated to use `useChunkedUpload` instead of a single FormData POST. Shows per-file progress bar (bytes uploaded / total), chunk retry indicator, and cancel button.
- [ ] Upload supports all agreed MIME types: PDF, DOCX/XLSX/PPTX, images (JPEG/PNG/TIFF), video (MP4/WebM/MOV), audio (MP3/WAV/M4A), DICOM, ZIP, CSV.
- [ ] Integration test verifies the full upload → complete flow against the MinIO container.
- [ ] `uv run ruff format .` and `uv run ruff check . --fix` pass with zero errors.

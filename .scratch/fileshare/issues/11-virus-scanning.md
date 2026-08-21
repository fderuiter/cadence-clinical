# 11: Virus / Malware Scanning on Upload

**What to build:** Every file uploaded through `fileshare` is automatically scanned for viruses and malware before it becomes accessible to any user. Files that fail scanning are quarantined: they cannot be previewed, downloaded, or shared until cleared. The scan result is recorded on the `FileRecord` and an alert notification is emitted to the uploading user and any `super_admin`.

**Blocked by:** 03 — `apps/fileshare` Microservice Scaffold. Can run in parallel with 04, 05, and 06.

**Status:** ready-for-agent

- [ ] `FileRecord` gains fields: `scan_status` (enum: `PENDING` / `CLEAN` / `INFECTED` / `SCAN_ERROR`), `scan_engine` (str — e.g. `"clamav-1.3"`), `scan_completed_at` (nullable datetime), `scan_threat_name` (nullable str).
- [ ] Alembic migration adds the new columns.
- [ ] ClamAV container added to `docker/docker-compose.yml` (image: `clamav/clamav:stable`). `fileshare` connects to ClamAV via its TCP socket (`clamd` protocol).
- [ ] `ScanPort` protocol defined in `apps/fileshare/ports/`: `async def scan_stream(stream: AsyncIterable[bytes]) -> ScanResult`. `ClamAVScanAdapter` implements `ScanPort`.
- [ ] On `complete-upload` signal, `fileshare` streams the uploaded object from the object store through `ScanPort.scan_stream()`. This runs before `transcode_status` is set to `PENDING` for videos.
- [ ] If `CLEAN`: `scan_status` → `CLEAN`; file transitions to `READY` (or `PENDING_TRANSCODE` for video).
- [ ] If `INFECTED`: `scan_status` → `INFECTED`, `scan_threat_name` populated. `FileRecord.status` → `QUARANTINED`. Object moved to a quarantine bucket prefix. Notification emitted to uploader and `super_admin` via `notifications_client`.
- [ ] If `SCAN_ERROR` (ClamAV unavailable): `scan_status` → `SCAN_ERROR`. File held in `PENDING_SCAN` state. Retry scheduled with exponential back-off (max 3 attempts). If all retries fail, notification emitted to `super_admin`.
- [ ] All download, preview, and share endpoints return `403 Forbidden` with `"reason": "QUARANTINED"` if `FileRecord.status == QUARANTINED`.
- [ ] Integration test uploads a harmless EICAR test string to verify the `INFECTED` path; uploads a clean file to verify the `CLEAN` path.
- [ ] `uv run ruff format .` and `uv run ruff check . --fix` pass with zero errors.
- [ ] GxP sync run after test additions.

# 13: Retention / Legal Hold Flag

**What to build:** Any `FileRecord` can be placed under a retention or legal hold, which prevents it from being deleted, archived, or purged — even by a `super_admin`. Holds are set and cleared via an audited REST endpoint that requires a `reason_for_change`. Files on hold surface a 🔒 badge in the Documents Hub. This satisfies the ICH E6 GCP requirement that trial master file documents be preserved for the required retention period and the 21 CFR Part 11 requirement that records under legal hold be immutable.

**Blocked by:** 03 — `apps/fileshare` Microservice Scaffold. Can run in parallel with 04, 05, 06, 11, and 12.

**Status:** ready-for-agent

- [ ] `FileRecord.is_on_hold` (boolean, default `False`) and `hold_reason` (nullable str) already present from T-03. This ticket wires the enforcement logic.
- [ ] `POST /api/v1/fileshare/files/{file_record_id}/hold` — places file on hold. Requires `super_admin` or `data_manager` role. Body: `{ "reason_for_change": "..." }`. Sets `is_on_hold = True`, records `hold_reason`. Appends to audit log.
- [ ] `DELETE /api/v1/fileshare/files/{file_record_id}/hold` — removes hold. Same role requirements. Body: `{ "reason_for_change": "..." }`. Appends to audit log.
- [ ] All delete, archive, and purge operations in `fileshare` (and in `apps/etmf` for migrated files) gate on `FileRecord.is_on_hold.is_(False)` (SQLAlchemy `.is_()` pattern per AGENTS.md). Attempting to delete a held file returns `409 Conflict` with `"reason": "FILE_ON_HOLD"`.
- [ ] `GET /api/v1/fileshare/files/{file_record_id}` response DTO includes `is_on_hold` and `hold_reason` fields.
- [ ] Documents Hub UI (T-10): rows with `is_on_hold == true` show a 🔒 badge in the Status column. Delete/archive actions are disabled and show a tooltip: "File is under retention hold."
- [ ] Audit log entry for hold set/clear includes: `file_record_id`, `action` (`HOLD_SET` / `HOLD_CLEARED`), `user_id`, `timestamp`, `reason_for_change`.
- [ ] Unit tests cover hold enforcement (attempted delete of held file returns 409) and audit log entries.
- [ ] `uv run ruff format .` and `uv run ruff check . --fix` pass with zero errors.
- [ ] GxP sync run after test additions.

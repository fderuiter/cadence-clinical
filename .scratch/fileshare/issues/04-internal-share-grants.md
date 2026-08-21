# 04: Internal Share Grants (RBAC)

**What to build:** Any authenticated user with sufficient permission can share a file, folder, study, or site with another internal Cadence user at a named permission level (view, comment, download, upload-revision, reshare, approve, expire/revoke). The recipient is notified and can immediately access the shared resource according to their grant. Existing role-based access is unaffected — grants are additive on top of role access.

**Blocked by:** 03 — `apps/fileshare` Microservice Scaffold.

**Status:** ready-for-agent

- [ ] `POST /api/v1/fileshare/grants` — create a share grant. Body includes `file_record_id` (or `folder_id` / `study_id` / `site_id` for scoped grants), `granted_to_user_id`, `permission_level`, and `reason_for_change`. Only users who themselves hold `reshare` permission (or are `super_admin`/`sponsor_designer`) can grant.
- [ ] `GET /api/v1/fileshare/grants?file_id=` — list all grants on a file, visible to the file owner and `super_admin`.
- [ ] `DELETE /api/v1/fileshare/grants/{grant_id}` — revoke a grant. Requires `expire_revoke` permission or ownership. Logs revocation with `reason_for_change` to the audit trail (21 CFR Part 11).
- [ ] Permission enforcement middleware: every access to a `FileRecord` checks the caller's role permissions AND any active `ShareGrant` rows for that user + file. `SQLAlchemy.is_(True)` / `is_(False)` used on all boolean columns.
- [ ] Folder/study/site scoped grants cascade: a grant on `study_id=X` implies `view` on all files in that study (unless a more specific grant overrides).
- [ ] Notification emitted via `notifications_client` when a grant is created: "User A has shared `<filename>` with you — permission: view."
- [ ] Every grant creation, update, and revocation appended to the `fileshare` audit log with `user_id`, `timestamp`, `permission_level`, and `reason_for_change`.
- [ ] Unit tests use `InMemoryRepository` from `packages.testing.fakes` and `create_test_security_context` from `packages.testing.security`.
- [ ] `uv run ruff format .` and `uv run ruff check . --fix` pass with zero errors.
- [ ] GxP sync run after any test additions.

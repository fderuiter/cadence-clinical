# 15: Per-Document Activity Feed

**What to build:** Every document in the Documents Hub (T-10) has a slide-out "Activity" panel showing a chronological, filterable feed of everything that has happened to that file: uploads, version bumps, share grants and revocations, approval events, scan results, format conversion completions, hold changes, guest link accesses, and video playback events. This surfaces the underlying `fileshare` audit log in a human-readable UI, providing the "inspection-readiness" view required by ICH E6 GCP.

**Blocked by:** 10 — Documents Hub UI.

**Status:** ready-for-agent

- [ ] `GET /api/v1/fileshare/files/{file_record_id}/activity` — paginated endpoint returning audit log entries for the file, ordered by `timestamp DESC`. Supports query params: `event_type` (filter), `from_date`, `to_date`, `page`, `page_size`. Accessible to any user with at least `view` permission on the file.
- [ ] Response DTO: `{ entries: [{ event_type, actor_user_id, actor_display_name, timestamp, detail: {...} }], total, page, page_size }`.
- [ ] Event types surfaced: `FILE_UPLOADED`, `VERSION_CREATED`, `GRANT_CREATED`, `GRANT_REVOKED`, `GUEST_LINK_CREATED`, `GUEST_LINK_ACCESSED`, `GUEST_LINK_REVOKED`, `SCAN_COMPLETE`, `SCAN_QUARANTINED`, `CONVERSION_COMPLETE`, `HOLD_SET`, `HOLD_CLEARED`, `DOCUMENT_APPROVED`, `DOCUMENT_REJECTED`, `APPROVAL_RESCINDED`, `VIEW_OPEN`, `PLAY_START`, `PLAY_END`.
- [ ] `ActivityFeed.vue` component in `apps/web/src/components/fileshare/`: vertical timeline with event-type icons, actor name, relative timestamp (e.g. "3 hours ago"), and an expandable detail row for events with rich data (e.g. grant permission level, guest link expiry, scan threat name).
- [ ] Integrated into the Documents Hub as a slide-out drawer: clicking the activity icon on a document row opens `ActivityFeed.vue` for that file alongside the existing share management drawer.
- [ ] Filter bar at the top of the feed: event type multi-select, date range picker. Filters applied via query params to the API (server-side, not client-side).
- [ ] Infinite scroll (or "Load more" button) for pagination.
- [ ] All styling uses semantic CSS variables — no Tailwind classes.
- [ ] Vitest tests cover the feed rendering with a mocked activity response and the filter bar interactions.
- [ ] `uv run ruff format .` and `uv run ruff check . --fix` pass with zero errors.
- [ ] GxP sync run after any test additions.

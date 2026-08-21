# 05: External Guest Links

**What to build:** An authenticated user can generate a secure, time-limited share link for a file that can be sent to someone outside the Cadence platform (e.g. a regulatory body, CRO partner, or external IRB). The recipient opens the link in a browser without logging in, is shown a protected-view preview of the file, and cannot download unless the link was created with download permission. Every access is logged. Links expire after a configurable period (default 30 days) or can be event-bound to a study milestone.

**Blocked by:** 03 — `apps/fileshare` Microservice Scaffold.

**Status:** ready-for-agent

- [ ] `POST /api/v1/fileshare/guest-links` — create a guest link. Body: `file_record_id`, `permission_level` (view or download), `expires_in_days` (default 30, max configurable via env), `reason_for_change`. Returns an opaque URL-safe token.
- [ ] Token is an HMAC-SHA256 signature over `{file_record_id}:{expires_at}:{nonce}` using the platform signing key from `packages.security.signing`. The raw token is never stored — only its HMAC hash is stored in `GuestLink.token_hmac`.
- [ ] `GET /api/v1/fileshare/guest/{token}` — public (unauthenticated) endpoint. Verifies HMAC, checks expiry, checks link is not revoked. On success returns a short-lived presigned GET URL (5-minute TTL) directly from the object store. Watermark applied at generation time for view-only links.
- [ ] `DELETE /api/v1/fileshare/guest-links/{guest_link_id}` — revoke a link before expiry. Logged to audit trail with `reason_for_change`.
- [ ] Background expiry scanner marks expired `GuestLink` rows as inactive (analogous to `expiration_scanner.py` in eTMF).
- [ ] Event-based expiry: `fileshare` subscribes to study milestone events (site close-out, study lock) and auto-revokes all guest links scoped to that study.
- [ ] Every access to a guest link (successful or failed) appended to the `fileshare` audit log: `token_hmac`, `accessed_at`, `requester_ip`, `outcome` (success/expired/revoked/invalid).
- [ ] Protected view page in `apps/web`: minimal unauthenticated route (`/share/:token`) that calls the guest endpoint and renders `MediaPreviewModal` (T-07) in read-only protected mode.
- [ ] Unit and integration tests cover token generation, HMAC verification, expiry, and revocation.
- [ ] `uv run ruff format .` and `uv run ruff check . --fix` pass with zero errors.

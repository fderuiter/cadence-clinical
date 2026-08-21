# 14: Electronic Signature on Document Approval

**What to build:** A recipient who holds `approve` permission on a file (granted via T-04) can formally approve the document with a 21 CFR Part 11-compliant electronic signature. The e-sig flow requires the approver to re-authenticate (PIN or password re-entry) in-product before the signature is recorded. The `SignatureManifestation` — signer identity, timestamp, SHA-256 hash of the file content, signing reason, and certificate PEM — is stored on the `FileRecord`. Approved documents are immutable: no new version can be uploaded unless the approval is explicitly rescinded (with reason).

**Blocked by:** 04 — Internal Share Grants (RBAC).

**Status:** ready-for-agent

- [ ] `FileRecord` gains fields: `approval_status` (enum: `PENDING_APPROVAL` / `APPROVED` / `REJECTED` / `APPROVAL_RESCINDED`), `signature_manifestation` (nullable JSON, using the existing `SignatureManifestation` model from `packages.security.signature`), `approved_by` (nullable str), `approved_at` (nullable datetime), `approval_reason` (nullable str).
- [ ] Alembic migration adds the new columns.
- [ ] `POST /api/v1/fileshare/files/{file_record_id}/approve` — initiates approval. Caller must hold `approve` permission. Body: `{ "signing_reason": "...", "credential": "<PIN or password>" }`. Backend re-verifies the credential against the identity provider before proceeding. On success: `approval_status = APPROVED`, `SignatureManifestation` constructed and stored, `FileRecord` becomes immutable.
- [ ] `POST /api/v1/fileshare/files/{file_record_id}/reject` — rejects the document for approval. Body: `{ "reason_for_change": "..." }`. Sets `approval_status = REJECTED`.
- [ ] `POST /api/v1/fileshare/files/{file_record_id}/rescind-approval` — rescinds a previous approval. Requires `super_admin`. Body: `{ "reason_for_change": "..." }`. Sets `approval_status = APPROVAL_RESCINDED`, clears immutability.
- [ ] Upload-revision endpoint blocks new version upload if `approval_status == APPROVED` (returns `409 Conflict` with `"reason": "DOCUMENT_APPROVED_IMMUTABLE"`).
- [ ] Audit log entry for approval includes: full `SignatureManifestation` JSON, `outcome`, `user_id`, `timestamp`.
- [ ] `MediaPreviewModal.vue` (T-07): if `approval_status == APPROVED`, an "✅ Approved" badge is shown with the signer name and timestamp. An "Approve this document" button is shown if `permissions.canApprove == true`.
- [ ] The in-product re-authentication modal (for PIN/credential entry) is a new shared component `ReAuthModal.vue` in `apps/web/src/components/shared/`.
- [ ] Unit tests cover approval state machine transitions (approve → immutable, rescind → mutable). Integration test calls the approve endpoint with a mock credential verifier.
- [ ] `uv run ruff format .` and `uv run ruff check . --fix` pass with zero errors.
- [ ] GxP sync run after test additions.

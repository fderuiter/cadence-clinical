# ADR 2026-07-22: Local USDM Validation & Optimistic Sequencing

## Status
Accepted

## Context
The clinical trial execution engine ingested raw study configurations without verifying CDISC USDM schema compliance or sequencing accuracy. This allowed structurally invalid payloads or late-arriving outdated events (due to network latency or database race conditions) to corrupt or overwrite newer clinical form definitions.

## Decision
We implemented a synchronous local validation and optimistic sequencing mechanism at the FastAPI ingestion endpoint (`/events/study-published`):
1. **Synchronous CDISC USDM Validation**: Prior to queueing any background work, the payload is validated against the official local USDM standard `usdm_model.Study` schema. If schema parsing fails, we immediately return an `HTTP 400 Bad Request` synchronously to the sender.
2. **Synchronous Optimistic Sequencing Check**: We extract the version identifier from the payload and query the database for the highest existing compiled study version. If the incoming version is equal to or lower than the existing stored version, we reject the update synchronously with an `HTTP 409 Conflict` error.
3. **Queueing and Audit Trails**: If both checks succeed, the translation is queued and returns `HTTP 202 Accepted` status. We pass the verified version to the translation background task where the `TranslationJob` is stored with this exact version, correctly logging writes and sequence changes inside the historical database audit trails.

## Alternatives Considered
- **Asynchronous validation in background jobs**: Rejecting payloads asynchronously was rejected because it does not provide immediate feedback to publishing clients and allows invalid or out-of-order payloads to pollute translation queue logs.
- **Visual/geometric page layout checks during sync validation**: Out of scope because the platform relies on static structural schema completeness checks only.

## Trade-offs
- **Positive**: Complete prevention of race conditions and out-of-order writes; immediate synchronous validation feedback.
- **Negative**: Adds a database read check to the ingestion request path. We mitigated this by indexing the `version` column in `AuditedModel`/`TranslationJob`.

# ADR-2816: Itemized Electronic Signature Token Verification for Offline Batch Sync

- **Status:** Accepted
- **Date:** 2026-10-15
- **Authors:** @jules
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Under FDA 21 CFR Part 11 and GxP standards, electronic signature re-authentication tokens must be verified item-by-item when queued offline deltas are submitted in batch synchronization requests (`/api/v1/offline/sync-batch`). Non-signature deltas (e.g., form draft updates) do not require electronic signatures, but signature-gated deltas (such as eCRF approvals) require active, unexpired, single-use signature re-authentication tokens (`sig_token`).

This decision implements requirements under PRD-SYS-001 and PRD-SYS-053.

## 2. Decision Drivers & Constraints

- **Compliance:** 21 CFR Part 11 electronic signature mandates require explicit step-up re-authentication token validation for regulated signature actions.
- **Single-Use Enforcement:** Signature tokens must be consumed exactly once; replayed tokens across batch sync retries must be rejected with HTTP 401 `REAUTHENTICATION_REQUIRED`.
- **Gateway Alignment:** `/api/v1/offline/{**catch-all}` route must be routed via the API Gateway to the execution service cluster.

## 3. Options Considered

### Option 1: Top-level Batch Signature
Require a single signature token for the whole batch request.
- *Pros:* Simple request payload.
- *Cons:* Blurs item-level audit attribution and forces signature tokens on non-signature updates.

### Option 2: Itemized Signature Token Resolution and Single-Use Verifier (Selected)
Inspect queued deltas item-by-item in `OfflineSyncEndpoints`. Extract `sig_token` from top-level or payload properties, verify signature, identity, and expiry using `SigTokenVerifier` backed by a static `SharedReplayCache`, apply `SignatureManifest` state transitions for valid items, and return HTTP 401 `REAUTHENTICATION_REQUIRED` on any invalid/missing/expired/replayed token.
- *Pros:* Strict Part 11 compliance, clear itemized audit manifests, robust offline queue resilience.
- *Cons:* Requires client and backend payload coordination.

## 4. Decision Outcome

- **Chosen Option:** Option 2.
- **Justification:** Meets all 21 CFR Part 11 mandates while preserving non-signature offline delta processing capability and client queue recovery.

## 5. Consequences & Trade-offs

- **Positive Impact:** Full regulatory compliance for offline-queued electronic signature actions, preventing replay attacks and enforcing single-use re-authentication tokens.
- **Negative Impact / Technical Debt:** Additional request parsing and signature validation overhead during batch sync endpoints execution.
- **Mitigation Strategy:** Fast-path rejection for non-signature deltas and memory-cached replay checks.

## 6. Implementation & Verification

- Backend: Implemented `OfflineDeltaItem`, `OfflineBatchSyncRequest`, `OfflineBatchSyncResponse`, and mapped `/api/v1/offline/sync-batch` and `/api/v1/execution/offline/sync` in `OfflineSyncEndpoints`.
- Frontend: Updated `syncEngine.ts` to attach signature tokens to queued batch deltas and dispatch `reauthentication-required` events on HTTP 401.
- Gateway: Mapped `execution-offline-route` in Gateway `Program.cs`.
- Verification: Tested with Vitest and xUnit unit tests (`OfflineSyncEndpointsTests`, `GatewayRouteCoverageTests`).

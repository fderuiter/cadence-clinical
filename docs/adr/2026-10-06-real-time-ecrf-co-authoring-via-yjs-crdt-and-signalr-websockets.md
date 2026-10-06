# ADR-2812: Real-Time eCRF Co-Authoring via Yjs CRDT and SignalR WebSockets

- **Status:** Accepted
- **Date:** 2026-10-06
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Designing electronic Case Report Forms (eCRFs) in clinical trials requires concurrent co-authoring across medical monitors, biostatisticians, and data managers. Traditional pessimistic locking creates editor contention, version lockouts, and lost edits during network disruptions. A decentralized conflict-free replicated data type (CRDT) synchronization protocol was required over WebSockets for eCRF form authoring (PRD-SYS-001).

## 2. Decision Drivers & Constraints

- **Driver 1:** Real-time multi-user concurrent editing of form structure and field attributes without lock conflicts.
- **Driver 2:** Real-time presence tracking, including 30 FPS cursor position broadcasts, active field focus highlights, and role badges.
- **Driver 3:** Offline edit accumulation and automatic state vector merging upon reconnection.
- **Driver 4:** Asynchronous background USDM schema compilation checks and PostgreSQL snapshot persistence.

## 3. Options Considered

### Option 1: Yjs CRDT Updates over SignalR WebSockets (Selected)

- **Overview:** Use Yjs (`Y.Doc`) on the frontend (`apps/web`) to manage form state and document CRDT binary update vectors. Transport binary updates and presence awareness through ASP.NET Core SignalR (`EcrfCollaborationHub`). In-memory session state is maintained in `EcrfSessionManager`, with snapshot persistence in PostgreSQL (`EcrfFormSnapshot`).
- **Pros:**
  - ✅ Guarantees eventual consistency and lock-free concurrent co-authoring.
  - ✅ Throttled 30 FPS cursor tracking and field focus highlights enhance co-author visibility.
  - ✅ Native offline edit queuing and state vector reconciliation on reconnect.
  - ✅ Integrated USDM schema compilation checks on document updates.
- **Cons:**
  - ❌ Requires binary byte-array transport in SignalR and state vector merging in `EcrfSessionManager`.

### Option 2: Pessimistic Field Locking over REST / Short Polling

- **Overview:** Lock individual eCRF fields during editing and push updates via REST.
- **Pros:**
  - ✅ Simple HTTP/REST CRUD operations.
- **Cons:**
  - ❌ High latency, lock contention during collaborative design sessions, and fragile offline behavior.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Option 1 satisfies PRD-SYS-001 by delivering real-time decentralized CRDT collaboration, seamless offline edit reconciliation, and robust active presence awareness.

## 5. Consequences & Trade-offs

- **Positive Impact:** Concurrent co-authoring experience for clinical trial eCRF design with live presence and zero lock conflicts.
- **Negative Impact / Technical Debt:** Memory management for active SignalR sessions in `EcrfSessionManager`.
- **Mitigation Strategy:** Periodic persistence to PostgreSQL (`DesignerDbContext`), automatic session cleanup on hub disconnect, and comprehensive unit testing.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `apps/designer`, `apps/web`, `packages/database`, `tests/Cadence.UnitTests`
- **Verification Plan:** Unit tests in `tests/Cadence.UnitTests/Designer/EcrfCollaborationSessionTests.cs` and Vitest frontend unit tests in `apps/web/tests/stores/designer_crdt.spec.ts`.


# ADR-2448: Gateway Routes Verified Against Service Endpoints

- **Status:** Accepted
- **Date:** 2026-09-29
- **Authors:** @fderuiter, Claude Code
- **Deciders:** @fderuiter
- **Requirements:** PRD-SYS-053

---

## 1. Context & Problem Statement

The gateway's YARP route table (`apps/gateway/src/Program.cs`) is written by hand, and nothing checks it against the endpoints the services map. It had drifted in both directions:

- Five routes led to prefixes their service does not map:
  - `/api/v1/subjects` and `/api/v1/compliance` (execution serves these under `/api/v1/execution`)
  - `/api/v1/sites` (ctms), `/api/v1/documents` (etmf) and `/api/v1/files` (fileshare)
- Designer's `/api/v1/protocols/compile` had no route, so clients could not reach it.

A route to nowhere authenticates the caller, signs identity headers and forwards the request, only to get the service's 404. That makes a missing backend look like a backend bug.

## 2. Decision Drivers & Constraints

- The route table must match what is deployed.
- A static source scan would be brittle, because routes are composed with `MapGroup` and extension methods.
- No new runtime dependency or configuration format.

## 3. Options Considered

### Option 1: Generate routes from each service's OpenAPI document

- **Pros:**
  - ✅ No hand-written table.
- **Cons:**
  - ❌ Needs every service's document at gateway startup or build time.
  - ❌ Most services publish no document yet.

### Option 2: A test that boots every host and compares endpoints (Selected)

- **Overview:** `tests/Gateway/GatewayRouteCoverageTests.cs` compares the gateway's configured routes with the `RouteEndpoint`s each service actually registers. It fails in two cases:
  - a route prefix has no endpoint under it
  - a service maps an `/api/v1` endpoint that no route reaches
- **Pros:**
  - ✅ Uses the real composition roots, so it sees every endpoint regardless of how it was mapped.
  - ✅ Reports every mismatch in one run.
- **Cons:**
  - ❌ Every routed service must be registered in the test. The test asserts this, so a new cluster without a registration fails.

## 4. Decision Outcome

- **Chosen Option:** Option 2.
  - Every gateway route forwards a whole path prefix (`/{**catch-all}`) to exactly one service.
  - The five dead routes are removed.
  - `/api/v1/protocols` is routed to designer.
- **Justification:** It catches drift at test time with no change to the runtime.

## 5. Consequences & Trade-offs

- **Positive Impact:**
  - Clients get a gateway 404 for paths nothing serves.
  - Adding an endpoint group without a route fails the test.
- **Negative Impact / Technical Debt:**
  - The web client still calls some paths no service implements, such as `/api/v1/documents/*`. Those now 404 at the gateway instead of at the service.

## 6. Implementation & Verification

- **Affected Repositories / Services:**
  - `apps/gateway/src/Program.cs`
  - `apps/gateway/README.md` (route table)
- **Verification Plan:**
  - `GatewayRouteCoverageTests`. Against the old table, it reports all six mismatches listed above.

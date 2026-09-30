# ADR-2451: Gateway Timeouts and Health-Checked Clusters

- **Status:** Accepted
- **Date:** 2026-09-29
- **Authors:** @fderuiter, Claude Code
- **Deciders:** @fderuiter
- **Requirements:** PRD-SYS-053

---

## 1. Context & Problem Statement

The gateway's YARP clusters (`apps/gateway/src/Program.cs`) had only a destination address, and so had these gaps:

- **No timeout of our choosing:** a stalled service held each request for YARP's default of 100 seconds.
- **No health checks:** the gateway kept forwarding to a service that was down or not ready. Callers waited for a connection failure or a timeout instead of getting an answer.

Every service already exposes `/healthz/ready` (database reachable, migrations applied), so the gateway can use it.

## 2. Decision Drivers & Constraints

- A caller should get a prompt, honest status (503 or 504) when a service can't serve it.
- One policy for every cluster, with no per-service tuning to drift.
- No new package. YARP already ships active and passive health checks.
- Each cluster has a single destination today.

## 3. Options Considered

### Option 1: Retries and circuit breaking with `Microsoft.Extensions.Http.Resilience`

- **Pros:**
  - ✅ Retries can hide brief blips.
- **Cons:**
  - ❌ YARP forwards through its own `HttpMessageInvoker`, so the resilience handlers don't apply without custom plumbing.
  - ❌ Retrying non-idempotent GxP writes at the proxy risks duplicate records.

### Option 2: YARP activity timeout plus active and passive health checks (Selected)

- **Overview:** `GatewayClusters.Create` builds every cluster with the same four settings:
  - **Activity timeout:** `GATEWAY_PROXY_TIMEOUT_SECONDS`, default 30. The request fails with 504 once the service sends nothing for that long.
  - **Active probe:** `GET /healthz/ready` every `GATEWAY_HEALTH_CHECK_INTERVAL_SECONDS` (default 10), timing out after `GATEWAY_HEALTH_CHECK_TIMEOUT_SECONDS` (default 5). Two consecutive failures mark the service unhealthy, and one success restores it.
  - **Passive checks:** YARP's transport-failure-rate policy removes a destination that keeps failing connections for 30 seconds.
  - **Available destinations:** the `HealthyAndUnknown` policy, so the gateway answers 503 at once while the service is unhealthy. YARP's default, `HealthyOrPanic`, falls back to unhealthy destinations, and with one destination per cluster it would never stop a request.
- **Pros:**
  - ✅ Uses built-in YARP features and the existing readiness probes.
  - ✅ Never retries a request, so writes are never duplicated.
- **Cons:**
  - ❌ A service is taken out of rotation up to two probe intervals after it fails, and put back up to one interval after it recovers.

## 4. Decision Outcome

- **Chosen Option:** Option 2.
- **Justification:** It bounds every proxied request and fails fast on an unhealthy service, with no duplicate-write risk and no new dependency.

## 5. Consequences & Trade-offs

- **Positive Impact:**
  - Callers get 503 from the gateway while a service is starting or down, instead of hanging.
  - A stalled service no longer holds gateway connections for 100 seconds.
- **Negative Impact / Technical Debt:**
  - Streaming responses that go quiet for longer than the activity timeout are cut off. Raise `GATEWAY_PROXY_TIMEOUT_SECONDS` if one is ever needed.
  - The gateway sends each service one readiness probe per interval.

## 6. Implementation & Verification

- **Affected Repositories / Services:**
  - `apps/gateway/src/GatewayClusters.cs`, `apps/gateway/src/Program.cs`
  - `apps/gateway/README.md`, `docs/ENVIRONMENT_VARIABLES.md`, `.env.example`
- **Verification Plan:** `tests/Gateway/GatewayResilienceTests.cs` checks three things:
  - every cluster carries the policy
  - a service whose readiness probe fails gets 503 with no request forwarded, and serves again once ready. This fails under `HealthyOrPanic`.
  - a service that stops responding gets 504 after the timeout

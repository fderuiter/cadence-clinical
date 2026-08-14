# ADR-2174: Gateway Catch All Streaming

* **Status:** Accepted
* **Date:** 2026-08-14
* **Authors:** @fderuiter
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

The active Python API gateway currently fully buffers incoming requests and outgoing responses in memory before forwarding them to or from downstream microservices. This fully-buffered request/response pattern causes severe memory spikes and potential out-of-memory (OOM) crashes during large payload transfers, such as 500MB electronic Trial Master File (eTMF) uploads, clinical reporting exports, and unauthenticated webhooks (e.g. inbound-email ingestion). To eliminate OOM gateway crashes and keep memory utilization flat under a stable 250MB baseline, the gateway must stream requests and responses chunk-by-chunk directly while preserving existing authentication, signature-gating, and multi-tenant capabilities.

## 2. Decision Drivers & Constraints

* **Stable Gateway RAM:** Gateway memory usage must remain low and stable (< 250MB) regardless of payload sizes.
* **No Infrastructure Changes:** The streaming implementation must reside entirely within the active Python gateway codebase, with no dependence on NestJS rewrite.
* **Zero Auth Disruptions:** Token verification, step-up signature validation, sandbox tenant isolation, and identity/scope header propagation must continue to function perfectly.
* **Downstream Safety:** Streaming must not disrupt downstream handlers. Signature-gated routes that require request body inspection for validation must still work.
* **GxP Compliance Reference:** This architectural decision satisfies system requirement PRD-SYS-001 regarding electronic trail/log streaming and memory stability.

## 3. Options Considered

1. **Option A (Selected): Catch-All Proxy Streaming.** Configure catch-all routers to stream request streams directly to downstream microservices using HTTPX's streaming capabilities, and stream downstream responses back via FastAPI's `StreamingResponse`. Selectively buffer the request body in-memory only when signature-gated/body-driven mutations are targeted.
2. **Option B: Full Buffering with Memory Optimization.** Increase container memory limits or optimize local Python GC parameters. This does not scale and risks OOM crashes under high concurrent load.

## 4. Decision Outcome

Chosen option: **Option A** because it solves the OOM memory spike issue by eliminating full in-memory buffering for standard and large payload routes, while gracefully preserving in-memory body checking on specific, isolated signature-gated mutations.

### Implementation Details:
* **Selective Request Body Parsing:** The gateway checks if a request is a mutation and matches any signature-gated pattern or body-driven regulated action before reading it. If not gated, the gateway forwards `request.stream()` as an async generator directly.
* **Response Chunking:** Downstream responses are fetched with `stream=True` on `httpx.AsyncClient` and returned to clients via `StreamingResponse` using an asynchronous chunk generator.

## 5. Consequences & Trade-offs

* **Positive:** Low, flat memory baseline regardless of transfer payload sizes; zero slow-downs or connection timeouts under concurrent uploads.
* **Positive:** Fully preserves step-up auth and GxP compliance constraints.
* **Negative:** Downstream services must handle chunked transfer encoding (already natively supported by FastAPI/Uvicorn).

## 6. Implementation & Verification

* **Target files/packages modified:**
  * `apps/gateway/main.py`: Updated catcher routers and webhook routing to stream bytes using `StreamingResponse`.
* **Verification tests added under `tests/`:**
  * Added unit tests verifying request/response streaming and selective buffering on signature gating inside `apps/gateway/tests/test_gateway.py`.

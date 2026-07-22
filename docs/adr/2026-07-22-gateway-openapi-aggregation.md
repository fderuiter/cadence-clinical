# ADR 2026-07-22: Dynamic OpenAPI Schema Aggregation at the Gateway

## Status
Accepted

## Context
Developers required a unified, interactive API documentation portal for all Cadence Clinical microservices (Designer and Execution). However, direct requests to individual service docs are blocked by downstream security middleware, and statically maintaining an aggregated OpenAPI registry is error-prone and requires manual updates upon every new deployment.

## Decision
We decided to implement a dynamic OpenAPI aggregation engine directly within the API Gateway (`apps/gateway/main.py`).

When a client accesses `/openapi.json`, the gateway dynamically:
1. Concurrently fetches the raw OpenAPI schemas from downstream services (Designer and Execution) using `asyncio.gather`.
2. Circumvents security middleware by signing the internal fetch requests using the established HMAC-SHA256 signature protocol.
3. Namespaces components and recursively rewrites `$ref` references (e.g., prefixing `Designer_` and `Execution_`) to prevent naming collisions.
4. Rewrites path prefixes so that Swagger UI requests route correctly through the gateway proxy back to the downstream services.

## Alternatives Considered
- **Static API Registry/Build Pipeline:** Compiling OpenAPI definitions into a static file during CI/CD. Rejected because it delays visibility of schema changes until a full deployment cycle completes.
- **Service Mesh / External API Portal:** Using an external tool (e.g., Kong, Apigee) for docs aggregation. Rejected as it introduces unnecessary infrastructure overhead for a relatively simple internal requirement.

## Trade-offs
- **Positive:** Developers have immediate access to updated, unified API documentation at runtime via `/docs`.
- **Positive:** No static configuration or external dependencies are required.
- **Negative:** The gateway incurs a slight performance penalty when dynamically fetching and rewriting JSON schemas on `/openapi.json` requests, though this is mitigated by concurrent fetching and only affects documentation rendering, not operational traffic.

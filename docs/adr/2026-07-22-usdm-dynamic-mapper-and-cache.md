# ADR 2026-07-22: Unified USDM Dynamic Mapper and Terminology Cache

## Status
Accepted

## Context
Exposing unified Unified Study Definitions Model (USDM) schemas directly to clients runs the risk of breaking existing backward compatibility with established endpoints. At the same time, resolving deeply nested database relationships (such as Study -> Arms -> Visits -> Activities) and repeatedly fetching Controlled Terminology codes on-demand causes severe API latency, violating performance requirements (Target: < 200ms).

## Decision
1. **API Route Versioning & Isolation**: Legacy endpoints (`/api/v1`) will remain unmodified to ensure backward compatibility. The new USDM compliance schemas will be served under `/api/v2`.
2. **Single Projection DB Retrieval**: Nested study relationships are aggregated via multi-level associations in a single database retrieval step, keeping latency low and preventing N+1 problems.
3. **Thread-Safe Terminology Cache**: We will implement a thread-safe, size-limited in-memory cache for Controlled Terminology lookups. In-memory caching avoids network latency and the dependency overhead of external servers (like Redis), while strict memory constraints prevent unbound growth.
4. **Strict Schema Validation**: Using Pydantic schemas, validation checks ensure that incomplete mappings trigger detailed structured errors instead of failing silently.

## Alternatives Considered
- **External Caching (e.g., Redis):** Considered for distributing the cache across multiple instances, but rejected to avoid the network latency and operational dependency overhead.
- **On-Demand DB Fetching:** Fetching codes dynamically per request was evaluated but rejected due to the severe performance impact and violation of our latency targets (< 200ms).

## Trade-offs
- **Positive**: We meet our performance target of < 200ms latency for export.
- **Positive**: Legacy systems face zero downtime and zero mapping changes.
- **Positive**: Terminology lookup avoids redundant DB queries.
- **Negative**: The local caching approach implies cache inconsistency if scaled out to multiple stateless containers, unless cache eviction is synchronized. We provide an admin `/api/admin/cache/clear` endpoint to mitigate this explicitly.

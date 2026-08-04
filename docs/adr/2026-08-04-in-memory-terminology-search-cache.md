# ADR-2155: In Memory Terminology Search Cache

* **Status:** Accepted
* **Date:** 2026-08-04
* **Authors:** @fderuiter
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Currently, clinical researchers experience noticeable user interface lag when building clinical forms. This is because every autocomplete keystroke triggers a debounced network request directly to an external terminology API. In addition to creating a sluggish user experience, this approach results in redundant, costly external network requests for identical queries across concurrent users.

To solve this, we are introducing a thread-safe, in-memory cache layer (`TerminologySearchCache`) tracing to **PRD-MDR-007**. Reusing our backend memory space allows us to drop autocomplete latency to sub-50ms for repeated queries, eliminating visible UI lag without introducing the operational complexity of new database migrations or persistent cache servers.

## 2. Decision Drivers & Constraints

* **Performance SLAs:** Drop autocomplete latency to sub-50ms for repeated queries.
* **Operational Overhead:** Avoid introducing heavy third-party caching middleware or additional containerized storage services (e.g., Redis) unless absolutely necessary.
* **Concurrency:** Ensure thread safety during concurrent lookups and updates since multiple clinical designers use the platform concurrently.
* **Memory Limits:** Prevent memory exhaustion/leaks on the designer microservice host.
* **Data Freshness:** Allow client components to force bypass or force refresh caches to fetch upstream terminology updates.

## 3. Options Considered

1. **Option A (Selected): In-Memory Cache with Python's Thread-Safe `threading.Lock`, Compound Keys, FIFO Eviction, and TTL**
   * Implements custom, lightweight class `TerminologySearchCache` in `apps/designer/db.py`.
   * Enforces a hard boundary of 1,000 cached records using a First-In-First-Out (FIFO) eviction strategy.
   * Leverages compound keys mapping query parameters (`term`, `from_record`, `page_size`) to prevent pagination collisons.
2. **Option B: Heavy Redis-based Caching Layer**
   * Integrates an external Redis container.
   * While scalable, it adds substantial architectural complexity, networking overhead, and deployment boundaries that are not required for our immediate memory footprint.

## 4. Decision Outcome

Chosen option: **Option A** because it is lightweight, requires zero additional external dependencies, ensures optimal memory bounds, and fully satisfies the sub-50ms query requirements under **PRD-MDR-007** while keeping the architecture elegant and maintainable.

### Key Decisions:
1. **Thread-Safe Memory Bound (`threading.Lock`):** Used to prevent race conditions during read/write cycles.
2. **Strict Limit of 1,000 Entries & FIFO Eviction:** Caps memory usage under heavy load.
3. **Compound Cache Keys:** Built dynamically using search queries and pagination parameters.
4. **Time-To-Live (TTL) Expiration:** Configured via `TERMINOLOGY_CACHE_TTL` or `CACHE_TTL` with a default of 3,600 seconds.
5. **Client-Side Control (Bypass / Force Refresh):** Exposed via endpoints to allow explicit refresh operations.

## 5. Consequences & Trade-offs

* **Positive:** Sub-50ms lookup times, high throughput, zero additional infrastructure dependencies, and robust concurrent safety.
* **Negative:** Cache is transient and cleared on microservice restarts (perfectly acceptable for search autocomplete lookup data).

## 6. Implementation & Verification

* **Target files/packages modified:**
  * `apps/designer/db.py`: Implemented `TerminologySearchCache` and initialized a global `terminology_search_cache` instance.
  * `apps/designer/main.py`: Integrated the caching interceptor inside `/api/v1/terminology/search`.
* **Verification tests added under `tests/`:**
  * `tests/test_terminology_validation.py` -> `test_terminology_search_cache_direct`, `test_search_terminology_endpoint_cache_behavior`, and `test_search_terminology_endpoint_bypass_and_refresh`.

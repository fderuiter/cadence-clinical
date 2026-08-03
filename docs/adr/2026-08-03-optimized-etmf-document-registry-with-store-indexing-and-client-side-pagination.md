# ADR-254: Optimized eTMF Document Registry with Store Indexing and Client-Side Pagination

* **Status:** Accepted
* **Date:** 2026-08-03
* **Authors:** @fderuiter
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

In the Cadence Clinical platform, loading large-scale electronic Trial Master File (eTMF) registries with thousands of files and deeply nested folder hierarchies caused browser freezes and heavy CPU usage. This degraded user experience during critical GxP activities such as regulatory inspections and site monitoring.

The two main bottlenecks identified were:
1. **Unbounded DOM Rendering and Re-renders:** Displaying thousands of documents in the document registry grid simultaneously.
2. **Sequential Recursive Searches:** On every folder/artifact selection, the system recursively traversed the folder structure tree to resolve the selected folder name and metadata.
3. **Inline Date Formatting:** Formatting timestamp strings inline inside the template render loop triggered expensive JavaScript string conversions repeatedly on every Vue render cycle.

To ensure strict GxP compliance and operational efficiency under `PRD-TMF-001`, these bottlenecks needed to be systematically resolved without introducing heavy third-party dependencies or altering the existing backend schema limitations.

## 2. Decision Drivers & Constraints

* **High-Performance UI Rendering:** The system must handle thousands of document files smoothly without blocking user input or freezing the main browser thread.
* **Backend Constraints:** The underlying eTMF document storage APIs do not natively support limit/offset pagination or server-side filtering of artifacts; all data is returned in a single batch, requiring client-side management.
* **Minimalist Design & Low Bundle Overhead:** Avoid heavy third-party UI table packages to maintain consistency with the rest of the lightweight Vue 3 SPA architecture.
* **Auditability & Traceability:** Date representations must remain localized, clean, and reliable for clinical operators inspecting audit metadata.

## 3. Options Considered

### Option 1: Introduce Third-Party Paginated Grid Libraries (e.g., AG Grid, PrimeVue)
* **Overview:** Integrate a comprehensive third-party Vue data table library to handle virtualization, pagination, and sorting out of the box.
* **Pros:**
  * ✅ High performance and virtual scrolling support natively built-in.
* **Cons:**
  * ❌ Heavy bundle size overhead, introducing security and regulatory compliance scanning dependencies.
  * ❌ Diverges from custom UI primitives used elsewhere in the frontend package workspace.

### Option 2: Custom Local Client-Side Pagination and Flat Pinia Store Indexing (Selected)
* **Overview:**
  * Centralize folder tree indexing into a flat reactive computed lookup map (`folderLookup`) in the Pinia store to enable instant, constant-time $O(1)$ folder resolution instead of recursive traversal.
  * Implement an active computed date-formatting property mapping `documents` to localized formatted representation prior to rendering.
  * Implement standard local pagination in `DocumentGrid.vue` capped at **20 documents per page** using custom controls.
* **Pros:**
  * ✅ Zero external dependencies, maintaining a very clean and compliant bundle size.
  * ✅ $O(1)$ constant-time folder details resolution from anywhere in the app.
  * ✅ Extremely fast rendering since dates are formatted exactly once per dataset update rather than on every render loop.
  * ✅ Solves DOM freezing issues by rendering at most 20 document items at a time.
* **Cons:**
  * ❌ Requires custom UI code for pagination control buttons, page range indicators, and navigation state.

## 4. Decision Outcome

**Chosen Option: Option 2**

We chose Option 2 because it directly addresses the CPU and rendering bottlenecks while staying 100% compliant with our lightweight frontend design guidelines and keeping the bundle audit-clean.

### Implementation Details:
1. **Flat Store Lookup (`apps/web/src/stores/etmf.js`):**
   Added a computed getter `folderLookup` that recursively traverses the `binderTree` once on load, generating a cached flat lookup dictionary keyed by node code and ID. This provides instant $O(1)$ lookups during active directory navigation.
2. **Pre-Render Formatting & Local Pagination (`apps/web/src/components/etmf/DocumentGrid.vue`):**
   * Added `formattedDocuments` computed property to pre-compute formatted date strings.
   * Capped the visible items in `DocumentGrid` to `20` per page.
   * Implemented robust page navigation (Previous page, Next page, page selection buttons) along with text summary details (e.g., "Showing 1 to 20 of 45 documents").
   * Automatically resets `currentPage` to `1` when switching folders or the documents list is reloaded.

## 5. Consequences & Trade-offs

* **Positive Impact:**
  * 🚀 Users experience instantaneous folder selection and zero UI lag when paging or scrolling documents.
  * 📈 Date conversions are optimized to occur exactly once per data change.
  * 🧩 Pinia lookup map is globally available to any other component needing rapid metadata resolution.
* **Negative Impact:**
  * ⚠️ Slightly increased local state complexity inside `DocumentGrid.vue` (tracking `currentPage`, `itemsPerPage`).
* **Mitigation Strategy:**
  * Formally tested the pagination, page-resetting, and index caching behaviors using a comprehensive Vitest spec suite.

## 6. Implementation & Verification

### Affected Repositories & Services:
* **Frontend SPA:** `apps/web`

### Modified Files:
* `apps/web/src/stores/etmf.js` (Added flat indexing getter)
* `apps/web/src/components/etmf/DocumentGrid.vue` (Added custom pagination controls, pre-computed date formatters, and pagination computed slices)

### Verification Plan:
* **Automated Unit & Integration Tests:**
  Created `apps/web/tests/components/GlobalStoreAndPagination.spec.js` covering:
  * Verification of the `folderLookup` computed map performance and dynamic node retrieval.
  * Validating a maximum of 20 items are rendered inside the table initially.
  * Page selection and page-state resetting upon parent collection updates.
* **Manual Verification:**
  * Navigated through large folder structures within the local development environment, confirming smooth animations and 60 FPS transitions.

# ADR-097: Dynamic MSW Contract-Mocking and Schema Validation

* Status: Accepted
* Date: 2026-08-17
* Authors: @jules
* Deciders: @fderuiter

---

## 1. Context & Problem Statement
Previously, our frontend integration and unit tests relied on fragile, manually maintained network mock overrides (`vi.fn()` and global fetch overrides). This approach led to silent API drift where frontend tests would pass successfully but staging or production deployments failed due to mismatches between frontend expectations and backend schemas. We needed a centralized contract-mocking solution that dynamically validates all mocked request and response payloads against the gateway OpenAPI specification in real-time.

## 2. Decision Drivers & Constraints
* **Prevention of API Drift:** Real-time validation of all mock HTTP interactions to eliminate silent failures.
* **Offline Resilience:** Ensure developers can run tests and the subject-portal offline or with a disconnected gateway.
* **Zero Client SDK Footprint:** Intercept network traffic at the fetch level without requiring or maintaining heavy generated SDK packages.
* **Developer Velocity:** Centralized mock setup to reduce manual mock boilerplate across test suites.

## 3. Options Considered
### Option 1: Fragile Manual Mock Overrides
* **Overview:** Retain the legacy method of setting up independent `globalThis.fetch` or inline Vitest mocks.
* **Pros:**
  * ✅ Simple to implement individually.
* **Cons:**
  * ❌ No schema validation; does not catch API/contract drift.
  * ❌ Extremely high manual maintenance overhead.

### Option 2: Dynamic MSW Interception with Cascading Schema Validation (Selected)
* **Overview:** Implement Mock Service Worker (MSW) to intercept fetch requests globally, asserting every intercepted interaction against the OpenAPI specification. To maintain offline capability, implement a cascading schema resolution mechanism: attempt to fetch the live gateway `/openapi.json`, fallback to a local cached JSON schema (`tests/cached-openapi.json`), and further fallback to compiling OpenAPI YAML specifications parsed directly from markdown (`docs/SDLC/03_API_Integration_Specification.md`).
* **Pros:**
  * ✅ Real-time schema validation prevents any silent mock payload drift.
  * ✅ Robust cascading fallbacks ensure uninterrupted offline local development and test runs.
  * ✅ Clean, standardized mock interface using `msw` instead of ad-hoc fetch spies.
  * ✅ Decoupled from the frontend bundle; zero SDK footprint.
* **Cons:**
  * ❌ Requires maintaining local cache and compiling fallback markdown files if cache is cleared.

## 4. Decision Outcome
* **Chosen Option:** Option 2
* **Justification:** Option 2 perfectly balances real-time schema validation and offline developer productivity without inflating bundle sizes with heavy generated SDKs. The fallback compiler guarantees high local development velocity even when completely disconnected from the network gateway.

## 5. Consequences & Trade-offs
* **Positive Impact:** All frontend test requests are now automatically checked against schema contracts. Intentionally violating schema rules instantly triggers descriptive Vitest failure.
* **Negative Impact / Technical Debt:** Requires a pre-compilation step or markdown specification file maintenance if the API definition diverges significantly.
* **Mitigation Strategy:** Created helper scripts (`tests/generate-cached-openapi.py`) to automate compiling cached JSON specs, and updated the SDLC guide to enforce standard markdown specification structures.

## 6. Implementation & Verification
* **Affected Repositories / Services:** Web portal and Subject Portal workspaces (`apps/web/`, `apps/subject-portal/`, `tests/setup-msw.js`).
* **Verification Plan:** Verify the validation engine by running unit and integration tests under `tests/test_api_contract_validation.py` and checking the Vitest output.

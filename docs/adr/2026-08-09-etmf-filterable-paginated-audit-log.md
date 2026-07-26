# ADR-062: eTMF Filterable and Paginated Audit Log API

* **Status:** Accepted
* **Date:** 2026-08-09
* **Authors:** @jules
* **Deciders:** @lead_architect, @gxp_compliance_officer

---

## 1. Context & Problem Statement
For regulatory inspections, the electronic Trial Master File (eTMF) audit trail must be searchable and safely consumable at scale. Returning an unbounded, unfiltered list of actions is impractical and potentially introduces memory or performance bottlenecks. Callers require the ability to filter the audit log trail by users, actions, document references, and chronological/timestamp windows, alongside validated pagination.

## 2. Decision Drivers & Constraints
* **Regulatory Inspection Workflows:** Audit trails must support robust and precise querying of actions (e.g. INGEST, VIEW, QC_TRANSITION) by specific inspectors or users over designated periods.
* **Scalability:** Unbounded endpoints pose significant load risks. Validated limit and offset pagination are required to ensure safe data consumption.
* **Compliance & Auditing:** The API endpoint must remain protected by secure auditor authorization dependencies and continue to write to the self-auditing `AUDIT_VIEW` log event.

## 3. Options Considered
### Option 1: Client-Side Filtering and Pagination
* **Overview:** Return the entire audit trail to the client, allowing the web or client layer to filter and paginate.
* **Pros:**
  * ✅ Extremely simple backend logic.
* **Cons:**
  * ❌ Severe scalability blocker for large datasets.
  * ❌ Exposes excessive unfiltered log data on initial requests.

### Option 2: Server-Side Filtered & Offset Paginated API Response
* **Overview:** Introduce dynamic SQL query parameters (user_id, action, document_id, start_time, end_time) and limit/offset boundaries handled on the database level. Return a wrapped JSON schema containing `items`, `total_count`, and next-page cursor metadata.
* **Pros:**
  * ✅ High performance and scalability.
  * ✅ Safe data access boundaries.
  * ✅ Full compliance with 21 CFR Part 11 auditing and inspectability requirements.
* **Cons:**
  * ❌ Requires updating existing test assertions that expected raw array list responses.

## 4. Decision Outcome
* **Chosen Option:** Option 2
* **Justification:** Option 2 solves the scaling and search inspectability requirements natively, matching GxP platform standards.

## 5. Consequences & Trade-offs
* **Positive Impact:**
  * Precise, joint and independent filters for audit actions.
  * Safe bounded payloads with pagination limit validation.
  * Enhanced next-page/cursor metadata support.
* **Negative Impact / Technical Debt:**
  * Breaking changes to the list response of the GET `/api/v1/etmf/audit-logs` endpoint (mitigated by updating all associated test suites to access the nested `items` array).

## 6. Implementation & Verification
* **Affected Repositories / Services:** `apps/etmf/`
* **Verification Plan:**
  * Run unit and integration tests under `tests/test_etmf.py`, `tests/test_rbac.py`, `tests/test_etmf_compliance.py`, and `tests/test_etmf_qc.py` to confirm correct response mapping, query filtering, pagination, and robust error handling.

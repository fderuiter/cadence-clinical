# ADR 2026-07-22: Background Translation Job and Layout Engine Data Contract

## Status
Accepted

## Context
Converting complex Unified Study Definition Model (USDM) protocol definitions into highly structured, validated XML formats (such as CDISC ODM for database schemas and OpenRosa XForms for mobile layouts) is CPU-intensive. Synchronous execution on the API level leads to timeouts and blocks the event loop, impacting the overall system performance.

## Decision
We decided to implement an asynchronous background worker pattern for processing semantic translations. To support this asynchronously, we have introduced a new data contract, the `TranslationJob` model, which inherits from our `AuditedModel` structure.

The `TranslationJob` schema tracks:
- `study_id`: The identifier of the incoming study payload.
- `status`: The execution state of the asynchronous process (`PENDING`, `PROCESSING`, `COMPLETED`, `FAILED`).
- `odm_payload`: The CDISC ODM XML layout representation generated for database automation.
- `openrosa_payload`: The OpenRosa XML representation generated for mobile data collection.
- `error_message`: A structured error description if the job fails.

## Alternatives Considered
- **Synchronous API Execution:** Keeping the translation within the HTTP request cycle was considered but rejected because of the high latency and blocking of the event loop.
- **Microservice Segregation:** Creating a completely separate microservice for translation was deemed too complex and heavy for the current system architecture constraints.

## Trade-offs
- **Positive:** API endpoints remain responsive; resource-intensive XML generation occurs in the background. Complete immutability and auditing are retained because `TranslationJob` automatically hooks into the SQLAlchemy audit logger.
- **Negative:** Eventual consistency – clients cannot immediately consume the XML payload in the same HTTP transaction and must query the status instead.

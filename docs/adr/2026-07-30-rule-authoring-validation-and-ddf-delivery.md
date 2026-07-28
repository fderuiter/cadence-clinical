# ADR 2026-07-30: Rule Authoring, Validation, and DDF Delivery

## Status
Accepted

## Context
Standard CDISC USDM clinical metadata studies require advanced, version-controlled skip logic, constraint checks, and cross-form edit check rule structures to direct subject workflows, data entry validation, and longitudinal queries.
To implement these capabilities in Cadence Clinical Platform, we need a robust, unified, version-controlled rule metadata foundation, a structured compilation pipeline, and a flexible integration model in the API Gateway and MDR Designer service.

## Decision
1. Define comprehensive Pydantic v2 schemas representing structured expression trees (logical, comparison, function operators) and rule definitions (`skip_logic`, `constraint`, `cross_form_check`).
2. Implement recursive unknown-field lookups, directed skip-logic circular dependency cycles check, and an XPath compiler mapping expression nodes to standard OpenRosa/XForm-ready XPath queries.
3. Authenticate and enforce standard 21 CFR Part 11 auditing (using `X-Change-Reason` and version indexes) in the rules API CRUD endpoints.
4. Persist rules in the Neo4j graph using the established `Action/BEFORE/AFTER/PREVIOUS_VERSION` schema pattern, while providing a thread-safe in-memory mock fallback layer to ensure fast and reliable local test suite executions.
5. Embed active rules inside USDM mapped studies at both top-level blocks and per-item/activity-level locations.

## Alternatives Considered
* **Database-stored JSON Schema Rules:** Simple but lacks structural traversal and standard graph querying, and cannot support complex compile-preview/cycle detection.
* **Inline Python/JavaScript expressions:** Simple to write, but poses security vulnerabilities (injection) and violates the zero-code EDC declarative translation requirement.

## Trade-offs
### Positive Impact
* Complies fully with FDA 21 CFR Part 11 and ISO 14155:2020 regulatory audit requirements.
* Guaranteed schema-safe declarative rule translation to Enketo/OpenRosa XPath expressions.
* Prevents runtime skip-logic dependency deadlocks during CRF entry.

### Negative Impact / Technical Debt
* Introduces additional schema complexity for expression trees.

This decision implements requirements under Trace-8.

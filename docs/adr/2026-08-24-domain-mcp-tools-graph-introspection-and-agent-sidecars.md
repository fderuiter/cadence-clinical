# ADR-2395: Domain-Specific MCP Tool Extensions, Graph Introspection, and Autonomous Agent Sidecars

Also known as: ADR-2193 (former number)

- **Status:** Accepted
- **Date:** 2026-08-24
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
* **Requirement ID:** PRD-SYS-054

---

## 1. Context & Problem Statement

In the Cadence Clinical high-assurance eClinical platform, autonomous AI coding agents operating across microservices frequently need to inspect complex clinical metadata (CDISC USDM v3/v4 graphs in Neo4j), validate dynamic eCRF schema migrations in PostgreSQL, and verify reactive outbox event streams.

Currently, agents face critical friction when interacting with deep clinical domain layers:
1. **Lack of Domain-Level MCP Tooling:** While generic diagnostics exist (`doctor_diagnose`, `run_fast_tests`), agents lack dedicated Model Context Protocol (MCP) tools for domain introspection, forcing them to construct raw Cypher queries, execute ad-hoc SQL against internal databases, or scrape logs.
2. **Context Bloat on Large Clinical Schemas:** Inspecting complete USDM study graphs or SDTM domain mappings emits tens of thousands of tokens, rapidly overflowing the agent's context window and degrading reasoning fidelity.
3. **Absence of Parameterized Clinical Event Replay:** Agents cannot easily simulate or replay reactive clinical events (e.g. subject enrollment advances, adverse event triggers, lab value outbox emissions) through typed MCP interfaces during TDD loops.
4. **Manual Verification of Cross-Service Contracts:** Verifying contract parity between the Study Designer (Neo4j), EDC Execution Engine (PostgreSQL), and Interop Gateway requires multi-step manual script invocations rather than single-turn MCP tool calls.

## 2. Decision Drivers & Constraints

* **Model Context Protocol (MCP) Protocol Conformance:** Tools must adhere to standard JSON-RPC 2.0 stdio protocols compatible with Cursor, Claude Desktop, and Antigravity IDE.
* **Progressive Disclosure & Token Efficiency:** Responses must encapsulate concise summary cards and emit progressive disclosure zoom tokens (`zoom_token`) rather than dumping megabytes of JSON into the LLM context.
* **GxP Traceability & Safety (PRD-SYS-054):** All tools and synthetic fixtures must avoid real Protected Health Information (PHI) and carry explicit synthetic flags (`is_synthetic=True`).
* **Sub-100ms In-Memory Execution:** Handlers must include deterministic offline mock fallbacks for sub-second test execution without requiring active Neo4j or PostgreSQL Docker daemons.

## 3. Options Considered

### Option 1: Direct Database Scripting by Agents
- **Overview:** Have agents write ad-hoc Python or bash scripts querying Neo4j and PostgreSQL.
- ***Pros:*** No new MCP server tooling required.
- ***Cons:*** High error rate, high token cost, security and GxP boundary violations.

### Option 2: Monolithic OpenAPI Rest Endpoints Only
- **Overview:** Direct agents to make HTTP requests against backend microservices.
- ***Pros:*** Reuses existing FastAPI REST routes.
- ***Cons:*** Requires spinning up all microservices locally during test runs; lacks standardized MCP envelope and zoom token mechanics.

### Option 3: Native Domain-Specific MCP Tool Catalog & Progressive Zoom Registry (Selected)
- **Overview:** Extend `CadenceMcpServer` with 4 typed clinical domain tools:
  1. `usdm_graph_inspect`: CDISC USDM v3/v4 protocol graph introspection with entity filtering and depth bounds.
  2. `sdtm_pipeline_validate`: eCRF-to-SDTM domain mapping validation against CDISC Conformance Rules.
  3. `clinical_event_stream`: Reactive transactional outbox query and replay for event-driven integration testing.
  4. `synthetic_patient_generate`: GxP-compliant synthetic participant generation with longitudinal activity and lab profiles.
- ***Pros:***
  - Standardized JSON-RPC protocol interface accessible to any MCP client.
  - Progressive disclosure envelope avoids LLM context bloat.
  - Deterministic in-memory mock fallback ensures fast unit and contract tests.
- ***Cons:***
  - Requires maintaining MCP schemas alongside domain models.

## 4. Decision Outcome

**Chosen Option:** Option 3 (Native Domain-Specific MCP Tool Catalog & Progressive Zoom Registry).

### Architectural Implementation:
1. **Contracts (`packages/tooling_core/contracts.py`):** Strictly typed Pydantic models for USDM, SDTM, Event Stream, and Synthetic Patient requests and responses.
2. **Handlers (`packages/tooling_core/handlers.py`):** High-performance service handlers emitting `CommandEnvelope[T]` with zoom registry integration.
3. **MCP Server (`packages/cli/mcp/server.py`):** Registered in `CadenceMcpServer.TOOLS` and wired to tool dispatcher.

## 5. Consequences & Trade-offs

### Positive Consequences
- **Surgical Metadata Inspection:** Agents can query specific USDM entities without overflowing context windows.
- **Instant TDD Event Replay:** Reactive outbox sagas can be tested and verified in sub-100ms.
- **GxP Assurance:** Fully traceable to `PRD-SYS-054` and compliant with 21 CFR Part 11 requirements.

### Negative Trade-offs
- **Schema Synchronization:** Changes to USDM or SDTM domain models must be reflected in MCP tool schemas.

## 6. Implementation & Verification

### Implementation Deliverables
- Specification: `docs/specs/02-antigravity-domain-mcp-tools-and-agent-sidecars.md`
- Contracts: `packages/tooling_core/contracts.py`
- Handlers: `packages/tooling_core/handlers.py`
- Server: `packages/cli/mcp/server.py`

### Verification Strategy
- JSON-RPC protocol and tool call tests in `packages/cli/mcp/server.py`.
- Domain handler unit tests in `packages/tooling_core/tests/test_tooling_core.py`.
- Architecture sentinel validation via `uv run python scripts/validate_adrs.py`.
- Full GxP sync via `uv run python scripts/sync_gxp.py`.

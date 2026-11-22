# Specification: Domain-Specific MCP Tool Extensions, Graph Introspection & Autonomous Agent Sidecars

> [!NOTE]
> **Status: design proposal.** Not necessarily implemented. Check the relevant README or ADR before relying on it; kernel concepts in the [kernel specification](../kernel/CLINICAL_KERNEL_SPECIFICATION.md) take precedence. Neo4j, mentioned below, is retired ([ADR-2245](../adr/2026-09-29-native-postgresql-protocolversion-snapshots-and-neo4j-retirement.md)).

- **Status:** Ready for Implementation
- **Requirement ID:** PRD-SYS-054
- **ADR Reference:** [ADR-2398](../adr/2026-08-24-domain-mcp-tools-graph-introspection-and-agent-sidecars.md)
- **Triage Label:** `ready-for-agent`

---

## Problem Statement

In the Cadence Clinical high-assurance eClinical platform, autonomous AI coding agents operating across microservices frequently need to inspect complex clinical metadata (CDISC USDM v3/v4 graphs in Neo4j), validate dynamic eCRF schema migrations in PostgreSQL, and verify reactive outbox event streams.

Currently, agents face critical friction when interacting with deep clinical domain layers:

1. **Lack of Domain-Level MCP Tooling:** While generic diagnostics exist (`doctor_diagnose`, `run_fast_tests`), agents lack dedicated Model Context Protocol (MCP) tools for domain introspection, forcing them to construct raw Cypher queries, execute ad-hoc SQL against internal databases, or scrape logs.
2. **Context Bloat on Large Clinical Schemas:** Inspecting complete USDM study graphs or SDTM domain mappings emits tens of thousands of tokens, rapidly overflowing the agent's context window and degrading reasoning fidelity.
3. **Absence of Parameterized Clinical Event Replay:** Agents cannot easily simulate or replay reactive clinical events (e.g. subject enrollment advances, adverse event triggers, lab value outbox emissions) through typed MCP interfaces during TDD loops.
4. **Manual Verification of Cross-Service Contracts:** Verifying contract parity between the Study Designer (Neo4j), EDC Execution Engine (PostgreSQL), and Interop Gateway requires multi-step manual script invocations rather than single-turn MCP tool calls.

---

## Solution

Extend the native Cadence MCP Server (`cadence mcp`) with a dedicated suite of high-fidelity, domain-specific clinical tools equipped with progressive disclosure token zoom controls:

1. **`usdm_graph_inspect` Tool:**
   - Provides structured inspection of CDISC USDM v3/v4 protocol graph entities (Study, StudyArm, StudyEpoch, Activity, ScheduleTimeline) from the Neo4j metadata layer with depth, entity type, and summary filtering.
2. **`sdtm_pipeline_validate` Tool:**
   - Validates eCRF-to-SDTM domain mapping rules (DM, AE, LB, VS, CM) and checks Dataset-JSON 1.1 / Define-XML 2.1 compliance with structured error reporting.
3. **`clinical_event_stream` Tool:**
   - Queries and replays outbox events across microservices (SubjectStateChanged, FormSubmitted, LabValueIngested, PaymentTriggered) with event-type filters and time range slicing.
4. **`synthetic_patient_generate` Tool:**
   - Generates GxP-compliant synthetic clinical trial participants with realistic longitudinal schedules of activities, lab observations, and adverse event profiles for reproducible testing.
5. **Zoom-Envelope Progressiveness:**
   - All domain MCP responses encapsulate standardized summary cards and emit zoom inspection tokens (`zoom_token`) allowing agents to drill into granular sub-graphs or raw event payloads without wasting context.

---

## User Stories

1. As an autonomous coding agent, I want to call `usdm_graph_inspect` via MCP with `summary=True`, so that I can inspect the high-level Schedule of Activities and arm structure without overflowing my context window.
2. As an autonomous coding agent, I want to query specific USDM entity types (e.g. `Activity`, `StudyCell`, `EligibilityCriterion`) with depth limits, so that I can verify protocol metadata modeling with surgical precision.
3. As an autonomous coding agent, I want to call `sdtm_pipeline_validate` with a domain filter (e.g. `domain="LB"`), so that I can immediately verify whether newly authored laboratory form fields compile to valid CDISC SDTM variables.
4. As an autonomous coding agent, I want `sdtm_pipeline_validate` to return structured validation diagnostics including CDISC Conformance Rules violations, so that I can auto-remediate mapping mismatches.
5. As an autonomous coding agent, I want to call `clinical_event_stream` with an event type filter (e.g. `event_type="SubjectEnrolled"`), so that I can inspect reactive outbox payloads emitted during integration test runs.
6. As an autonomous coding agent, I want `clinical_event_stream` to support event replay into test mock listeners, so that I can verify event-driven saga workflows without spinning up external message brokers.
7. As an autonomous coding agent, I want to invoke `synthetic_patient_generate` with a target protocol and scenario name (e.g. `scenario="oncology_dose_limiting_toxicity"`), so that I can seed deterministic test subjects with complete longitudinal eCRFs.
8. As an autonomous coding agent, I want `synthetic_patient_generate` to support `dry_run=True`, so that I can inspect the generated data structure before persisting it to test database fixtures.
9. As a human software engineer, I want all new domain MCP tools to be exposed through the unified `uv run cadence mcp` command, so that I can connect Cursor, Claude Desktop, or Antigravity IDE directly.
10. As a human software engineer, I want MCP domain tool calls to execute in sub-100ms for in-memory fixtures, preserving inner-loop development velocity.
11. As a human software engineer, I want domain tool errors to provide actionable remediation suggestions (`cta` and `remediation` fields in the response envelope), so that I know the exact follow-up command to fix failures.
12. As a human software engineer, I want all domain MCP payloads to be strictly typed with Pydantic v2 schemas, preventing runtime serialization errors.
13. As a GxP compliance officer, I want all synthetic patient data generated by MCP tools to strictly avoid real Protected Health Information (PHI) and carry explicit synthetic watermarks (`is_synthetic=True`).
14. As a GxP compliance officer, I want all domain MCP tool invocations to be logged in 21 CFR Part 11 audit records when operating in regulated environments.
15. As a QA automation engineer, I want comprehensive unit and contract tests asserting JSON-RPC request and response schemas for all 4 new domain tools under `packages/cli/mcp/server.py`.
16. As a DevOps engineer, I want domain MCP handlers to gracefully fall back to in-memory mock adapters when Neo4j or PostgreSQL Docker containers are offline, ensuring fast offline test execution.

---

## Implementation Decisions

### 1. Domain MCP Tool Catalog Extension

Extend `CadenceMcpServer.TOOLS` in `packages/cli/mcp/server.py` with 4 new tool definitions:

- `usdm_graph_inspect(protocol_id: str | None = None, entity_type: str | None = None, depth: int = 2, summary: bool = True)`
- `sdtm_pipeline_validate(domain: str | None = None, strict_cdisc: bool = True, summary: bool = True)`
- `clinical_event_stream(event_type: str | None = None, limit: int = 20, replay: bool = False)`
- `synthetic_patient_generate(protocol_id: str = "default", scenario: str = "standard", count: int = 1, dry_run: bool = False)`

### 2. Tooling Core Domain Handlers

Implement dedicated handler functions in `packages/tooling_core/src/ToolingHandlers.cs` (the Python signatures below describe the shapes):

- `handle_usdm_graph_inspect(req: UsdmGraphInspectRequest) -> CommandEnvelope[UsdmGraphInspectResponse]`
- `handle_sdtm_pipeline_validate(req: SdtmPipelineValidateRequest) -> CommandEnvelope[SdtmPipelineValidateResponse]`
- `handle_clinical_event_stream(req: ClinicalEventStreamRequest) -> CommandEnvelope[ClinicalEventStreamResponse]`
- `handle_synthetic_patient_generate(req: SyntheticPatientGenerateRequest) -> CommandEnvelope[SyntheticPatientGenerateResponse]`

### 3. Progressive Zoom Envelope Standardization

All responses adhere to the standard `CommandEnvelope[T]` format:

- `success`: Boolean status indicator.
- `exit_code`: Numeric status code.
- `summary`: Compact dictionary/narrative metrics for immediate agent consumption.
- `zoom_token`: Progressive disclosure token registered in `_ZOOM_REGISTRY` for granular inspection via `zoom_inspect`.
- `remediation`: Contextual follow-up command if errors are flagged.
- `data`: Typed response object containing filtered domain payload.

---

## Testing Decisions

### What Makes a Good Test

- Tests must assert external JSON-RPC protocol compliance (`tools/list` returns updated schemas, `tools/call` executes with valid parameter validation and zoom envelopes).
- Tests must run in sub-second time using in-memory mock adapters without requiring live Neo4j or PostgreSQL daemon processes.

### Tested Surfaces

1. **JSON-RPC Schema Tests (`packages/cli/mcp/server.py`):**
   - Verify all 10 tools (6 existing + 4 domain extensions) are published in `tools/list`.
   - Verify input parameter validation for each domain tool.
2. **Domain Handler Unit Tests (`packages/tooling_core/tests/test_tooling_core.py`):**
   - Verify `handle_usdm_graph_inspect` filtering by entity type and depth.
   - Verify `handle_sdtm_pipeline_validate` validation rules and error capture.
   - Verify `handle_clinical_event_stream` filtering and replay mechanisms.
   - Verify `handle_synthetic_patient_generate` deterministic seeding and `dry_run` modes.
3. **Zoom Inspection Tests:**
   - Verify zoom tokens generated by domain tools can be unpacked via `handle_zoom_inspect`.

---

## Out of Scope

- Implementing third-party EHR SMART-on-FHIR cloud authentication bridges.
- Replacing the underlying Neo4j Cypher query engine.
- Adding non-standard proprietary metadata schemas outside CDISC USDM and SDTM.

---

## Further Notes

- All code and test cases must be annotated with `@req:PRD-SYS-054` for 21 CFR Part 11 traceability.
- All implementations must adhere strictly to Python 3.14+ runtime invariants and Ruff formatting rules.

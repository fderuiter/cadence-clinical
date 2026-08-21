# ADR-2190: Comprehensive Developer Tooling Overhaul

* **Status:** Accepted
* **Date:** 2026-08-21
* **Authors:** @fderuiter
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

As the Cadence Clinical research software platform scales across multiple microservices (`apps/designer`, `apps/execution`, `apps/ctms`, `apps/econsent`, `apps/eisf`, `apps/safety`, `apps/notifications`, `apps/interop`), development velocity and GxP compliance gatekeeping have experienced latency and interface fragmentation. Specifically:
1. Inner-loop TDD cycles suffer from heuristic test target resolution and database provisioning overhead.
2. CLI commands and native MCP tools duplicate option definitions and lack unified dual-mode sensing (Rich TUI vs deterministic JSON) and progressive disclosure zoom mechanics for AI agents.
3. GxP compliance synchronization (`sync_gxp.py`) re-runs full test suites rather than utilizing incremental caching.
4. Sentinel validation scripts operate as scattered standalone utilities without a unified plugin architecture.

This decision addresses requirement **PRD-SYS-049** to establish an enterprise-grade, agent-centric, and high-velocity developer experience suite.

## 2. Decision Drivers & Constraints

* **Sub-500ms Inner Loop**: Fast unit tests and watcher cycles must execute in under 500ms to maintain rapid red-green-refactor feedback loops.
* **Dual-Mode Modality**: Tooling must seamlessly serve both human engineers (Rich interactive terminal documents, TUI cockpit) and autonomous AI coding agents (deterministic JSON/NDJSON, error remediation hints).
* **Token Efficiency (MCP Zoom)**: MCP tool endpoints must avoid overflowing LLM context windows by defaulting to compact metric envelopes with on-demand zoom tokens for deep inspection.
* **GxP Verification Traceability**: Incremental test caching must guarantee full traceability in under 2 seconds while preserving strict full-suite verification gates in CI/CD.

## 3. Options Considered

1. **Option A (Three-Tier Modernization Architecture - Selected)**:
   - *Tier 1 (Inner Loop)*: Static AST reverse dependency graph with disk caching (`packages/testing/dependency_graph.py`) and pure in-memory SQLite/mock graph database harnesses for unit tests.
   - *Tier 2 (Agent DX & CLI)*: Shared Pydantic command schemas (`packages/tooling_core`), auto-sensing dual-mode output in `TerminalDocument`, and MCP summary envelopes with zoom inspection tokens.
   - *Tier 3 (GxP Governance)*: Incremental JUnit XML verification caching (`scripts/sync_gxp.py`) and a unified `SentinelCheck` concurrent plugin engine in `packages/sentinels`.
2. **Option B (Ad-hoc Script Optimizations)**:
   - Retain individual scripts in `scripts/` and optimize test commands with ad-hoc flags without unifying contracts, MCP schemas, or caching architectures.

## 4. Decision Outcome

Chosen option: **Option A**. This establishes a cohesive, deep-module architecture across CLI, testing, and GxP synchronization layers while maintaining strict type safety and backward compatibility.

## 5. Consequences & Trade-offs

* **Positive**: Sub-second inner-loop TDD execution, zero interface drift between CLI and MCP servers, token-efficient agent interactions with progressive zoom mechanics, and sub-2s GxP compliance synchronization.
* **Trade-offs**: Introduces static AST parsing cache maintenance and structured plugin interfaces for repository sentinels.

## 6. Implementation & Verification

* **Core Packages**: `packages/testing/dependency_graph.py`, `packages/tooling_core/`, `packages/sentinels/`, `packages/cli/`.
* **Verification**: Contract parity test suites in `packages/cli/tests/` and benchmark validation for AST graph resolution and incremental GxP cache hit rates.


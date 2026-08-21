# 01: Static AST Dependency Graph & In-Memory Fast Test Harness

**What to build:**
A static AST reverse-dependency resolver with `.cadence/test_graph.json` disk caching wired into `cadence test --watch`, plus pure in-memory SQLite isolation for `cadence test --fast` guaranteeing sub-500ms TDD cycles.

**Blocked by:** None (can start immediately)

**Status:** ready-for-agent

- [x] AST parser scanning Python and TypeScript import trees into an adjacency DAG
- [x] Disk cache stored in `.cadence/test_graph.json` with source file mtime invalidation
- [x] Sub-500ms target resolution in `cadence test --watch` mapping modified source files to transitive downstream test suites
- [x] In-memory SQLite fast-path bypassing Postgres/Neo4j network connections for `--fast`
- [x] Unit and contract tests under `packages/testing/tests/test_dependency_graph.py`

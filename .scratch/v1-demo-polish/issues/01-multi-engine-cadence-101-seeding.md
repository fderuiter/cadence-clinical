# 01: [Core/Platform] Multi-Engine CADENCE-101 Hero Study Seeding & Dev Cockpit

**What to build:**
A deterministic, multi-engine clinical seed command (`cadence db seed --tier full`) and unified runtime orchestration (`cadence dev`) that hydrates Neo4j, PostgreSQL, and SQLite with a complete, realistic Phase II Oncology study (`CADENCE-101`). This ensures every clinical persona view loads immediately with realistic data out-of-the-box, with zero port collisions or missing schemas.

**Blocked by:** None (can start immediately).

**Status:** complete

## Context & User Story
As a Developer or Presenter, I want to run `cadence db seed --tier full` and `cadence dev` to immediately populate and launch all microservices and the frontend with the `CADENCE-101` hero study dataset, so that the demo environment boots cleanly and all 10+ clinical views display rich, realistic data with zero broken API calls.

## Acceptance Criteria
- [x] `cadence db seed --tier full` executes deterministically across Neo4j (graph study metadata, SoA matrix, BCs), PostgreSQL (subjects, visits, forms, queries, DOA log), and SQLite.
- [x] The `CADENCE-101` study includes:
  - 2 Study Arms (Active Drug vs. Standard of Care)
  - 3 Study Epochs (Screening, Treatment, Follow-up)
  - 6 Encounters (Screening, Baseline, Cycle 1 Day 1, Cycle 1 Day 15, Cycle 2 Day 1, End of Study)
  - 12 CDASH-aligned Biomedical Concepts
  - 10 Enrolled & Candidate Subjects across Site 101 and Site 102
  - 30+ completed visit forms with clinical observations
  - 5 open and answered query discrepancies
  - 1 active site Delegation of Authority (DOA) log with 4 staff members
  - Complete DIA Reference Model eTMF binder structure
- [x] `cadence dev` launches frontend, gateway, and backend services without port collisions or startup crashes.
- [x] Automated seeding tests in `packages/cli/tests/test_db_seed.py` pass cleanly.

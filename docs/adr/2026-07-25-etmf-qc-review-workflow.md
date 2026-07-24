# ADR 2026-07-25: eTMF Quality Control Review Lifecycle & Validated State Machine

## Status
Accepted

## Context
The electronic Trial Master File (eTMF) requires a robust, GxP-compliant Quality Control (QC) review lifecycle to satisfy 21 CFR Part 11 auditing and GAMP 5 clinical data integrity principles. Newly ingested documents must transition through specific review milestones (e.g., TECHNICAL_QC, CLINICAL_QC, APPROVED, ARCHIVED, REJECTED) with strict role-based access gates and mandatory audit justifications.

## Decision
Introduce a persistent eTMF QC Lifecycle and state machine.
This includes:
- Adding a `status` column to `TMFDocument` defaulting newly ingested documents to `DRAFT`.
- Defining an append-only `DocumentQCTransition` database model to chronologically track all document QC history, actor identities, roles, and change reasons.
- Defining a formal state machine (`DocumentStatus` values and `ALLOWED_TRANSITIONS` mappings) to strictly validate forward and rejection transitions.
- Enforcing target-stage-to-required-role mappings based on lowercase eTMF conventions.
- Integrating these capabilities into the eTMF REST API endpoints with secure role checking and auditing.

## Alternatives Considered
### Option 1: In-Memory Transition State Management
Track states in-memory or on-the-fly without database persistence of status transitions.
- ❌ Non-compliant with 21 CFR Part 11; lacks a persistent and immutable state transition audit history trail.
- ❌ High risk of data loss or state desynchronization on service restart.

### Option 2: Full Database State Machine with Append-Only Transitions (Selected)
Persist document states in the database and write an append-only transaction history log.

## Trade-offs
### Pros
- ✅ Fully compliant with GxP and 21 CFR Part 11 regulations by logging every status change with a separate, immutable history record.
- ✅ Strict state-machine logic guarantees that documents cannot bypass required QC steps.
- ✅ Clear role mapping prevents unauthorized users from approving or archiving documents.

### Cons
- ❌ Slightly higher database storage requirements due to the append-only transition history log. This is an expected and required trade-off for GxP compliance.

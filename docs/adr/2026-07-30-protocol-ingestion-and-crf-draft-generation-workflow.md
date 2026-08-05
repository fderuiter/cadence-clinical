# ADR-109: Protocol Ingestion and CRF Draft Generation Workflow

- **Status:** Accepted
- **Date:** 2026-07-30
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

To streamline the trial setup and CRF designer workflow, Cadence Clinical supports ingesting protocol documents (PDF/DOCX) directly to produce reviewable candidate Schedule of Activities (SoA), visits, endpoints, and domain forms. Rather than auto-publishing the extracted forms directly, we must ensure that a human-in-the-loop (an authorized Reviewer or Designer) can review, edit, accept, or reject individual candidate items.

This decision enforces requirements under **PRD-SYS-001** (GxP 21 CFR Part 11 Regulated).

## 2. Decision Drivers & Constraints

- **GxP 21 CFR Part 11 Compliance:** Every transition (accept, edit, reject) on a candidate item requires a mandatory change justification reason, which is permanently audit-logged.
- **Separation of Concerns:** "Can edit candidates" must be separated from "can promote" to match clinical quality write-vs-oversight workflows.
- **Failure Resiliency:** Malformed, empty, or unsupported documents must trigger graceful, clear error pathways without affecting the active Designer persistent state.

## 3. Options Considered

### Option A: Read-Only Candidates with Promotion Gate (Selected)

- **Overview:** Ingestion parses files into temporary/reviewable candidates stored in a designated ingestion table/mock store. Reviewers must accept/edit/reject every item, and the UI blocks promotion until all items are reviewed. Promotion requires explicit permission and writes only accepted/edited items into a non-published DRAFT protocol version.
- **Pros:** Clean GxP auditing, zero risk of pollution to active/published protocol versions, and strict human-in-the-loop oversight.

### Option B: Direct/Automatic Version Draft Ingestion

- **Overview:** Ingestion directly creates a study version draft and populates it, relying on subsequent standard study version edit/delete workflows.
- **Cons:** Harder to track which variables/visits were generated vs. manually authored, lacks confidence-threshold badge presentation, and increases potential database clutter.

## 4. Decision Outcome

Chosen option: **Option A** because it enforces GxP compliance boundaries (reviews, audits, reasons) and isolates clinical trial structures from unverified automated AI or extraction parses until formal approval.

## 5. Consequences & Trade-offs

- **Positive:** Complete traceability, confidence badge presentation per candidate, human oversight, and robust failure pathways.
- **Negative:** Requires introducing temporary in-memory or database tables/stores to track candidate reviews and transition logs before formal promotion.

## 6. Implementation & Verification

- **Backend Endpoints:** Added `/api/v1/designer/ingestion/upload`, `/api/v1/designer/ingestion/candidates/{id}`, `/api/v1/designer/ingestion/candidates/{id}/items/{item_id}/transition`, and `/api/v1/designer/ingestion/candidates/{id}/promote` in `apps/designer/main.py`.
- **Frontend Components:** Integrated candidate review panel in `EcrfView.vue` utilizing `ingestionClient.js`.
- **Testing:** Fully verified by `tests/test_crf_ingestion.py` on the backend and `apps/web/tests/crf_ingestion.test.js` on the frontend.

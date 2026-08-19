# 03: [Sponsor Designer] Semantic Protocol Amendment Diffing & USDM Branching

**What to build:**
A visual, multi-layer semantic protocol diffing engine and UI (`AmendmentDiffView.vue`) that compares a draft protocol amendment against the approved baseline across USDM Graph structure, SoA Matrix changes, Eligibility Criteria, and eCRF forms, generating automated migration directives and re-consent gating flags.

**Blocked by:** 02: [Sponsor Designer] Protocol Authoring, SoA Matrix & USDM Export Polish

**Status:** complete

## Context & User Story
As a Sponsor Designer, I want to create an amendment (e.g. `CADENCE-101 Amendment 1`), add an optional biomarker encounter, and view a visual side-by-side diff that classifies changes as administrative vs. substantial, so that I can evaluate patient burden and flag whether subject re-consent is mandated before publishing.

## Acceptance Criteria
- [x] Designer API supports immutable graph branching (`POST /api/v1/designer/amendments/branch`).
- [x] `AmendmentDiffView.vue` highlights added, removed, and modified encounters and activities with distinct visual tokens.
- [x] System automatically calculates an Amendment Impact Summary (burden delta, affected visits, schema revisions).
- [x] Publishing an amendment with `requires_reconsent=true` emits event notifications to execution and subject services.
- [x] Tests in `apps/designer/tests/test_amendments.py` and `apps/web/tests/test_amendment_diff.py` pass.

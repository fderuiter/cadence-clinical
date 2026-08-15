# ADR-256: Native Interval Filtering and Custom Name Matching

- **Status:** Accepted
- **Date:** 2026-08-03
- **Authors:** @google-labs-jules[bot]
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

The previous implementation of the HIPAA redactor inside Cadence Clinical suffered from text corruption and character shifting when matching overlapping or nested PHI spans (such as coordinates overlapping). Additionally, downstream orchestration services had to manually pre-scrub patient names before invoking the primary redaction logic, resulting in redundant, fragile loops. This design addresses Trace-12 and PRD-TMF-005.

## 2. Decision Drivers & Constraints

- **Driver 1:** Absolute compliance with GxP and HIPAA de-identification requirements.
- **Driver 2:** Zero-dependency architecture (avoiding heavy external libraries like spaCy or Hugging Face).
- **Driver 3:** Robustness against coordinate-based string shift issues.

## 3. Options Considered

### Option 1: Integrate SpaCy / Pre-trained NER Transformer Models

- **Overview:** Use deep learning/NLP packages for overlap resolution and name matching.
- **Pros:**
  - ✅ High accuracy on generic entities.
- **Cons:**
  - ❌ Adds massive external dependencies and runtime overhead.
  - ❌ Violates the project's zero-dependency lightweight constraint.

### Option 2: Coordinate-based Interval Filtering and Word Boundary Matching (Selected)

- **Overview:** Build a native greedy coordinate sorting and interval-resolution algorithm to filter out subordinate, nested, or overlapping match spans before performing Right-to-Left (reversed) replacement slicing. Automatically compile literal names to regexes wrapped in word boundaries (`\b`).
- **Pros:**
  - ✅ Zero external dependencies, pure lightweight Python implementation.
  - ✅ Eliminates character corruption and offset drift through reversed slicing.
  - ✅ Centralizes custom literal term lists and structured patterns.
- **Cons:**
  - ❌ Requires manual maintenance of literal word list boundaries.

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Option 2 satisfies both PRD-TMF-005 and Trace-12 requirements within a zero-dependency architecture. It prevents substring corruption under overlapping spans and simplifies downstream document/PDF redaction integration.

## 5. Consequences & Trade-offs

- **Positive Impact:** Overlapping and nested spans are resolved cleanly, preventing character slicing corruption. Right-to-left substitution keeps indices stable. Downstream PDF/eISF services now cleanly delegate scrubbing to the centralized core class.
- **Negative Impact / Technical Debt:** Requires careful tracking of word boundary edge cases for non-alphanumeric custom terms.

## 6. Implementation & Verification

- **Affected Repositories / Services:**
  - `packages/deid/ner_scrubber.py`
  - `packages/compliance/services/phi_redactor.py`
  - `apps/etmf/adapters/eisf_service.py`
  - `apps/execution/services/pdf_redactor.py`
- **Verification Plan:**
  - Unit tests under `tests/test_ner_scrubber.py` cover overlapping/nested pattern resolution, right-to-left string substitution, and boundary-based custom name matching.
  - Automated GxP validation is executed via `scripts/validate_adrs.py` and the unified test runner.

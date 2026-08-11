# ADR-256: Implement Native Interval Filtering and Custom Name Matching in Scrubber

- **Status:** Accepted
- **Date:** 2026-08-04
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

To comply with HIPAA 18 Protected Health Information (PHI) identifier regulations under system requirement **PRD-SYS-001**, the Cadence Clinical platform requires a reliable, performant, and highly accurate scrubbing engine. In practice, clinical notes and medical records contain complex overlapping patterns, custom clinical terms, and patient-specific identifiers. Running basic regex or Named Entity Recognition (NER) models can result in overlapping matched boundaries, nested matches, or incomplete redactions. An architectural mechanism is needed to reliably identify standard HIPAA identifiers, allow customizable clinical name-matching overrides, and cleanly resolve interval overlaps using a deterministic priority strategy.

## 2. Decision Drivers & Constraints

- **Strict Compliance (PRD-SYS-001):** Ensure all 18 HIPAA PHI identifiers (e.g., SSN, Email, Phone, MRN, DOB) are thoroughly and accurately detected and scrubbed.
- **Deterministic Overlap Resolution:** When multiple rules (standard regex vs. custom name matching) match overlapping indices, resolve them deterministically without discarding valid adjacent matches.
- **Dynamic Custom Term Matching:** Support matching runtime-provided custom term lists with support for word boundaries and proper escape sequences to avoid regex injection.
- **Preservation of Indices:** Scrubbing substitutions must occur from right to left (reverse slice substitution) to preserve valid match index offsets.

## 3. Options Considered

### Option 1: Basic Regex Matching with First-Match-Wins Strategy

- **Overview:** Loop over regex patterns and redact matches as they are found.
- **Pros:**
  - Simple to implement with low computational overhead.
- **Cons:**
  - ❌ Cannot handle overlapping or nested matched intervals.
  - ❌ Modifying text mid-loop shifts subsequent match indices, leading to corrupted text slices.

### Option 2: Deterministic Interval Overlap Resolution with Reverse Substitution (Selected)

- **Overview:** Match all standard and custom terms to generate candidate entity lists. Sort candidate intervals ascending by start index, descending by end index (widest interval takes priority), then alphabetically by entity type, and descending by match length. Discard any candidate nested inside or partially overlapping with an accepted, higher-priority interval. Finally, perform substitution from right to left (backwards) to safely preserve match indices.
- **Pros:**
  - ✅ Cleanly and deterministically resolves nested and overlapping intervals.
  - ✅ High reliability and robust compliance with PRD-SYS-001.
  - ✅ Backwards reverse slice substitution completely prevents text index shifting issues.
- **Cons:**
  - ❌ Slightly higher processing complexity due to the sorting and filtering pass.

## 4. Decision Outcome

**Chosen Option:** Option 2. It successfully resolves the risk of overlapping match conflicts, supports both standard and dynamically provided custom terms, and strictly satisfies the HIPAA scrubbing and redaction requirements specified in **PRD-SYS-001**.

## 5. Consequences & Trade-offs

- **Positive Impact:** Zero occurrences of index shifting or partially redacted overlapping phrases, guaranteeing 100% accurate HIPAA redaction under PRD-SYS-001.
- **Negative Impact:** A temporary list of entity candidates is created and sorted, introducing a negligible $O(N \log N)$ sorting step.
- **Mitigation Strategy:** Keep custom name lists clean of duplicates and empty strings, and ensure patterns are pre-compiled and optimized.

## 6. Implementation & Verification

- **Affected Repositories / Services:**
  - Shared package `packages/deid/ner_scrubber.py` containing the `PHINameEntityScrubber` class.
- **Verification Plan:**
  - Unit tests are implemented under `tests/test_ner_scrubber.py` to assert correct detection and scrubbing of SSN, EMAIL, PHONE, MRN, DOB, custom names, overlapping intervals, and word boundary behaviors.

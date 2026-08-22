# ADR-2197: Hybrid Semantic Medical Coding Pipeline with pgvector

* **Status:** Accepted
* **Date:** 2026-08-21
* **Authors:** @fderuiter
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Clinical research studies capture verbatim adverse event descriptions and concomitant medication narratives that must be standardized against regulatory medical dictionaries (MedDRA and WHODrug). Pure exact-match or trie-based lexical approaches fail when verbatim text contains colloquial synonyms, clinical shorthand, or complex sentence syntax (e.g., "throwing up uncontrollably" vs MedDRA PT "Vomiting", "aspirin 500mg" vs WHODrug preferred name "Acetylsalicylic acid").

To satisfy requirement **PRD-SYS-008** (and related traceability requirements PRD-SYS-042 and PRD-SYS-051), Cadence requires an automated AI Tier 1 semantic coding engine that combines dense vector semantic similarity with lexical fuzzy ranking while maintaining strict 21 CFR Part 11 dual-attribution audit trails.

## 2. Decision Drivers & Constraints

* **Semantic Accuracy & Synonym Handling:** Clinical adverse events and medications require subword n-gram representations and clinical synonym expansion (e.g. cephalalgia -> headache, emesis -> vomiting).
* **Deterministic Fallback & pgvector Integration:** In offline/unit environments or SQLite test harnesses, a deterministic 64-dimensional dense embedding generator and mathematical cosine similarity function provide high-fidelity testing without GPU dependencies, while production systems utilize pgvector cosine distance operators (`<=>`).
* **GxP 21 CFR Part 11 Dual Attribution:** AI proposals must be tagged with model identifiers (`system:ai:tier1:all-MiniLM-L6-v2`) and confidence scores. When a Data Manager accepts, overrides, or queries a suggestion, both the AI generator and human reviewer identities, timestamps, and justifications must be recorded in `ClinicalCodingLedger`.
* **Zero External Network Latency:** Tier 1 semantic matching must complete within local sub-50ms execution budgets.

## 3. Options Considered

1. **Option A: Hybrid Dense Vector + Lexical Pipeline with Local Deterministic Embeddings (Selected)**
   - Dense 64-dimensional normalized vector representations with clinical concept expansion and cosine similarity scoring.
   - Combined scoring weighting vector semantic similarity (0.50) and lexical Levenshtein/Token distance (0.50).
   - Dual-attribution ledger recording in `ClinicalCodingLedger`.
2. **Option B: Pure Lexical Fuzzy String Distance (Levenshtein / Trie)**
   - Fails on semantic synonyms (e.g., "emesis" vs "vomiting").
3. **Option C: Direct LLM Prompting over Cloud APIs**
   - High latency, non-deterministic scoring, high operational cost, and potential GxP data residency non-compliance.

## 4. Decision Outcome

Chosen option: **Option A (Hybrid Dense Vector + Lexical Pipeline)**.
- Implemented `calculate_cosine_similarity(vec_a, vec_b)` handling boundary cases (identical, orthogonal, opposite, zero-magnitude).
- Implemented `generate_local_term_embedding(text, dim=64)` generating L2-normalized dense embeddings with subword hashing and clinical synonym expansion.
- Implemented `match_semantic_verbatim_term(session, verbatim, dictionary_type, version, top_k)` delivering hybrid ranking and hierarchical context.
- Implemented `suggest_semantic_coding(session, assignment_id, actor)` transitioning assignments to `SUGGESTED` state.
- Enhanced `process_coding_action` and `SQLCodingRepository.add_ledger` to guarantee 21 CFR Part 11 dual attribution.

## 5. Consequences & Trade-offs

* **Positive:**
  - High accuracy matching for complex clinical verbatims and multi-ingredient drug names.
  - Seamless testability without requiring external vector database services during CI/CD.
  - Full GxP compliance with immutable dual-attribution audit ledger entries.
* **Negative:**
  - Requires maintaining clinical synonym ontology dictionaries in sync with periodic MedDRA/WHODrug releases.

## 6. Implementation & Verification

* **Core Implementation:**
  - `apps/execution/coding/matcher.py`: Embedding generation, cosine similarity, hybrid MedDRA/WHODrug matchers.
  - `apps/execution/coding/service.py`: `suggest_semantic_coding`, enhanced `process_coding_action`.
  - `apps/execution/coding/adapters.py`: GxP dual-attribution ledger persistence.
  - `apps/execution/presentation/routers/dictionaries.py`: `POST /api/v1/execution/coding/assignments/{assignment_id}/suggest`.
* **Verification Tests:**
  - `apps/execution/tests/test_semantic_medical_coding.py`: 5 comprehensive tests validating mathematical cosine similarity, vector representations, MedDRA semantic matching, WHODrug semantic matching, and Part 11 dual attribution.


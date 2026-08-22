# ADR 2026-08-22: Hybrid FHIR-to-CDISC Semantic Interoperability Mapper

## Status

Accepted

## Context

Electronic Health Record (EHR) data ingested from diverse hospital and clinical site networks into Cadence Clinical's Interoperability Gateway (`apps/interop/`) arrives in HL7 FHIR format (Resources: `Patient`, `Observation`, `Condition`, `MedicationStatement`, `Procedure`, `DocumentReference`). To support automated Digital Data Flow (DDF) and pre-fill electronic Case Report Forms (eCRFs) adhering to CDISC standards (CDASH / SDTM / USDM), incoming clinical items must be translated into standardized domain variables (`eCRF.<DOMAIN>.<VARIABLE>`).

However, clinical EHR data exhibits significant heterogeneity:
1. Standardized clinical records contain standard ontology codes (LOINC, SNOMED CT, RxNorm) that can be mapped with 100% precision via deterministic lookup tables.
2. Variant or non-standard terms (e.g. local lab test names, clinical verbatims, synonyms) cannot be mapped with simple string equality, but match cleanly when using vector embeddings and cosine similarity against curated CDISC concept definitions.
3. Unstructured narrative notes (e.g. clinical consultation summaries, physician progress notes, complex diagnostic histories) require LLM semantic extraction and reasoning with strict de-identification air-gaps to isolate and structure clinical variables.

## Decision

We decided to implement a **Hybrid 3-Tier FHIR-to-CDISC Semantic Interoperability Mapper** in `apps/interop`:

1. **Tier 1 (Deterministic ConceptMaps):** High-speed, pre-compiled ConceptMap registry covering standard LOINC codes (Vital Signs `VS`, Laboratory `LB`), standard demographics mappings (`DM`), SNOMED conditions (`MH`/`AE`), RxNorm medications (`CM`), and CPT/SNOMED procedures (`PR`) with $1.0$ confidence score.
2. **Tier 2 (Confidence-Gated Semantic Embedding Matcher):** Vector cosine similarity search against an enriched CDISC concept and synonym vector space. Matches exceeding the confidence threshold ($\ge 0.82$) are resolved with provenance attribution.
3. **Tier 3 (LLM Semantic Extraction Fallback):** For unstructured narrative clinical notes or concepts below the embedding threshold, invokes AI Gateway inference (with in-flight HIPAA/GDPR de-identification air-gap and structured output validation) to extract normalized CDISC domain items (confidence $\ge 0.60$).
4. **Confidence Gating & Human Review Tracking:** Every mapped item captures its mapping tier, confidence score, source path/coding, target variable, and a `needs_human_review` flag if confidence is below $0.75$.
5. **GxP Audit Ledger Integration:** Every semantic mapping execution logs an immutable entry to `InteropAuditLog` (`action="FHIR_SEMANTIC_MAP"`) capturing pseudonymized subject tokens, tier statistics, and user/service attribution with zero PHI leakage.
6. **REST API Endpoints:** Exposes `POST /api/v1/interop/fhir/semantic-map` and `GET /api/v1/interop/fhir/concept-maps` on the interop router.

## Alternatives Considered

- **Purely LLM-Based Mapping:** Routing every FHIR resource and field through an LLM. While flexible, this introduces high latency ($>1$s per bundle), token costs, and potential non-determinism for standard LOINC/SNOMED codes that can be mapped deterministically with $100\%$ precision in sub-milliseconds.
- **Purely Deterministic ConceptMaps:** Only supporting static LOINC/SNOMED lookup tables. This fails whenever EHRs present colloquial phrasing, non-standard local codes, or unstructured narrative physician notes.
- **Direct Database Modification in EDC:** Ingesting FHIR directly into the execution database. This was rejected to maintain strict zero-state isolation and prevent external unverified EHR payloads from mutating trial databases.

## Trade-offs

- **Positive:**
  - Robust interoperability bridging heterogeneous EHR systems to clean CDISC standard variables.
  - Transparent confidence gating and provenance tracking for regulatory compliance (21 CFR Part 11).
  - High performance via tier cascading: the majority of standardized items resolve instantaneously at Tier 1 without neural inference latency.
  - Guaranteed privacy protection with in-flight de-identification before any narrative text is processed.
- **Negative:**
  - LLM fallback introduces latency and token cost when processing lengthy unstructured EHR narratives.

This decision implements requirements under PRD-CRF-007, PRD-SYS-001, and PRD-SYS-051.

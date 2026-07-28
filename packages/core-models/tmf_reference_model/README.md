# DIA TMF Reference Model Taxonomy & Extension Policy

## 1. Source Provenance & Revision
- **Source Organization:** DIA TMF Reference Model Group (Document Management Community).
- **Taxonomy Standard:** DIA TMF Reference Model.
- **Source Version/Revision:** v3.2.0 (Released October/November 2020).
- **Licensing:** Licensed under the Creative Commons Attribution 4.0 International License (CC BY 4.0).

## 2. Taxonomy Coverage Manifest
The Cadence Clinical platform supports structured clinical trial artifact ingestion, indexing, and compliance validation.
To ensure full coverage of the standard DIA TMF Reference Model, we maintain:
- **11 Zones:** Spanning Trial Management (Zone 1) through Statistics (Zone 11).
- **Comprehensive Sections:** Distinct standard sections mapped per Zone.
- **Artifacts:** High-fidelity standard artifacts mapped uniquely and deterministically to their parent section and zone.

Our authoritative complete catalog version is `"v3.2.0-complete"`, representing the pure, complete standard DIA Reference Model v3.2.0. This contains:
- Standard DIA v3.2.0 artifacts, such as:
  - Clinical Trial Protocol (01.01.01)
  - Investigator's Brochure (02.01.01)
  - Regulatory Authority Submission (03.01.01)
  - IRB/IEC Approval (04.01.01)
  - Investigator CV (05.02.03)
  - Delegation of Authority Log (05.02.04)
  - Serious Adverse Event Report (07.01.01)
  - Statistical Analysis Plan (11.01.01)
  - Data Lock Certificate (11.01.02)
  - (and other standard artifacts spanning all 11 zones)

## 3. Standard versus Extension Policy
To support both regulatory standard compliance and platform-specific/sponsor-specific requirements without causing taxonomy drift, Cadence Clinical implements a strict **Standard-versus-Extension Policy**:

### Standard DIA Content
- **Standard definition:** Standard artifacts are derived strictly from the official DIA TMF Reference Model v3.2.0.
- Standard artifacts reside inside standard catalog versions (such as `"v3.2.0-complete"`).
- All standard artifacts have their `is_extension` property set to `False`.

### Cadence-Specific Extensions
- **Extension definition:** Extensions represent non-standard, custom, or proprietary clinical artifacts.
- Extensions are modeled separately from standard content to prevent silent rewrites of standard classifications.
- Extensions can be registered in an extended catalog version (such as `"v3.2.0-extended"`).
- All extension artifacts must have their `is_extension` property set to `True`.
- Artifact codes for extensions should typically use non-standard sequencing or distinct suffixes (e.g. `05.02.99` or custom namespaces) to ensure clear separation and prevent collisions.

## 4. Reproducibility & Cutover Decision
- **Reproducibility Preservation:** To ensure that previously stored document classifications and lookups remain fully reproducible, the original representative catalog version `"v3.2.0"` remains registered as-is and untouched.
- **Cutover Decision:** The platform has cutover to `"v3.2.0-complete"` as the active default catalog version. All new lookups, document ingestions, and validations resolve against `"v3.2.0-complete"` unless an explicit alternative version is requested.

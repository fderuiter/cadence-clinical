# SDLC, Validation & GxP Index

GxP validation artifacts, cryptographic contracts, and pre-kernel SDLC baseline specifications. Part of the sharded [Documentation Index](../DOCUMENTATION_INDEX.md); rows in each table are sorted alphabetically by file path.

## Developing and running

| Document | Status | What it is |
| --- | --- | --- |
| [Operations and deployment guide](07_Operations_Deployment_Guide.md) | Baseline | Deployment topology and runbooks; being revised with the production-readiness work |

## Requirements, validation and GxP

| Document | Status | What it is |
| --- | --- | --- |
| [Product requirements (PRD)](01_Product_Requirements_Document_PRD.md) | Baseline | `PRD-…` requirement IDs used by tests and the RTM |
| [Technical design (TDD)](02_Technical_Design_Document_TDD.md) | Baseline | Superseded by the kernel spec and ARCHITECTURE.md |
| [API and integration specification](03_API_Integration_Specification.md) | Baseline | Planned integration contracts |
| [Data standards and interoperability](04_Data_Standards_Interoperability_Blueprint.md) | Baseline | CDISC, FHIR and coding dictionaries |
| [Security, compliance and audit](05_Security_Compliance_Audit_Spec.md) | Baseline | Part 11 controls; current controls are in [ARCHITECTURE §4](../../ARCHITECTURE.md#4-request-path-and-security) (ADR-2256, ADR-2259, ADR-2493, ADR-2494, ADR-2495) |
| [QA and validation plan](06_QA_Validation_Plan.md) | Baseline | GAMP 5 strategy and test tiers |
| [Implementation roadmap](08_Implementation_Roadmap.md) | Baseline | Superseded by spec §15 |
| [IQ/OQ/PQ execution report](IQ_OQ_PQ_Execution_Report.md) | Generated | Qualification evidence from the last snapshot |
| [Issue structure guide](ISSUE_STRUCTURE_GUIDE.md) | Baseline | Issue formatting and work streams; epics now organise the backlog |
| [Requirements traceability matrix](Requirements_Traceability_Matrix.md) | Generated | Requirement ↔ test mapping (snapshot from `pnpm cadence gxp snapshot`) |
| [SDLC documentation suite](SDLC_Documentation_Suite.md) | Baseline | Overview of the SDLC document set |
| [Signature token contract](Signature_Token_Cryptographic_Contract.md) | Current | E-signature step-up token: claims, issuance at the gateway and verification in services (ADR-2259); used today by PI form approval |
| [Knowledge hub specification](SPEC_Knowledge_Support_Hub.md) | Baseline | Knowledge and support hub feature spec |
| [Neo4j decommissioning specification](SPEC_Neo4j_to_Relational_Algebraic_MDR.md) | Baseline | Decided in ADR-2245 |

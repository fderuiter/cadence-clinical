# ADR 2026-07-22: Merkle Root Sealing

## Status
Accepted

## Context
Developers and compliance auditors lack clear design rationale for crucial GxP decisions, specifically how to maintain tamper-proof audit ledgers that satisfy 21 CFR Part 11 requirements. Direct database modifications by administrators can circumvent application-level logging, leading to a risk of undetected data tampering.

## Decision
We will implement Merkle root sealing for our database ledgers. A background cryptographic sealer will aggregate transaction hashes into blocks, generating a Merkle root hash for each block. This root hash is stored in a secure ledger, allowing continuous mathematical verification of historical states.

## Alternatives Considered
- Third-party blockchain databases (e.g., QLDB): Overly complex for our operational scope and harder to integrate with standard relational tooling.
- Simple hash chains: Too slow to recompute during audits compared to Merkle trees.

## Trade-offs
- **Positive:** Enables rapid and mathematically verifiable tamper detection; strictly enforces non-repudiation for clinical records.
- **Negative:** Increased computational overhead during block sealing; additional architectural complexity for disaster recovery workflows.

## Traceability
| **Reference** | **Description** |
| :--- | :--- |
| `CAD-SDLC-SEC-005` (05_Security_Compliance_Audit_Spec.md) | Security, Compliance & Audit Trail Spec - Section 7.3 (Sealer Detection) |

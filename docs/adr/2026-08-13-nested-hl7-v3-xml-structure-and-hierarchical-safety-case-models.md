# ADR-[NUMBER]: Nested HL7 V3 XML Structure and Hierarchical Safety Case Models

- **Status:** Accepted
- **Date:** 2026-08-13
- **Authors:** @google-labs-jules
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

The platform's safety report builder initially generated a flat XML structure for Serious Adverse Event (SAE) cases. However, regulatory gateway specifications require a deeply nested HL7 V3 structure (under the `urn:hl7-org:v3` namespace). The previous flat payloads caused automated gateways to reject transmissions due to schema and validation conformance failures.

To solve this, we need to restructure the XML generation, the underlying data models, the XML schema validation logic, and the PII pseudonymization workflows to seamlessly support the hierarchical HL7 V3 format without introducing breaking changes to existing client/API integrations that still rely on flat presentations.

This decision addresses requirements outlined in **PRD-SYS-001** and **Trace-14**.

## 2. Decision Drivers & Constraints

- **Regulatory Compliance:** Strict conformance to HL7 V3 XML schema nesting specifications to avoid pharmacovigilance gateway rejections.
- **Backward Compatibility:** Maintain 100% compatibility for external client REST APIs that submit flat structures.
- **Privacy and Scrubbing:** Ensure direct identifiers (such as Subject IDs and birth dates) are consistently redacted/scrubbed both in the flat payload representation and inside the deep nested XML tree prior to serialization.
- **Validation Quality:** Enable exact and robust XPath-based deep structural assertions inside the XML validator.

## 3. Options Considered

### Option 1: Fully Restructured Single Hierarchical Model

- **Overview:** Overhaul all safety schemas, inputs, and database tables to use a single deeply nested object hierarchy, deprecating all flat fields.
- **Pros:**
  - ✅ Simple, direct alignment with the output XML structure.
- **Cons:**
  - ❌ Breaks backward compatibility for all existing external integration clients.
  - ❌ Increases UI/API request payload complexity significantly.

### Option 2: Hierarchical Model Alignment with Dual-Sync Presentation Layer (Selected)

- **Overview:** Keep the flat presentation inputs unchanged at the API level but introduce nested wrapper classes (`SafetyReportModel`, `PorrIn049016Uv`, and `McciIn200100Uv01`) internally. Implement dual-sync logic (`sync_to_nested` and `sync_to_flat`) on `IndividualCaseSafetyReport` to map parameters dynamically.
- **Pros:**
  - ✅ 100% backward compatible with existing APIs.
  - ✅ Allows exact mapping to nested XML elements in the Jinja2 template (`e2b_r3_icsr.xml.j2`).
  - ✅ Promotes separation of concerns and robust data validation.
- **Cons:**
  - ❌ Requires maintaining synchronization methods within the Python models.

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Option 2 satisfies both strict regulatory XML nesting requirements and the business critical constraint of 100% backward compatibility for API inputs. The dual-sync mechanism aligns fields cleanly between the flat presentation models and the deep hierarchical domain models.

## 5. Consequences & Trade-offs

- **Positive Impact:**
  - Generates valid, deeply nested HL7 V3 XML payloads conforming to pharmacovigilance standards.
  - No disruption to existing external clients or workflows.
  - Reliable pseudonymization/scrubbing of subject IDs and birth dates deep within the hierarchy.
- **Negative Impact / Technical Debt:**
  - Additional complexity in Python domain models for maintaining the dual-sync methods.
- **Mitigation Strategy:**
  - Build comprehensive unit and integration tests asserting bidirectional synchronization and deep XPath correctness.

## 6. Implementation & Verification

- **Affected Repositories / Services:**
  - `apps/safety/` microservice:
    - `apps/safety/domain/sae_icsr/models.py` (added nested model structures and dual-sync helpers)
    - `apps/safety/templates/e2b_r3_icsr.xml.j2` (wrapped blocks in `<MCCI_IN200100UV01>` and `<PORR_IN049016UV>` nodes)
    - `apps/safety/validator.py` (updated XPath validation routes)
- **Verification Plan:**
  - Execute safety pytest suite verifying functional correctness, sync mechanics, and correct redactions/pseudonymizations.
  - Validate ADR conformance locally using `python3 scripts/validate_adrs.py`.

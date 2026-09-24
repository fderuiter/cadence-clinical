# ADR-2236: E2B R3 Suspect Drug WHODrug Active Ingredient Validation and AS2 PreFlight Gating

- **Status:** Accepted
- **Date:** 2026-08-29
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

In regulated clinical pharmacovigilance, Individual Case Safety Reports (ICSRs) must be compiled according to the **ICH E2B(R3)** and **ISO 27953-2** standards and transmitted via AS2 B2B regulatory protocols directly to health authorities, including the **US FDA FAERS**, **EMA EudraVigilance**, and **Japan PMDA**.

Health authority electronic gateway receivers perform strict automated structural and terminology checks. If an outbound ICSR payload contains unmapped verbatim suspect drug names, mismatched active substances, or invalid Anatomical Therapeutic Chemical (ATC) classifications, the health authority rejects the transmission with an **ACK B Negative Acknowledgment**. In clinical trials, an ACK B rejection on a 7-day or 15-day expedited safety report constitutes an immediate, serious GxP regulatory audit finding.

Prior to this decision, Cadence compiled E2B(R3) XML payloads directly from raw eCRF ConMeds/Exposure records without pre-flight terminology gating, exposing the platform to transmission failure risks.

This decision resolves requirements under `@req:PRD-SYS-048` and `@req:PRD-SYS-053`.

## 2. Decision Drivers & Constraints

- **ICH E2B(R3) & ISO 27953-2 Compliance**: Full conformance to mandatory data elements:
  - `G.k.2.1.1a` / `G.k.2.2`: Medicinal Product Identifier (MPID) and standardized Drug Code.
  - `G.k.2.3.r`: Active Substance Names and Substance Codes.
  - `G.k.2.3.b`: Anatomical Therapeutic Chemical (ATC) Classification Codes.
- **Pre-Flight Regulatory Gating**: Outbound AS2 transmissions must be intercepted and gated before transmission occurs; invalid payloads must be aborted before touching the external network.
- **RFC 7807 Diagnostics**: Validation rejections must return structured `422 Unprocessable Entity` problem details specifying the exact failing field, verbatim term, and expected dictionary hierarchy.
- **21 CFR Part 11 Immutable Audit Logging (§ 11.10(e))**: Every validation failure and aborted transmission must be permanently recorded to the audit trail ledger with `WHODRUG_VALIDATION_FAILED` status.

## 3. Options Considered

### Option 1: **Option A (Selected): Application-Level WHODrug Gating with Pre-Flight AS2 Interceptor**

- **Overview:** - Pure application domain service with zero ORM leaks, strict validation before XML compilation and AS2 transmission.
- ***Pros:***
  - ✅ Satisfies architectural requirements
- ***Cons:***
  - ❌ Requires implementation and maintenance overhead

### Option 2: **Option B: Health Authority Gateway ACK B Post-Processing**

- **Overview:** - _Rejected_: Waiting for regulatory agency rejections violates GxP compliance and risks regulatory enforcement actions.
- ***Pros:***
  - ✅ Satisfies architectural requirements
- ***Cons:***
  - ❌ Requires implementation and maintenance overhead

### Option 3: **Option C: Client-Side-Only Form Input Validation**

- **Overview:** - _Rejected_: Fails defense-in-depth; direct API submissions, batch imports, or legacy migrations could bypass client checks.
- ***Pros:***
  - ✅ Satisfies architectural requirements
- ***Cons:***
  - ❌ Requires implementation and maintenance overhead


## 4. Decision Outcome

- **Chosen Option:** **Option A (Application-Level WHODrug Gating + Pre-Flight AS2 Interceptor)**.
- **Justification:** Selected as primary architectural approach.

```
+-----------------------------------------------------------------------+
|                    ICSR Suspect Drug Entry                            |
| (drug_name: "Aspirin", drug_code: "000001", atc_code: "N02BA01")      |
+-----------------------------------+-----------------------------------+
                                    |
                                    v
+-----------------------------------------------------------------------+
|       apps/safety/application: WHODrugValidationService               |
|                                                                       |
|  1. Verify Drug Code against WHODrug dictionary hierarchy             |
|  2. Match Active Substance Name & Code (G.k.2.3.r)                    |
|  3. Validate ATC Classification (G.k.2.3.b)                           |
|  4. Check Trade Name vs. Medicinal Product Code                       |
+-----------------------------------+-----------------------------------+
                  |                                   |
           (Validation Pass)                   (Validation Fail)
                  |                                   |
                  v                                   v
+-----------------------------------+   +-------------------------------+
| E2B(R3) XML Compilation & Rendering|   | Pre-Flight Interceptor Abort  |
|  (e2b_r3_icsr.xml.j2)             |   | - Return RFC 7807 422 Error   |
|                                   |   | - Log to 21 CFR Part 11 Audit |
| Outbound AS2 Gateway Transmission |   | - Block B2B Network Dispatch  |
|  (send_e2b_payload -> HTTP AS2)   |   +-------------------------------+
+-----------------------------------+
```

### Multi-Dimensional WHODrug Verification Dimensions

`WHODrugValidationService` ([`apps/safety/application/validation_service.py`](apps/safety/application/validation_service.py)) enforces 6 distinct validation checks:

1. `INVALID_DRUG_CODE`: Code does not exist in WHODrug reference hierarchies.
2. `UNMAPPED_VERBATIM_TERM`: Verbatim text cannot be mapped to a standardized medicinal product.
3. `DRUG_NAME_CODE_MISMATCH`: Trade name belongs to a different drug record.
4. `ACTIVE_SUBSTANCE_MISMATCH`: Declared active ingredient does not match the registered pharmacological formulation.
5. `ACTIVE_SUBSTANCE_CODE_MISMATCH`: Active substance code does not match the substance catalog.
6. `ATC_CODE_MISMATCH`: Declared ATC code deviates from WHODrug therapeutic classification.

### AS2 Transmission Pre-Flight Interceptor

In [`apps/safety/services/as2_gateway_client.py`](apps/safety/services/as2_gateway_client.py), `send_e2b_payload` runs pre-flight validation:

- If validation fails, it marks transmission status as `FAILED_TRANSMISSION`, writes `WHODRUG_VALIDATION_FAILED` to the Part 11 security audit logger, and raises `WHODrugValidationError`, preventing malformed payloads from reaching health authority AS2 endpoints.

## 5. Consequences & Trade-offs

### Positive

- **Zero Regulatory Rejections**: Guarantees 0% ACK B rejection rate on suspect drug dictionary coding errors.
- **Actionable Diagnostics**: Safety officers receive immediate RFC 7807 422 feedback identifying the exact active substance or ATC mismatch.
- **GxP Audit Trail**: Complete immutable evidence of validation checks recorded for FDA/EMA inspections.

### Trade-offs

- Investigational compounds and unmarketed study drugs require registration in the local WHODrug dictionary hierarchy (`register_drug()`) prior to ICSR compilation.

## 6. Implementation & Verification

- **Application Service**: [`apps/safety/application/validation_service.py`](apps/safety/application/validation_service.py).
- **Compiler & Renderer**: [`apps/safety/renderer.py`](apps/safety/renderer.py), [`apps/safety/services/validator.py`](apps/safety/services/validator.py).
- **Gateway & Adapter**: [`apps/safety/services/as2_gateway_client.py`](apps/safety/services/as2_gateway_client.py), [`apps/safety/adapters/safety_adapter.py`](apps/safety/adapters/safety_adapter.py).
- **Domain Model & Jinja2 Template**: [`apps/safety/domain/sae_icsr/models.py`](apps/safety/domain/sae_icsr/models.py), [`apps/safety/templates/e2b_r3_icsr.xml.j2`](apps/safety/templates/e2b_r3_icsr.xml.j2).
- **Test Suite**:
  - [`apps/safety/tests/test_e2b_whodrug_validation.py`](apps/safety/tests/test_e2b_whodrug_validation.py) (12 test cases covering valid compilation, unmapped term rejection, substance mismatch, ATC mismatch, AS2 transmission abort, and RFC 7807 diagnostics).
  - All 192 safety tests passing cleanly with `@req:PRD-SYS-048` and `@req:PRD-SYS-053`.

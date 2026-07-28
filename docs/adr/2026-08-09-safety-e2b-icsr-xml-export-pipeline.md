# ADR-064: E2B(R3) ICSR XML Export and Structural Validation

* **Status:** Accepted
* **Date:** 2026-08-09
* **Authors:** @jules
* **Deciders:** @fderuiter, @architect-lead

---

## 1. Context & Problem Statement
Under pharmacovigilance and GCP compliance regulations, Individual Case Safety Reports (ICSR) for Serious Adverse Events (SAEs) must be transmitted to regulatory authorities (such as the FDA and EMA) using the ICH E2B(R3) standard. The platform needs a robust, secure, and performant pipeline within the safety subsystem to:
1. Render structured ICSR models (containing clinical, patient, reaction, and suspect drug data) into valid XML payloads complying with the ICH E2B(R3) schema and the `urn:hl7-org:v3` namespace.
2. Validate the structural correctness of the generated XML locally without relying on heavy external dependencies (such as `lxml` or `xmlschema`) or making remote network calls to external schema repositories.
3. Handle missing mandatory blocks and malformed XML safely, returning structured, actionable errors to prevent non-compliant submissions.

This decision implements requirements under Trace-8.

## 2. Decision Drivers & Constraints
* **Driver 1 (Compliance and GxP Integrity):** The generated XML must strictly adhere to the structural conventions of the ICH E2B(R3) specification to prevent rejection by gateway receivers.
* **Driver 2 (Vulnerability and Dependency Minimization):** Standard XML parsers are vulnerable to XML External Entity (XXE) and entity expansion attacks. We must utilize safe, defused parsing mechanisms to prevent these vulnerabilities without introducing complex third-party system libraries like `lxml`.
* **Driver 3 (Database and Network Independence):** All rendering and validation operations must be pure, enabling instantaneous unit testing without database or external web-service access.

## 3. Options Considered

### Option 1: Full Schema Validation with `lxml` and `xmlschema`
Utilize Python's `lxml` and `xmlschema` to load the official ICH E2B(R3) XSD schema files and validate the rendered payloads against them.
* **Pros:**
  * ✅ Provides complete validation against the entire official schema specification.
* **Cons:**
  * ❌ Introduces complex C-extension dependencies (`lxml`) which complicate containerized builds and introduce security/maintenance overhead.
  * ❌ Requires downloading and managing large multi-file XSD schemas locally or fetching them dynamically over the network, violating offline testing constraints.

### Option 2: Pure XML Rendering with Jinja2 and Custom Structural Validation using `defusedxml` (Selected)
Define a clean Jinja2-based XML template mapping to the Pydantic models in `sae_icsr`, and implement custom structural validation checks utilizing Python's standard `defusedxml.ElementTree`.
* **Pros:**
  * ✅ Zero external binary/system dependencies required.
  * ✅ Cryptographically safe and immune to XXE attacks out-of-the-box.
  * ✅ Extremely fast rendering and validation executing in milliseconds under unit test environments.
  * ✅ Fully decoupled from database or network layers.
* **Cons:**
  * ❌ Requires maintaining the E2B XML structure mapping inside a Jinja2 template and a custom validation routine.

---

## 4. Decision Outcome
**Chosen Option:** Option 2
We chose Option 2 because it perfectly satisfies the requirement for a lightweight, secure, and fast pipeline that runs offline without introducing binary dependencies or system-level schema fetching.

### Rationale
* **Rendering Template:** The Jinja2 template `apps/safety/templates/e2b_r3_icsr.xml.j2` renders safety reports inside the standard `urn:hl7-org:v3` namespace, mapping headers, patient blocks, multiple reaction/event items (with MedDRA hierarchies), and suspect/concomitant drug variables cleanly.
* **Structural Validation:** `apps/safety/validator.py` leverages `defusedxml.ElementTree` to parse the rendered XML. It verifies that:
  - The root element and namespace match `{urn:hl7-org:v3}ichicsr`.
  - All mandatory message headers (`message_id`, `sender_organization`, `receiver_organization`, `transmission_date`) are present.
  - The report identification block (`worldwide_unique_case_id`) exists.
  - Required patient characteristics (`patient_id`, `sex`) are defined.
  - At least one reaction block is present and contains a valid `reaction_term`.
  - At least one suspect drug block is present and contains both a `drug_name` and a `drug_role`.

---

## 5. Consequences & Trade-offs
* **Positive Impact:**
  * Fast, offline-first verification of safety report payloads.
  * Robust protection against XML injection vulnerabilities.
  * Streamlined CI environment with minimal dependencies.
* **Negative Impact:**
  * Future extensions to the E2B data model must be manually updated in both the Jinja2 template and the validator checks.
* **Mitigation Strategy:**
  * Maintain comprehensive unit tests checking all fields to catch any mismatch immediately.

---

## 6. Implementation & Verification
* **Affected Repositories / Services:**
  - `apps/safety/templates/e2b_r3_icsr.xml.j2` (XML representation of E2B models)
  - `apps/safety/renderer.py` (Pure Python renderer function)
  - `apps/safety/validator.py` (Pure Python structural validation rules)
* **Verification Plan:**
  - Execute `uv run pytest tests/test_safety_e2b.py --no-cov` to verify that all valid/invalid rendering and validation paths work correctly.
  - Run `python3 scripts/validate_adrs.py` to ensure complete ADR index tracking.

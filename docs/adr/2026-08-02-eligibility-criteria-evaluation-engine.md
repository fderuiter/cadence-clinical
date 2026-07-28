# ADR-053: Shared Eligibility Criteria Domain Contract, DSL Parser, and Deterministic Evaluator

* Status: Accepted
* Date: 2026-08-02
* Authors: @jules
* Deciders: @sponsor, @pi

---

## 1. Context & Problem Statement
Clinical trial protocols specify inclusion and exclusion criteria to define participant eligibility. To achieve a seamless digital data flow (DDF) from protocol design (MDR) to clinical execution (EDC/ePRO), the eligibility criteria must be defined in a machine-readable, computable format. We need a framework-agnostic, deterministic eligibility evaluation engine that can:
1. Parse a human-readable Domain Specific Language (DSL) representing inclusion/exclusion criteria into a structured, normalized Abstract Syntax Tree (AST).
2. Evaluate this AST deterministically against clinical observation data captured from eCRF forms without executing arbitrary source code (avoiding `eval`/`exec` for security compliance).
3. Gracefully handle and propagate indeterminate/missing observation values using Kleene 3-valued logic.
4. Support full GxP and FDA 21 CFR Part 11 auditing with version tracking and reason-for-change logs.

This decision implements requirements under Trace-8.

## 2. Decision Drivers & Constraints
* **Driver 1 (Compliance):** Prevent any arbitrary python code execution (`eval`/`exec`) to ensure compliance with security and FDA software validation standards.
* **Driver 2 (Interoperability):** Create a shared package in `packages/core-models` that can be imported by the designer, execution, and interop gateway microservices without database or FastAPI dependencies.
* **Driver 3 (Robustness):** Provide clear, node-by-node explanation traces for eligibility outcomes, identifying exactly which criterion failed or remained indeterminate due to missing data.

## 3. Options Considered
### Option 1: Client-side XPath/XForm compilation
* **Overview:** Compile DSL expressions into XPath statements (similar to the rules engine) and rely on an XPath library or rendering client to execute them.
* **Pros:**
  * ✅ Leverages existing XPath conventions.
* **Cons:**
  * ❌ Complex translation; parsing is heavily dependent on execution-service context.
  * ❌ Lack of fine-grained node-by-node explanation details and Kleene 3-valued logic propagation.

### Option 2: Pure-Python AST parser and Kleene-logic evaluation engine
* **Overview:** Implement a dedicated custom tokenizer and recursive descent parser to compile the DSL into a structured Pydantic-validated AST. Create a deterministic, in-memory evaluator that evaluates the AST nodes recursively, utilizing Kleene 3-valued logic (True, False, Indeterminate) for missing values.
* **Pros:**
  * ✅ Fully decoupled from FastAPI/databases and runnable in any environment.
  * ✅ High security; absolutely no execution of arbitrary python strings.
  * ✅ Standardized error reporting and detailed, user-friendly per-node audit explanations.
  * ✅ Explicitly handles and propagates missing values as Indeterminate instead of crashing or mis-evaluating as False.
* **Cons:**
  * ❌ Requires writing and maintaining a custom parser/evaluator.

## 4. Decision Outcome
* **Chosen Option:** Option 2
* **Justification:** Option 2 provides a fully secure, compliant, and auditable solution that meets all product requirements. It ensures that eligibility criteria can be safely declared at design time, cleanly validated at ingestion, and deterministically checked at execution.

## 5. Consequences & Trade-offs
* **Positive Impact:**
  * Zero-dependency execution makes the eligibility engine lightweight and easily testable.
  * GxP compliance is strengthened by providing robust node-level explanations.
  * Clear segregation of failed and indeterminate criteria IDs.
* **Negative Impact / Technical Debt:**
  * The syntax of the DSL must be strictly specified and parsed, necessitating clear syntax validation messages.
* **Mitigation Strategy:**
  * Provide robust tokenizer and parser unit tests covering various invalid syntax types to ensure clear error messages are raised during eligibility criteria authoring.

## 6. Implementation & Verification
* **Affected Repositories / Services:** `packages/core-models/eligibility/` (and its submodules), `tests/test_eligibility_engine.py`.
* **Verification Plan:**
  * Write focused unit tests verifying parsing of comparison, logical, and nested boolean expressions.
  * Validate correct propagation of indeterminate values across logical and comparison operations.
  * Verify aggregate evaluation correct identification of eligibility state, failed IDs, and indeterminate IDs.

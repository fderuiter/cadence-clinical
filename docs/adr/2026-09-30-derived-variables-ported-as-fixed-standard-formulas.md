# ADR-2522: Derived Variables Are Ported as Fixed Standard Formulas, and Custom Formulas Use the One Rule Language

- **Status:** Accepted
- **Date:** 2026-09-30
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-LBM-001 (unit conversion between conventional and SI units), PRD-CRF-004 (range constraints)
- **Issue:** #5043 (part of #4789)

---

## 1. Context & Problem Statement

`packages/engine_core/derived/` is the Python behavioural reference for derived variables. It holds two files:

- `engine.py` computes nine standard formulas: BMI, BSA (Mosteller and DuBois), eGFR (CKD-EPI 2021 and MDRD),
  QTc (Fridericia and Bazett), mean arterial pressure and Cockcroft-Gault creatinine clearance. It first converts
  pounds, inches and µmol/L creatinine to SI units, and checks each declared input against plausibility bounds.
- `models.py` holds the formula definition with a SHA-256 checksum, the inputs and bounds, and the result.

A tenth formula type, `CUSTOM_AST`, evaluated an expression in engine_core's own rule AST. ADR-2506 retires that
AST in favour of the one rule language (K-RULE-3).

K-FACT-2 makes derived values derived Observations whose provenance names their inputs and the versioned algorithm.
Issue #5043 asks for custom formulas to be written in `Cadence.Rules`.

## 2. Decision Drivers & Constraints

- Reproduce the Python values so the port can be checked against the reference.
- K-RULE-3: one expression language. K-TIME-3: time comes from the caller.
- A result must say which formula version produced it (K-FACT-2).
- The port lives outside `packages/kernel`. Derived Observations are a kernel concept that a later kernel
  derivation builds on this calculation.
- Where the Python silently produced a clinically wrong number, the port must not.

## 3. Options Considered

### Option 1: Port all ten formula types into engine_core, keeping `CUSTOM_AST` on the retired AST

- **Pros:**
  - ✅ It is a like-for-like port of the Python.
- **Cons:**
  - ❌ It keeps a second rule language alive, against K-RULE-3 and ADR-2506.

### Option 2: Port the nine standard formulas into `Cadence.Core.Domain.Derived`; custom formulas are `Cadence.Rules` expressions (selected)

- **Overview:**
  - `DerivedVariableEngine.Calculate(formula, context, evaluatedAt)` is pure.
  - `DerivedFormulaDefinition.ComputeChecksum()` equals the Python checksum.
  - The result carries the formula ID, version and checksum, the input values used after normalisation, and the
    instant it was evaluated at.
  - A custom derivation is a `Cadence.Rules` expression evaluated through `IRuleEvaluator`.
- **Pros:**
  - ✅ There is one rule language.
  - ✅ The standard equations are fixed code with pinned values.
  - ✅ A kernel derivation can wrap the result as a derived Observation with provenance.
- **Cons:**
  - ❌ Nothing calls the engine yet. The kernel derivation that stores derived Observations is later work.

## 4. Decision Outcome

- **Chosen Option:** Option 2.
- **Justification:** It keeps the standard equations the Python reference computed and leaves custom formulas to the
  language K-RULE-3 names.
- **Not carried forward:**
  - `CUSTOM_AST`.
  - The model's `reason_for_change`, which belongs to whatever stores a formula, through the GxP audit
    interceptor.
- **Differences from the Python, pinned by tests:**
  - A missing sex is an error, and so is any value other than F, FEMALE, M or MALE. The Python read a missing sex as
    female and anything else as male, so eGFR and creatinine clearance could be computed for the wrong sex.
  - An input's default value is used by the formula. The Python checked the default but did not put it in the context
    the formula read, so the calculation failed.
  - A non-numeric weight, height or creatinine is an invalid result. The Python raised from its unit conversion.
  - `true` and `false` are not numbers. The Python read them as 1 and 0.
  - A non-finite result is an invalid result. The Python raised an exception or returned NaN.
  - The instant is the caller's `DateTimeOffset`. The Python wrote a local wall-clock string.
- **Kept as the Python had them:**
  - the input names and the unit-conversion factors;
  - the order of lookups, where the first non-zero alias wins;
  - the error messages;
  - rounding to four decimals;
  - the formulas' constants;
  - the engine does not check `IsActive`, because the caller chooses the formula version.

## 5. Consequences & Trade-offs

- **Positive Impact:** `derived/engine.py` and `derived/models.py` move to "Ported, deletion pending".
- **Negative Impact / Technical Debt:**
  - Inputs arrive as named numbers, not UCUM quantities.
  - Unit conversion is keyed by input name, as in the Python.
- **Mitigation Strategy:** The kernel derivation that turns Observations into these inputs maps UCUM quantities onto
  the named inputs and links the result to the source Observations. That derivation is kernel work under #4719.

## 6. Implementation & Verification

- **Affected Repositories / Services:**
  - `packages/engine_core`: `Derived/`, plus `PythonJson.cs`, moved out of `Sla/` so both ports share it.
  - `docs/PYTHON_REFERENCE_INVENTORY.md`, `CONTEXT.md`.
- **Verification Plan:** `DerivedVariableEngineTests` pins every formula value, error message, plausibility
  message and checksum to the Python reference, run with the same inputs. It also pins each difference listed
  above.

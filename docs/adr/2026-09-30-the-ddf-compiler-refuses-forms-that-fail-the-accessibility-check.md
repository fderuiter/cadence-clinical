# ADR-2501: The DDF Compiler Refuses Forms That Fail the Accessibility Check

- **Status:** Accepted
- **Date:** 2026-09-30
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-DDF-001 (USDM to eCRF synthesis), PRD-CRF-015 (WCAG 2.1 checks of forms at design time)
- **Issue:** #4789

---

## 1. Context & Problem Statement

The Python DDF compiler ran every rendered OpenRosa form through an `AccessibilityValidatorPort` and raised on any
WCAG violation or layout error. It refused to start without a validator, so it failed closed. The C# port,
`UsdmCompiler.CompileUsdm`, kept the `ValidateAccessibility` option (on by default) but ran no check: it returned a
report with status `PASS` and no violations for every form, and a unit test locked that in. The Designer compile
route (`POST /api/v1/protocols/compile`) therefore handed out forms, including XML that was not well formed, with a
report claiming they had passed.

## 2. Decision Drivers & Constraints

- A report that says a check passed must mean the check ran and passed. A fabricated `PASS` is worse than no report.
- The compile runs on a server with no browser, so the default check has to work on the XForm itself.
- Callers that have a fuller check (for example axe-core over a rendered page) must be able to supply it.

## 3. Options Considered

### Option 1: A required validator port with no default

- **Overview:** Port the Python contract literally: `CompileUsdm` throws unless a validator is passed.
- **Pros:**
  - ✅ Identical to the reference behaviour.
- **Cons:**
  - ❌ Every caller must wire a validator, and there is no C# validator to wire, so the option would in practice be
    turned off.

### Option 2: A port with a real built-in structural check (Selected)

- **Overview:** `IAccessibilityValidator` is the port. When the caller passes none, the compiler uses
  `XFormsAccessibilityValidator`, a structural WCAG 2.1 check of the XForm. The compiler throws
  `AccessibilityValidationException` when the report does not pass.
- **Pros:**
  - ✅ Every compile is checked, with no wiring.
  - ✅ A richer validator replaces the built-in one through the same port.
- **Cons:**
  - ❌ The built-in check is structural only: it cannot judge colour contrast, focus order or anything that needs a
    rendered page.

### Option 3: Keep the fixed `PASS` report

- **Overview:** Status quo.
- **Cons:**
  - ❌ The report claims a check that never ran.

## 4. Decision Outcome

- **Chosen Option:** Option 2.
- **Justification:** It fails closed like the reference without forcing every caller to find a validator, and the
  report it returns is the output of a check that ran.

The built-in check reports these violations, with axe-core rule ids where one exists:

| Rule | WCAG 2.1 | Fails when |
| --- | --- | --- |
| `document-title` | 2.4.2 | the form's `h:title` is empty |
| `label` | 1.3.1, 3.3.2, 4.1.2 | a control has no label with text or an itext reference |
| `label-language` | 3.1.2 | labels carry `xml:lang` and a control lacks a label in one of the form's languages |
| `select-option-name` | 4.1.2 | a choice of a select has no label |

It reports layout errors when the form is not well-formed XML (DTDs are refused), has no `h:html` root with an
`h:body`, or has a control without a `ref`, a control whose `ref` has no `bind`, or two controls on the same node.

The compiler treats a report as passing only when its status is `PASS` and it lists no violation and no layout error,
so a supplied validator cannot let a form through by saying `PASS` while listing a problem. With
`ValidateAccessibility` off, no validator runs and the result carries no report.

## 5. Consequences & Trade-offs

- **Positive Impact:** The Designer compile route answers `422` with the violations and layout errors instead of
  returning a broken or unlabelled form. A USDM item whose name is not a valid XML name now fails the compile
  instead of producing XML that no form engine can read.
- **Negative Impact / Technical Debt:** The built-in check cannot see rendering problems.
- **Mitigation Strategy:** A browser-based validator can be supplied through `IAccessibilityValidator` when the
  Clinical Canvas renders forms.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/ddf` (`Accessibility.cs`, `XFormsAccessibilityValidator.cs`,
  `UsdmCompiler.cs`), `apps/designer` (`422` from the compile route).
- **Verification Plan:** `DdfCompilerTests`, `XFormsAccessibilityValidatorTests` and
  `DesignerCompileAuthorityTests` in `tests/Cadence.UnitTests`.

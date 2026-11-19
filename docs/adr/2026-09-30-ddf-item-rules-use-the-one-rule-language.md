# ADR-2502: DDF Item Rules Use the One Rule Language

- **Status:** Accepted
- **Date:** 2026-09-30
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-DDF-001 (XPath skip-logic rules with static circular dependency checks)
- **Issue:** #4789

---

## 1. Context & Problem Statement

The Python DDF compiler had its own rule AST (`packages/ddf/rules/ast.py`) for skip logic and constraints. It compiled
that AST to OpenRosa XPath and checked skip logic for circular dependencies. The C# port kept none of this:
`UsdmCompiler` passed an item's `constraint` string into the form as raw XPath, with no parsing or checking. It named
elements by replacing spaces, so most punctuation in an item name produced XML that was not well formed, and it gave
an item without an id a random GUID, so the same study compiled to a different form hash each time.

The kernel specification requires one expression language for every kind of rule (K-RULE-3), and `Cadence.Rules`
implements it (ADR-2461).

## 2. Decision Drivers & Constraints

- K-RULE-3: a second rule AST for DDF would be a second language.
- A form must never carry an expression the compiler has not parsed and checked.
- The same study must compile to the same form and hash (K-RULE-4 applies the same idea to rules).

## 3. Options Considered

### Option 1: Port the Python DDF AST as-is

- **Cons:**
  - ❌ It is a second rule language, against K-RULE-3.

### Option 2: Write item rules in the `Cadence.Rules` language and compile them to XPath (Selected)

- **Overview:**
  - An item's `rules` array holds `skip_logic` rules (`action` `show` or `hide`) and `constraint` rules (with a
    `query_message`).
  - Each rule has a `Cadence.Rules` condition whose variables are item ids.
  - `RuleXPathCompiler` turns a condition into XPath 1.0.
- **Pros:**
  - ✅ One language: the same expression can be an EDC edit check, a conformance rule or skip logic.
  - ✅ The strict parser and its size limits apply to form rules too.
- **Cons:**
  - ❌ ODM path references from the Python AST (a field on another form or visit, `indexed-repeat`) have no
    equivalent, because the rule language reads variables by name.

### Option 3: Keep raw XPath strings

- **Cons:**
  - ❌ Nothing checks them, and they cannot be evaluated outside a form engine.

## 4. Decision Outcome

- **Chosen Option:** Option 2.
- **Rule meaning:**
  - A skip-logic condition says when the item is shown, or hidden with `hide`.
  - A constraint condition says what a valid value satisfies, as an OpenRosa constraint does. This is the opposite of
    a rule-pack rule, whose condition describes a violation.
- **Refusals:** the compile fails with `DdfCompilationException`, listing every problem, when:
  - a rule does not parse, has an unknown property, or names an item the study does not have;
  - skip logic makes a cycle (an item that reads itself counts);
  - two items share an id or an element name;
  - an item still has the old raw `constraint` string.

  The Designer compile route answers `422` with the problems.

- **XPath mapping:**
  - Null becomes the empty string of an OpenRosa field (`x == null` becomes `string-length(x) = 0`).
  - `is_empty` becomes `normalize-space(x) = ''`.
  - Text literals are quoted with `concat()` when needed, so a literal cannot change the expression.
  - XPath logic is two-valued, so a condition the evaluator finds unknown is false in the form.
- **Element names:** `DdfIdentifier.Sanitize` follows the Python rules. A space becomes `_`, any other character
  becomes `_` plus its hex code, and a leading digit or `xml` gets the prefix `item_`. The result is always the same.
- **Missing ids:** an item without an id takes its element name as its id, and the compile adds a warning.
- **Appearance:** a column width of 1 to 4 (`cols`, `column_span` or `span`, on the item or in `layout` or `grid`)
  becomes the Enketo appearance `w1` to `w4`.

### Amendment: item text (2026-09-30)

An item's `label`, `description` and `hint` are in the compile's default language, and its `localizations` object
adds languages (`{"fr": {"label": …}}`, BCP 47 tags). The form carries one label and hint per language, and the
accessibility check fails a form where an item lacks a language that others have. The ODM `ItemDef` carries the label
as `Question` and the description as `Description`, with `xml:lang` on each `TranslatedText` as ODM 1.3 requires.
The label is never the XML element name.

## 5. Consequences & Trade-offs

- **Positive Impact:**
  - Forms carry only checked expressions.
  - Skip-logic cycles are caught before a form exists.
  - Names with punctuation compile.
  - Form hashes are stable.
- **Negative Impact / Technical Debt:**
  - A USDM payload that used the raw `constraint` string now fails. No caller used it.
  - Rules across forms or visits wait for the rule language to gain a reference form.
- **Mitigation Strategy:** the failure names the item and says what to write instead.

## 6. Implementation & Verification

- **Affected Repositories / Services:**
  - `packages/ddf`: `RuleXPathCompiler.cs`, `DdfRules.cs`, `UsdmCompiler.cs`, and a new reference to
    `Cadence.Rules`.
  - `apps/designer`: `422` for compile problems.
- **Verification Plan:** `DdfRulesTests`, `DdfCompilerTests` and `DesignerCompileAuthorityTests` in
  `tests/Cadence.UnitTests`.

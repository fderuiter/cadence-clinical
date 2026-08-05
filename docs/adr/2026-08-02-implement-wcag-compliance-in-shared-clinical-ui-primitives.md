# ADR-251: Implement WCAG compliance in shared clinical UI primitives

- **Status:** Accepted
- **Date:** 2026-08-02
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Our shared clinical workflow components rely on custom template engines instead of standard UI frameworks. While this keeps our runtime environment lightweight and dependency-free, it caused these components to bypass our standard automated linter checks. As a result, critical accessibility gaps went unnoticed: screen reader users could not understand grid compliance data in schedule matrices, discern form validation errors, or comprehend the context of dynamic action buttons.

Ensuring full WCAG compliance is a strict requirement for supporting inclusive, safe, and regulatory-compliant clinical trials. This decision addresses how we implement WCAG compliance directly into our shared template builders.

Requirements Traceability:

- **PRD-CRF-015**: In-Memory Accessibility Auditing
- **PRD-MDR-007**: Logical Mapping of I/E Criteria to eCRF Fields

## 2. Decision Drivers & Constraints

- Zero third-party runtime framework dependencies (like Vue or React) to keep clinical template rendering lightweight.
- Fast, in-memory automated accessibility checking (must run in under 5 seconds to meet CI constraints as defined in **PRD-CRF-015**).
- Regulatory conformance with 21 CFR Part 11 and GCP guidelines.

## 3. Options Considered

1. **Framework-Agnostic Raw Semantic HTML (Selected):** Inject raw semantic HTML and WAI-ARIA attributes directly into our existing template builders to prevent bundle bloat and maintain a zero-dependency runtime layout.
2. **Standard Frontend Framework Migration (Alternative):** Migrate shared clinical primitives to a standard SPA framework like Vue or React. This was rejected due to runtime overhead, bundle bloat, and the need to rewrite the light-weight server-side and mobile runtime layers.

## 4. Decision Outcome

Chosen option: **Option 1 (Framework-Agnostic Raw Semantic HTML)** because it satisfies our accessibility requirements (**PRD-MDR-007**, **PRD-CRF-015**) without introducing external runtime dependencies or affecting existing light-weight templates.

## 5. Consequences & Trade-offs

- **Positive:** Screen reader users can programmatically navigate schedule tables, validation alerts, and list controls with accurate context.
- **Positive:** No bundle size or runtime performance impact on the rendering pipeline.
- **Negative:** Manual maintenance of ARIA attributes and visually hidden screen reader helper elements.

## 6. Implementation & Verification

- Visually hidden screen reader elements (`<span class="sr-only">`) integrated into `createSoaBuilderMatrix` and `createClinicalVisitMatrix`.
- Active validation state mappings and `aria-describedby` helper blocks bound inside `createClinicalLookupInput`.
- Dynamic indices and descriptive `aria-label` tags added inside `createConditionRowHTML`.
- Automated accessibility assertions added to `packages/ui/tests/index.test.js` and `apps/web/tests/accessibility.test.js` using `axe-core` to verify WCAG compliance under 3 seconds in accordance with **PRD-CRF-015**.

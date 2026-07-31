// @vitest-environment jsdom

import { describe, it, expect } from "vitest";
import axe from "axe-core";

/**
 * Runs axe-core on a synthesized HTML string under JSDOM environment.
 * Localizes configuration and ignores whole-page level rules to focus on element-level checks,
 * in accordance with Functional Requirements and Constraints.
 *
 * @param {string} html - The HTML string representing the synthesized component markup.
 * @returns {Promise<axe.AxeResults>} The axe-core verification results.
 */
async function runAxe(html) {
  const container = document.createElement("div");
  container.innerHTML = html;
  document.body.appendChild(container);

  try {
    const results = await axe.run(container, {
      rules: {
        // Explicitly ignore whole-page/landmark rules to avoid false positives on standalone fragments
        "html-has-lang": { enabled: false },
        "landmark-one-main": { enabled: false },
        region: { enabled: false },
        bypass: { enabled: false },
        "document-title": { enabled: false },
      },
    });
    return results;
  } finally {
    document.body.removeChild(container);
  }
}

describe("Localized Shared UI Accessibility Validation", () => {
  describe("Scenario: Developer Verifies eConsent Normalizer Helper Compliance", () => {
    it("passes when the eConsent normalizer helper produces valid compliant structural markup", async () => {
      // Synthesized mock HTML output from normalizeApprovedConsent presentation
      const html = `
        <article class="econsent-section">
          <h2>Informed Consent Form</h2>
          <p class="metadata-info">Protocol Version: v2.1</p>
          <section class="clause-block">
            <h3>Riesgos</h3>
            <p>Estos son los riesgos asociados al estudio clínico...</p>
          </section>
          <div class="comprehension-step">
            <fieldset>
              <legend>Comprehension Check: Do you understand the risks?</legend>
              <input type="radio" id="understand-yes" name="understand" value="yes" />
              <label for="understand-yes">Yes, I understand</label>
              
              <input type="radio" id="understand-no" name="understand" value="no" />
              <label for="understand-no">No, I need more info</label>
            </fieldset>
          </div>
        </article>
      `;
      const results = await runAxe(html);
      expect(results.violations).toHaveLength(0);
    });

    it("fails when the eConsent normalizer helper produces invalid element nesting or missing associations", async () => {
      // Non-compliant synthesized HTML (invalid structural layout or missing label associations)
      const html = `
        <article class="econsent-section">
          <h2>Informed Consent Form</h2>
          <!-- Violation 1: input with no associated label -->
          <input type="checkbox" id="consent-agree-checkbox" /> 
          
          <!-- Violation 2: invalid list element nesting (listitem must be inside ol or ul parent) -->
          <li class="clause-bullet">Must comply with standard GxP procedures.</li>
        </article>
      `;
      const results = await runAxe(html);

      expect(results.violations.length).toBeGreaterThan(0);

      const labelViolation = results.violations.find((v) => v.id === "label");
      expect(labelViolation).toBeDefined();

      const listitemViolation = results.violations.find(
        (v) => v.id === "listitem"
      );
      expect(listitemViolation).toBeDefined();
    });
  });

  describe("Scenario: Prevent Regression in Digital Signing Layout Helper", () => {
    it("passes when the digital signing helper produces valid and fully-labeled signature components", async () => {
      const html = `
        <div class="signing-container">
          <h2>Digital Signature</h2>
          <p>Please enter your credentials to authenticate and sign the eConsent document.</p>
          <form class="signing-form">
            <div class="form-group">
              <label for="sig-username">Subject/Witness Username</label>
              <input type="text" id="sig-username" required />
            </div>
            <div class="form-group">
              <label for="sig-reason">Reason for Signature</label>
              <input type="text" id="sig-reason" required value="I agree to participate in this trial." />
            </div>
            <div class="form-actions">
              <button type="submit" aria-label="Submit Electronic Signature">Sign Document</button>
            </div>
          </form>
        </div>
      `;
      const results = await runAxe(html);
      expect(results.violations).toHaveLength(0);
    });

    it("fails when the digital signing helper produces invalid attributes or missing accessible text", async () => {
      const html = `
        <div class="signing-container">
          <h2>Digital Signature</h2>
          <form class="signing-form">
            <!-- Violation 1: invalid/misspelled ARIA attribute -->
            <input type="text" id="sig-witness" aria-labelledbyyy="witness-label" />
            
            <!-- Violation 2: button has no text and no aria-label -->
            <button type="submit"></button>
          </form>
        </div>
      `;
      const results = await runAxe(html);

      expect(results.violations.length).toBeGreaterThan(0);

      // button-name violation
      const buttonViolation = results.violations.find(
        (v) => v.id === "button-name"
      );
      expect(buttonViolation).toBeDefined();

      // aria-valid-attr violation (since aria-labelledbyyy is misspelled/invalid)
      const ariaAttrViolation = results.violations.find(
        (v) => v.id === "aria-valid-attr"
      );
      expect(ariaAttrViolation).toBeDefined();
    });
  });

  describe("Requirement-Specific Verifications", () => {
    it("explicitly skips missing page-level context rules (landmarks, html lang, bypass) on a standalone layout", async () => {
      // This is a standalone layout with NO main landmark, NO html tag, NO bypass skip links.
      // Under page-level rules, this would fail. Under our localized settings, it MUST pass.
      const html = `
        <div class="standalone-fragment">
          <h2>Standard Consent Fragment</h2>
          <p>All element-level attributes are fully compliant.</p>
          <button type="button">Confirm</button>
        </div>
      `;
      const results = await runAxe(html);
      expect(results.violations).toHaveLength(0);
    });
  });
});

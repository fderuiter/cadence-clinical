import { test, expect } from "@playwright/test";
import { ConsentPage } from "./pages/ConsentPage";

test.describe("eConsent and ICF Builder Workspace E2E Tests", () => {
  let consentPage: ConsentPage;

  test.beforeEach(async ({ page }) => {
    consentPage = new ConsentPage(page);
  });

  test("should successfully compose, arrange, and save an eConsent template", async () => {
    await consentPage.gotoAuthoring();

    // Create a new template version
    await consentPage.createNewTemplate();

    // Configure metadata
    await consentPage.setMetadata(
      "study-econsent-101",
      "Global ICF Waiver",
      "v1.2"
    );

    // Configure clauses (by default 1 empty row exists)
    await consentPage.setClauseValue(0, "clause-benefits");
    await consentPage.addClauseRow(); // Append row 1
    await consentPage.setClauseValue(1, "clause-risks");

    // Verify initial ordering
    await consentPage.verifyClauseOrder(["clause-benefits", "clause-risks"]);

    // Swapping / arranging sections: Move first clause down
    await consentPage.moveClauseDown(0);

    // Verify updated ordering
    await consentPage.verifyClauseOrder(["clause-risks", "clause-benefits"]);

    // Save template with Part 11 electronic signature justification
    await consentPage.saveTemplate(
      "Adding clinical risk disclosure sections in display sequence"
    );

    // Verify it exists in the active registry list
    await consentPage.verifyTemplateExists("Global ICF Waiver");
  });

  test("should support ICF builder operations, glossary hovers, and comprehension score tracking", async () => {
    await consentPage.gotoBuilder();

    // Add a new clinical consent section
    await consentPage.addSection("Disclosure and Privacy");
    await consentPage.selectSection("Disclosure and Privacy");

    // Verify live glossary term annotations & hover cards
    await consentPage.insertGlossaryTermViaHTML(
      "Biopsy",
      "Removal of cells/tissue for clinical examination."
    );
    await consentPage.hoverGlossaryTerm("Biopsy");

    // Assert that the hover popup tooltip is visible and renders correct term definition text
    await consentPage.verifyGlossaryPopover(
      "Biopsy",
      "Removal of cells/tissue for clinical examination."
    );

    // Evaluate target comprehension scores configuration
    await consentPage.selectComprehensionQuizTab();

    // Configure passing threshold and quiz question
    await consentPage.setPassingScoreThreshold(85);
    await consentPage.addQuizQuestion(
      "What is the required fasting period before biopsy?",
      ["Fasting not required", "12 hours", "8 hours", "1 hour"],
      2, // Option 2 (8 hours) is correct
      "Refer to Section 3 risk and preparation instructions."
    );

    // Publish GxP ICF version with change control signature
    await consentPage.publishIcf(
      "Finalizing v2.0 clinical consent structure with comprehension checks"
    );
  });
});

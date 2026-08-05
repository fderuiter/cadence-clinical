import { test, expect } from "@playwright/test";
import { RulesPage } from "./pages/RulesPage";

test.describe("Rules Workspace and Query Management E2E Tests", () => {
  let rulesPage: RulesPage;

  test.beforeEach(async ({ page }) => {
    rulesPage = new RulesPage(page);
    await rulesPage.goto();
  });

  test("should support adding, editing, compiling, and saving clinical rules", async () => {
    // Select rules designer tab
    await rulesPage.selectRulesTab();

    // Create a new rule
    await rulesPage.createNewRule();

    // Set rule configuration metadata
    await rulesPage.setRuleType("skip_logic");
    await rulesPage.setTargetField("pulse");
    await rulesPage.setTargetForm("form_vs");

    // Add and configure condition row
    await rulesPage.addCondition();
    await rulesPage.setCondition(0, "form_vs", "pulse", ">", "constant", "100");

    // Verify visual compiled XPath live compilation preview
    await rulesPage.verifyXpathPreview("/clinical_data/form_vs/pulse > 100");

    // Save rule with 21 CFR Part 11 Electronic Signature reason
    await rulesPage.saveRule(
      "Added high pulse rate warning/skip logic constraint"
    );

    // Verify that the rule is successfully listed on the left panel
    await rulesPage.verifyRuleExists("rule_");
  });

  test("should search clinical coding dictionary within the Query Dashboard", async () => {
    // Switch to query life-cycle dashboard tab
    await rulesPage.selectQueriesTab();

    // Search dictionary
    await rulesPage.searchCodingDictionary("pulse");

    // Verify MedDRA terminology results render
    await rulesPage.verifyCodingResultsContain("Pulse irregular");
    await rulesPage.verifyCodingResultsContain("MedDRA");
  });
});

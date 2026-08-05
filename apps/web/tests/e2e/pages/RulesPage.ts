import { Page, expect } from "@playwright/test";

export class RulesPage {
  constructor(private page: Page) {}

  async goto() {
    await this.page.goto("rules");
  }

  async selectRulesTab() {
    await this.page.click(".tab-btn-rules");
  }

  async selectQueriesTab() {
    await this.page.click(".tab-btn-queries");
  }

  async createNewRule() {
    await this.page.click("button:has-text('Create New Rule')");
  }

  async setRuleType(type: string) {
    await this.page.selectOption("select.rule-type-selector", type);
  }

  async setTargetField(field: string) {
    await this.page.selectOption("select.target-field-selector", field);
  }

  async setTargetForm(form: string) {
    await this.page.selectOption("select.target-form-selector", form);
  }

  async setQueryMessage(message: string) {
    await this.page.fill("input.query-message-input", message);
  }

  async addCondition() {
    await this.page.click("button[data-action='add-condition']");
  }

  async setCondition(
    index: number,
    formId: string,
    fieldId: string,
    operator: string,
    rightType: string,
    rightValue: string
  ) {
    await this.page.selectOption(
      `select.cond-form[data-index='${index}']`,
      formId
    );
    await this.page.selectOption(
      `select.cond-field[data-index='${index}']`,
      fieldId
    );
    await this.page.selectOption(
      `select.cond-operator[data-index='${index}']`,
      operator
    );
    await this.page.selectOption(
      `select.cond-right-type[data-index='${index}']`,
      rightType
    );
    if (rightType === "constant") {
      await this.page.fill(
        `input.cond-right-value[data-index='${index}']`,
        rightValue
      );
    }
  }

  async verifyXpathPreview(expectedXpath: string) {
    const preview = this.page.locator("fieldset:has-text('Live Compilation')");
    await expect(preview).toBeVisible();
    await expect(preview).toContainText(expectedXpath);
  }

  async saveRule(reason: string) {
    await this.page.click("button:has-text('Save Signed Rule')");
    // Handle the Part 11 Reason modal
    const modal = this.page.locator("#reason-modal");
    await expect(modal).toBeVisible();
    await this.page.selectOption("#change-reason-select", "Protocol Amendment");
    await this.page.fill("#change-reason-text", reason);
    await this.page.click("#btn-save-change");
  }

  async verifyRuleExists(ruleId: string) {
    const list = this.page.locator(".rule-card-item");
    await expect(list.filter({ hasText: ruleId })).toBeVisible();
  }

  async deleteRule(ruleId: string, reason: string) {
    // Find the delete button in the rule card for this ruleId
    const card = this.page.locator(".rule-card-item", { hasText: ruleId });
    await card.locator("button:has-text('Delete')").click();

    // Handle the Part 11 Reason modal
    const modal = this.page.locator("#reason-modal");
    await expect(modal).toBeVisible();
    await this.page.fill("#change-reason-text", reason);
    await this.page.click("#btn-save-change");
  }

  async searchCodingDictionary(query: string) {
    await this.page.fill("#dm-dict-lookup-input", query);
    await this.page.click("button:has-text('Search Dictionary')");
  }

  async verifyCodingResultsContain(text: string) {
    const results = this.page.locator("text=" + text);
    await expect(results.first()).toBeVisible();
  }
}

import { Page, expect } from "@playwright/test";

export class ConsentPage {
  constructor(private page: Page) {}

  async gotoAuthoring() {
    await this.page.goto("econsent-authoring");
  }

  async gotoBuilder() {
    await this.page.goto("icf-builder");
  }

  // --- Authoring Actions ---
  async createNewTemplate() {
    await this.page.click("#btn-create-template");
  }

  async setMetadata(studyId: string, templateName: string, protocolVersion: string) {
    await this.page.fill("#input-study-id", studyId);
    await this.page.fill("#input-template-name", templateName);
    await this.page.fill("#input-protocol-version", protocolVersion);
  }

  async addClauseRow() {
    await this.page.click(".btn-add-clause");
  }

  async setClauseValue(index: number, clauseId: string) {
    const row = this.page.locator(".clause-order-row").nth(index);
    await row.locator(".clause-id-input").fill(clauseId);
  }

  async moveClauseDown(index: number) {
    const row = this.page.locator(".clause-order-row").nth(index);
    await row.locator(".btn-move-down").click();
  }

  async moveClauseUp(index: number) {
    const row = this.page.locator(".clause-order-row").nth(index);
    await row.locator(".btn-move-up").click();
  }

  async verifyClauseOrder(expectedClauseIds: string[]) {
    const inputs = this.page.locator(".clause-id-input");
    const count = await inputs.count();
    expect(count).toBe(expectedClauseIds.length);
    for (let i = 0; i < count; i++) {
      await expect(inputs.nth(i)).toHaveValue(expectedClauseIds[i]);
    }
  }

  async addWorkflowStep(type: "comprehension_check" | "signature_placeholder") {
    if (type === "comprehension_check") {
      await this.page.click(".btn-add-step-comp");
    } else {
      await this.page.click(".btn-add-step-sig");
    }
  }

  async setWorkflowStepDetails(index: number, value: string) {
    const row = this.page.locator(".step-config-row").nth(index);
    const input = row.locator("input");
    await input.fill(value);
  }

  async saveTemplate(reason: string) {
    await this.page.click(".btn-save");
    const modal = this.page.locator("#reason-modal");
    await expect(modal).toBeVisible();
    await this.page.selectOption("#change-reason-select", "Protocol Amendment");
    await this.page.fill("#change-reason-text", reason);
    await this.page.click("#btn-save-change");
  }

  async verifyTemplateExists(templateName: string) {
    const card = this.page.locator(".template-card", { hasText: templateName });
    await expect(card.first()).toBeVisible();
  }

  async publishTemplate(templateName: string, reason: string) {
    const card = this.page.locator(".template-card", { hasText: templateName });
    await card.locator(".btn-publish").click();

    const modal = this.page.locator("#reason-modal");
    await expect(modal).toBeVisible();
    await this.page.fill("#change-reason-text", reason);
    await this.page.click("#btn-save-change");
  }

  // --- Builder Actions ---
  async addSection(title: string) {
    await this.page.fill("input.inline-input", title);
    await this.page.click("button:has-text('Add Section')");
  }

  async selectSection(title: string) {
    const item = this.page.locator(".outline-item", { hasText: title });
    await item.click();
  }

  async selectComprehensionQuizTab() {
    const item = this.page.locator(".outline-item", { hasText: "Comprehension Quiz" });
    await item.click();
  }

  async insertGlossaryTermViaHTML(term: string, definition: string) {
    await this.page.evaluate(({ term, definition }) => {
      const el = document.querySelector(".editor-canvas");
      if (el) {
        el.innerHTML = `<p>Testing a term annotation for <span class="glossary-term" data-definition="${definition}">${term}</span> right here.</p>`;
        el.dispatchEvent(new Event("input"));
        el.dispatchEvent(new Event("blur"));
      }
    }, { term, definition });
  }

  async hoverGlossaryTerm(term: string) {
    const span = this.page.locator(".glossary-term", { hasText: term });
    await span.hover();
  }

  async verifyGlossaryPopover(term: string, definition: string) {
    const popover = this.page.locator(".glossary-popover");
    await expect(popover).toBeVisible();
    await expect(popover.locator(".popover-body")).toContainText(term);
    await expect(popover.locator(".popover-body")).toContainText(definition);
  }

  async setPassingScoreThreshold(percentage: number) {
    await this.page.fill("#threshold-select", String(percentage));
  }

  async addQuizQuestion(text: string, options: string[], correctIndex: number, hint: string) {
    await this.page.click(".btn-add-question");
    const lastQuestion = this.page.locator(".question-item").last();

    // Fill Question text
    await lastQuestion.locator("input[placeholder*='What is the main benefit']").fill(text);

    // Fill Options
    for (let i = 0; i < options.length; i++) {
      if (i >= 2) {
        await lastQuestion.locator("button:has-text('Add Option')").click();
      }
      const optionInput = lastQuestion.locator(".option-input").nth(i);
      await optionInput.fill(options[i]);
    }

    // Select Correct Answer Radio
    const radio = lastQuestion.locator("input[type='radio']").nth(correctIndex);
    await radio.click();

    // Fill Hint
    await lastQuestion.locator("input[placeholder*='Hint:']").fill(hint);
  }

  async publishIcf(reason: string) {
    await this.page.click("button:has-text('Publish Version')");
    await this.page.fill("#publish-reason", reason);
    await this.page.click("button:has-text('Confirm & Publish')");
  }
}

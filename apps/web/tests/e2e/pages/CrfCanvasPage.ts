import { Page, expect } from "@playwright/test";

export class CrfCanvasPage {
  constructor(private page: Page) {}

  async goto() {
    // Navigate relative to baseURL: http://localhost:3000/cadence-clinical/
    await this.page.goto("mdr");
  }

  async openInteractiveBuilder() {
    const btn = this.page.locator("button:has-text('Open Interactive Builder')");
    await expect(btn).toBeVisible();
    await btn.click();
  }

  async addEpoch(id: string, name: string) {
    await this.page.fill("#new-epoch-id", id);
    await this.page.fill("#new-epoch-name", name);
    await this.page.click("button:has-text('Add Epoch')");

    // Handle the Part 11 Reason modal
    await this.page.fill("#change-reason-text", "Initial study design epoch setup");
    await this.page.click("#btn-save-change");
  }

  async addVisit(id: string, name: string, epochId: string) {
    await this.page.fill("#new-enc-id", id);
    await this.page.fill("#new-enc-name", name);
    await this.page.selectOption("#new-enc-epoch", epochId);
    await this.page.click("button:has-text('Add Visit')");

    // Handle the Part 11 Reason modal
    await this.page.fill("#change-reason-text", "Adding visit encounter to timeline");
    await this.page.click("#btn-save-change");
  }

  async addProcedure(id: string, name: string) {
    await this.page.fill("#new-proc-id", id);
    await this.page.fill("#new-proc-name", name);
    await this.page.click("button:has-text('Add Procedure')");

    // Handle the Part 11 Reason modal
    await this.page.fill("#change-reason-text", "Adding clinical procedure requirement");
    await this.page.click("#btn-save-change");
  }

  async configureApplicability(procId: string, visitId: string, timing: string) {
    await this.page.selectOption("#link-procedure", procId);
    await this.page.selectOption("#link-visit", visitId);
    await this.page.fill("#link-timing", timing);
    await this.page.click("button:has-text('Apply Applicability & Timing')");

    // Handle the Part 11 Reason modal
    await this.page.fill("#change-reason-text", "Establishing applicability rules for procedure");
    await this.page.click("#btn-save-change");
  }

  async verifyMatrixContains(text: string) {
    const matrix = this.page.locator("#soa-matrix-container");
    await expect(matrix).toBeVisible();
    await expect(matrix).toContainText(text);
  }
}

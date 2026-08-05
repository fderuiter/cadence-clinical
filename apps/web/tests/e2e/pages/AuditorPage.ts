import { Page, expect } from "@playwright/test";

export class AuditorPage {
  constructor(private page: Page) {}

  async goto() {
    await this.page.goto("audit");
  }

  async verifyIntegrity() {
    await this.page.click("button:has-text('Verify Now')");
  }

  async assertGreenIntegrityBanner() {
    const banner = this.page.locator("text=INTEGRITY VERIFIED");
    await expect(banner).toBeVisible();
  }

  async selectTmfFile(filePath: string) {
    // Set file to input
    await this.page.setInputFiles("#tmf-file-input", filePath);
  }

  async setTmfTaxonomy(zone: string, section: string) {
    await this.page.selectOption("#tmf-zone-select", zone);
    await this.page.fill("#tmf-section-input", section);
  }

  async uploadAndIngestTmf() {
    await this.page.click("button:has-text('Upload & Ingest Document')");
  }

  async verifyDocumentInDirectory(filename: string) {
    const row = this.page.locator("tr", { hasText: filename });
    await expect(row.first()).toBeVisible();
  }

  async previewDocument(filename: string) {
    const row = this.page.locator("tr", { hasText: filename });
    await row.locator(".btn-preview-doc").click();
  }

  async verifySecureWatermarkedPreview(
    filename: string,
    expectedContent: string
  ) {
    const panel = this.page.locator(".secure-preview-panel");
    await expect(panel).toBeVisible();
    await expect(panel).toContainText(`Secure Preview: ${filename}`);

    // Verify repeating SVG client-side watermark presence
    const overlay = panel.locator(".watermark-overlay");
    await expect(overlay).toBeVisible();
    await expect(overlay).toHaveCSS("background-repeat", "repeat");

    // Verify raw preview text
    await expect(panel.locator("pre")).toContainText(expectedContent);
  }

  async closePreview() {
    await this.page.click(".btn-close-preview");
  }

  async filterAuditLogs(
    actorId: string,
    actionType: string,
    documentId: string
  ) {
    if (actorId) {
      await this.page.fill("#filter-user-id-input", actorId);
    }
    if (actionType) {
      await this.page.selectOption("#filter-action-select", actionType);
    }
    if (documentId) {
      await this.page.fill(".filter-document-id", documentId);
    }
    await this.page.click(".btn-apply-filters");
  }

  async clearFilters() {
    await this.page.click(".btn-clear-filters");
  }

  async verifyLedgerContainsDetails(expectedDetails: string) {
    const table = this.page.locator("table").nth(1); // Second table is audit trail logs
    await expect(table).toContainText(expectedDetails);
  }

  async triggerRegulatoryBinderExport(
    studyId: string,
    includeHistory: boolean
  ) {
    await this.page.fill("input[placeholder*='e.g. study_001']", studyId);
    if (includeHistory) {
      await this.page.check("#chk-history");
    } else {
      await this.page.uncheck("#chk-history");
    }
    await this.page.click("button:has-text('Export Regulatory Binder')");
  }
}

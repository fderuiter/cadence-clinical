import { Page, expect } from "@playwright/test";

export interface TagSpec {
  zone: string;
  section: string;
}

export class EtmfPage {
  constructor(private page: Page) {}

  async goto() {
    // Navigate relative to baseURL: http://localhost:3000/cadence-clinical/
    await this.page.goto("audit");
  }

  async uploadDocument(filePath: string, metadata: TagSpec) {
    // Set file input
    await this.page.setInputFiles("#tmf-file-input", filePath);

    // Select TMF Zone
    await this.page.selectOption("#tmf-zone-select", metadata.zone);

    // Fill Section
    await this.page.fill("#tmf-section-input", metadata.section);

    // Submit Ingestion
    await this.page.click(".btn-upload-doc-submit");
  }

  async verifyDocumentInBinder(documentName: string) {
    const table = this.page.locator(".clinical-table").first();
    await expect(table).toContainText(documentName);
  }

  async assertAuditLogEventRecorded(action: string, filename: string) {
    const logsTable = this.page.locator(".clinical-table").last();
    await expect(logsTable).toContainText(action);
    await expect(logsTable).toContainText(filename);
  }
}

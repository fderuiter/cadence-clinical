import { test, expect } from "@playwright/test";
import { EtmfPage } from "./pages/EtmfPage";
import * as path from "path";
import * as fs from "fs";
import { fileURLToPath } from "url";

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

test.describe("eTMF Document Management Workflow", () => {
  test.use({ storageState: "playwright/.auth/user.json" });

  test.beforeEach(async ({ page }) => {
    // Intercept signature verification endpoint
    await page.route("**/api/v1/auth/signature-verification", async (route) => {
      await route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify({ sig_token: "mock-sig-token-123" }),
      });
    });

    // Intercept document signing endpoint
    await page.route("**/api/v1/etmf/documents/**/sign-off", async (route) => {
      await route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify({
          id: "doc_mock_signed",
          filename: "sample_protocol.pdf",
          zone: "01. Trial Management",
          section: "01.01 Trial Steering Committee",
          artifact_type: "Protocol",
          status: "SIGNED",
          version_index: 1.1,
          signature_manifestation: {
            signer_id: "admin@cadence.clinical",
            timestamp: new Date().toISOString(),
            signing_reason: "APPROVAL",
            sha256_hash: "mock-sha256-hash-xyz-789",
            signature: "mock-digital-signature-proof",
          },
        }),
      });
    });

    // Intercept watermarked preview endpoint
    await page.route("**/watermarked**", async (route) => {
      await route.fulfill({
        status: 200,
        contentType: "text/plain",
        body: "Mock Audited PDF Content - Watermarked Preview is successfully active.",
      });
    });
  });

  test("should successfully upload document and verify DIA TMF tag and preview", async ({ page }) => {
    const etmf = new EtmfPage(page);
    await etmf.goto();

    // Create a temporary sample file to upload
    const tempFilePath = path.join(__dirname, "sample_protocol.pdf");
    fs.writeFileSync(tempFilePath, "Mock PDF Content");

    try {
      await etmf.uploadDocument(tempFilePath, {
        zone: "01. Trial Management",
        section: "01.01 Trial Steering Committee",
      });

      // Verify the document is added to the directory table
      await etmf.verifyDocumentInBinder("sample_protocol.pdf");

      // Verify audit log event is recorded in the ledger table
      await etmf.assertAuditLogEventRecorded("INGEST", "sample_protocol.pdf");

      // Test Scenario 1 PDF preview: Click preview button
      await page.click("button.btn-preview-doc");
      const previewPanel = page.locator(".secure-preview-panel");
      await expect(previewPanel).toBeVisible();
      await expect(previewPanel).toContainText("sample_protocol.pdf");
    } finally {
      // Clean up temp file
      if (fs.existsSync(tempFilePath)) {
        fs.unlinkSync(tempFilePath);
      }
    }
  });

  test("should digitally sign document and verify version increment to v1.1", async ({ page }) => {
    const etmf = new EtmfPage(page);
    await etmf.goto();

    // In demo mode, we should have the mock uploaded document or some documents. Let's upload a document first:
    const tempFilePath = path.join(__dirname, "sample_protocol.pdf");
    fs.writeFileSync(tempFilePath, "Mock PDF Content");

    try {
      await etmf.uploadDocument(tempFilePath, {
        zone: "01. Trial Management",
        section: "01.01 Trial Steering Committee",
      });

      // Find the Sign button for this document and click it
      const signBtn = page.locator("button.btn-sign-doc").first();
      await expect(signBtn).toBeVisible();
      await signBtn.click();

      // Complete SignatureCaptureModal inputs
      const modal = page.locator("#signature-capture-modal");
      await expect(modal).toBeVisible();

      await page.fill("#sig-username", "admin@cadence.clinical");
      await page.fill("#sig-password", "admin_password");
      await page.selectOption("#sig-reason", "APPROVAL");

      // Click sign button
      await page.click("#btn-confirm-sig");

      // Verify version incremented to v1.1 and status is SIGNED
      const versionCell = page.locator(".clinical-table tbody tr").first().locator("td").nth(5);
      await expect(versionCell).toContainText("v1.1");

      const statusCell = page.locator(".clinical-table tbody tr").first().locator("td").nth(4);
      await expect(statusCell).toContainText("SIGNED");
    } finally {
      if (fs.existsSync(tempFilePath)) {
        fs.unlinkSync(tempFilePath);
      }
    }
  });
});

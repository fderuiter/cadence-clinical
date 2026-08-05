import { test, expect } from "@playwright/test";
import { AuditorPage } from "./pages/AuditorPage";

test.describe("Auditor Portal and Ledger Integrity E2E Tests", () => {
  let auditorPage: AuditorPage;

  test.beforeEach(async ({ page }) => {
    auditorPage = new AuditorPage(page);
    await auditorPage.goto();
  });

  test("should verify ledger integrity seal verification and test log filtering", async () => {
    // 1. Verify GxP Cryptographic Integrity Seal
    await auditorPage.verifyIntegrity();

    // Confirm that the green verification integrity banner displays successfully
    await auditorPage.assertGreenIntegrityBanner();

    // 2. Filter chronological log ledger
    await auditorPage.filterAuditLogs("auditor", "AUDIT_VIEW", "");

    // Verify ledger contains the filtered records
    await auditorPage.verifyLedgerContainsDetails("auditor");
    await auditorPage.verifyLedgerContainsDetails(
      "Accessed eTMF immutable audit trail logs"
    );

    // Clear filters
    await auditorPage.clearFilters();
  });

  test("should preview documents with repeating watermark overlay and trigger binder export", async () => {
    // 1. Click Preview on a document in the table
    await auditorPage.previewDocument("form_1572_v1.txt");

    // Verify secure watermarked preview panel is displayed with visual repeating watermark overlay and proper definition content
    await auditorPage.verifySecureWatermarkedPreview(
      "form_1572_v1.txt",
      "MOCK DOCUMENT"
    );

    // Close preview panel
    await auditorPage.closePreview();

    // 2. Trigger regulatory binder ZIP export compilation
    await auditorPage.triggerRegulatoryBinderExport("study_001", false);
  });
});

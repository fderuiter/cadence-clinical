/**
 * @req: PRD-PAT-002 PRD-SYS-182 PRD-PAY-006
 * Mobile Receipt OCR Engine, Auto-Approval Policy, and Automated Direct ACH Reimbursement Dispatch Tests
 */

import "fake-indexeddb/auto";
import { describe, it, expect, beforeEach, afterEach, vi } from "vitest";
import { createApp, nextTick } from "vue";
import App from "../src/App.vue";
import { state, extractReceiptOcr, submitExpenseClaim } from "../src/index.js";
import { pinia } from "../src/stores/pinia.js";
import {
  initSessionKey,
  clearSessionKey,
  clearAllSubmissions,
} from "../src/sync-queue.js";

describe("Mobile Receipt OCR & Direct ACH Reimbursement Engine", () => {
  const mockUserId = "subject_ocr_001";
  let activeApp = null;

  beforeEach(async () => {
    state.session.userId = mockUserId;
    state.session.roles = "Subject";
    state.session.token = "test_ocr_token";
    state.session.isOfflineMode = false;
    state.session.isDemoMode = true;
    state.walletBalance = 325.0;
    state.expenseError = null;
    state.ledgerBlocks = [];
    state.expenseResult = null;
    state.expenseOcr = {
      loading: false,
      error: null,
      data: null,
      previewUrl: null,
    };
    state.expenseForm = {
      subjectId: mockUserId,
      studyId: "study_001",
      siteId: "site_001",
      category: "TRANSIT",
      merchantName: "",
      grossAmount: 0.0,
      isMealAllowance: false,
      perDiemCap: 75.0,
      expenseDate: "",
      ocrConfidence: 0.98,
      receiptImageName: "",
    };

    await initSessionKey("ocr-secret-passphrase", mockUserId);
    await clearAllSubmissions();

    document.body.innerHTML = `<div id="app"></div>`;
  });

  afterEach(async () => {
    if (activeApp) {
      try {
        activeApp.unmount();
      } catch {
        // Ignore unmount errors during teardown
      }
      activeApp = null;
    }
    clearSessionKey(mockUserId);
    await clearAllSubmissions();
    vi.restoreAllMocks();
  });

  it("extracts receipt metadata via mobile OCR engine and writes 21 CFR Part 11 audit record", async () => {
    const rawText =
      "METRO TRANSIT AUTHORITY\nRoute: Clinical Trial Express\nDate: 2026-08-23\nFare Paid: $28.50";
    const res = await extractReceiptOcr(rawText);

    expect(res).toBeDefined();
    expect(res.merchant_name).toBe("METRO TRANSIT AUTHORITY");
    expect(res.gross_amount).toBe(28.5);
    expect(res.confidence_score).toBe(0.98);

    expect(state.expenseForm.merchantName).toBe("METRO TRANSIT AUTHORITY");
    expect(state.expenseForm.grossAmount).toBe(28.5);
    expect(state.expenseForm.category).toBe("TRANSIT");

    // Audit log verification
    const auditRecord = state.ledgerBlocks.find(
      (b) => b.action === "OCR_RECEIPT_EXTRACTED"
    );
    expect(auditRecord).toBeDefined();
    expect(auditRecord.details).toHaveProperty(
      "merchant_name",
      "METRO TRANSIT AUTHORITY"
    );
  });

  it("parses file object hints for meal and fuel receipts", async () => {
    const mealFile = { name: "receipt_meal_dinner.png" };
    const resMeal = await extractReceiptOcr(mealFile);
    expect(resMeal.category).toBe("MEAL");
    expect(state.expenseForm.isMealAllowance).toBe(true);

    const fuelFile = { name: "receipt_gas_shell.jpg" };
    const resFuel = await extractReceiptOcr(fuelFile);
    expect(resFuel.category).toBe("MILEAGE");
  });

  it("auto-approves low-risk claim (<$100), dispatches direct ACH reimbursement, and updates wallet balance", async () => {
    // 1. OCR Extract
    await extractReceiptOcr(
      "METRO TRANSIT AUTHORITY\nDate: 2026-08-23\nFare Paid: $28.50"
    );

    // 2. Submit reimbursement claim
    const initialBalance = state.walletBalance; // 325.00
    const res = await submitExpenseClaim();

    expect(res.status).toBe("AUTO_APPROVED");
    expect(res.auto_approved).toBe(true);
    expect(res.disbursement_reference).toMatch(/^DISB-ACH-/);

    // Wallet balance should be dynamically credited by 28.50
    expect(state.walletBalance).toBe(initialBalance + 28.5);

    // Audit trailing verification
    const approvedLog = state.ledgerBlocks.find(
      (b) => b.action === "EXPENSE_CLAIM_AUTO_APPROVED"
    );
    expect(approvedLog).toBeDefined();

    const dispatchedLog = state.ledgerBlocks.find(
      (b) => b.action === "DIGITAL_DISBURSEMENT_DISPATCHED"
    );
    expect(dispatchedLog).toBeDefined();
    expect(dispatchedLog.details).toHaveProperty(
      "disbursement_reference",
      res.disbursement_reference
    );
    expect(dispatchedLog.details).toHaveProperty("method", "DIRECT_ACH");
  });

  it("places expense claim on hold for manual review when gross amount >= $100", async () => {
    // Setup high-value claim ($125.00)
    await extractReceiptOcr(
      "CITY BISTRO RESTAURANT\nDate: 2026-08-23\nMeal Charge: $125.00"
    );
    state.expenseForm.grossAmount = 125.0;

    const initialBalance = state.walletBalance;
    const res = await submitExpenseClaim();

    expect(res.status).toBe("PENDING_REVIEW");
    expect(res.auto_approved).toBe(false);
    expect(res.disbursement_reference).toBeNull();
    expect(res.review_notes).toContain("$100.00 auto-approval threshold");

    // Wallet balance should NOT change
    expect(state.walletBalance).toBe(initialBalance);

    // Audit trailing verification
    const reviewLog = state.ledgerBlocks.find(
      (b) => b.action === "EXPENSE_CLAIM_HELD_FOR_REVIEW"
    );
    expect(reviewLog).toBeDefined();
  });

  it("places meal expense claim on hold when exceeding $75 per-diem cap", async () => {
    state.expenseForm.category = "MEAL";
    state.expenseForm.merchantName = "DOWNTOWN GRILL";
    state.expenseForm.grossAmount = 85.0; // Exceeds $75.00 meal per-diem cap
    state.expenseForm.isMealAllowance = true;
    state.expenseForm.perDiemCap = 75.0;

    const res = await submitExpenseClaim();

    expect(res.status).toBe("PENDING_REVIEW");
    expect(res.auto_approved).toBe(false);
    expect(res.review_notes).toContain("per-diem allowance");
  });

  it("places claim on hold when OCR confidence score is low (< 0.90)", async () => {
    state.expenseForm.category = "TRANSIT";
    state.expenseForm.grossAmount = 30.0;
    state.expenseForm.ocrConfidence = 0.75; // Low confidence

    const res = await submitExpenseClaim();

    expect(res.status).toBe("PENDING_REVIEW");
    expect(res.auto_approved).toBe(false);
    expect(res.review_notes).toContain("0.90 threshold");
  });

  it("handles OCR extraction and submission errors gracefully", async () => {
    const origFetch = global.fetch;
    global.fetch = vi
      .fn()
      .mockRejectedValueOnce(new Error("Network connection lost"));

    await expect(extractReceiptOcr("INVALID")).rejects.toThrow(
      "Network connection lost"
    );
    expect(state.expenseOcr.loading).toBe(false);
    expect(state.expenseOcr.error).toBe("Network connection lost");

    global.fetch = vi.fn().mockRejectedValueOnce(new Error("Server error"));
    await expect(submitExpenseClaim()).rejects.toThrow("Server error");

    global.fetch = origFetch;
  });

  it("mounts App.vue and renders interactive OCR reimbursement workflow in My Wallet view", async () => {
    activeApp = createApp(App);
    activeApp.use(pinia);
    activeApp.mount("#app");
    await nextTick();

    state.currentView = "view-wallet";
    await nextTick();

    const dropzone = document.getElementById("receipt-upload-dropzone");
    expect(dropzone).not.toBeNull();

    const fileInput = document.getElementById("receipt-file-input");
    expect(fileInput).not.toBeNull();

    // Trigger dropzone click
    dropzone.click();

    // Dispatch file input change event
    const mockFile = new File(["dummy content"], "transit_receipt.png", {
      type: "image/png",
    });
    const changeEvent = new Event("change");
    Object.defineProperty(changeEvent, "target", {
      value: { files: [mockFile] },
    });
    fileInput.dispatchEvent(changeEvent);

    // Wait for OCR extraction
    await new Promise((r) => setTimeout(r, 100));
    await nextTick();

    // Verify OCR review card rendered
    const reviewCard = document.getElementById("ocr-review-card");
    expect(reviewCard).not.toBeNull();

    const merchantInput = document.getElementById("ocr-merchant-name");
    expect(merchantInput).not.toBeNull();
    expect(merchantInput.value).toBe("METRO TRANSIT AUTHORITY");

    // Click confirm & reimburse button
    const confirmBtn = document.getElementById("btn-confirm-reimburse");
    expect(confirmBtn).not.toBeNull();
    confirmBtn.click();

    // Wait for async claim submission
    await new Promise((r) => setTimeout(r, 100));
    await nextTick();

    // Verify auto-approved banner and ACH reference rendered
    const approvedBanner = document.getElementById(
      "expense-auto-approved-banner"
    );
    expect(approvedBanner).not.toBeNull();

    const achRef = document.getElementById("ach-disbursement-reference");
    expect(achRef).not.toBeNull();
    expect(achRef.textContent).toMatch(/^DISB-ACH-/);

    // Wallet balance display updated
    const balanceEl = document.getElementById("wallet-balance-amount");
    expect(balanceEl.textContent).toContain("$353.50 USD");
  });

  it("refuses simulated OCR and expense submission outside demo mode and shows user-visible error banner", async () => {
    state.session.isDemoMode = false;
    state.walletBalance = null;

    activeApp = createApp(App);
    activeApp.use(pinia);
    activeApp.mount("#app");
    await nextTick();

    state.currentView = "view-wallet";
    await nextTick();

    expect(document.getElementById("wallet-unavailable-card")).not.toBeNull();
    expect(document.getElementById("receipt-upload-dropzone")).toBeNull();

    await expect(extractReceiptOcr("METRO TRANSIT")).rejects.toThrow(
      /not available in this environment/i
    );
    await nextTick();

    const errorBanner = document.getElementById("wallet-error-banner");
    expect(errorBanner).not.toBeNull();
    expect(errorBanner.textContent).toContain(
      "not available in this environment"
    );

    await expect(submitExpenseClaim()).rejects.toThrow(
      /not available in this environment/i
    );
  });
});

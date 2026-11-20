/**
 * @req: PRD-PAT-002 PRD-SYS-182 PRD-PAY-006
 * Mobile Receipt OCR Engine, Auto-Approval Policy, Live Wallet, and Direct ACH Reimbursement Tests
 */

import "fake-indexeddb/auto";
import { describe, it, expect, beforeEach, afterEach, vi } from "vitest";
import { createApp, nextTick } from "vue";
import App from "../src/App.vue";
import {
  state,
  loadParticipantWallet,
  extractReceiptOcr,
  submitExpenseClaim,
  disburseAvailableBalance,
} from "../src/index.js";
import { pinia } from "../src/stores/pinia.js";
import {
  initSessionKey,
  clearSessionKey,
  clearAllSubmissions,
} from "../src/sync-queue.js";

function buildReceiptSvgFile(lines, fileName, mimeType = "image/svg+xml") {
  const textNodes = lines
    .map(
      (line, idx) =>
        `<text x="16" y="${28 + idx * 24}" font-family="monospace" font-size="14">${line}</text>`
    )
    .join("");
  const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="360" height="220">${textNodes}</svg>`;
  return new File([svg], fileName, { type: mimeType });
}

describe("Mobile Receipt OCR & Direct ACH Reimbursement Engine", () => {
  const mockUserId = "subject_ocr_001";
  let activeApp = null;

  beforeEach(async () => {
    state.session.userId = mockUserId;
    state.session.roles = "Subject";
    state.session.token = "test_ocr_token";
    state.session.isOfflineMode = false;
    state.session.isDemoMode = false;
    state.walletId = "wallet_ocr_001";
    state.walletStudyId = "study_001";
    state.walletSiteId = "site_001";
    state.walletBalance = 325.0;
    state.lifetimeStipends = 325.0;
    state.lifetimeReimbursements = 0.0;
    state.lifetimeDisbursements = 0.0;
    state.taxableYtd = 150.0;
    state.is1099ThresholdExceeded = false;
    state.w9Status = "NOT_REQUIRED";
    state.virtualCardMask = "**** **** **** 4821";
    state.achRoutingMask = "*****0021";
    state.achAccountMask = "ACH-***4321";
    state.recentTransactions = [];
    state.expenseClaims = [];
    state.disbursingBalance = false;
    state.disbursementConfirmation = null;
    state.expenseError = null;
    state.ledgerBlocks = [];
    state.expenseResult = null;
    state.expenseOcr = {
      loading: false,
      error: null,
      warning: null,
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
      ocrConfidence: 0.0,
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

  it("extracts distinct transit, fuel, and meal receipt image contents without filename sniffing and writes Part 11 audit logs", async () => {
    const transitFile = buildReceiptSvgFile(
      ["METRO TRANSIT AUTHORITY", "Date: 2026-08-23", "Fare Paid: $28.50"],
      "scan_001.svg"
    );
    const resTransit = await extractReceiptOcr(transitFile);
    expect(resTransit.ocrReadable).toBe(true);
    expect(resTransit.merchant_name).toBe("METRO TRANSIT AUTHORITY");
    expect(resTransit.expense_date).toBe("2026-08-23");
    expect(resTransit.gross_amount).toBe(28.5);
    expect(resTransit.category).toBe("TRANSIT");
    expect(resTransit.confidence_score).toBeGreaterThanOrEqual(0.9);

    // Deliberately pass a misleading meal filename containing fuel receipt content to prove zero filename sniffing
    const fuelFile = buildReceiptSvgFile(
      ["SHELL FUEL STATION", "Date: 2026-09-14", "Fuel Total: $42.75"],
      "receipt_meal_dinner.svg"
    );
    const resFuel = await extractReceiptOcr(fuelFile);
    expect(resFuel.ocrReadable).toBe(true);
    expect(resFuel.merchant_name).toBe("SHELL FUEL STATION");
    expect(resFuel.gross_amount).toBe(42.75);
    expect(resFuel.category).toBe("FUEL");
    expect(state.expenseForm.isMealAllowance).toBe(false);

    const mealFile = buildReceiptSvgFile(
      ["BISTRO CAFE", "Date: 2026-10-05", "Total: $19.25"],
      "upload_99.svg"
    );
    const resMeal = await extractReceiptOcr(mealFile);
    expect(resMeal.ocrReadable).toBe(true);
    expect(resMeal.merchant_name).toBe("BISTRO CAFE");
    expect(resMeal.gross_amount).toBe(19.25);
    expect(resMeal.category).toBe("MEAL");
    expect(state.expenseForm.isMealAllowance).toBe(true);

    const auditRecord = state.ledgerBlocks.find(
      (b) => b.action === "OCR_RECEIPT_EXTRACTED"
    );
    expect(auditRecord).toBeDefined();
    expect(auditRecord.details).toHaveProperty(
      "merchant_name",
      "METRO TRANSIT AUTHORITY"
    );
  });

  it("never fabricates merchant or amount on blank or corrupted receipt images and surfaces an OCR warning", async () => {
    const corruptedFile = new File(
      [new Uint8Array([0x00, 0xff, 0x13, 0x37, 0x00, 0x89])],
      "metro_transit_28_50.png",
      { type: "image/png" }
    );

    const res = await extractReceiptOcr(corruptedFile);
    expect(res.ocrReadable).toBe(false);
    expect(res.confidence_score).toBe(0);
    expect(res.merchant_name).toBe("");
    expect(res.gross_amount).toBe(0);
    expect(state.expenseForm.merchantName).toBe("");
    expect(state.expenseForm.grossAmount).toBe(0);
    expect(state.expenseOcr.warning).toBeTruthy();
  });

  it("auto-approves low-risk claim (<=$100), dispatches direct ACH reimbursement, and records lifetime reimbursements", async () => {
    await extractReceiptOcr(
      buildReceiptSvgFile(
        ["METRO TRANSIT AUTHORITY", "Date: 2026-08-23", "Fare Paid: $28.50"],
        "transit.svg"
      )
    );

    const initialReimbursements = state.lifetimeReimbursements;
    const res = await submitExpenseClaim();

    expect(res.status).toBe("AUTO_APPROVED");
    expect(res.auto_approved).toBe(true);
    expect(res.disbursement_reference).toMatch(/^DISB-ACH-/);
    expect(state.lifetimeReimbursements).toBe(initialReimbursements + 28.5);

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

  it("places expense claim on hold for manual review when gross amount > $100", async () => {
    await extractReceiptOcr(
      buildReceiptSvgFile(
        ["CITY RAIL EXPRESS", "Date: 2026-08-23", "Total: $125.00"],
        "rail.svg"
      )
    );

    const initialBalance = state.walletBalance;
    const res = await submitExpenseClaim();

    expect(res.status).toBe("PENDING_REVIEW");
    expect(res.auto_approved).toBe(false);
    expect(res.disbursement_reference).toBeNull();
    expect(res.review_notes).toContain("$100.00 auto-approval threshold");
    expect(state.walletBalance).toBe(initialBalance);

    const reviewLog = state.ledgerBlocks.find(
      (b) => b.action === "EXPENSE_CLAIM_HELD_FOR_REVIEW"
    );
    expect(reviewLog).toBeDefined();
  });

  it("places meal expense claim on hold when exceeding $75 per-diem cap", async () => {
    state.expenseForm.category = "MEAL";
    state.expenseForm.merchantName = "DOWNTOWN GRILL";
    state.expenseForm.grossAmount = 85.0;
    state.expenseForm.isMealAllowance = true;
    state.expenseForm.perDiemCap = 75.0;
    state.expenseForm.ocrConfidence = 0.96;

    const res = await submitExpenseClaim();

    expect(res.status).toBe("PENDING_REVIEW");
    expect(res.auto_approved).toBe(false);
    expect(res.review_notes).toContain("per-diem cap");
  });

  it("places claim on hold when OCR confidence score is low (< 0.90)", async () => {
    state.expenseForm.category = "TRANSIT";
    state.expenseForm.merchantName = "METRO BUS";
    state.expenseForm.grossAmount = 30.0;
    state.expenseForm.ocrConfidence = 0.75;

    const res = await submitExpenseClaim();

    expect(res.status).toBe("PENDING_REVIEW");
    expect(res.auto_approved).toBe(false);
    expect(res.review_notes).toContain("0.90 threshold");
  });

  it("handles OCR extraction and submission errors gracefully", async () => {
    const fetchSpy = vi
      .spyOn(globalThis, "fetch")
      .mockRejectedValueOnce(new Error("Network connection lost"));

    await expect(extractReceiptOcr("INVALID")).rejects.toThrow(
      "Network connection lost"
    );
    expect(state.expenseOcr.loading).toBe(false);
    expect(state.expenseOcr.error).toBe("Network connection lost");

    fetchSpy.mockRejectedValueOnce(new Error("Server error"));
    await expect(submitExpenseClaim()).rejects.toThrow("Server error");

    fetchSpy.mockRestore();
  });

  it("renders live authenticated My Wallet view, extracts receipt OCR, and disburses available balance with duplicate-click guard", async () => {
    await loadParticipantWallet();

    activeApp = createApp(App);
    activeApp.use(pinia);
    activeApp.mount("#app");
    await nextTick();

    state.currentView = "view-wallet";
    await nextTick();

    expect(document.body.textContent).not.toContain("021000021");
    expect(
      document.getElementById("wallet-ach-account-mask")?.textContent
    ).toContain("ACH-***");

    const dropzone = document.getElementById("receipt-upload-dropzone");
    expect(dropzone).not.toBeNull();

    const fileInput = document.getElementById("receipt-file-input");
    expect(fileInput).not.toBeNull();

    const receiptFile = buildReceiptSvgFile(
      ["METRO TRANSIT AUTHORITY", "Date: 2026-08-23", "Fare Paid: $28.50"],
      "transit_receipt.svg"
    );
    const changeEvent = new Event("change");
    Object.defineProperty(changeEvent, "target", {
      value: { files: [receiptFile] },
    });
    fileInput.dispatchEvent(changeEvent);

    await new Promise((r) => setTimeout(r, 120));
    await nextTick();

    const reviewCard = document.getElementById("ocr-review-card");
    expect(reviewCard).not.toBeNull();

    const merchantInput = document.getElementById("ocr-merchant-name");
    expect(merchantInput).not.toBeNull();
    expect(merchantInput.value).toBe("METRO TRANSIT AUTHORITY");

    const confirmBtn = document.getElementById("btn-confirm-reimburse");
    expect(confirmBtn).not.toBeNull();
    confirmBtn.click();

    await new Promise((r) => setTimeout(r, 120));
    await nextTick();

    const approvedBanner = document.getElementById(
      "expense-auto-approved-banner"
    );
    expect(approvedBanner).not.toBeNull();

    const achRef = document.getElementById("ach-disbursement-reference");
    expect(achRef).not.toBeNull();
    expect(achRef.textContent).toMatch(/^DISB-ACH-/);

    // Exercise on-demand balance payout and verify duplicate clicks are ignored while in flight
    const disburseBtn = document.getElementById("btn-disburse-balance");
    expect(disburseBtn).not.toBeNull();
    const firstPayout = disburseAvailableBalance();
    const duplicatePayout = disburseAvailableBalance();
    expect(await duplicatePayout).toBeNull();
    await firstPayout;
    await nextTick();

    const confirmBanner = document.getElementById(
      "wallet-disbursement-confirmation"
    );
    expect(confirmBanner).not.toBeNull();
    expect(
      document.getElementById("wallet-disbursement-ref")?.textContent
    ).toMatch(/^DISB-/);

    const balanceEl = document.getElementById("wallet-balance-amount");
    expect(balanceEl.textContent).toContain("$0.00 USD");
  });

  it("refuses OCR, expense submission, and payout when unauthenticated outside demo mode and shows user-visible error banner", async () => {
    state.session.isDemoMode = false;
    state.session.token = null;
    state.session.userId = null;
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
    await expect(disburseAvailableBalance()).rejects.toThrow(
      /not available in this environment/i
    );
  });
});

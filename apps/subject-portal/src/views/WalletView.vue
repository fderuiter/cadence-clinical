<template>
  <section
    id="view-wallet"
    class="portal-view"
    :class="{ active: ui.currentView === 'view-wallet' }"
    role="tabpanel"
    aria-labelledby="btn-tab-btn-wallet"
    tabindex="0"
  >
    <div class="view-header">
      <h2>Participant Financial Wallet &amp; Stipends</h2>
      <p>
        Review participant stipend status and travel expense reimbursement
        submissions when enabled for your study.
      </p>
    </div>

    <!-- User-visible error banner (replaces silent console-only error handling) -->
    <div
      v-if="walletError"
      id="wallet-error-banner"
      role="alert"
      aria-live="assertive"
      style="
        margin-bottom: 16px;
        padding: 12px 16px;
        border-radius: 8px;
        background: var(--semantic-color-error-bg);
        border: 1px solid var(--semantic-color-error);
        color: var(--semantic-color-error);
        font-size: 14px;
        font-weight: 600;
      "
    >
      {{ walletError }}
    </div>

    <div
      v-if="!isDemo"
      id="wallet-unavailable-card"
      class="card"
      role="status"
      aria-live="polite"
      style="margin-bottom: 20px"
    >
      <h3 style="margin-top: 0">Participant Wallet Not Available</h3>
      <p style="font-size: 14px; color: var(--text-muted); line-height: 1.5">
        Direct participant stipend balances, payout cards, and receipt
        reimbursements are not enabled in this portal environment. Please
        contact your study coordinator regarding visit stipends or travel
        reimbursements.
      </p>
    </div>

    <template v-else>
      <div
        class="card"
        style="
          margin-bottom: 20px;
          background: linear-gradient(
            135deg,
            var(--semantic-color-wallet-start) 0%,
            var(--semantic-color-wallet-end) 100%
          );
          color: var(--semantic-color-text-on-solid);
        "
      >
        <div style="font-size: 14px; font-weight: 600; opacity: 0.9">
          Demo Sandbox Wallet Balance
        </div>
        <div
          id="wallet-balance-amount"
          style="font-size: 32px; font-weight: 800; margin: 4px 0 0 0"
        >
          {{
            typeof wallet.walletBalance === "number"
              ? `$${wallet.walletBalance.toFixed(2)} USD`
              : "Unavailable"
          }}
        </div>
      </div>

      <div class="card">
        <h3 style="margin-top: 0">
          📷 Travel Expense Reimbursement Upload (Demo Preview)
        </h3>
        <p style="font-size: 14px; color: var(--text-muted)">
          Upload a sample receipt image in demo mode to preview OCR extraction
          and policy review states.
        </p>
        <div
          id="receipt-upload-dropzone"
          class="dropzone-upload"
          tabindex="0"
          role="button"
          aria-label="Upload receipt image for travel reimbursement"
          aria-describedby="receipt-upload-hint"
          style="
            border: 2px dashed var(--semantic-color-border);
            border-radius: 8px;
            padding: 20px;
            text-align: center;
            cursor: pointer;
            background: var(--semantic-color-surface-muted);
            min-height: 120px;
          "
          @click="triggerReceiptUpload"
          @keydown.enter.prevent="triggerReceiptUpload"
          @keydown.space.prevent="triggerReceiptUpload"
        >
          <span aria-hidden="true" style="font-size: 24px">📷</span>
          <p style="margin: 6px 0 0 0; font-size: 14px; font-weight: 600">
            Upload receipt image for travel reimbursement
          </p>
        </div>
        <small
          id="receipt-upload-hint"
          style="
            display: block;
            margin-top: 6px;
            text-align: center;
            color: var(--text-muted);
          "
          >Tap to capture or upload; OCR extraction will automatically detect
          merchant, date, and amount</small
        >
        <input
          id="receipt-file-input"
          type="file"
          accept="image/*"
          class="sr-only"
          style="display: none"
          @change="handleReceiptFileSelected"
        />

        <!-- Loading State -->
        <div
          v-if="wallet.expenseOcr?.loading"
          id="ocr-loading-spinner"
          style="
            margin-top: 16px;
            text-align: center;
            padding: 12px;
            background: var(--semantic-color-surface-muted);
            border-radius: 6px;
          "
        >
          <p
            style="
              margin: 0;
              font-weight: 600;
              color: var(--semantic-color-primary);
            "
          >
            🔍 Extracting Receipt Metadata via Mobile OCR...
          </p>
        </div>

        <!-- OCR Confirmation & Review Card -->
        <div
          v-if="wallet.expenseOcr?.data || wallet.expenseForm?.merchantName"
          id="ocr-review-card"
          style="
            margin-top: 20px;
            padding: 16px;
            border: 1px solid var(--semantic-color-border);
            border-radius: 8px;
            background: rgba(255, 255, 255, 0.02);
          "
        >
          <div
            style="
              display: flex;
              justify-content: space-between;
              align-items: center;
              margin-bottom: 12px;
            "
          >
            <h4 style="margin: 0; font-size: 16px">
              OCR Extracted Claim Confirmation
            </h4>
            <span id="ocr-confidence-badge" class="badge badge-gxp">
              Confidence:
              {{ Math.round((wallet.expenseForm.ocrConfidence || 0) * 100) }}%
            </span>
          </div>

          <div class="form-group mb-12">
            <label for="ocr-category">Expense Category</label>
            <select id="ocr-category" v-model="wallet.expenseForm.category">
              <option value="TRANSIT">
                TRANSIT (Public Transit / Ride Share)
              </option>
              <option value="MILEAGE">
                MILEAGE (Gas / Fuel / Personal Auto)
              </option>
              <option value="PARKING">PARKING (Parking Garage / Lot)</option>
              <option value="MEAL">MEAL (Per-Diem Allowance)</option>
              <option value="LODGING">LODGING (Hotel / Accommodation)</option>
            </select>
          </div>

          <div class="form-group mb-12">
            <label for="ocr-merchant-name">Merchant Name</label>
            <input
              id="ocr-merchant-name"
              v-model="wallet.expenseForm.merchantName"
              type="text"
              required
            />
          </div>

          <div class="form-group mb-12">
            <label for="ocr-expense-date">Expense Date</label>
            <input
              id="ocr-expense-date"
              v-model="wallet.expenseForm.expenseDate"
              type="text"
              required
            />
          </div>

          <div class="form-group mb-12">
            <label for="ocr-gross-amount">Gross Amount ($ USD)</label>
            <input
              id="ocr-gross-amount"
              v-model.number="wallet.expenseForm.grossAmount"
              type="number"
              step="0.01"
              required
            />
          </div>

          <div
            v-if="wallet.expenseForm.category === 'MEAL'"
            class="form-group mb-12"
            style="
              background: var(--semantic-color-surface-muted);
              padding: 10px;
              border-radius: 6px;
            "
          >
            <label
              style="
                display: flex;
                align-items: center;
                gap: 8px;
                cursor: pointer;
              "
            >
              <input
                id="ocr-meal-allowance-check"
                v-model="wallet.expenseForm.isMealAllowance"
                type="checkbox"
              />
              <span>Meal Allowance / Per-Diem Cap ($75.00 limit)</span>
            </label>
          </div>

          <button
            id="btn-confirm-reimburse"
            type="button"
            class="btn btn-primary"
            style="width: 100%; margin-top: 12px"
            @click="handleConfirmReimburse"
          >
            🚀 Confirm &amp; Reimburse
          </button>
        </div>

        <!-- Submission Result Banner -->
        <div
          v-if="wallet.expenseResult"
          id="expense-result-container"
          style="margin-top: 20px"
        >
          <!-- Auto Approved -->
          <div
            v-if="
              wallet.expenseResult.status === 'AUTO_APPROVED' ||
              wallet.expenseResult.auto_approved
            "
            id="expense-auto-approved-banner"
            style="
              padding: 16px;
              border-radius: 8px;
              background: rgba(16, 185, 129, 0.1);
              border: 1px solid var(--semantic-color-success);
              color: var(--semantic-color-text-dark);
            "
          >
            <h4
              style="
                margin: 0 0 6px 0;
                color: var(--semantic-color-success);
                font-size: 16px;
              "
            >
              ✅ Reimbursement Auto-Approved &amp; Dispatched
            </h4>
            <p style="margin: 0 0 8px 0; font-size: 14px">
              Direct ACH disbursement dispatched in demo preview.
            </p>
            <div
              style="
                font-size: 13px;
                font-family: monospace;
                background: rgba(0, 0, 0, 0.1);
                padding: 6px 10px;
                border-radius: 4px;
              "
            >
              ACH Reference:
              <strong id="ach-disbursement-reference">{{
                wallet.expenseResult.disbursement_reference
              }}</strong>
            </div>
          </div>

          <!-- Pending Review -->
          <div
            v-else
            id="expense-pending-review-banner"
            style="
              padding: 16px;
              border-radius: 8px;
              background: rgba(245, 158, 11, 0.1);
              border: 1px solid var(--semantic-color-warning-border-strong);
              color: var(--semantic-color-text-dark);
            "
          >
            <h4
              style="
                margin: 0 0 6px 0;
                color: var(--semantic-color-warning-text);
                font-size: 16px;
              "
            >
              ⚠️ Expense Claim Held for Manual Review
            </h4>
            <p id="pending-review-notes" style="margin: 0; font-size: 14px">
              {{
                wallet.expenseResult.review_notes ||
                "Expense claim exceeds policy threshold and requires study coordinator review."
              }}
            </p>
          </div>
        </div>
      </div>
    </template>
  </section>
</template>

<script setup>
import { computed } from "vue";

import { extractReceiptOcr, submitExpenseClaim } from "../features/wallet.js";
import { useSessionStore } from "../stores/session.js";
import { useUiStore } from "../stores/ui.js";
import { useWalletStore } from "../stores/wallet.js";
import { isDemoModeActive } from "../utils/demoMode.js";

const ui = useUiStore();
const session = useSessionStore();
const wallet = useWalletStore();

const isDemo = computed(() => {
  void session.session?.isDemoMode;
  return isDemoModeActive();
});
const walletError = computed(
  () => wallet.expenseOcr?.error || wallet.expenseError || ""
);

function triggerReceiptUpload() {
  const fileInput = document.getElementById("receipt-file-input");
  if (fileInput) {
    fileInput.click();
  }
}

async function handleReceiptFileSelected(e) {
  const file = e.target?.files?.[0];
  if (file) {
    try {
      await extractReceiptOcr(file);
    } catch (err) {
      const msg =
        err?.message ||
        "Unable to extract receipt details right now. Please try again later.";
      wallet.expenseError = msg;
    }
  }
}

async function handleConfirmReimburse() {
  try {
    await submitExpenseClaim();
  } catch (err) {
    const msg =
      err?.message ||
      "Unable to submit travel expense claim right now. Please try again later.";
    wallet.expenseError = msg;
  }
}
</script>

<template>
  <section
    id="view-wallet"
    class="portal-view"
    :class="{ active: ui.currentView === 'view-wallet' }"
    role="tabpanel"
    aria-labelledby="tab-btn-wallet"
    tabindex="0"
  >
    <div class="view-header">
      <h2>Participant Financial Wallet &amp; Stipends</h2>
      <p>
        Track your protocol visit stipends, upload travel expense receipts, and
        manage your virtual debit card payouts.
      </p>
    </div>

    <div
      class="card"
      style="
        margin-bottom: 20px;
        background: linear-gradient(
          135deg,
          var(--semantic-color-wallet-start) 0%,
          var(--semantic-color-wallet-end) 100%
        );
        color: var(--semantic-color-surface);
      "
    >
      <div style="font-size: 14px; font-weight: 600; opacity: 0.9">
        Available Wallet Balance
      </div>
      <div
        id="wallet-balance-amount"
        style="font-size: 32px; font-weight: 800; margin: 4px 0 12px 0"
      >
        ${{ (wallet.walletBalance || 0).toFixed(2) }} USD
      </div>
      <div style="display: flex; gap: 16px; font-size: 13px; opacity: 0.95">
        <div>Lifetime Stipends: <strong>$250.00</strong></div>
        <div>Lifetime Travel: <strong>$75.00</strong></div>
      </div>
    </div>

    <div class="card" style="margin-bottom: 20px">
      <h3 style="margin-top: 0">💳 Digital Payout Method</h3>
      <p style="font-size: 14px; color: var(--text-muted)">
        Virtual Card: <strong>**** **** **** 4821</strong> (EXP: 12/29) | Direct
        ACH Enabled
      </p>
      <button type="button" class="btn btn-primary" style="width: 100%">
        🚀 Disburse Available Balance (${{
          (wallet.walletBalance || 0).toFixed(2)
        }})
      </button>
    </div>

    <div class="card">
      <h3 style="margin-top: 0">📷 Travel Expense Reimbursement Upload</h3>
      <p style="font-size: 14px; color: var(--text-muted)">
        Upload receipt photos for clinical trial travel, mileage, parking, and
        meals. Policy auto-approval applies for low-risk claims under $100.
      </p>
      <div
        id="receipt-upload-dropzone"
        class="dropzone-upload"
        tabindex="0"
        role="button"
        aria-label="Upload receipt image for travel reimbursement"
        style="
          border: 2px dashed var(--border-color);
          border-radius: 8px;
          padding: 20px;
          text-align: center;
          cursor: pointer;
          background: var(--bg-muted);
          min-height: 120px;
        "
        @click="triggerReceiptUpload"
        @keydown.enter.prevent="triggerReceiptUpload"
        @keydown.space.prevent="triggerReceiptUpload"
      >
        <span style="font-size: 24px">📷</span>
        <p style="margin: 6px 0 0 0; font-size: 14px; font-weight: 600">
          Tap to Capture or Upload Receipt Image
        </p>
        <small style="color: var(--text-muted)"
          >OCR extraction will automatically detect merchant, date, and
          amount</small
        >
      </div>
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
          background: var(--bg-muted);
          border-radius: 6px;
        "
      >
        <p style="margin: 0; font-weight: 600; color: var(--primary-color)">
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
          border: 1px solid var(--border-color);
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
            {{ Math.round((wallet.expenseForm.ocrConfidence || 0.98) * 100) }}%
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
            type="text"
            v-model="wallet.expenseForm.merchantName"
            required
          />
        </div>

        <div class="form-group mb-12">
          <label for="ocr-expense-date">Expense Date</label>
          <input
            id="ocr-expense-date"
            type="text"
            v-model="wallet.expenseForm.expenseDate"
            required
          />
        </div>

        <div class="form-group mb-12">
          <label for="ocr-gross-amount">Gross Amount ($ USD)</label>
          <input
            id="ocr-gross-amount"
            type="number"
            step="0.01"
            v-model.number="wallet.expenseForm.grossAmount"
            required
          />
        </div>

        <div
          v-if="wallet.expenseForm.category === 'MEAL'"
          class="form-group mb-12"
          style="background: var(--bg-muted); padding: 10px; border-radius: 6px"
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
              type="checkbox"
              v-model="wallet.expenseForm.isMealAllowance"
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
          🚀 Confirm & Reimburse
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
            ✅ Reimbursement Auto-Approved & Dispatched
          </h4>
          <p style="margin: 0 0 8px 0; font-size: 14px">
            Direct ACH disbursement dispatched to your registered participant
            account.
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
  </section>
</template>

<script setup>
import { extractReceiptOcr, submitExpenseClaim } from "../features/wallet.js";
import { useUiStore } from "../stores/ui.js";
import { useWalletStore } from "../stores/wallet.js";

const ui = useUiStore();
const wallet = useWalletStore();

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
      console.error("Receipt OCR extraction error:", err);
    }
  }
}

async function handleConfirmReimburse() {
  try {
    await submitExpenseClaim();
  } catch (err) {
    console.error("Submit reimbursement error:", err);
  }
}
</script>

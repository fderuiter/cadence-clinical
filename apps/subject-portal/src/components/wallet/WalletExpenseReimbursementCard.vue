<template>
  <div class="card">
    <h3 class="wallet-card-heading--icon">
      <ClinicalIcon name="camera" :size="20" />
      <span>Travel &amp; Meal Receipt Reimbursement</span>
    </h3>
    <p class="wallet-muted-copy">
      Upload a receipt photo or PDF and we will fill in the merchant, date,
      category, and amount for you to review. Routine claims under $100.00
      (meals up to $75.00) with clear receipts are approved automatically.
    </p>

    <slot name="dropzone" />

    <small id="receipt-upload-hint" class="wallet-upload-hint">
      Tap to capture or upload a receipt; we will pre-fill the merchant, date,
      category, and amount for you to check
    </small>
    <div class="wallet-manual-entry-wrap">
      <button
        id="btn-manual-expense-entry"
        type="button"
        class="btn btn-secondary"
        @click="handleOpenManualEntry"
      >
        Enter Receipt Details Manually
      </button>
    </div>

    <div
      v-if="wallet.expenseOcr?.loading"
      id="ocr-loading-spinner"
      class="wallet-ocr-loading"
      role="status"
      aria-live="polite"
    >
      <p class="wallet-ocr-loading-text">
        <ClinicalIcon name="search" :size="16" />
        <span>Reading Receipt Details...</span>
      </p>
    </div>

    <div
      v-if="wallet.expenseOcr?.warning"
      id="ocr-warning-banner"
      class="wallet-ocr-warning"
      role="status"
      aria-live="polite"
    >
      {{ wallet.expenseOcr.warning }}
    </div>

    <div
      v-if="showExpenseReviewCard"
      id="ocr-review-card"
      class="wallet-ocr-review"
    >
      <div class="wallet-ocr-review-header">
        <h4 id="ocr-review-heading" tabindex="-1" class="wallet-ocr-title">
          {{
            wallet.expenseManualEntry && !wallet.expenseOcr?.data
              ? "Enter Receipt Details"
              : "Review Receipt Details"
          }}
        </h4>
        <span id="ocr-confidence-badge" class="badge badge-gxp tabular-nums">
          Read clarity:
          {{ Math.round((wallet.expenseForm.ocrConfidence || 0) * 100) }}%
        </span>
      </div>

      <div class="form-group mb-12">
        <label for="ocr-category">Expense Category</label>
        <select id="ocr-category" v-model="wallet.expenseForm.category">
          <option value="TRANSIT">Public Transit, Ride Share, or Taxi</option>
          <option value="FUEL">Gas or Fuel</option>
          <option value="MILEAGE">Personal Auto Mileage</option>
          <option value="PARKING">Parking</option>
          <option value="MEAL">Meals (Per-Diem Allowance)</option>
          <option value="LODGING">Hotel or Lodging</option>
          <option value="OTHER">Other Study Travel Expense</option>
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
          class="tabular-nums"
          type="date"
          required
        />
      </div>

      <div class="form-group mb-12">
        <label for="ocr-gross-amount">
          Gross Amount ({{ normalizedCurrency }})
        </label>
        <input
          id="ocr-gross-amount"
          v-model.number="wallet.expenseForm.grossAmount"
          class="tabular-nums"
          type="number"
          step="0.01"
          min="0"
          required
        />
      </div>

      <div
        v-if="wallet.expenseForm.category === 'MEAL'"
        class="form-group mb-12 wallet-meal-allowance-box"
      >
        <label for="ocr-meal-allowance-check" class="meal-allowance-label">
          <input
            id="ocr-meal-allowance-check"
            v-model="wallet.expenseForm.isMealAllowance"
            type="checkbox"
            class="wallet-meal-checkbox"
          />
          <span class="tabular-nums">
            Meal Allowance / Per-Diem Cap ({{
              formatCurrency(wallet.expenseForm.perDiemCap || 75)
            }}
            limit)
          </span>
        </label>
      </div>

      <button
        id="btn-confirm-reimburse"
        type="button"
        class="btn btn-primary wallet-confirm-reimburse-btn"
        :disabled="wallet.expenseSubmitting"
        @click="handleConfirmReimburse"
      >
        <ClinicalIcon
          v-if="!wallet.expenseSubmitting"
          name="check"
          :size="16"
        />
        <span>{{
          wallet.expenseSubmitting
            ? "Submitting Claim..."
            : "Confirm & Reimburse"
        }}</span>
      </button>
    </div>

    <div
      v-if="wallet.expenseResult"
      id="expense-result-container"
      class="wallet-section-card--spaced"
    >
      <div
        v-if="isAutoApprovedResult"
        id="expense-auto-approved-banner"
        class="wallet-expense-approved"
        role="status"
        aria-live="polite"
      >
        <h4
          id="expense-auto-approved-heading"
          tabindex="-1"
          class="wallet-expense-approved-heading"
        >
          <ClinicalIcon name="check" :size="16" />
          <span>Reimbursement Auto-Approved &amp; Dispatched</span>
        </h4>
        <p class="wallet-expense-approved-copy">
          Direct ACH reimbursement has been dispatched to your linked payout
          account.
        </p>
        <div class="wallet-ach-ref-box">
          ACH Reference:
          <strong id="ach-disbursement-reference">{{
            wallet.expenseResult.disbursement_reference ||
            wallet.expenseResult.disbursementReference
          }}</strong>
        </div>
      </div>

      <div
        v-else
        id="expense-pending-review-banner"
        class="wallet-expense-pending"
        role="status"
        aria-live="polite"
      >
        <h4
          id="expense-pending-review-heading"
          tabindex="-1"
          class="wallet-expense-pending-heading"
        >
          <ClinicalIcon name="alert-circle" :size="16" />
          <span>Expense Claim Held for Manual Review</span>
        </h4>
        <p id="pending-review-notes" class="wallet-expense-pending-copy">
          {{
            formatPlainReviewNotes(
              wallet.expenseResult.review_notes ||
                wallet.expenseResult.reviewNotes
            ) || "Your study coordinator will check this receipt for you."
          }}
        </p>
      </div>
    </div>
  </div>
</template>

<script setup>
import { computed, watch } from "vue";
import { ClinicalIcon, handoffFocus } from "ui";
import {
  openManualExpenseEntry,
  submitExpenseClaim,
} from "../../features/wallet.js";
import { useUiStore } from "../../stores/ui.js";
import { useWalletStore } from "../../stores/wallet.js";
import { formatPlainReviewNotes } from "./walletFormatters.js";

defineProps({
  normalizedCurrency: { type: String, required: true },
  formatCurrency: { type: Function, required: true },
});

const ui = useUiStore();
const wallet = useWalletStore();

const showExpenseReviewCard = computed(() =>
  Boolean(
    wallet.expenseManualEntry ||
    wallet.expenseOcr?.data ||
    wallet.expenseForm?.merchantName
  )
);

const isAutoApprovedResult = computed(() =>
  Boolean(
    wallet.expenseResult?.status === "AUTO_APPROVED" ||
    wallet.expenseResult?.auto_approved ||
    wallet.expenseResult?.autoApproved
  )
);

watch(showExpenseReviewCard, (isOpen, wasOpen) => {
  if (isOpen && !wasOpen && ui.currentView === "view-wallet") {
    void handoffFocus(["#ocr-category", "#ocr-review-heading"], {
      scrollIfOffscreen: true,
    });
  }
});

watch(
  () => wallet.expenseResult,
  (nextResult, prevResult) => {
    if (
      nextResult &&
      nextResult !== prevResult &&
      ui.currentView === "view-wallet"
    ) {
      void handoffFocus([
        "#expense-auto-approved-heading",
        "#expense-pending-review-heading",
        "#btn-manual-expense-entry",
        "#wallet-view-heading",
        "#main-content",
      ]);
    }
  }
);

function handleOpenManualEntry() {
  openManualExpenseEntry();
}

async function handleConfirmReimburse() {
  try {
    await submitExpenseClaim();
    if (wallet.expenseResult) {
      void handoffFocus([
        "#expense-auto-approved-heading",
        "#expense-pending-review-heading",
        "#btn-manual-expense-entry",
        "#wallet-view-heading",
        "#main-content",
      ]);
    }
  } catch (err) {
    wallet.expenseError =
      err?.message ||
      "Unable to submit travel expense claim right now. Please try again later.";
    void handoffFocus([
      "#wallet-error-banner",
      "#btn-confirm-reimburse",
      "#wallet-view-heading",
    ]);
  }
}
</script>

<style scoped src="./walletView.css"></style>
<style scoped>
.wallet-upload-hint {
  display: block;
  margin-top: 6px;
  text-align: center;
  color: var(--text-muted);
}

.wallet-manual-entry-wrap {
  margin-top: 10px;
  text-align: center;
}

.wallet-ocr-loading {
  margin-top: 16px;
  text-align: center;
  padding: 12px;
  background: var(--semantic-color-surface-muted);
  border-radius: 6px;
}

.wallet-ocr-loading-text {
  margin: 0;
  font-weight: 600;
  color: var(--semantic-color-primary-dark);
  display: inline-flex;
  align-items: center;
  gap: 8px;
}

.wallet-ocr-warning {
  margin-top: 16px;
  padding: 12px 14px;
  border-radius: 8px;
  background: var(--semantic-color-warning-bg-light);
  border: 1px solid var(--semantic-color-warning-border-strong);
  color: var(--semantic-color-warning-text-dark);
  font-size: 14px;
  font-weight: 600;
}

.wallet-ocr-review {
  margin-top: 20px;
  padding: 16px;
  border: 1px solid var(--semantic-color-border);
  border-radius: 8px;
  background: var(--semantic-color-surface);
}

.wallet-ocr-review-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 12px;
  gap: 8px;
  flex-wrap: wrap;
}

.wallet-ocr-title {
  margin: 0;
  font-size: 16px;
}

.wallet-meal-allowance-box {
  background: var(--semantic-color-surface-muted);
  padding: 8px 12px;
  border-radius: 6px;
}

.meal-allowance-label {
  display: flex;
  align-items: center;
  gap: var(--spacing-sm);
  min-height: var(--touch-target-min);
  margin-bottom: 0;
  cursor: pointer;
}

.wallet-meal-checkbox {
  width: 24px;
  min-width: 24px;
  height: var(--touch-target-min);
  min-height: var(--touch-target-min);
  margin: 0;
  cursor: pointer;
  accent-color: var(--semantic-color-primary);
}

.wallet-confirm-reimburse-btn {
  width: 100%;
  margin-top: 12px;
  gap: 8px;
}

.wallet-expense-approved {
  padding: 16px;
  border-radius: 8px;
  background: var(--semantic-color-success-bg);
  border: 1px solid var(--semantic-color-success);
  color: var(--semantic-color-text);
}

.wallet-expense-approved-heading {
  margin: 0 0 6px 0;
  color: var(--semantic-color-success);
  font-size: 16px;
  display: flex;
  align-items: center;
  gap: 8px;
}

.wallet-expense-approved-copy {
  margin: 0 0 8px 0;
  font-size: 14px;
}

.wallet-ach-ref-box {
  font-size: 13px;
  font-family: var(--semantic-font-mono);
  background: var(--semantic-color-surface);
  padding: 6px 10px;
  border-radius: 4px;
  border: 1px solid var(--semantic-color-border);
}

.wallet-expense-pending {
  padding: 16px;
  border-radius: 8px;
  background: var(--semantic-color-warning-bg-light);
  border: 1px solid var(--semantic-color-warning-border-strong);
  color: var(--semantic-color-text-dark);
}

.wallet-expense-pending-heading {
  margin: 0 0 6px 0;
  color: var(--semantic-color-warning-text-dark);
  font-size: 16px;
  display: flex;
  align-items: center;
  gap: 8px;
}

.wallet-expense-pending-copy {
  margin: 0;
  font-size: 14px;
}

#ach-disbursement-reference {
  font-family: var(--semantic-font-mono);
  font-variant-numeric: tabular-nums;
}

#ocr-confidence-badge,
#ocr-expense-date,
#ocr-gross-amount {
  font-variant-numeric: tabular-nums;
}
</style>

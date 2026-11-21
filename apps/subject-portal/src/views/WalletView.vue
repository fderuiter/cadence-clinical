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
        Track your study stipends, upload travel or meal receipts for
        reimbursement, and transfer your available balance to your linked payout
        method.
      </p>
    </div>

    <!-- User-visible error banner -->
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
        color: var(--semantic-color-error-text-dark);
        font-size: 14px;
        font-weight: 600;
      "
    >
      {{ walletError }}
    </div>

    <div
      v-if="!isWalletEnabled"
      id="wallet-unavailable-card"
      class="card"
      role="status"
      aria-live="polite"
      style="margin-bottom: 20px"
    >
      <h3 style="margin-top: 0">Sign In Required for Participant Wallet</h3>
      <p style="font-size: 14px; color: var(--text-muted); line-height: 1.5">
        Direct participant stipend balances, payout cards, and receipt
        reimbursements require an active authenticated participant session.
        Please sign in or contact your study coordinator for assistance.
      </p>
    </div>

    <template v-else>
      <!-- Wallet Summary & On-Demand Balance Disbursement Card -->
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
        <div
          style="
            display: flex;
            justify-content: space-between;
            align-items: flex-start;
            flex-wrap: wrap;
            gap: 12px;
          "
        >
          <div>
            <div style="font-size: 14px; font-weight: 600">
              {{
                isDemo
                  ? "Demo Sandbox Wallet Balance"
                  : "Available Wallet Balance"
              }}
            </div>
            <div
              id="wallet-balance-amount"
              style="font-size: 32px; font-weight: 800; margin: 4px 0 0 0"
            >
              {{ formattedAvailableBalance }}
            </div>
          </div>

          <button
            id="btn-disburse-balance"
            type="button"
            class="btn"
            :disabled="
              wallet.disbursingBalance || !(Number(wallet.walletBalance) > 0)
            "
            :aria-busy="wallet.disbursingBalance ? 'true' : 'false'"
            style="
              background: var(--semantic-color-surface);
              color: var(--semantic-color-primary-dark);
              font-weight: 700;
              padding: 10px 16px;
              border-radius: 8px;
              border: none;
              cursor: pointer;
            "
            @click="handleDisburseBalance"
          >
            {{
              wallet.disbursingBalance
                ? "Processing Payout..."
                : "Disburse Available Balance"
            }}
          </button>
        </div>

        <div
          style="
            display: grid;
            grid-template-columns: repeat(auto-fit, minmax(150px, 1fr));
            gap: 12px;
            margin-top: 16px;
            padding-top: 12px;
            border-top: 1px solid var(--semantic-color-primary-light);
            font-size: 13px;
          "
        >
          <div>
            <div style="font-weight: 600">Lifetime Stipends</div>
            <strong id="wallet-lifetime-stipends" style="font-size: 16px">
              ${{ Number(wallet.lifetimeStipends || 0).toFixed(2) }}
            </strong>
          </div>
          <div>
            <div style="font-weight: 600">Lifetime Reimbursements</div>
            <strong id="wallet-lifetime-reimbursements" style="font-size: 16px">
              ${{ Number(wallet.lifetimeReimbursements || 0).toFixed(2) }}
            </strong>
          </div>
          <div>
            <div style="font-weight: 600">Total Paid Out</div>
            <strong id="wallet-lifetime-disbursements" style="font-size: 16px">
              ${{ Number(wallet.lifetimeDisbursements || 0).toFixed(2) }}
            </strong>
          </div>
          <div>
            <div style="font-weight: 600">Tax Form (W-9)</div>
            <strong id="wallet-w9-status" style="font-size: 15px">
              {{ formatEnumLabel(wallet.w9Status || "NOT_REQUIRED") }}
            </strong>
          </div>
        </div>
      </div>

      <!-- On-Demand Payout Confirmation -->
      <div
        v-if="wallet.disbursementConfirmation"
        id="wallet-disbursement-confirmation"
        role="status"
        aria-live="polite"
        style="
          margin-bottom: 20px;
          padding: 14px 16px;
          border-radius: 8px;
          background: var(--semantic-color-success-bg);
          border: 1px solid var(--semantic-color-success);
          color: var(--semantic-color-text);
        "
      >
        <h3
          style="
            margin: 0 0 4px 0;
            font-size: 16px;
            color: var(--semantic-color-success);
          "
        >
          ✅ Balance Payout Dispatched
        </h3>
        <p style="margin: 0; font-size: 14px">
          Transferred
          <strong
            >${{
              Number(wallet.disbursementConfirmation.amount || 0).toFixed(2)
            }}</strong
          >
          to
          <strong>{{
            wallet.disbursementConfirmation.destinationMask ||
            "linked payout account"
          }}</strong>
          (Ref:
          <code id="wallet-disbursement-ref">{{
            wallet.disbursementConfirmation.referenceNumber
          }}</code>).
        </p>
      </div>

      <!-- Linked Masked Payout Accounts -->
      <div
        v-if="wallet.virtualCardMask || wallet.achAccountMask"
        id="wallet-payout-methods-card"
        class="card"
        style="margin-bottom: 20px"
      >
        <h3 style="margin-top: 0; font-size: 16px">
          Linked Payout Methods (Masked)
        </h3>
        <div
          style="
            display: grid;
            grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
            gap: 12px;
            font-size: 14px;
          "
        >
          <div
            v-if="wallet.achAccountMask"
            style="
              padding: 10px 12px;
              border-radius: 6px;
              background: var(--semantic-color-surface-muted);
              border: 1px solid var(--semantic-color-border);
            "
          >
            <div style="font-weight: 700">Direct ACH Bank Account</div>
            <div id="wallet-ach-account-mask" style="margin-top: 4px">
              Account: {{ wallet.achAccountMask }}
            </div>
            <div
              v-if="wallet.achRoutingMask"
              id="wallet-ach-routing-mask"
              style="color: var(--text-muted); font-size: 13px"
            >
              Routing: {{ wallet.achRoutingMask }}
            </div>
          </div>

          <div
            v-if="wallet.virtualCardMask"
            style="
              padding: 10px 12px;
              border-radius: 6px;
              background: var(--semantic-color-surface-muted);
              border: 1px solid var(--semantic-color-border);
            "
          >
            <div style="font-weight: 700">Reloadable Study Card</div>
            <div id="wallet-virtual-card-mask" style="margin-top: 4px">
              Card: {{ wallet.virtualCardMask }}
            </div>
            <div
              v-if="wallet.virtualCardExpiry"
              style="color: var(--text-muted); font-size: 13px"
            >
              Expires: {{ wallet.virtualCardExpiry }}
            </div>
          </div>
        </div>
      </div>

      <!-- Receipt OCR & Expense Claim Card -->
      <div class="card">
        <h3 style="margin-top: 0">
          📷 Travel &amp; Meal Receipt Reimbursement
        </h3>
        <p style="font-size: 14px; color: var(--text-muted)">
          Upload a receipt photo or PDF and we will fill in the merchant, date,
          category, and amount for you to review. Routine claims under $100.00
          (meals up to $75.00) with clear receipts are approved automatically.
        </p>
        <div
          id="receipt-upload-dropzone"
          class="dropzone-upload"
          tabindex="0"
          role="button"
          aria-label="Upload receipt image for travel reimbursement"
          aria-describedby="receipt-upload-hint"
          style="
            border: 2px dashed var(--semantic-color-border-control);
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
          <p
            v-if="wallet.expenseForm.receiptImageName"
            id="receipt-selected-filename"
            style="margin: 4px 0 0 0; font-size: 13px; color: var(--text-muted)"
          >
            Selected: {{ wallet.expenseForm.receiptImageName }}
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
          >Tap to capture or upload a receipt; we will pre-fill the merchant,
          date, category, and amount for you to check</small
        >
        <input
          id="receipt-file-input"
          type="file"
          accept="image/*,application/pdf,.svg"
          class="sr-only"
          style="display: none"
          @change="handleReceiptFileSelected"
        />

        <!-- Loading State -->
        <div
          v-if="wallet.expenseOcr?.loading"
          id="ocr-loading-spinner"
          role="status"
          aria-live="polite"
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
              color: var(--semantic-color-primary-dark);
            "
          >
            🔍 Extracting Receipt Metadata via OCR...
          </p>
        </div>

        <!-- Low-Clarity / Unreadable OCR Warning -->
        <div
          v-if="wallet.expenseOcr?.warning"
          id="ocr-warning-banner"
          role="status"
          aria-live="polite"
          style="
            margin-top: 16px;
            padding: 12px 14px;
            border-radius: 8px;
            background: var(--semantic-color-warning-bg);
            border: 1px solid var(--semantic-color-warning);
            color: var(--semantic-color-warning);
            font-size: 14px;
            font-weight: 600;
          "
        >
          {{ wallet.expenseOcr.warning }}
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
            background: var(--semantic-color-surface);
          "
        >
          <div
            style="
              display: flex;
              justify-content: space-between;
              align-items: center;
              margin-bottom: 12px;
              gap: 8px;
              flex-wrap: wrap;
            "
          >
            <h4 style="margin: 0; font-size: 16px">Review Receipt Details</h4>
            <span id="ocr-confidence-badge" class="badge badge-gxp">
              Read clarity:
              {{ Math.round((wallet.expenseForm.ocrConfidence || 0) * 100) }}%
            </span>
          </div>

          <div class="form-group mb-12">
            <label for="ocr-category">Expense Category</label>
            <select id="ocr-category" v-model="wallet.expenseForm.category">
              <option value="TRANSIT">
                Public Transit, Ride Share, or Taxi
              </option>
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
            <label for="ocr-expense-date">Expense Date (YYYY-MM-DD)</label>
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
              min="0"
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
            :disabled="wallet.expenseSubmitting"
            style="width: 100%; margin-top: 12px"
            @click="handleConfirmReimburse"
          >
            {{
              wallet.expenseSubmitting
                ? "Submitting Claim..."
                : "🚀 Confirm & Reimburse"
            }}
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
              wallet.expenseResult.auto_approved ||
              wallet.expenseResult.autoApproved
            "
            id="expense-auto-approved-banner"
            role="status"
            aria-live="polite"
            style="
              padding: 16px;
              border-radius: 8px;
              background: var(--semantic-color-success-bg-soft);
              border: 1px solid var(--semantic-color-success);
              color: var(--semantic-color-text);
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
              Direct ACH reimbursement has been dispatched to your linked payout
              account.
            </p>
            <div
              style="
                font-size: 13px;
                font-family: monospace;
                background: var(--semantic-color-surface);
                padding: 6px 10px;
                border-radius: 4px;
                border: 1px solid var(--semantic-color-border);
              "
            >
              ACH Reference:
              <strong id="ach-disbursement-reference">{{
                wallet.expenseResult.disbursement_reference ||
                wallet.expenseResult.disbursementReference
              }}</strong>
            </div>
          </div>

          <!-- Pending Review -->
          <div
            v-else
            id="expense-pending-review-banner"
            role="status"
            aria-live="polite"
            style="
              padding: 16px;
              border-radius: 8px;
              background: var(--semantic-color-warning-bg-light);
              border: 1px solid var(--semantic-color-warning-border-strong);
              color: var(--semantic-color-text-dark);
            "
          >
            <h4
              style="
                margin: 0 0 6px 0;
                color: var(--semantic-color-warning);
                font-size: 16px;
              "
            >
              ⚠️ Expense Claim Held for Manual Review
            </h4>
            <p id="pending-review-notes" style="margin: 0; font-size: 14px">
              {{
                wallet.expenseResult.review_notes ||
                wallet.expenseResult.reviewNotes ||
                "Expense claim exceeds auto-approval policy and has been routed to your study coordinator."
              }}
            </p>
          </div>
        </div>
      </div>

      <!-- Recent Ledger Activity -->
      <div
        v-if="
          Array.isArray(wallet.recentTransactions) &&
          wallet.recentTransactions.length > 0
        "
        id="wallet-recent-transactions"
        class="card"
        style="margin-top: 20px"
      >
        <h3 style="margin-top: 0; font-size: 16px">Recent Wallet Activity</h3>
        <ul style="list-style: none; padding: 0; margin: 0">
          <li
            v-for="tx in wallet.recentTransactions"
            :key="tx.id || tx.Id"
            style="
              display: flex;
              justify-content: space-between;
              align-items: center;
              padding: 10px 0;
              border-bottom: 1px solid var(--semantic-color-border);
              font-size: 14px;
              gap: 12px;
            "
          >
            <div>
              <div style="font-weight: 600">
                {{
                  tx.description ||
                  tx.Description ||
                  formatEnumLabel(tx.transactionType || tx.TransactionType)
                }}
              </div>
              <div style="font-size: 12px; color: var(--text-muted)">
                {{ formatEnumLabel(tx.transactionType || tx.TransactionType) }}
              </div>
            </div>
            <strong
              :style="{
                color:
                  Number(tx.amount ?? tx.Amount ?? 0) >= 0
                    ? 'var(--semantic-color-success)'
                    : 'var(--semantic-color-text)',
              }"
            >
              {{ Number(tx.amount ?? tx.Amount ?? 0) >= 0 ? "+" : "" }}${{
                Math.abs(Number(tx.amount ?? tx.Amount ?? 0)).toFixed(2)
              }}
            </strong>
          </li>
        </ul>
      </div>
    </template>
  </section>
</template>

<script setup>
import { computed, onMounted, watch } from "vue";

import {
  disburseAvailableBalance,
  extractReceiptOcr,
  loadParticipantWallet,
  submitExpenseClaim,
} from "../features/wallet.js";
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

const isAuthenticated = computed(() =>
  Boolean(
    session.session?.token &&
    session.session?.userId &&
    !session.session?.isOfflineMode
  )
);

const isWalletEnabled = computed(() => isDemo.value || isAuthenticated.value);

const formattedAvailableBalance = computed(() => {
  const amount =
    typeof wallet.walletBalance === "number" ? wallet.walletBalance : 0;
  return `$${amount.toFixed(2)} ${wallet.currency || "USD"}`;
});

const WALLET_ENUM_LABELS = Object.freeze({
  NOT_REQUIRED: "Not required",
  REQUIRED: "Required",
  SUBMITTED: "Submitted",
  VERIFIED: "Verified",
  PENDING_REVIEW: "Pending review",
  AUTO_APPROVED: "Approved",
  APPROVED: "Approved",
  PAID: "Paid",
  STIPEND_CREDIT: "Study stipend",
  EXPENSE_REIMBURSEMENT: "Expense reimbursement",
  BALANCE_DISBURSEMENT: "Balance payout",
});

function formatEnumLabel(rawValue) {
  const key = String(rawValue || "").trim();
  if (!key) return "";
  if (WALLET_ENUM_LABELS[key]) return WALLET_ENUM_LABELS[key];
  const words = key
    .replace(/([a-z0-9])([A-Z])/g, "$1 $2")
    .replace(/[._-]+/g, " ")
    .trim()
    .toLowerCase();
  return words ? words.charAt(0).toUpperCase() + words.slice(1) : key;
}

const walletError = computed(
  () => wallet.expenseOcr?.error || wallet.expenseError || ""
);

function maybeHydrateWallet() {
  if (
    ui.currentView === "view-wallet" &&
    isAuthenticated.value &&
    !isDemo.value &&
    !wallet.walletLoaded &&
    !wallet.walletLoading
  ) {
    loadParticipantWallet().catch(() => {});
  }
}

onMounted(() => {
  maybeHydrateWallet();
});

watch(
  () => [ui.currentView, session.session?.token, session.session?.userId],
  () => {
    maybeHydrateWallet();
  }
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

async function handleDisburseBalance() {
  try {
    await disburseAvailableBalance("DIRECT_ACH");
  } catch (err) {
    const msg =
      err?.message ||
      "Unable to disburse available wallet balance right now. Please try again later.";
    wallet.expenseError = msg;
  }
}
</script>

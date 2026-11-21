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
        Track verified study stipends, submit travel or meal receipts via OCR,
        and disburse your available balance to your linked payout method.
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
        color: var(--semantic-color-error);
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

    <!-- Loading Skeleton while walletLoading is true -->
    <div
      v-else-if="wallet.walletLoading"
      id="wallet-loading-skeleton"
      class="card wallet-skeleton-card"
      role="status"
      aria-live="polite"
      aria-busy="true"
      style="margin-bottom: 20px"
    >
      <div class="wallet-skeleton-line wallet-skeleton-title" />
      <div class="wallet-skeleton-line wallet-skeleton-balance" />
      <div class="wallet-skeleton-grid">
        <div class="wallet-skeleton-tile" />
        <div class="wallet-skeleton-tile" />
        <div class="wallet-skeleton-tile" />
        <div class="wallet-skeleton-tile" />
      </div>
      <span class="sr-only">Loading participant wallet balances...</span>
    </div>

    <template v-else>
      <!-- IRS $600 Form W-9 Hold / Warning Banners -->
      <div
        v-if="isW9HoldActive"
        id="wallet-w9-hold-banner"
        role="alert"
        aria-live="assertive"
        style="
          margin-bottom: 16px;
          padding: 14px 16px;
          border-radius: 8px;
          background: var(--semantic-color-error-bg);
          border: 1px solid var(--semantic-color-error);
          color: var(--semantic-color-error);
          font-size: 14px;
        "
      >
        <strong style="display: block; margin-bottom: 4px">
          ⚠️ IRS $600.00 Form W-9 Verification Hold Active
        </strong>
        <span>
          Your year-to-date taxable study compensation ({{
            formatCurrency(wallet.taxableYtd)
          }}) has reached the $600.00 IRS Form 1099 reporting threshold while
          your Form W-9 status is
          <strong>{{ wallet.w9Status || "NOT_REQUIRED" }}</strong>. On-demand
          payouts and automated expense disbursements are paused until site
          staff verify your Form W-9.
        </span>
      </div>

      <div
        v-else-if="isApproachingW9Threshold"
        id="wallet-w9-warning-banner"
        role="status"
        aria-live="polite"
        style="
          margin-bottom: 16px;
          padding: 12px 16px;
          border-radius: 8px;
          background: var(--semantic-color-warning-bg-light);
          border: 1px solid var(--semantic-color-warning-border-strong);
          color: var(--semantic-color-warning-text-dark);
          font-size: 14px;
          font-weight: 600;
        "
      >
        ℹ️ Year-to-date taxable compensation ({{
          formatCurrency(wallet.taxableYtd)
        }}) is approaching the $600.00 IRS Form W-9 threshold (current status:
        {{ wallet.w9Status || "NOT_REQUIRED" }}). Submit Form W-9 with your
        study coordinator to avoid payout holds.
      </div>

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
              wallet.disbursingBalance ||
              !(Number(wallet.walletBalance) > 0) ||
              isW9HoldActive
            "
            :aria-busy="wallet.disbursingBalance ? 'true' : 'false'"
            aria-haspopup="dialog"
            style="
              background: var(--semantic-color-surface);
              color: var(--semantic-color-primary-dark);
              font-weight: 700;
              padding: 10px 16px;
              border-radius: 8px;
              border: none;
              cursor: pointer;
            "
            @click="handleOpenDisburseModal"
          >
            {{
              wallet.disbursingBalance
                ? "Processing Payout..."
                : isW9HoldActive
                  ? "Payout Paused (W-9 Hold)"
                  : "Disburse Available Balance"
            }}
          </button>
        </div>

        <div
          style="
            display: grid;
            grid-template-columns: repeat(auto-fit, minmax(140px, 1fr));
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
              {{ formatCurrency(wallet.lifetimeStipends) }}
            </strong>
          </div>
          <div>
            <div style="font-weight: 600">Lifetime Reimbursements</div>
            <strong id="wallet-lifetime-reimbursements" style="font-size: 16px">
              {{ formatCurrency(wallet.lifetimeReimbursements) }}
            </strong>
          </div>
          <div>
            <div style="font-weight: 600">Total Paid Out</div>
            <strong id="wallet-lifetime-disbursements" style="font-size: 16px">
              {{ formatCurrency(wallet.lifetimeDisbursements) }}
            </strong>
          </div>
          <div>
            <div style="font-weight: 600">Taxable YTD (1099)</div>
            <strong id="wallet-taxable-ytd" style="font-size: 16px">
              {{ formatCurrency(wallet.taxableYtd) }}
            </strong>
          </div>
          <div>
            <div style="font-weight: 600">IRS W-9 Status</div>
            <strong id="wallet-w9-status" style="font-size: 15px">
              {{ wallet.w9Status || "NOT_REQUIRED" }}
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
          <strong>{{
            formatCurrency(
              wallet.disbursementConfirmation.amount,
              wallet.disbursementConfirmation.currency || wallet.currency
            )
          }}</strong>
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
          Upload a receipt photo or PDF to extract merchant, date, category, and
          amount via OCR. Routine claims under $100.00 (meals up to $75.00) with
          high scan confidence are auto-approved for direct ACH reimbursement.
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
          >Tap to capture or upload; OCR extraction detects merchant, date,
          category, and amount from your receipt image</small
        >
        <div style="margin-top: 10px; text-align: center">
          <button
            id="btn-manual-expense-entry"
            type="button"
            class="btn btn-secondary"
            @click="handleOpenManualEntry"
          >
            Enter Receipt Details Manually
          </button>
        </div>
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
            background: var(--semantic-color-warning-bg-light);
            border: 1px solid var(--semantic-color-warning-border-strong);
            color: var(--semantic-color-warning-text-dark);
            font-size: 14px;
            font-weight: 600;
          "
        >
          {{ wallet.expenseOcr.warning }}
        </div>

        <!-- OCR Confirmation & Manual Fallback Review Card -->
        <div
          v-if="showExpenseReviewCard"
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
            <h4 style="margin: 0; font-size: 16px">
              {{
                wallet.expenseManualEntry && !wallet.expenseOcr?.data
                  ? "Manual Receipt Claim Entry"
                  : "OCR Extracted Claim Confirmation"
              }}
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
                TRANSIT (Public Transit / Ride Share / Cab)
              </option>
              <option value="FUEL">FUEL (Gas / Fuel Station)</option>
              <option value="MILEAGE">MILEAGE (Personal Auto Mileage)</option>
              <option value="PARKING">PARKING (Parking Garage / Lot)</option>
              <option value="MEAL">MEAL (Per-Diem Allowance)</option>
              <option value="LODGING">LODGING (Hotel / Accommodation)</option>
              <option value="OTHER">OTHER (Study Travel Expense)</option>
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
              padding: 8px 12px;
              border-radius: 6px;
            "
          >
            <label
              for="ocr-meal-allowance-check"
              class="meal-allowance-label"
              style="
                display: flex;
                align-items: center;
                gap: var(--spacing-sm);
                min-height: var(--touch-target-min);
                margin-bottom: 0;
                cursor: pointer;
              "
            >
              <input
                id="ocr-meal-allowance-check"
                v-model="wallet.expenseForm.isMealAllowance"
                type="checkbox"
                style="
                  width: 24px;
                  min-width: 24px;
                  height: var(--touch-target-min);
                  min-height: var(--touch-target-min);
                  margin: 0;
                  cursor: pointer;
                  accent-color: var(--semantic-color-primary);
                "
              />
              <span
                >Meal Allowance / Per-Diem Cap ({{
                  formatCurrency(wallet.expenseForm.perDiemCap || 75)
                }}
                limit)</span
              >
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
              background: var(--semantic-color-success-bg);
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
                color: var(--semantic-color-warning-text-dark);
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

      <!-- Submitted Reimbursement Claims History -->
      <div id="wallet-expense-claims" class="card" style="margin-top: 20px">
        <h3 style="margin-top: 0; font-size: 16px">
          Expense Reimbursement Claims
        </h3>
        <p
          v-if="
            !Array.isArray(wallet.expenseClaims) ||
            wallet.expenseClaims.length === 0
          "
          id="wallet-expense-claims-empty"
          style="margin: 0; font-size: 14px; color: var(--text-muted)"
        >
          No travel or meal reimbursement claims submitted yet.
        </p>
        <ul v-else style="list-style: none; padding: 0; margin: 0">
          <li
            v-for="claim in wallet.expenseClaims"
            :key="claim.id || claim.Id || claim.claim_id"
            class="wallet-expense-claim-row"
            style="
              display: flex;
              justify-content: space-between;
              align-items: flex-start;
              padding: 12px 0;
              border-bottom: 1px solid var(--semantic-color-border);
              font-size: 14px;
              gap: 12px;
              flex-wrap: wrap;
            "
          >
            <div>
              <div style="font-weight: 700">
                {{
                  claim.merchantName ||
                  claim.merchant_name ||
                  claim.MerchantName ||
                  "Expense Claim"
                }}
              </div>
              <div style="font-size: 12px; color: var(--text-muted)">
                {{ claim.category || claim.Category || "TRAVEL" }} ·
                {{
                  claim.expenseDate ||
                  claim.expense_date ||
                  claim.ExpenseDate ||
                  "—"
                }}
              </div>
              <div
                v-if="
                  claim.disbursementReference ||
                  claim.disbursement_reference ||
                  claim.DisbursementReference
                "
                style="font-size: 12px; font-family: monospace; margin-top: 2px"
              >
                Ref:
                {{
                  claim.disbursementReference ||
                  claim.disbursement_reference ||
                  claim.DisbursementReference
                }}
              </div>
              <div
                v-else-if="
                  claim.reviewNotes || claim.review_notes || claim.ReviewNotes
                "
                style="
                  font-size: 12px;
                  color: var(--text-muted);
                  margin-top: 2px;
                "
              >
                {{
                  claim.reviewNotes || claim.review_notes || claim.ReviewNotes
                }}
              </div>
            </div>
            <div style="text-align: right">
              <strong style="display: block">
                {{
                  formatCurrency(
                    claim.netAmount ??
                      claim.net_amount ??
                      claim.NetAmount ??
                      claim.grossAmount ??
                      claim.gross_amount ??
                      claim.GrossAmount ??
                      0
                  )
                }}
              </strong>
              <span
                class="status-pill"
                :class="claimStatusClass(claim)"
                style="margin-top: 4px"
              >
                {{ claimStatusLabel(claim) }}
              </span>
            </div>
          </li>
        </ul>
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
                {{ tx.description || tx.Description || tx.transactionType }}
              </div>
              <div style="font-size: 12px; color: var(--text-muted)">
                {{ tx.transactionType || tx.TransactionType }}
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
              {{ formatSignedCurrency(tx.amount ?? tx.Amount ?? 0) }}
            </strong>
          </li>
        </ul>
      </div>

      <!-- Accessible Focus-Trapped Payout Confirmation Modal -->
      <div
        v-if="wallet.disburseModalOpen"
        id="wallet-disburse-modal"
        ref="disburseModalRef"
        class="modal-overlay"
        role="dialog"
        aria-modal="true"
        aria-labelledby="disburse-modal-title"
        aria-describedby="disburse-modal-desc"
        tabindex="-1"
      >
        <div class="modal">
          <form
            id="wallet-disburse-form"
            novalidate
            @submit.prevent="handleConfirmDisburse"
          >
            <div id="disburse-modal-title" class="modal-header">
              Confirm Wallet Balance Payout
            </div>
            <div class="modal-body">
              <p id="disburse-modal-desc">
                Choose your payout destination and verify the transfer amount
                from your available balance of
                <strong>{{ formattedAvailableBalance }}</strong>.
              </p>

              <div
                v-if="disburseModalError"
                id="disburse-modal-error"
                role="alert"
                aria-live="assertive"
                style="
                  padding: 10px 12px;
                  border-radius: 6px;
                  background: var(--semantic-color-error-bg);
                  border: 1px solid var(--semantic-color-error);
                  color: var(--semantic-color-error);
                  font-size: 13px;
                  font-weight: 600;
                  margin-bottom: 12px;
                "
              >
                {{ disburseModalError }}
              </div>

              <fieldset class="clinical-radio-grid" style="margin-bottom: 16px">
                <legend
                  style="font-weight: 700; font-size: 13px; padding: 0 4px"
                >
                  Payout Destination
                </legend>
                <div style="display: flex; flex-direction: column; gap: 8px">
                  <label
                    for="disburse-method-ach"
                    class="radio-option"
                    style="
                      min-height: var(--touch-target-min);
                      padding: 6px 10px;
                      border: 1px solid var(--semantic-color-border-control);
                      border-radius: 6px;
                    "
                  >
                    <input
                      id="disburse-method-ach"
                      v-model="wallet.disburseForm.disbursementType"
                      type="radio"
                      name="disbursementType"
                      value="DIRECT_ACH"
                    />
                    <span>
                      <strong>Direct ACH Bank Transfer</strong>
                      ({{ wallet.achAccountMask || "ACH-***4321" }})
                    </span>
                  </label>

                  <label
                    for="disburse-method-card"
                    class="radio-option"
                    style="
                      min-height: var(--touch-target-min);
                      padding: 6px 10px;
                      border: 1px solid var(--semantic-color-border-control);
                      border-radius: 6px;
                    "
                  >
                    <input
                      id="disburse-method-card"
                      v-model="wallet.disburseForm.disbursementType"
                      type="radio"
                      name="disbursementType"
                      value="VIRTUAL_CARD"
                    />
                    <span>
                      <strong>Reloadable Study Virtual Card</strong>
                      ({{ wallet.virtualCardMask || "**** **** **** 4821" }})
                    </span>
                  </label>
                </div>
              </fieldset>

              <div class="form-group" style="margin-bottom: 0">
                <div
                  style="
                    display: flex;
                    justify-content: space-between;
                    align-items: center;
                    margin-bottom: 6px;
                    gap: 8px;
                  "
                >
                  <label for="disburse-amount-input" style="margin-bottom: 0">
                    Payout Amount ({{ normalizedCurrency }})
                  </label>
                  <button
                    id="btn-disburse-full-amount"
                    type="button"
                    class="btn btn-secondary"
                    style="
                      min-height: var(--touch-target-min);
                      padding: 6px 12px;
                      font-size: 12px;
                    "
                    @click="fillFullDisburseAmount"
                  >
                    Full Balance ({{ formattedAvailableBalance }})
                  </button>
                </div>
                <input
                  id="disburse-amount-input"
                  v-model="wallet.disburseForm.amount"
                  type="number"
                  step="0.01"
                  min="0.01"
                  :max="Number(wallet.walletBalance || 0)"
                  required
                />
              </div>
            </div>
            <div class="modal-footer modal-footer-wrap">
              <button
                id="btn-disburse-cancel"
                type="button"
                class="btn btn-secondary"
                :disabled="wallet.disbursingBalance"
                @click="handleCloseDisburseModal"
              >
                Cancel
              </button>
              <button
                id="btn-disburse-confirm"
                type="submit"
                class="btn btn-primary"
                :disabled="wallet.disbursingBalance || isW9HoldActive"
              >
                {{
                  wallet.disbursingBalance
                    ? "Processing Payout..."
                    : "Confirm Payout"
                }}
              </button>
            </div>
          </form>
        </div>
      </div>
    </template>
  </section>
</template>

<script setup>
import { computed, onMounted, ref, watch } from "vue";
import { useFocusTrap } from "ui";

import {
  closeDisburseModal,
  disburseAvailableBalance,
  extractReceiptOcr,
  loadParticipantWallet,
  openDisburseModal,
  openManualExpenseEntry,
  submitExpenseClaim,
} from "../features/wallet.js";
import { state } from "../stores/state.js";
import { useSessionStore } from "../stores/session.js";
import { useUiStore } from "../stores/ui.js";
import { useWalletStore } from "../stores/wallet.js";
import {
  formatSignedWalletCurrency,
  formatWalletCurrency,
  normalizeCurrencyCode,
} from "../utils/currency.js";
import { isDemoModeActive } from "../utils/demoMode.js";

const ui = useUiStore();
const session = useSessionStore();
const wallet = useWalletStore();
const disburseModalRef = ref(null);

useFocusTrap(disburseModalRef, {
  isOpen: () => wallet.disburseModalOpen,
  onEscape: handleCloseDisburseModal,
  initialFocus: () =>
    wallet.disburseForm?.disbursementType === "VIRTUAL_CARD"
      ? "#disburse-method-card"
      : "#disburse-method-ach",
  restoreFocus: () => {
    const target = state.initiatingButton;
    state.initiatingButton = null;
    return target || "#btn-disburse-balance";
  },
});

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

const normalizedCurrency = computed(() =>
  normalizeCurrencyCode(wallet.currency)
);

function formatCurrency(amount, currencyOverride = null) {
  return formatWalletCurrency(amount, currencyOverride || wallet.currency);
}

function formatSignedCurrency(amount) {
  return formatSignedWalletCurrency(amount, wallet.currency);
}

const formattedAvailableBalance = computed(() => {
  const amount =
    typeof wallet.walletBalance === "number" ? wallet.walletBalance : 0;
  return formatWalletCurrency(amount, wallet.currency);
});

const isW9HoldActive = computed(() => {
  const thresholdExceeded =
    Boolean(wallet.is1099ThresholdExceeded) ||
    Number(wallet.taxableYtd || 0) >= 600;
  const verified =
    String(wallet.w9Status || "")
      .trim()
      .toUpperCase() === "VERIFIED";
  return thresholdExceeded && !verified;
});

const isApproachingW9Threshold = computed(() => {
  const ytd = Number(wallet.taxableYtd || 0);
  const verified =
    String(wallet.w9Status || "")
      .trim()
      .toUpperCase() === "VERIFIED";
  return !isW9HoldActive.value && ytd >= 450 && ytd < 600 && !verified;
});

const showExpenseReviewCard = computed(() =>
  Boolean(
    wallet.expenseManualEntry ||
    wallet.expenseOcr?.data ||
    wallet.expenseForm?.merchantName
  )
);

const walletError = computed(
  () => wallet.expenseOcr?.error || wallet.expenseError || ""
);

const disburseModalError = computed(() => wallet.disburseForm?.error || "");

function claimStatusLabel(claim) {
  const raw = String(
    claim.approvalDecision || claim.status || claim.Status || "PENDING_REVIEW"
  ).toUpperCase();
  if (raw === "AUTO_APPROVED" || raw === "APPROVED") return "Approved";
  if (raw === "REJECTED") return "Rejected";
  return "Pending Review";
}

function claimStatusClass(claim) {
  const raw = String(
    claim.approvalDecision || claim.status || claim.Status || "PENDING_REVIEW"
  ).toUpperCase();
  if (raw === "AUTO_APPROVED" || raw === "APPROVED") return "completed";
  if (raw === "REJECTED") return "overdue";
  return "pending";
}

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

function handleOpenManualEntry() {
  openManualExpenseEntry();
}

async function handleReceiptFileSelected(e) {
  const file = e.target?.files?.[0];
  if (e.target && "value" in e.target) {
    try {
      e.target.value = "";
    } catch {
      // Ignore synthetic event targets with read-only value descriptors
    }
  }
  if (file) {
    try {
      await extractReceiptOcr(file);
    } catch (err) {
      const msg =
        err?.message ||
        "Unable to extract receipt details right now. Please enter details manually below.";
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

function handleOpenDisburseModal(event) {
  openDisburseModal(event?.currentTarget || null);
}

function handleCloseDisburseModal() {
  closeDisburseModal();
}

function fillFullDisburseAmount() {
  if (typeof wallet.walletBalance === "number" && wallet.walletBalance > 0) {
    wallet.disburseForm.amount = Number(wallet.walletBalance).toFixed(2);
    wallet.disburseForm.error = null;
  }
}

async function handleConfirmDisburse() {
  wallet.disburseForm.error = null;
  const rawAmount = wallet.disburseForm.amount;
  const numericAmount = Number(rawAmount);
  const available = Number(wallet.walletBalance || 0);

  if (!Number.isFinite(numericAmount) || numericAmount <= 0) {
    wallet.disburseForm.error =
      "Please enter a payout amount greater than zero.";
    return;
  }
  if (numericAmount > available + 0.0001) {
    wallet.disburseForm.error = `Requested payout (${formatCurrency(numericAmount)}) exceeds your available balance (${formatCurrency(available)}).`;
    return;
  }

  try {
    await disburseAvailableBalance(wallet.disburseForm.disbursementType, {
      amount: numericAmount,
    });
  } catch (err) {
    const msg =
      err?.message ||
      "Unable to disburse available wallet balance right now. Please try again later.";
    wallet.disburseForm.error = msg;
  }
}
</script>

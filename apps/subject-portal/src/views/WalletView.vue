<template>
  <section
    id="view-wallet"
    class="portal-view"
    :class="{ active: ui.currentView === 'view-wallet' }"
    :hidden="ui.currentView !== 'view-wallet'"
    :inert="ui.currentView !== 'view-wallet' || undefined"
    :role="hasWalletTab ? 'tabpanel' : 'region'"
    :aria-labelledby="hasWalletTab ? 'btn-tab-btn-wallet' : undefined"
    :aria-label="hasWalletTab ? undefined : 'Participant Financial Wallet'"
    tabindex="0"
  >
    <div class="view-header">
      <h2 id="wallet-view-heading" tabindex="-1">
        Participant Financial Wallet &amp; Stipends
      </h2>
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
      <div class="wallet-skeleton-line wallet-skeleton-title"></div>
      <div class="wallet-skeleton-line wallet-skeleton-balance"></div>
      <div class="wallet-skeleton-grid">
        <div class="wallet-skeleton-tile"></div>
        <div class="wallet-skeleton-tile"></div>
        <div class="wallet-skeleton-tile"></div>
        <div class="wallet-skeleton-tile"></div>
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
        <strong
          style="
            display: flex;
            align-items: center;
            gap: 8px;
            margin-bottom: 4px;
          "
        >
          <ClinicalIcon name="alert-circle" :size="18" />
          <span>IRS $600.00 Form W-9 Verification Hold Active</span>
        </strong>
        <span>
          Your year-to-date taxable study compensation ({{
            formatCurrency(wallet.taxableYtd)
          }}) has reached the $600.00 IRS Form 1099 reporting threshold while
          your Form W-9 status is
          <strong>{{
            formatEnumLabel(wallet.w9Status || "NOT_REQUIRED")
          }}</strong>. On-demand payouts and automated expense disbursements are
          paused until site staff verify your Form W-9.
        </span>
        <div class="w9-resolution-links">
          <a
            id="link-w9-coordinator-support"
            href="#wallet-w9-support"
            class="w9-banner-link"
            @click.prevent="jumpToW9Support"
          >
            Contact Study Coordinator to Verify Form W-9
          </a>
          <a
            id="link-w9-email-coordinator"
            :href="siteContact.emailHref"
            class="w9-banner-link"
          >
            Email Coordinator ({{ siteContact.emailDisplay }})
          </a>
          <a
            id="link-w9-call-coordinator"
            :href="siteContact.phoneHref"
            class="w9-banner-link"
          >
            Call Coordinator ({{ siteContact.phoneDisplay }})
          </a>
        </div>
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
        <div style="display: flex; align-items: flex-start; gap: 8px">
          <ClinicalIcon name="info" :size="18" />
          <span>
            Year-to-date taxable compensation ({{
              formatCurrency(wallet.taxableYtd)
            }}) is approaching the $600.00 IRS Form W-9 threshold (current
            status: {{ formatEnumLabel(wallet.w9Status || "NOT_REQUIRED") }}).
            Submit Form W-9 with your study coordinator to avoid payout holds.
          </span>
        </div>
        <div class="w9-resolution-links">
          <a
            id="link-w9-warning-support"
            href="#wallet-w9-support"
            class="w9-banner-link"
            @click.prevent="jumpToW9Support"
          >
            Contact Study Coordinator for Form W-9
          </a>
        </div>
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
              class="tabular-nums"
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
            :aria-describedby="disburseDescribedBy"
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
          class="tabular-nums"
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
            <strong
              id="wallet-lifetime-stipends"
              class="tabular-nums"
              style="font-size: 16px"
            >
              {{ formatCurrency(wallet.lifetimeStipends) }}
            </strong>
          </div>
          <div>
            <div style="font-weight: 600">Lifetime Reimbursements</div>
            <strong
              id="wallet-lifetime-reimbursements"
              class="tabular-nums"
              style="font-size: 16px"
            >
              {{ formatCurrency(wallet.lifetimeReimbursements) }}
            </strong>
          </div>
          <div>
            <div style="font-weight: 600">Total Paid Out</div>
            <strong
              id="wallet-lifetime-disbursements"
              class="tabular-nums"
              style="font-size: 16px"
            >
              {{ formatCurrency(wallet.lifetimeDisbursements) }}
            </strong>
          </div>
          <div>
            <div style="font-weight: 600">Taxable YTD (1099)</div>
            <strong
              id="wallet-taxable-ytd"
              class="tabular-nums"
              style="font-size: 16px"
            >
              {{ formatCurrency(wallet.taxableYtd) }}
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
          id="wallet-disbursement-heading"
          tabindex="-1"
          style="
            margin: 0 0 4px 0;
            font-size: 16px;
            color: var(--semantic-color-success);
            display: flex;
            align-items: center;
            gap: 8px;
          "
        >
          <ClinicalIcon name="check" :size="18" />
          <span>Balance Payout Dispatched</span>
        </h3>
        <p style="margin: 0; font-size: 14px">
          Transferred
          <strong class="tabular-nums">{{
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
        <h3 style="margin-top: 0; display: flex; align-items: center; gap: 8px">
          <ClinicalIcon name="camera" :size="20" />
          <span>Travel &amp; Meal Receipt Reimbursement</span>
        </h3>
        <p style="font-size: 14px; color: var(--text-muted)">
          Upload a receipt photo or PDF and we will fill in the merchant, date,
          category, and amount for you to review. Routine claims under $100.00
          (meals up to $75.00) with clear receipts are approved automatically.
        </p>
        <ClinicalDropzone
          id="receipt-upload-dropzone"
          class="dropzone-upload"
          input-id="receipt-file-input"
          accept="image/*,application/pdf,.svg"
          described-by="receipt-upload-hint"
          button-label="Upload receipt image for travel reimbursement"
          input-aria-label="Upload receipt image for travel reimbursement"
          :has-selection="Boolean(wallet.expenseForm.receiptImageName)"
          removable
          remove-label="Remove Receipt"
          :remove-context="wallet.expenseForm.receiptImageName || ''"
          @file-selected="handleReceiptSelectedFile"
          @remove="handleClearReceiptSelection"
        >
          <template #icon>
            <ClinicalIcon name="camera" :size="28" />
          </template>
          <template #selected>
            <p
              v-if="wallet.expenseForm.receiptImageName"
              id="receipt-selected-filename"
              style="
                margin: 4px 0 0 0;
                font-size: 13px;
                color: var(--text-muted);
              "
            >
              Selected: {{ wallet.expenseForm.receiptImageName }}
            </p>
          </template>
        </ClinicalDropzone>
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
              display: inline-flex;
              align-items: center;
              gap: 8px;
            "
          >
            <ClinicalIcon name="search" :size="18" />
            <span>Reading Receipt Details...</span>
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
            <h4
              id="ocr-review-heading"
              tabindex="-1"
              style="margin: 0; font-size: 16px"
            >
              {{
                wallet.expenseManualEntry && !wallet.expenseOcr?.data
                  ? "Enter Receipt Details"
                  : "Review Receipt Details"
              }}
            </h4>
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
              <span class="tabular-nums"
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
            style="width: 100%; margin-top: 12px; gap: 8px"
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
              id="expense-auto-approved-heading"
              tabindex="-1"
              style="
                margin: 0 0 6px 0;
                color: var(--semantic-color-success);
                font-size: 16px;
                display: flex;
                align-items: center;
                gap: 8px;
              "
            >
              <ClinicalIcon name="check" :size="18" />
              <span>Reimbursement Auto-Approved &amp; Dispatched</span>
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
              id="expense-pending-review-heading"
              tabindex="-1"
              style="
                margin: 0 0 6px 0;
                color: var(--semantic-color-warning-text-dark);
                font-size: 16px;
                display: flex;
                align-items: center;
                gap: 8px;
              "
            >
              <ClinicalIcon name="alert-circle" :size="18" />
              <span>Expense Claim Held for Manual Review</span>
            </h4>
            <p id="pending-review-notes" style="margin: 0; font-size: 14px">
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
            :id="`wallet-claim-row-${claim.id || claim.Id || claim.claim_id}`"
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
              <div
                class="tabular-nums"
                style="font-size: 12px; color: var(--text-muted)"
              >
                {{
                  formatEnumLabel(claim.category || claim.Category || "TRAVEL")
                }}
                ·
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
                  formatPlainReviewNotes(
                    claim.reviewNotes || claim.review_notes || claim.ReviewNotes
                  )
                }}
              </div>
              <div style="margin-top: 4px">
                <EntityLink
                  :id="`btn-peek-claim-${claim.id || claim.Id || claim.claim_id}`"
                  label="View details"
                  :sr-context="claimDisplayTitle(claim)"
                  @peek="
                    ({ triggerEl }) => openExpenseClaimPeek(claim, triggerEl)
                  "
                />
              </div>
            </div>
            <div style="text-align: right">
              <strong class="tabular-nums" style="display: block">
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
            :id="`wallet-tx-row-${tx.id || tx.Id || tx.referenceId}`"
            :key="tx.id || tx.Id || tx.referenceId"
            class="wallet-tx-row"
            style="
              display: flex;
              justify-content: space-between;
              align-items: center;
              padding: 10px 0;
              border-bottom: 1px solid var(--semantic-color-border);
              font-size: 14px;
              gap: 12px;
              flex-wrap: wrap;
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
              <div style="margin-top: 4px">
                <EntityLink
                  :id="`btn-peek-tx-${tx.id || tx.Id || tx.referenceId}`"
                  label="View details"
                  :sr-context="txDisplayTitle(tx)"
                  @peek="({ triggerEl }) => openTransactionPeek(tx, triggerEl)"
                />
              </div>
            </div>
            <strong
              class="tabular-nums"
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

      <!-- Form W-9 & Payment Coordinator Support -->
      <SiteContactLinks
        id="wallet-w9-support"
        tabindex="-1"
        heading="Form W-9 & Payment Coordinator Support"
        region-label="Form W-9 and payment coordinator contact options"
        style="margin-top: 20px"
      />

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
                <strong class="tabular-nums">{{
                  formattedAvailableBalance
                }}</strong>.
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
                    flex-wrap: wrap;
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
import {
  ClinicalDropzone,
  ClinicalIcon,
  EntityLink,
  handoffFocus,
  useFocusTrap,
} from "ui";

import SiteContactLinks from "../components/SiteContactLinks.vue";
import { openExpenseClaimPeek, openTransactionPeek } from "../features/peek.js";
import { getSiteSupportContact } from "../features/site-support.js";
import {
  closeDisburseModal,
  disburseAvailableBalance,
  extractReceiptOcr,
  loadParticipantWallet,
  openDisburseModal,
  openManualExpenseEntry,
  submitExpenseClaim,
} from "../features/wallet.js";
import { isScreenAvailable } from "../router/availability.js";
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
import { humanizeExpenseReviewNotes } from "../utils/participantCopy.js";

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
    const hasPositiveBalance = Number(wallet.walletBalance || 0) > 0;
    if (target && !target.disabled && hasPositiveBalance) {
      return target;
    }
    const disburseBtn =
      typeof document !== "undefined"
        ? document.getElementById("btn-disburse-balance")
        : null;
    if (disburseBtn && !disburseBtn.disabled && hasPositiveBalance) {
      return disburseBtn;
    }
    return (
      (typeof document !== "undefined" &&
        (document.getElementById("wallet-disbursement-heading") ||
          document.getElementById("wallet-view-heading"))) ||
      "#wallet-view-heading"
    );
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

const hasWalletTab = computed(() => {
  void session.session?.isDemoMode;
  void session.session?.token;
  void session.session?.userId;
  void session.session?.isOfflineMode;
  return isDemo.value || isScreenAvailable("wallet");
});

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

const disburseDescribedBy = computed(() => {
  if (isW9HoldActive.value) return "wallet-w9-hold-banner";
  if (isApproachingW9Threshold.value) return "wallet-w9-warning-banner";
  return undefined;
});

const siteContact = computed(
  () => ui.siteSupportContact || getSiteSupportContact()
);

function jumpToW9Support() {
  const supportEl = document.getElementById("wallet-w9-support");
  supportEl?.scrollIntoView?.({ behavior: "smooth", block: "start" });
  supportEl?.focus?.();
}

function formatPlainReviewNotes(rawNotes) {
  return humanizeExpenseReviewNotes(rawNotes);
}

function claimDisplayTitle(claim) {
  return (
    claim?.merchantName ||
    claim?.merchant_name ||
    claim?.MerchantName ||
    "Expense Claim"
  );
}

function txDisplayTitle(tx) {
  return (
    tx?.description ||
    tx?.Description ||
    formatEnumLabel(tx?.transactionType || tx?.TransactionType) ||
    "Wallet Activity"
  );
}

const showExpenseReviewCard = computed(() =>
  Boolean(
    wallet.expenseManualEntry ||
    wallet.expenseOcr?.data ||
    wallet.expenseForm?.merchantName
  )
);

watch(showExpenseReviewCard, (isOpen, wasOpen) => {
  if (isOpen && !wasOpen && ui.currentView === "view-wallet") {
    void handoffFocus(["#ocr-category", "#ocr-review-heading"], {
      scrollIfOffscreen: true,
    });
  }
});

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

watch(
  () => wallet.disbursementConfirmation,
  (nextConfirm, prevConfirm) => {
    if (
      nextConfirm &&
      nextConfirm !== prevConfirm &&
      ui.currentView === "view-wallet"
    ) {
      void handoffFocus([
        "#wallet-disbursement-heading",
        "#btn-disburse-balance",
        "#wallet-view-heading",
        "#main-content",
      ]);
    }
  }
);
function handleOpenManualEntry() {
  openManualExpenseEntry();
}

function handleClearReceiptSelection() {
  wallet.expenseForm.receiptImageName = "";
  wallet.expenseForm.receiptImageDataUrl = "";
  wallet.expenseError = "";
}

async function handleReceiptSelectedFile(file) {
  if (!file) return;
  try {
    await extractReceiptOcr(file);
  } catch (err) {
    const msg =
      err?.message ||
      "Unable to extract receipt details right now. Please enter details manually below.";
    wallet.expenseError = msg;
  }
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
    const msg =
      err?.message ||
      "Unable to submit travel expense claim right now. Please try again later.";
    wallet.expenseError = msg;
    void handoffFocus([
      "#wallet-error-banner",
      "#btn-confirm-reimburse",
      "#wallet-view-heading",
    ]);
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
    if (wallet.disbursementConfirmation) {
      void handoffFocus([
        "#wallet-disbursement-heading",
        "#btn-disburse-balance",
        "#wallet-view-heading",
        "#main-content",
      ]);
    }
  } catch (err) {
    const msg =
      err?.message ||
      "Unable to disburse available wallet balance right now. Please try again later.";
    wallet.disburseForm.error = msg;
  }
}
</script>

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

    <div
      v-if="walletError"
      id="wallet-error-banner"
      class="wallet-error-alert"
      role="alert"
      aria-live="assertive"
    >
      {{ walletError }}
    </div>

    <div
      v-if="!isWalletEnabled"
      id="wallet-unavailable-card"
      class="card wallet-section-card"
      role="status"
      aria-live="polite"
    >
      <h3 class="wallet-card-heading">
        Sign In Required for Participant Wallet
      </h3>
      <p class="wallet-muted-copy">
        Direct participant stipend balances, payout cards, and receipt
        reimbursements require an active authenticated participant session.
        Please sign in or contact your study coordinator for assistance.
      </p>
    </div>

    <div
      v-else-if="wallet.walletLoading"
      id="wallet-loading-skeleton"
      class="card wallet-skeleton-card wallet-section-card"
      role="status"
      aria-live="polite"
      aria-busy="true"
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
      <WalletW9ComplianceBanners
        :is-w9-hold-active="isW9HoldActive"
        :is-approaching-w9-threshold="isApproachingW9Threshold"
        :taxable-ytd-formatted="formatCurrency(wallet.taxableYtd)"
        :w9-status-label="formatEnumLabel(wallet.w9Status || 'NOT_REQUIRED')"
        :site-contact="siteContact"
        @jump-support="jumpToW9Support"
      />

      <div class="card wallet-hero-card">
        <div class="wallet-hero-top">
          <div>
            <div class="wallet-balance-caption">
              {{
                isDemo
                  ? "Demo Sandbox Wallet Balance"
                  : "Available Wallet Balance"
              }}
            </div>
            <div
              id="wallet-balance-amount"
              class="tabular-nums wallet-balance-figure"
            >
              {{ formattedAvailableBalance }}
            </div>
          </div>

          <button
            id="btn-disburse-balance"
            type="button"
            class="btn wallet-disburse-trigger"
            :disabled="
              wallet.disbursingBalance ||
              !(Number(wallet.walletBalance) > 0) ||
              isW9HoldActive
            "
            :aria-busy="wallet.disbursingBalance ? 'true' : 'false'"
            :aria-describedby="disburseDescribedBy"
            aria-haspopup="dialog"
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

        <div class="tabular-nums wallet-kpi-grid">
          <div>
            <div class="wallet-kpi-label">Lifetime Stipends</div>
            <strong
              id="wallet-lifetime-stipends"
              class="tabular-nums wallet-kpi-value"
            >
              {{ formatCurrency(wallet.lifetimeStipends) }}
            </strong>
          </div>
          <div>
            <div class="wallet-kpi-label">Lifetime Reimbursements</div>
            <strong
              id="wallet-lifetime-reimbursements"
              class="tabular-nums wallet-kpi-value"
            >
              {{ formatCurrency(wallet.lifetimeReimbursements) }}
            </strong>
          </div>
          <div>
            <div class="wallet-kpi-label">Total Paid Out</div>
            <strong
              id="wallet-lifetime-disbursements"
              class="tabular-nums wallet-kpi-value"
            >
              {{ formatCurrency(wallet.lifetimeDisbursements) }}
            </strong>
          </div>
          <div>
            <div class="wallet-kpi-label">Taxable YTD (1099)</div>
            <strong
              id="wallet-taxable-ytd"
              class="tabular-nums wallet-kpi-value"
            >
              {{ formatCurrency(wallet.taxableYtd) }}
            </strong>
          </div>
          <div>
            <div class="wallet-kpi-label">Tax Form (W-9)</div>
            <strong id="wallet-w9-status">
              {{ formatEnumLabel(wallet.w9Status || "NOT_REQUIRED") }}
            </strong>
          </div>
        </div>
      </div>

      <div
        v-if="wallet.disbursementConfirmation"
        id="wallet-disbursement-confirmation"
        class="wallet-disbursement-banner"
        role="status"
        aria-live="polite"
      >
        <h3
          id="wallet-disbursement-heading"
          tabindex="-1"
          class="wallet-disbursement-title"
        >
          <ClinicalIcon name="check" :size="16" />
          <span>Balance Payout Dispatched</span>
        </h3>
        <p class="wallet-disbursement-copy">
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

      <div
        v-if="wallet.virtualCardMask || wallet.achAccountMask"
        id="wallet-payout-methods-card"
        class="card wallet-section-card"
      >
        <h3 class="wallet-card-heading">Linked Payout Methods (Masked)</h3>
        <div class="wallet-payout-grid">
          <div v-if="wallet.achAccountMask" class="wallet-payout-tile">
            <strong>Direct ACH Bank Account</strong>
            <div id="wallet-ach-account-mask" class="wallet-payout-mask">
              Account: {{ wallet.achAccountMask }}
            </div>
            <div
              v-if="wallet.achRoutingMask"
              id="wallet-ach-routing-mask"
              class="wallet-payout-meta"
            >
              Routing: {{ wallet.achRoutingMask }}
            </div>
          </div>

          <div v-if="wallet.virtualCardMask" class="wallet-payout-tile">
            <strong>Reloadable Study Card</strong>
            <div id="wallet-virtual-card-mask" class="wallet-payout-mask">
              Card: {{ wallet.virtualCardMask }}
            </div>
            <div v-if="wallet.virtualCardExpiry" class="wallet-payout-meta">
              Expires: {{ wallet.virtualCardExpiry }}
            </div>
          </div>
        </div>
      </div>

      <WalletExpenseReimbursementCard
        :normalized-currency="normalizedCurrency"
        :format-currency="formatCurrency"
      >
        <template #dropzone>
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
              <ClinicalIcon name="camera" :size="20" />
            </template>
            <template #selected>
              <p
                v-if="wallet.expenseForm.receiptImageName"
                id="receipt-selected-filename"
                class="wallet-selected-file"
              >
                Selected: {{ wallet.expenseForm.receiptImageName }}
              </p>
            </template>
          </ClinicalDropzone>
        </template>
      </WalletExpenseReimbursementCard>

      <WalletExpenseClaimsCard :format-currency="formatCurrency" />

      <div
        v-if="
          Array.isArray(wallet.recentTransactions) &&
          wallet.recentTransactions.length > 0
        "
        id="wallet-recent-transactions"
        class="card wallet-section-card--spaced"
      >
        <h3 class="wallet-card-heading">Recent Wallet Activity</h3>
        <ul class="wallet-ledger-list">
          <li
            v-for="tx in wallet.recentTransactions"
            :id="`wallet-tx-row-${tx.id || tx.Id || tx.referenceId}`"
            :key="tx.id || tx.Id || tx.referenceId"
            class="wallet-tx-row wallet-tx-item"
          >
            <div>
              <div class="wallet-tx-title">
                {{
                  tx.description ||
                  tx.Description ||
                  formatEnumLabel(tx.transactionType || tx.TransactionType)
                }}
              </div>
              <div class="wallet-subtext">
                {{ formatEnumLabel(tx.transactionType || tx.TransactionType) }}
              </div>
              <div class="wallet-tx-peek">
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

      <SiteContactLinks
        id="wallet-w9-support"
        tabindex="-1"
        heading="Form W-9 & Payment Coordinator Support"
        region-label="Form W-9 and payment coordinator contact options"
        class="wallet-section-card--spaced"
      />

      <WalletDisburseModal
        :formatted-available-balance="formattedAvailableBalance"
        :normalized-currency="normalizedCurrency"
        :is-w9-hold-active="isW9HoldActive"
        :format-currency="formatCurrency"
      />
    </template>
  </section>
</template>

<script setup>
import { ClinicalDropzone, ClinicalIcon, EntityLink } from "ui";

import SiteContactLinks from "../components/SiteContactLinks.vue";
import WalletDisburseModal from "../components/wallet/WalletDisburseModal.vue";
import WalletExpenseClaimsCard from "../components/wallet/WalletExpenseClaimsCard.vue";
import WalletExpenseReimbursementCard from "../components/wallet/WalletExpenseReimbursementCard.vue";
import WalletW9ComplianceBanners from "../components/wallet/WalletW9ComplianceBanners.vue";
import { useWalletViewState } from "../components/wallet/useWalletViewState.js";
import {
  formatEnumLabel,
  txDisplayTitle,
} from "../components/wallet/walletFormatters.js";
import { openTransactionPeek } from "../features/peek.js";

const {
  disburseDescribedBy,
  formatCurrency,
  formatSignedCurrency,
  formattedAvailableBalance,
  handleClearReceiptSelection,
  handleOpenDisburseModal,
  handleReceiptSelectedFile,
  hasWalletTab,
  isApproachingW9Threshold,
  isDemo,
  isW9HoldActive,
  isWalletEnabled,
  jumpToW9Support,
  normalizedCurrency,
  siteContact,
  ui,
  wallet,
  walletError,
} = useWalletViewState();
</script>

<style scoped src="../components/wallet/walletView.css"></style>
<style scoped>
.wallet-disbursement-copy {
  margin: 0;
  font-size: 14px;
}

.wallet-payout-mask {
  margin-top: 4px;
}

.wallet-selected-file {
  margin: 4px 0 0 0;
  font-size: 13px;
  color: var(--text-muted);
}

.wallet-tx-title {
  font-weight: 600;
}

.wallet-tx-peek {
  margin-top: 4px;
}

#wallet-ach-account-mask,
#wallet-ach-routing-mask,
#wallet-virtual-card-mask,
#wallet-disbursement-ref {
  font-family: var(--semantic-font-mono);
  font-variant-numeric: tabular-nums;
}

#wallet-balance-amount,
#wallet-lifetime-stipends,
#wallet-lifetime-reimbursements,
#wallet-lifetime-disbursements,
#wallet-taxable-ytd,
#wallet-disbursement-confirmation,
#wallet-recent-transactions {
  font-variant-numeric: tabular-nums;
}
</style>

<template>
  <div id="wallet-expense-claims" class="card wallet-section-card--spaced">
    <h3 class="wallet-card-heading">Expense Reimbursement Claims</h3>
    <p
      v-if="
        !Array.isArray(wallet.expenseClaims) ||
        wallet.expenseClaims.length === 0
      "
      id="wallet-expense-claims-empty"
      class="wallet-claims-empty"
    >
      No travel or meal reimbursement claims submitted yet.
    </p>
    <ul v-else class="wallet-ledger-list">
      <li
        v-for="claim in wallet.expenseClaims"
        :id="`wallet-claim-row-${claim.id || claim.Id || claim.claim_id}`"
        :key="claim.id || claim.Id || claim.claim_id"
        class="wallet-expense-claim-row wallet-claim-item"
      >
        <div>
          <div class="wallet-claim-merchant">
            {{ claimDisplayTitle(claim) }}
          </div>
          <div class="tabular-nums wallet-subtext">
            {{ formatEnumLabel(claim.category || claim.Category || "TRAVEL") }}
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
            class="wallet-claim-ref"
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
            class="wallet-claim-notes"
          >
            {{
              formatPlainReviewNotes(
                claim.reviewNotes || claim.review_notes || claim.ReviewNotes
              )
            }}
          </div>
          <div class="wallet-claim-peek">
            <EntityLink
              :id="`btn-peek-claim-${claim.id || claim.Id || claim.claim_id}`"
              label="View details"
              :sr-context="claimDisplayTitle(claim)"
              @peek="({ triggerEl }) => openExpenseClaimPeek(claim, triggerEl)"
            />
          </div>
        </div>
        <div class="wallet-claim-amount-col">
          <strong class="tabular-nums wallet-claim-amount">
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
            class="status-pill wallet-claim-pill"
            :class="claimStatusClass(claim)"
          >
            {{ claimStatusLabel(claim) }}
          </span>
        </div>
      </li>
    </ul>
  </div>
</template>

<script setup>
import { EntityLink } from "ui";
import { openExpenseClaimPeek } from "../../features/peek.js";
import { useWalletStore } from "../../stores/wallet.js";
import {
  claimDisplayTitle,
  claimStatusClass,
  claimStatusLabel,
  formatEnumLabel,
  formatPlainReviewNotes,
} from "./walletFormatters.js";

defineProps({
  formatCurrency: { type: Function, required: true },
});

const wallet = useWalletStore();
</script>

<style scoped src="./walletView.css"></style>
<style scoped>
.wallet-claims-empty {
  margin: 0;
  font-size: 14px;
  color: var(--text-muted);
}

.wallet-claim-merchant {
  font-weight: 700;
}

.wallet-claim-ref {
  font-size: 12px;
  font-family: var(--semantic-font-mono);
  margin-top: 2px;
}

.wallet-claim-notes {
  font-size: 12px;
  color: var(--text-muted);
  margin-top: 2px;
}

.wallet-claim-peek {
  margin-top: 4px;
}

.wallet-claim-amount-col {
  text-align: right;
}

.wallet-claim-amount {
  display: block;
}

.wallet-claim-pill {
  margin-top: 4px;
}

#wallet-expense-claims {
  font-variant-numeric: tabular-nums;
}
</style>

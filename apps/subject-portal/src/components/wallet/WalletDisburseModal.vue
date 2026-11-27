<template>
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
            Choose your payout destination and verify the transfer amount from
            your available balance of
            <strong class="tabular-nums">{{
              formattedAvailableBalance
            }}</strong>.
          </p>

          <div
            v-if="disburseModalError"
            id="disburse-modal-error"
            class="wallet-modal-error"
            role="alert"
            aria-live="assertive"
          >
            {{ disburseModalError }}
          </div>

          <fieldset class="clinical-radio-grid wallet-destination-fieldset">
            <legend class="wallet-destination-legend">
              Payout Destination
            </legend>
            <div class="wallet-destination-options">
              <label
                for="disburse-method-ach"
                class="radio-option wallet-radio-option"
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
                class="radio-option wallet-radio-option"
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

          <div class="form-group wallet-amount-group">
            <div class="wallet-amount-header">
              <label for="disburse-amount-input" class="wallet-amount-label">
                Payout Amount ({{ normalizedCurrency }})
              </label>
              <button
                id="btn-disburse-full-amount"
                type="button"
                class="btn btn-secondary wallet-full-balance-btn tabular-nums"
                @click="fillFullDisburseAmount"
              >
                Full Balance ({{ formattedAvailableBalance }})
              </button>
            </div>
            <input
              id="disburse-amount-input"
              v-model="wallet.disburseForm.amount"
              class="tabular-nums"
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

<script setup>
import { computed, ref } from "vue";
import { handoffFocus, useFocusTrap } from "ui";
import {
  closeDisburseModal,
  disburseAvailableBalance,
} from "../../features/wallet.js";
import { state } from "../../stores/state.js";
import { useWalletStore } from "../../stores/wallet.js";

const props = defineProps({
  formattedAvailableBalance: { type: String, required: true },
  normalizedCurrency: { type: String, required: true },
  isW9HoldActive: { type: Boolean, required: true },
  formatCurrency: { type: Function, required: true },
});

const wallet = useWalletStore();
const disburseModalRef = ref(null);
const disburseModalError = computed(() => wallet.disburseForm?.error || "");

function handleCloseDisburseModal() {
  closeDisburseModal();
}

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

function fillFullDisburseAmount() {
  if (typeof wallet.walletBalance === "number" && wallet.walletBalance > 0) {
    wallet.disburseForm.amount = Number(wallet.walletBalance).toFixed(2);
    wallet.disburseForm.error = null;
  }
}

async function handleConfirmDisburse() {
  wallet.disburseForm.error = null;
  const numericAmount = Number(wallet.disburseForm.amount);
  const available = Number(wallet.walletBalance || 0);

  if (!Number.isFinite(numericAmount) || numericAmount <= 0) {
    wallet.disburseForm.error =
      "Please enter a payout amount greater than zero.";
    return;
  }
  if (numericAmount > available + 0.0001) {
    wallet.disburseForm.error = `Requested payout (${props.formatCurrency(numericAmount)}) exceeds your available balance (${props.formatCurrency(available)}).`;
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
    wallet.disburseForm.error =
      err?.message ||
      "Unable to disburse available wallet balance right now. Please try again later.";
  }
}
</script>

<style scoped>
.wallet-modal-error {
  padding: 10px 12px;
  border-radius: 6px;
  background: var(--semantic-color-error-bg);
  border: 1px solid var(--semantic-color-error);
  color: var(--semantic-color-error);
  font-size: 13px;
  font-weight: 600;
  margin-bottom: 12px;
}

.wallet-destination-fieldset {
  margin-bottom: 16px;
}

.wallet-destination-legend {
  font-weight: 700;
  font-size: 13px;
  padding: 0 4px;
}

.wallet-destination-options {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.wallet-radio-option {
  min-height: var(--touch-target-min);
  padding: 6px 10px;
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 6px;
}

.wallet-amount-group {
  margin-bottom: 0;
}

.wallet-amount-header {
  display: flex;
  flex-wrap: wrap;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 6px;
  gap: 8px;
}

.wallet-amount-label {
  margin-bottom: 0;
}

.wallet-full-balance-btn {
  min-height: var(--touch-target-min);
  padding: 6px 12px;
  font-size: 12px;
}

#disburse-amount-input,
#wallet-disburse-modal {
  font-variant-numeric: tabular-nums;
}
</style>

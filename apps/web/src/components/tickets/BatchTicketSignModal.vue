<template>
  <!-- eslint-disable-next-line vuejs-accessibility/click-events-have-key-events, vuejs-accessibility/no-static-element-interactions -->
  <div v-if="isOpen" id="batch-ticket-sign-modal" class="modal-backdrop" @click.self="close">
    <div
      class="modal-dialog batch-ticket-sign-modal"
      role="dialog"
      aria-modal="true"
      aria-labelledby="batch-sign-modal-title"
    >
      <div class="modal-header">
        <div class="modal-header-icon">✍️</div>
        <div>
          <h3 id="batch-sign-modal-title" class="modal-title">
            21 CFR Part 11 Batch Electronic Signature
          </h3>
          <p class="modal-subtitle">
            Bulk resolution sign-off across {{ selectedTickets.length }} selected clinical issues
          </p>
        </div>
        <button
          type="button"
          class="btn-close"
          aria-label="Close"
          @click="close"
        >
          ✕
        </button>
      </div>

      <div class="modal-body">
        <div class="compliance-alert">
          <strong>21 CFR Part 11 Regulated Execution:</strong> Confirming this action
          applies a single, legally binding electronic signature to all
          <strong>{{ selectedTickets.length }}</strong> selected tickets.
          Individual Merkle-chained audit log entries will be generated per ticket record.
        </div>

        <!-- Selected Tickets Summary Table -->
        <div class="tickets-summary-box">
          <span class="summary-label">Selected Queries / Deviations:</span>
          <div class="tickets-pills-list">
            <span
              v-for="t in selectedTickets.slice(0, 10)"
              :key="t.id"
              class="ticket-pill"
            >
              {{ t.reference || `#${t.id?.substring(0, 8)}` }}
            </span>
            <span v-if="selectedTickets.length > 10" class="more-pill">
              +{{ selectedTickets.length - 10 }} more
            </span>
          </div>
        </div>

        <div class="form-group mt-3">
          <label for="batch-resolution-rationale" class="form-label">
            Bulk Resolution Rationale / Note <span class="text-danger">*</span>
          </label>
          <textarea
            id="batch-resolution-rationale"
            v-model="rationale"
            class="form-control rationale-input"
            rows="2"
            placeholder="Provide justification for bulk discrepancy resolution..."
          />
        </div>

        <div class="form-group">
          <label for="batch-root-cause" class="form-label">
            Root Cause Category
          </label>
          <select id="batch-root-cause" v-model="rootCauseCategory" class="form-control">
            <option value="DATA_ENTRY_CORRECTION">Data Entry Correction</option>
            <option value="MONITORING_VERIFIED">Monitoring Verification</option>
            <option value="PROTOCOL_AMENDMENT">Protocol Amendment Adjustment</option>
            <option value="SITE_CLARIFICATION">Site Clarification Provided</option>
            <option value="OTHER">Other / General Operations</option>
          </select>
        </div>

        <div class="form-group">
          <label for="batch-signature-meaning" class="form-label">
            Signature Meaning / Intent <span class="text-danger">*</span>
          </label>
          <select id="batch-signature-meaning" v-model="meaning" class="form-control signature-meaning-picker">
            <option value="I authoritatively verify and close these clinical deviations/queries under 21 CFR Part 11.">
              I authoritatively verify and close these clinical deviations/queries.
            </option>
            <option value="I approve the batch clinical assessment and corrective medical action.">
              I approve the batch clinical assessment and corrective medical action.
            </option>
            <option value="I confirm that all root-cause analyses and CAPA requirements are satisfied.">
              I confirm that all root-cause analyses and CAPA requirements are satisfied.
            </option>
          </select>
        </div>

        <div class="form-group">
          <label for="batch-signature-password" class="form-label">
            Re-Authenticate Password / Security Token <span class="text-danger">*</span>
          </label>
          <input
            id="batch-signature-password"
            v-model="password"
            type="password"
            class="form-control password-input"
            placeholder="Enter user password to authorize batch signature"
            autocomplete="current-password"
            @keyup.enter="handleBatchSign"
          />
        </div>

        <div v-if="errorMessage" class="error-banner">
          ⚠️ {{ errorMessage }}
        </div>
      </div>

      <div class="modal-footer">
        <button
          type="button"
          class="btn btn-secondary"
          :disabled="loading"
          @click="close"
        >
          Cancel
        </button>
        <button
          type="button"
          class="btn btn-primary btn-sign-batch-action"
          :disabled="loading || !password.trim() || !rationale.trim()"
          @click="handleBatchSign"
        >
          <span v-if="loading" class="spinner" />
          <span v-else>✍️ Execute Batch Signature ({{ selectedTickets.length }})</span>
        </button>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, computed } from "vue";
import { useAuthStore } from "../../stores/auth";
import { allowUnbackedAction } from "../../utils/unbackedActions.js";

const props = defineProps({
  isOpen: {
    type: Boolean,
    default: false,
  },
  selectedTickets: {
    type: Array,
    default: () => [],
  },
});

const emit = defineEmits(["close", "execute-batch"]);

const authStore = useAuthStore();

const rationale = ref("Bulk verification and resolution of verified site queries.");
const rootCauseCategory = ref("DATA_ENTRY_CORRECTION");
const meaning = ref(
  "I authoritatively verify and close these clinical deviations/queries under 21 CFR Part 11."
);
const password = ref("");
const loading = ref(false);
const errorMessage = ref("");

const currentUser = computed(() => {
  return authStore.identity?.username || "authenticated_user";
});

const close = () => {
  password.value = "";
  errorMessage.value = "";
  emit("close");
};

const handleBatchSign = async () => {
  if (!password.value.trim()) {
    errorMessage.value = "Password is required for 21 CFR Part 11 re-authentication.";
    return;
  }
  if (!rationale.value.trim()) {
    errorMessage.value = "Resolution rationale is required for batch updates.";
    return;
  }

  if (!allowUnbackedAction("Electronic signature")) {
    errorMessage.value = "Electronic signatures aren't available yet. Nothing was signed.";
    return;
  }

  loading.value = true;
  errorMessage.value = "";

  try {
    const mockToken = "sig_batch_" + btoa(`${currentUser.value}:${Date.now()}`);

    emit("execute-batch", {
      action: "RESOLVE",
      rationale: rationale.value,
      root_cause_category: rootCauseCategory.value,
      signature_token: mockToken,
      signature_meaning: meaning.value,
    });

    close();
  } catch (err) {
    errorMessage.value = err.message || "Failed to execute batch signature.";
  } finally {
    loading.value = false;
  }
};
</script>

<style scoped>
.modal-dialog {
  max-width: 580px;
}

.compliance-alert {
  background: #eff6ff;
  border: 1px solid #bfdbfe;
  border-left: 4px solid #2563eb;
  padding: 12px 14px;
  border-radius: 6px;
  font-size: 0.82rem;
  color: #1e3a8a;
  line-height: 1.45;
}

.tickets-summary-box {
  margin-top: 12px;
  background: #f8fafc;
  border: 1px solid #e2e8f0;
  border-radius: 6px;
  padding: 10px 12px;
}

.summary-label {
  font-size: 0.78rem;
  font-weight: 600;
  color: #475569;
  text-transform: uppercase;
  letter-spacing: 0.03em;
  display: block;
  margin-bottom: 6px;
}

.tickets-pills-list {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}

.ticket-pill {
  background: #e2e8f0;
  color: #1e293b;
  font-size: 0.75rem;
  font-weight: 600;
  padding: 2px 8px;
  border-radius: 4px;
  font-family: monospace;
}

.more-pill {
  font-size: 0.75rem;
  color: #64748b;
  font-weight: 600;
  align-self: center;
}

.text-danger {
  color: #dc2626;
}

.error-banner {
  background: #fef2f2;
  border: 1px solid #fecaca;
  color: #991b1b;
  padding: 10px 12px;
  border-radius: 6px;
  margin-top: 12px;
  font-size: 0.85rem;
}

.spinner {
  display: inline-block;
  width: 14px;
  height: 14px;
  border: 2px solid #ffffff;
  border-top-color: transparent;
  border-radius: 50%;
  animation: spin 0.6s linear infinite;
}

@keyframes spin {
  to {
    transform: rotate(360deg);
  }
}
</style>

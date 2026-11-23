<template>
  <ClinicalModal
    :is-open="isOpen"
    :teleport="false"
    :dismissible="!loading"
    :fallback-focus="fallbackFocus"
    size="md"
    dialog-class="modal-dialog ticket-sign-modal"
    aria-labelledby="sign-modal-title"
    close-label="Close"
    @close="close"
  >
    <template #header>
      <div class="ticket-sign-header">
        <div class="modal-header-icon" aria-hidden="true">✍️</div>
        <div class="ticket-sign-header-text">
          <h2 id="sign-modal-title" class="modal-title">
            21 CFR Part 11 Electronic Signature
          </h2>
          <p class="modal-subtitle">
            Legally binding electronic signature sign-off on ticket resolution
          </p>
        </div>
      </div>
    </template>

    <div class="compliance-alert">
      <strong>GxP Regulatory Manifestation:</strong> Executing this action is
      equivalent to a handwritten signature under 21 CFR Part 11. Your identity,
      timestamp, role, and intent will be immutably recorded in the audit trail.
    </div>

    <div class="form-group">
      <label for="ticket-signer-identity" class="form-label"
        >Signer Identity</label
      >
      <input
        id="ticket-signer-identity"
        type="text"
        class="form-control"
        :value="currentUser"
        readonly
        disabled
      />
    </div>

    <div class="form-group">
      <label for="ticket-signer-role" class="form-label">Active Role</label>
      <input
        id="ticket-signer-role"
        type="text"
        class="form-control"
        :value="currentRole"
        readonly
        disabled
      />
    </div>

    <div class="form-group">
      <label for="signature-meaning" class="form-label"
        >Signature Meaning / Intent <span class="text-danger">*</span></label
      >
      <select id="signature-meaning" v-model="meaning" class="form-control">
        <option
          value="I approve the clinical assessment and corrective medical action."
        >
          I approve the clinical assessment and corrective medical action.
        </option>
        <option
          value="I authoritatively verify and close this clinical deviation/query."
        >
          I authoritatively verify and close this clinical deviation/query.
        </option>
        <option
          value="I confirm that all root-cause analyses and CAPA actions are fulfilled."
        >
          I confirm that all root-cause analyses and CAPA actions are fulfilled.
        </option>
        <option
          value="I acknowledge and endorse this technical/system operational resolution."
        >
          I acknowledge and endorse this technical/system operational
          resolution.
        </option>
      </select>
    </div>

    <div class="form-group">
      <label for="signature-reason" class="form-label"
        >GxP Reason for Change <span class="text-danger">*</span></label
      >
      <input
        id="signature-reason"
        v-model="reasonForChange"
        type="text"
        class="form-control"
        placeholder="Mandatory regulatory justification for closure sign-off"
      />
    </div>

    <div class="form-group">
      <label for="signature-password" class="form-label"
        >Re-Authenticate Password / Security Token
        <span class="text-danger">*</span></label
      >
      <input
        id="signature-password"
        v-model="password"
        type="password"
        class="form-control"
        placeholder="Enter your user password to confirm identity"
        autocomplete="current-password"
        @keyup.enter="handleSign"
      />
    </div>

    <div class="form-group">
      <label for="signature-totp" class="form-label"
        >One-Time Verification Code (TOTP, if MFA enabled)</label
      >
      <input
        id="signature-totp"
        v-model="totp"
        type="text"
        inputmode="numeric"
        autocomplete="one-time-code"
        class="form-control"
        placeholder="Optional 6-digit authenticator code"
        @keyup.enter="handleSign"
      />
    </div>

    <div v-if="errorMessage" class="error-banner" role="alert">
      ⚠️ {{ errorMessage }}
    </div>

    <template #footer>
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
        class="btn btn-primary btn-sign-action"
        :disabled="loading || !password.trim() || !reasonForChange.trim()"
        @click="handleSign"
      >
        <span v-if="loading" class="spinner" aria-hidden="true" />
        <span v-else>✍️ Sign &amp; Authorize</span>
      </button>
    </template>
  </ClinicalModal>
</template>

<script setup>
import { ref, computed } from "vue";
import { ClinicalModal } from "ui";
import { ticketsApi } from "../../api/ticketsApi";
import { useAuthStore } from "../../stores/auth";
import { describeApiError } from "../../utils/apiErrors.js";
import { isDemoBuild } from "../../utils/demoMode.js";

const props = defineProps({
  isOpen: {
    type: Boolean,
    default: false,
  },
  ticket: {
    type: Object,
    default: () => ({}),
  },
  fallbackFocus: {
    type: [String, Object, Function],
    default: "#ticket-drawer-close-btn",
  },
});

const emit = defineEmits(["close", "signed"]);

const authStore = useAuthStore();
const meaning = ref(
  "I approve the clinical assessment and corrective medical action."
);
const reasonForChange = ref(
  "21 CFR Part 11 closure authorization for resolved clinical issue"
);
const password = ref("");
const totp = ref("");
const loading = ref(false);
const errorMessage = ref("");

const currentUser = computed(() => {
  return (
    authStore.user?.username ||
    authStore.identity?.username ||
    "authenticated_user"
  );
});

const currentRole = computed(() => {
  return (
    authStore.normalizedRoles?.[0] || authStore.rawRoles?.[0] || "sponsor_admin"
  );
});

const close = () => {
  password.value = "";
  totp.value = "";
  errorMessage.value = "";
  emit("close");
};

const handleSign = async () => {
  if (!password.value.trim()) {
    errorMessage.value =
      "Password is required for 21 CFR Part 11 re-authentication.";
    return;
  }
  if (!reasonForChange.value.trim()) {
    errorMessage.value =
      "Reason for change is required for 21 CFR Part 11 sign-off.";
    return;
  }

  loading.value = true;
  errorMessage.value = "";
  try {
    if (isDemoBuild()) {
      let demoToken = "";
      try {
        const verified = await ticketsApi.verifyTicketSignature({
          ticketId: props.ticket.id,
          username: currentUser.value,
          password: password.value,
          totp: totp.value.trim() || null,
        });
        demoToken = verified?.signature_token || "";
      } catch {
        demoToken = "sig_token_" + btoa(`${currentUser.value}:${Date.now()}`);
      }
      emit("signed", {
        signature_token:
          demoToken ||
          "sig_token_" + btoa(`${currentUser.value}:${Date.now()}`),
        meaning: meaning.value,
        reason_for_change: reasonForChange.value.trim(),
        version_index: props.ticket.version_index || 1,
      });
      close();
      return;
    }

    const verified = await ticketsApi.verifyTicketSignature({
      ticketId: props.ticket.id,
      username: currentUser.value,
      password: password.value,
      totp: totp.value.trim() || null,
    });
    const signedTicket = await ticketsApi.signTicket(
      props.ticket.id,
      {
        signature_meaning: meaning.value,
        signature_action: "SIGN_CLOSURE",
      },
      verified.signature_token,
      reasonForChange.value.trim()
    );
    emit("signed", {
      ticket: signedTicket,
      signature_token: signedTicket.signature_token || verified.signature_token,
      meaning: meaning.value,
      reason_for_change: reasonForChange.value.trim(),
      version_index:
        signedTicket.version_index || props.ticket.version_index || 1,
    });
    close();
  } catch (err) {
    const detail = describeApiError(err);
    errorMessage.value = detail
      ? `${detail} Nothing was signed.`
      : "Failed to capture electronic signature. Nothing was signed.";
  } finally {
    loading.value = false;
  }
};
</script>

<style scoped>
:deep(.ticket-sign-modal) {
  width: 100%;
  max-width: 540px;
}

.ticket-sign-header {
  display: flex;
  align-items: flex-start;
  gap: 10px;
  min-width: 0;
}

.ticket-sign-header-text {
  min-width: 0;
}

.modal-header-icon {
  font-size: 1.5rem;
  flex-shrink: 0;
}

.modal-title {
  font-size: 1.15rem;
  font-weight: 700;
  color: var(--semantic-color-text);
  margin: 0;
}

.modal-subtitle {
  font-size: 0.8rem;
  color: var(--semantic-color-text-secondary);
  margin: 4px 0 0 0;
}

.compliance-alert {
  background: var(--semantic-color-info-bg);
  border: 1px solid var(--semantic-color-info-border);
  border-left: 4px solid var(--semantic-color-interactive-primary);
  padding: 12px 14px;
  margin-bottom: 12px;
  border-radius: 6px;
  font-size: 0.82rem;
  color: var(--semantic-color-info);
  line-height: 1.45;
}

.form-group {
  display: flex;
  flex-direction: column;
  gap: 6px;
  margin-bottom: 12px;
  min-width: 0;
}

.form-label {
  font-size: 0.82rem;
  font-weight: 600;
  color: var(--semantic-color-text-body);
}

.form-control {
  width: 100%;
  min-width: 0;
  min-height: var(--surface-input-min-height, 44px);
  box-sizing: border-box;
  padding: 8px 12px;
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 6px;
  font-size: var(--surface-input-font-size, 1rem);
  color: var(--semantic-color-text-dark);
  background: var(--semantic-color-surface);
}

.text-danger {
  color: var(--semantic-color-error);
}

.form-control:disabled {
  background: var(--semantic-color-surface-header);
  color: var(--semantic-color-text-secondary);
  cursor: not-allowed;
}

.btn {
  max-width: 100%;
  box-sizing: border-box;
}

.spinner {
  display: inline-block;
  width: 14px;
  height: 14px;
  border: 2px solid var(--semantic-color-surface);
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

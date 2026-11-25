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
        <div class="modal-header-icon" aria-hidden="true">
          <FileSignature :size="22" aria-hidden="true" />
        </div>
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

    <form
      id="ticket-sign-form"
      ref="formRef"
      novalidate
      @submit.prevent="handleSign"
    >
      <div class="compliance-alert">
        <strong>GxP Regulatory Manifestation:</strong> Executing this action is
        equivalent to a handwritten signature under 21 CFR Part 11. Your
        identity, timestamp, role, and intent will be immutably recorded in the
        audit trail.
      </div>

      <div class="form-group">
        <ClinicalInput
          id="ticket-signer-identity"
          :model-value="currentUser"
          label="Signer Identity"
          density="compact"
          :show-query-flag="false"
          control-class="form-control"
          v-bind="SIGNATURE_USERNAME_INPUT_ATTRS"
          readonly
          disabled
        />
      </div>

      <div class="form-group">
        <ClinicalInput
          id="ticket-signer-role"
          :model-value="currentRole"
          label="Active Role"
          density="compact"
          :show-query-flag="false"
          control-class="form-control"
          readonly
          disabled
        />
      </div>

      <div class="form-group">
        <ClinicalInput
          id="signature-meaning"
          v-model="meaning"
          as="select"
          label="Signature Meaning / Intent"
          required
          density="compact"
          :show-query-flag="false"
          control-class="form-control"
          @keydown.enter.prevent="handleSign"
        >
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
            I confirm that all root-cause analyses and CAPA actions are
            fulfilled.
          </option>
          <option
            value="I acknowledge and endorse this technical/system operational resolution."
          >
            I acknowledge and endorse this technical/system operational
            resolution.
          </option>
        </ClinicalInput>
      </div>

      <div class="form-group">
        <ClinicalInput
          id="signature-reason"
          v-model="reasonForChange"
          label="GxP Reason for Change"
          required
          density="compact"
          :show-query-flag="false"
          control-class="form-control"
          placeholder="Mandatory regulatory justification for closure sign-off"
          @keydown.enter.prevent="handleSign"
        />
      </div>

      <div class="form-group">
        <ClinicalInput
          id="signature-password"
          v-model="password"
          v-bind="SIGNATURE_PASSWORD_INPUT_ATTRS"
          label="Re-Authenticate Password / Security Token"
          required
          density="compact"
          :show-query-flag="false"
          control-class="form-control"
          placeholder="Enter your user password to confirm identity"
          @keydown.enter.prevent="handleSign"
        />
      </div>

      <div class="form-group">
        <ClinicalInput
          id="signature-totp"
          v-model="totp"
          v-bind="SIGNATURE_TOTP_INPUT_ATTRS"
          label="One-Time Verification Code (TOTP, if MFA enabled)"
          density="compact"
          :show-query-flag="false"
          control-class="form-control"
          placeholder="Optional 6-digit authenticator code"
          @keydown.enter.prevent="handleSign"
        />
      </div>

      <ModalErrorAlert
        id="ticket-sign-error"
        :error="errorMessage"
        retry-label="Retry"
        dismiss-label="Dismiss"
        @retry="handleRetry"
        @dismiss="errorMessage = ''"
      />
    </form>

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
        type="submit"
        form="ticket-sign-form"
        class="btn btn-primary btn-sign-action"
        :disabled="loading"
        @click.prevent="handleSign"
      >
        <span v-if="loading" class="spinner" aria-hidden="true" />
        <span v-else>Sign &amp; Authorize</span>
      </button>
    </template>
  </ClinicalModal>
</template>

<script setup>
import { ref, computed } from "vue";
import { FileSignature } from "lucide-vue-next";
import {
  ClinicalInput,
  ClinicalModal,
  ModalErrorAlert,
  SIGNATURE_PASSWORD_INPUT_ATTRS,
  SIGNATURE_TOTP_INPUT_ATTRS,
  SIGNATURE_USERNAME_INPUT_ATTRS,
} from "ui";
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
const formRef = ref(null);
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

const focusModalField = (selector) => {
  const el =
    formRef.value?.querySelector(selector) ||
    (typeof document !== "undefined" ? document.querySelector(selector) : null);
  if (el && typeof el.focus === "function") {
    el.focus();
  }
};

const handleRetry = async () => {
  if (!reasonForChange.value.trim()) {
    focusModalField("#signature-reason");
    return;
  }
  if (!password.value.trim()) {
    focusModalField("#signature-password");
    return;
  }
  await handleSign();
};

const handleSign = async () => {
  if (!password.value.trim()) {
    errorMessage.value =
      "Password is required for 21 CFR Part 11 re-authentication.";
    focusModalField("#signature-password");
    return;
  }
  if (!reasonForChange.value.trim()) {
    errorMessage.value =
      "Reason for change is required for 21 CFR Part 11 sign-off.";
    focusModalField("#signature-reason");
    return;
  }

  const submittedPassword = password.value;
  const submittedTotp = totp.value.trim() || null;
  password.value = "";
  totp.value = "";

  loading.value = true;
  errorMessage.value = "";
  try {
    if (isDemoBuild()) {
      let demoToken = "";
      try {
        const verified = await ticketsApi.verifyTicketSignature({
          ticketId: props.ticket.id,
          username: currentUser.value,
          password: submittedPassword,
          totp: submittedTotp,
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
      password: submittedPassword,
      totp: submittedTotp,
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
  display: inline-flex;
  align-items: center;
  justify-content: center;
  color: var(--semantic-color-interactive-primary);
  flex-shrink: 0;
  margin-top: 2px;
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
  margin-bottom: 12px;
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

<style scoped src="./ticketModalControls.css"></style>

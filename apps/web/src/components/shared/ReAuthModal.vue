<template>
  <ClinicalModal
    id="re-auth-modal"
    :is-open="isOpen"
    :teleport="false"
    :dismissible="!busy"
    :initial-focus="credentialInputRef"
    size="sm"
    dialog-class="modal-card reauth-modal-card"
    aria-labelledby="reauth-modal-title"
    close-label="Close modal"
    @close="handleCancel"
    @contextmenu.prevent
  >
    <template #header>
      <div class="reauth-header-row">
        <h3 id="reauth-modal-title">
          <Lock class="lock-icon" :size="18" aria-hidden="true" />
          <span>{{ title }}</span>
        </h3>
        <button
          type="button"
          class="close-btn"
          aria-label="Close modal"
          :disabled="busy"
          @click="handleCancel"
        >
          <X :size="18" aria-hidden="true" />
        </button>
      </div>
    </template>

    <form
      id="reauth-form"
      ref="formRef"
      class="reauth-form-stack"
      novalidate
      @submit.prevent="handleConfirm"
    >
      <p class="modal-instructions">
        In compliance with <strong>FDA 21 CFR Part 11 / EU Annex 11</strong>,
        document approval requires an electronic signature. Please re-enter your
        credential (PIN or password) and confirm your signing reason.
      </p>

      <div v-if="filename" class="document-context-card">
        <span class="context-label">Target Document:</span>
        <span class="context-value font-mono">{{ filename }}</span>
      </div>

      <div class="form-group">
        <label for="reauth-username">Signer Identity</label>
        <input
          id="reauth-username"
          v-bind="SIGNATURE_USERNAME_INPUT_ATTRS"
          class="form-control"
          :value="username || 'Current User'"
          disabled
          readonly
        />
      </div>

      <div class="form-group">
        <label for="reauth-credential">
          Credential (PIN or Password) <span class="required-star">*</span>
        </label>
        <input
          id="reauth-credential"
          ref="credentialInputRef"
          v-model="credentialVal"
          v-bind="SIGNATURE_PASSWORD_INPUT_ATTRS"
          class="form-control"
          placeholder="Enter your PIN or password"
          required
          aria-required="true"
          :aria-invalid="effectiveError ? 'true' : undefined"
          :aria-describedby="effectiveError ? 'reauth-error-alert' : undefined"
          :disabled="busy"
          @keydown.enter.prevent="handleConfirm"
        />
      </div>

      <div class="form-group">
        <label for="reauth-signing-reason">
          Meaning of Signature / Reason <span class="required-star">*</span>
        </label>
        <select
          id="reauth-signing-reason"
          ref="signingReasonRef"
          v-model="signingReasonVal"
          class="form-control"
          required
          aria-required="true"
          :disabled="busy"
          @keydown.enter.prevent="handleConfirm"
        >
          <option v-for="r in availableReasons" :key="r" :value="r">
            {{ r }}
          </option>
        </select>
      </div>

      <ModalErrorAlert
        id="reauth-error-alert"
        :error="effectiveError"
        retry-label="Retry"
        dismiss-label="Dismiss"
        @retry="handleRetry"
        @dismiss="handleDismissError"
      />
    </form>

    <template #footer>
      <button
        id="btn-cancel-reauth"
        type="button"
        class="btn btn-secondary"
        :disabled="busy"
        @click="handleCancel"
      >
        Cancel
      </button>
      <button
        id="btn-confirm-reauth"
        type="submit"
        form="reauth-form"
        class="btn btn-primary"
        :disabled="busy"
        @click.prevent="handleConfirm"
      >
        {{ busy ? "Verifying & Signing..." : "Confirm & Sign" }}
      </button>
    </template>
  </ClinicalModal>
</template>

<script setup>
import { ref, computed, watch } from "vue";
import { Lock, X } from "lucide-vue-next";
import {
  ClinicalModal,
  ModalErrorAlert,
  SIGNATURE_PASSWORD_INPUT_ATTRS,
  SIGNATURE_USERNAME_INPUT_ATTRS,
} from "ui";

const props = defineProps({
  isOpen: {
    type: Boolean,
    default: false,
  },
  title: {
    type: String,
    default: "Electronic Signature Document Approval",
  },
  filename: {
    type: String,
    default: "",
  },
  username: {
    type: String,
    default: "",
  },
  reasons: {
    type: Array,
    default: () => [
      "APPROVAL",
      "SPONSOR_APPROVAL",
      "INVESTIGATOR_SIGNATURE",
      "TECHNICAL_QC",
      "CLINICAL_QC",
      "REVIEW",
      "AUTHOR",
    ],
  },
  defaultReason: {
    type: String,
    default: "APPROVAL",
  },
  busy: {
    type: Boolean,
    default: false,
  },
  errorMessage: {
    type: String,
    default: "",
  },
});

const emit = defineEmits(["close", "cancel", "confirm"]);

const formRef = ref(null);
const credentialInputRef = ref(null);
const signingReasonRef = ref(null);

const credentialVal = ref("");
const signingReasonVal = ref(props.defaultReason || "APPROVAL");
const localError = ref("");
const propErrorDismissed = ref(false);

const availableReasons = computed(() => {
  return props.reasons && props.reasons.length > 0
    ? props.reasons
    : ["APPROVAL", "SPONSOR_APPROVAL", "REVIEW", "AUTHOR"];
});

const effectiveError = computed(() => {
  if (localError.value) return localError.value;
  if (propErrorDismissed.value) return "";
  return props.errorMessage;
});

watch(
  () => props.errorMessage,
  () => {
    propErrorDismissed.value = false;
  }
);

watch(
  () => props.isOpen,
  (open) => {
    if (open) {
      credentialVal.value = "";
      signingReasonVal.value = props.defaultReason || "APPROVAL";
      localError.value = "";
      propErrorDismissed.value = false;
    } else {
      // Clear sensitive credentials immediately on modal close
      credentialVal.value = "";
      localError.value = "";
      propErrorDismissed.value = false;
    }
  }
);

function focusCredentialInput() {
  const el =
    credentialInputRef.value ||
    formRef.value?.querySelector("#reauth-credential");
  if (el && typeof el.focus === "function") {
    el.focus();
  }
}

function focusSigningReason() {
  const el =
    signingReasonRef.value ||
    formRef.value?.querySelector("#reauth-signing-reason");
  if (el && typeof el.focus === "function") {
    el.focus();
  }
}

function handleDismissError() {
  localError.value = "";
  propErrorDismissed.value = true;
}

function handleRetry() {
  propErrorDismissed.value = true;
  localError.value = "";
  if (!credentialVal.value || !credentialVal.value.trim()) {
    localError.value = "PIN or password credential is required.";
    focusCredentialInput();
    return;
  }
  if (!signingReasonVal.value) {
    localError.value = "Please select a controlled signing reason.";
    focusSigningReason();
    return;
  }
  handleConfirm();
}

function handleCancel() {
  credentialVal.value = "";
  localError.value = "";
  propErrorDismissed.value = false;
  emit("cancel");
  emit("close");
}

function handleConfirm() {
  propErrorDismissed.value = true;
  if (!credentialVal.value || !credentialVal.value.trim()) {
    localError.value = "PIN or password credential is required.";
    focusCredentialInput();
    return;
  }
  if (!signingReasonVal.value) {
    localError.value = "Please select a controlled signing reason.";
    focusSigningReason();
    return;
  }

  localError.value = "";
  const cred = credentialVal.value.trim();
  const reason = signingReasonVal.value;

  // Immediately clear the input buffer to prevent credential retention
  credentialVal.value = "";

  emit("confirm", {
    credential: cred,
    signing_reason: reason,
  });
}

defineExpose({
  credentialVal,
  signingReasonVal,
  handleConfirm,
  handleRetry,
  handleCancel,
});
</script>

<style scoped>
:deep(.reauth-modal-card) {
  max-width: 480px;
}

.reauth-header-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  width: 100%;
  min-width: 0;
}

.reauth-header-row h3 {
  margin: 0;
  font-size: 1.05rem;
  font-weight: 600;
  color: var(--semantic-color-text);
  display: flex;
  align-items: center;
  gap: 8px;
  min-width: 0;
}

.lock-icon {
  flex-shrink: 0;
  color: var(--semantic-color-interactive-primary);
}

.close-btn {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  background: transparent;
  border: none;
  color: var(--semantic-color-text-secondary);
  cursor: pointer;
  padding: 6px;
  border-radius: 4px;
  flex-shrink: 0;
}

.close-btn:hover:not(:disabled) {
  color: var(--semantic-color-text);
  background: var(--border);
}

.reauth-form-stack {
  display: flex;
  flex-direction: column;
  gap: 16px;
  min-width: 0;
}

.modal-instructions {
  margin: 0;
  font-size: 0.875rem;
  color: var(--semantic-color-text-secondary);
  line-height: 1.45;
}

.document-context-card {
  padding: 10px 12px;
  background: var(--semantic-color-surface-muted);
  border: 1px solid var(--border);
  border-radius: var(--radius-sm, 6px);
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.context-label {
  font-size: 0.75rem;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  color: var(--semantic-color-text-secondary);
  font-weight: 600;
}

.context-value {
  font-size: 0.875rem;
  color: var(--semantic-color-text);
  word-break: break-all;
}

.form-group {
  display: flex;
  flex-direction: column;
  gap: 6px;
  min-width: 0;
}

.form-group label {
  font-size: 0.85rem;
  font-weight: 500;
  color: var(--semantic-color-text);
}

.required-star {
  color: var(--semantic-color-error);
}

.form-control {
  width: 100%;
  min-width: 0;
  box-sizing: border-box;
  padding: 9px 12px;
  font-size: 0.9rem;
  border: 1px solid var(--border);
  border-radius: var(--radius-md, 6px);
  background: var(--semantic-color-surface);
  color: var(--semantic-color-text);
  transition:
    border-color 0.15s ease,
    box-shadow 0.15s ease;
}

.form-control:focus-visible {
  border-color: var(--primary);
}

.form-control:disabled {
  background: var(--semantic-color-surface-muted);
  color: var(--semantic-color-text-secondary);
  cursor: not-allowed;
}

.btn {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  padding: 8px 16px;
  font-size: 0.875rem;
  font-weight: 500;
  border-radius: var(--radius-md, 6px);
  cursor: pointer;
  max-width: 100%;
  box-sizing: border-box;
  transition:
    background-color 0.15s ease,
    border-color 0.15s ease;
}

.btn:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

.btn-secondary {
  background: var(--semantic-color-surface);
  border: 1px solid var(--border);
  color: var(--semantic-color-text);
}

.btn-secondary:hover:not(:disabled) {
  background: var(--semantic-color-surface-header);
  border-color: var(--semantic-color-border-control);
}

.btn-primary {
  background: var(--primary);
  border: 1px solid var(--primary);
  color: var(--semantic-color-surface);
}

.btn-primary:hover:not(:disabled) {
  background: var(--semantic-color-interactive-primary-hover);
}

@media (max-width: 1024px), (pointer: coarse) {
  .form-control {
    min-height: 48px;
    font-size: 16px;
  }
}
</style>

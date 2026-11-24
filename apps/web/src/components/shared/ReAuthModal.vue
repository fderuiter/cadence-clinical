<template>
  <div
    v-if="isOpen"
    class="modal-backdrop"
    role="presentation"
    @click.self="handleCancel"
  >
    <div
      id="re-auth-modal"
      ref="modalRef"
      class="modal-card reauth-modal-card"
      role="dialog"
      aria-modal="true"
      :aria-labelledby="'reauth-modal-title'"
      tabindex="-1"
      @contextmenu.prevent
    >
      <div class="modal-header">
        <h3 id="reauth-modal-title">
          <span class="lock-icon" aria-hidden="true">🔐</span>
          {{ title }}
        </h3>
        <button
          type="button"
          class="close-btn"
          aria-label="Close modal"
          :disabled="busy"
          @click="handleCancel"
        >
          ×
        </button>
      </div>

      <form
        class="reauth-modal-form"
        novalidate
        @submit.prevent="handleConfirm"
      >
        <div class="modal-body">
          <p class="modal-instructions">
            In compliance with
            <strong>FDA 21 CFR Part 11 / EU Annex 11</strong>, document approval
            requires an electronic signature. Please re-enter your credential
            (PIN or password) and confirm your signing reason.
          </p>

          <div v-if="filename" class="document-context-card">
            <span class="context-label">Target Document:</span>
            <span class="context-value font-mono">{{ filename }}</span>
          </div>

          <div class="form-group">
            <label for="reauth-username">Signer Identity</label>
            <input
              id="reauth-username"
              type="text"
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
              type="password"
              class="form-control"
              placeholder="Enter your PIN or password"
              autocomplete="current-password"
              :disabled="busy"
              :aria-invalid="
                effectiveError && !isReasonError ? 'true' : undefined
              "
              :aria-describedby="
                effectiveError && !isReasonError
                  ? 'reauth-error-alert'
                  : undefined
              "
              @input="handleCredentialInput"
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
              :disabled="busy"
              :aria-invalid="isReasonError ? 'true' : undefined"
              :aria-describedby="
                isReasonError ? 'reauth-error-alert' : undefined
              "
              @change="handleReasonChange"
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
        </div>

        <div class="modal-footer">
          <button
            type="button"
            class="btn btn-secondary"
            :disabled="busy"
            @click="handleCancel"
          >
            Cancel
          </button>
          <button
            type="submit"
            class="btn btn-primary"
            :disabled="busy || !credentialVal.trim()"
            @click="handleSubmitClick"
          >
            {{ busy ? "Verifying & Signing..." : "Confirm & Sign" }}
          </button>
        </div>
      </form>
    </div>
  </div>
</template>

<script setup>
import { ref, computed, watch, nextTick } from "vue";
import { ModalErrorAlert, useFocusTrap } from "ui";

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

const emit = defineEmits(["close", "cancel", "confirm", "clear-error"]);

const modalRef = ref(null);
const credentialInputRef = ref(null);
const signingReasonRef = ref(null);

const availableReasons = computed(() => {
  return props.reasons && props.reasons.length > 0
    ? props.reasons
    : ["APPROVAL", "SPONSOR_APPROVAL", "REVIEW", "AUTHOR"];
});

function resolveDefaultReason() {
  const reasons = availableReasons.value;
  if (props.defaultReason && reasons.includes(props.defaultReason)) {
    return props.defaultReason;
  }
  return reasons[0] || "APPROVAL";
}

const credentialVal = ref("");
const signingReasonVal = ref(resolveDefaultReason());
const localError = ref("");
const parentErrorDismissed = ref(false);

useFocusTrap(modalRef, {
  isOpen: () => props.isOpen,
  onEscape: handleCancel,
  initialFocus: credentialInputRef,
});

const effectiveError = computed(() => {
  if (localError.value) return localError.value;
  if (parentErrorDismissed.value) return "";
  return props.errorMessage;
});

const isReasonError = computed(() =>
  Boolean(
    effectiveError.value &&
    !signingReasonVal.value &&
    credentialVal.value.trim()
  )
);

async function focusCredentialInput() {
  await nextTick();
  credentialInputRef.value?.focus?.();
}

async function focusSigningReason() {
  await nextTick();
  signingReasonRef.value?.focus?.();
}

watch(
  () => props.errorMessage,
  () => {
    parentErrorDismissed.value = false;
  }
);

watch(
  () => props.busy,
  async (isBusy, wasBusy) => {
    if (
      wasBusy &&
      !isBusy &&
      props.isOpen &&
      effectiveError.value &&
      !credentialVal.value
    ) {
      await focusCredentialInput();
    }
  }
);

watch(
  () => props.isOpen,
  (open) => {
    if (open) {
      credentialVal.value = "";
      signingReasonVal.value = resolveDefaultReason();
      localError.value = "";
      parentErrorDismissed.value = false;
    } else {
      // Clear sensitive credentials immediately on modal close
      credentialVal.value = "";
      localError.value = "";
      parentErrorDismissed.value = false;
    }
  }
);

function handleCredentialInput() {
  if (localError.value === "PIN or password credential is required.") {
    localError.value = "";
  }
}

function handleReasonChange() {
  if (localError.value === "Please select a controlled signing reason.") {
    localError.value = "";
  }
}

async function handleDismissError() {
  localError.value = "";
  parentErrorDismissed.value = true;
  emit("clear-error");
  if (!credentialVal.value.trim()) {
    await focusCredentialInput();
  } else if (!signingReasonVal.value) {
    await focusSigningReason();
  }
}

async function handleRetry() {
  if (!credentialVal.value || !credentialVal.value.trim()) {
    await focusCredentialInput();
    return;
  }
  if (!signingReasonVal.value) {
    await focusSigningReason();
    return;
  }
  handleConfirm();
}

function handleSubmitClick(event) {
  event?.preventDefault?.();
  handleConfirm();
}

function handleCancel() {
  credentialVal.value = "";
  localError.value = "";
  parentErrorDismissed.value = false;
  emit("cancel");
  emit("close");
}

function handleConfirm() {
  if (!credentialVal.value || !credentialVal.value.trim()) {
    localError.value = "PIN or password credential is required.";
    void focusCredentialInput();
    return;
  }
  if (!signingReasonVal.value) {
    localError.value = "Please select a controlled signing reason.";
    void focusSigningReason();
    return;
  }

  localError.value = "";
  parentErrorDismissed.value = false;
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
  handleCancel,
});
</script>

<style scoped>
.modal-backdrop {
  position: fixed;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  background-color: rgba(15, 23, 42, 0.65);
  backdrop-filter: blur(3px);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 1100;
  padding: 16px;
}

.reauth-modal-card {
  background: var(--semantic-color-surface);
  border-radius: var(--radius-lg, 12px);
  border: 1px solid var(--border);
  box-shadow:
    0 20px 25px -5px rgba(0, 0, 0, 0.15),
    0 10px 10px -5px rgba(0, 0, 0, 0.08);
  width: 100%;
  max-width: 480px;
  overflow: hidden;
  display: flex;
  flex-direction: column;
}

.modal-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 16px 20px;
  border-bottom: 1px solid var(--border);
  background: var(--semantic-color-surface-muted);
}

.modal-header h3 {
  margin: 0;
  font-size: 1.1rem;
  font-weight: 600;
  color: var(--semantic-color-text);
  display: flex;
  align-items: center;
  gap: 8px;
}

.close-btn {
  background: transparent;
  border: none;
  font-size: 1.5rem;
  line-height: 1;
  color: var(--semantic-color-text-secondary);
  cursor: pointer;
  padding: 4px 8px;
  border-radius: 4px;
}

.close-btn:hover:not(:disabled) {
  color: var(--semantic-color-text);
  background: var(--border);
}

.reauth-modal-form {
  display: flex;
  flex-direction: column;
  min-width: 0;
}

.modal-body {
  padding: 20px;
  display: flex;
  flex-direction: column;
  gap: 16px;
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

.alert-box {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 10px 14px;
  border-radius: var(--radius-md, 6px);
  font-size: 0.85rem;
  line-height: 1.35;
}

.error-box {
  background: var(--semantic-color-error-bg-soft);
  border: 1px solid var(--semantic-color-error-border-soft);
  color: var(--semantic-color-error-text-dark);
}

.modal-footer {
  display: flex;
  align-items: center;
  justify-content: flex-end;
  gap: 12px;
  padding: 14px 20px;
  border-top: 1px solid var(--border);
  background: var(--semantic-color-surface-muted);
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
</style>

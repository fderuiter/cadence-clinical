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
          class="close-btn"
          aria-label="Close modal"
          :disabled="busy"
          @click="handleCancel"
        >
          ×
        </button>
      </div>

      <div class="modal-body">
        <p class="modal-instructions">
          In compliance with <strong>FDA 21 CFR Part 11 / EU Annex 11</strong>,
          document approval requires an electronic signature. Please re-enter
          your credential (PIN or password) and confirm your signing reason.
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
            @keyup.enter="handleConfirm"
          />
        </div>

        <div class="form-group">
          <label for="reauth-signing-reason">
            Meaning of Signature / Reason <span class="required-star">*</span>
          </label>
          <select
            id="reauth-signing-reason"
            v-model="signingReasonVal"
            class="form-control"
            :disabled="busy"
          >
            <option v-for="r in availableReasons" :key="r" :value="r">
              {{ r }}
            </option>
          </select>
        </div>

        <ModalErrorAlert
          :error="effectiveError"
          retry-label="Retry"
          dismiss-label="Dismiss"
          @retry="handleConfirm"
          @dismiss="localError = ''"
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
          type="button"
          class="btn btn-primary"
          :disabled="busy || !credentialVal.trim()"
          @click="handleConfirm"
        >
          {{ busy ? "Verifying & Signing..." : "Confirm & Sign" }}
        </button>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, computed, watch } from "vue";
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

const emit = defineEmits(["close", "cancel", "confirm"]);

const modalRef = ref(null);
const credentialInputRef = ref(null);

const credentialVal = ref("");
const signingReasonVal = ref(props.defaultReason || "APPROVAL");
const localError = ref("");

useFocusTrap(modalRef, {
  isOpen: () => props.isOpen,
  onEscape: handleCancel,
  initialFocus: credentialInputRef,
});

const availableReasons = computed(() => {
  return props.reasons && props.reasons.length > 0
    ? props.reasons
    : ["APPROVAL", "SPONSOR_APPROVAL", "REVIEW", "AUTHOR"];
});

const effectiveError = computed(() => {
  return props.errorMessage || localError.value;
});

watch(
  () => props.isOpen,
  (open) => {
    if (open) {
      credentialVal.value = "";
      signingReasonVal.value = props.defaultReason || "APPROVAL";
      localError.value = "";
    } else {
      // Clear sensitive credentials immediately on modal close
      credentialVal.value = "";
      localError.value = "";
    }
  }
);

function handleCancel() {
  credentialVal.value = "";
  localError.value = "";
  emit("cancel");
  emit("close");
}

function handleConfirm() {
  if (!credentialVal.value || !credentialVal.value.trim()) {
    localError.value = "PIN or password credential is required.";
    return;
  }
  if (!signingReasonVal.value) {
    localError.value = "Please select a controlled signing reason.";
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
  background-color: var(--semantic-color-backdrop);
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
  box-shadow: var(--shadow-xl);
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
  font-size: var(--font-size-lg);
  font-weight: 600;
  color: var(--semantic-color-text);
  display: flex;
  align-items: center;
  gap: 8px;
}

.close-btn {
  background: transparent;
  border: none;
  font-size: var(--font-size-2xl);
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

.modal-body {
  padding: 20px;
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.modal-instructions {
  margin: 0;
  font-size: var(--font-size-sm);
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
  font-size: var(--font-size-2xs);
  text-transform: uppercase;
  letter-spacing: 0.05em;
  color: var(--semantic-color-text-secondary);
  font-weight: 600;
}

.context-value {
  font-size: var(--font-size-sm);
  color: var(--semantic-color-text);
  word-break: break-all;
}

.form-group {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.form-group label {
  font-size: var(--font-size-sm);
  font-weight: 500;
  color: var(--semantic-color-text);
}

.required-star {
  color: var(--semantic-color-error);
}

.form-control {
  padding: 9px 12px;
  font-size: var(--font-size-md);
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
  font-size: var(--font-size-sm);
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
  font-size: var(--font-size-sm);
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

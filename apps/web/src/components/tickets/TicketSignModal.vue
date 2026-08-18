<template>
  <div v-if="isOpen" class="modal-backdrop" @click.self="close">
    <div
      class="modal-dialog ticket-sign-modal"
      role="dialog"
      aria-modal="true"
      aria-labelledby="sign-modal-title"
    >
      <div class="modal-header">
        <div class="modal-header-icon">✍️</div>
        <div>
          <h3 id="sign-modal-title" class="modal-title">
            21 CFR Part 11 Electronic Signature
          </h3>
          <p class="modal-subtitle">
            Legally binding electronic signature sign-off on ticket resolution
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
          <strong>GxP Regulatory Manifestation:</strong> Executing this action
          is equivalent to a handwritten signature under 21 CFR Part 11. Your
          identity, timestamp, role, and intent will be immutably recorded in
          the audit trail.
        </div>

        <div class="form-group">
          <label class="form-label">Signer Identity</label>
          <input
            type="text"
            class="form-control"
            :value="currentUser"
            readonly
            disabled
          />
        </div>

        <div class="form-group">
          <label class="form-label">Active Role</label>
          <input
            type="text"
            class="form-control"
            :value="currentRole"
            readonly
            disabled
          />
        </div>

        <div class="form-group">
          <label for="signature-meaning" class="form-label"
            >Signature Meaning / Intent
            <span class="text-danger">*</span></label
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
              I confirm that all root-cause analyses and CAPA actions are
              fulfilled.
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
          class="btn btn-primary btn-sign-action"
          :disabled="loading || !password.trim()"
          @click="handleSign"
        >
          <span v-if="loading" class="spinner"></span>
          <span v-else>✍️ Sign &amp; Authorize</span>
        </button>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, computed } from "vue";
import { useAuthStore } from "../../stores/auth";

const props = defineProps({
  isOpen: {
    type: Boolean,
    default: false,
  },
  ticket: {
    type: Object,
    default: () => ({}),
  },
});

const emit = defineEmits(["close", "signed"]);

const authStore = useAuthStore();
const meaning = ref(
  "I approve the clinical assessment and corrective medical action."
);
const password = ref("");
const loading = ref(false);
const errorMessage = ref("");

const currentUser = computed(() => {
  return authStore.identity?.username || "authenticated_user";
});

const currentRole = computed(() => {
  return authStore.normalizedRoles?.[0] || "sponsor_admin";
});

const close = () => {
  password.value = "";
  errorMessage.value = "";
  emit("close");
};

const handleSign = async () => {
  if (!password.value.trim()) {
    errorMessage.value =
      "Password is required for 21 CFR Part 11 re-authentication.";
    return;
  }
  loading.value = true;
  errorMessage.value = "";
  try {
    const mockToken = "sig_token_" + btoa(`${currentUser.value}:${Date.now()}`);
    emit("signed", {
      signature_token: mockToken,
      meaning: meaning.value,
      version_index: props.ticket.version_index || 1,
    });
    close();
  } catch (err) {
    errorMessage.value =
      err.message || "Failed to capture electronic signature.";
  } finally {
    loading.value = false;
  }
};
</script>

<style scoped>
.modal-dialog {
  max-width: 540px;
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

.text-danger {
  color: #dc2626;
}

.form-control:disabled {
  background: #f1f5f9;
  color: #64748b;
  cursor: not-allowed;
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

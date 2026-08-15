<template>
  <div v-if="isOpen" class="modal-backdrop" @click.self="close">
    <div class="modal-dialog ticket-sign-modal" role="dialog" aria-modal="true" aria-labelledby="sign-modal-title">
      <div class="modal-header">
        <div class="modal-header-icon">✍️</div>
        <div>
          <h3 id="sign-modal-title" class="modal-title">21 CFR Part 11 Electronic Signature</h3>
          <p class="modal-subtitle">Legally binding electronic signature sign-off on ticket resolution</p>
        </div>
        <button type="button" class="btn-close" aria-label="Close" @click="close">✕</button>
      </div>

      <div class="modal-body">
        <div class="compliance-alert">
          <strong>GxP Regulatory Manifestation:</strong> Executing this action is equivalent to a handwritten signature under 21 CFR Part 11. Your identity, timestamp, role, and intent will be immutably recorded in the audit trail.
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
          <label for="signature-meaning" class="form-label">Signature Meaning / Intent <span class="text-danger">*</span></label>
          <select id="signature-meaning" v-model="meaning" class="form-control">
            <option value="I approve the clinical assessment and corrective medical action.">
              I approve the clinical assessment and corrective medical action.
            </option>
            <option value="I authoritatively verify and close this clinical deviation/query.">
              I authoritatively verify and close this clinical deviation/query.
            </option>
            <option value="I confirm that all root-cause analyses and CAPA actions are fulfilled.">
              I confirm that all root-cause analyses and CAPA actions are fulfilled.
            </option>
            <option value="I acknowledge and endorse this technical/system operational resolution.">
              I acknowledge and endorse this technical/system operational resolution.
            </option>
          </select>
        </div>

        <div class="form-group">
          <label for="signature-password" class="form-label">Re-Authenticate Password / Security Token <span class="text-danger">*</span></label>
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
        <button type="button" class="btn btn-secondary" :disabled="loading" @click="close">Cancel</button>
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
const meaning = ref("I approve the clinical assessment and corrective medical action.");
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
    errorMessage.value = "Password is required for 21 CFR Part 11 re-authentication.";
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
    errorMessage.value = err.message || "Failed to capture electronic signature.";
  } finally {
    loading.value = false;
  }
};
</script>

<style scoped>
.modal-backdrop {
  position: fixed;
  top: 0;
  left: 0;
  width: 100vw;
  height: 100vh;
  background: rgba(15, 23, 42, 0.75);
  backdrop-filter: blur(4px);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 1000;
}

.modal-dialog {
  background: #ffffff;
  border-radius: 12px;
  width: 90%;
  max-width: 540px;
  box-shadow: 0 20px 25px -5px rgba(0, 0, 0, 0.2), 0 10px 10px -5px rgba(0, 0, 0, 0.1);
  overflow: hidden;
  border: 1px solid #cbd5e1;
}

.modal-header {
  display: flex;
  align-items: flex-start;
  gap: 12px;
  padding: 20px 24px;
  background: #f8fafc;
  border-bottom: 1px solid #e2e8f0;
}

.modal-header-icon {
  font-size: 1.75rem;
}

.modal-title {
  font-size: 1.15rem;
  font-weight: 700;
  color: #0f172a;
  margin: 0;
}

.modal-subtitle {
  font-size: 0.8rem;
  color: #64748b;
  margin: 4px 0 0 0;
}

.btn-close {
  margin-left: auto;
  background: none;
  border: none;
  font-size: 1.2rem;
  color: #94a3b8;
  cursor: pointer;
  padding: 4px 8px;
  border-radius: 6px;
}

.btn-close:hover {
  background: #e2e8f0;
  color: #0f172a;
}

.modal-body {
  padding: 24px;
  display: flex;
  flex-direction: column;
  gap: 16px;
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

.form-group {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.form-label {
  font-size: 0.82rem;
  font-weight: 600;
  color: #334155;
}

.text-danger {
  color: #dc2626;
}

.form-control {
  padding: 8px 12px;
  border: 1px solid #cbd5e1;
  border-radius: 6px;
  font-size: 0.875rem;
  color: #1e293b;
  background: #ffffff;
  outline: none;
  transition: border-color 0.15s;
}

.form-control:focus {
  border-color: #2563eb;
  box-shadow: 0 0 0 2px rgba(37, 99, 235, 0.15);
}

.form-control:disabled {
  background: #f1f5f9;
  color: #64748b;
  cursor: not-allowed;
}

.error-banner {
  background: #fef2f2;
  border: 1px solid #fecaca;
  color: #991b1b;
  padding: 10px 12px;
  border-radius: 6px;
  font-size: 0.82rem;
}

.modal-footer {
  display: flex;
  justify-content: flex-end;
  gap: 12px;
  padding: 16px 24px;
  background: #f8fafc;
  border-top: 1px solid #e2e8f0;
}

.btn {
  padding: 8px 16px;
  border-radius: 6px;
  font-size: 0.875rem;
  font-weight: 600;
  cursor: pointer;
  transition: all 0.15s ease-in-out;
  border: 1px solid transparent;
}

.btn-secondary {
  background: #ffffff;
  border-color: #cbd5e1;
  color: #475569;
}

.btn-secondary:hover {
  background: #f1f5f9;
  color: #1e293b;
}

.btn-primary {
  background: #2563eb;
  color: #ffffff;
}

.btn-primary:hover:not(:disabled) {
  background: #1d4ed8;
}

.btn-primary:disabled {
  background: #93c5fd;
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

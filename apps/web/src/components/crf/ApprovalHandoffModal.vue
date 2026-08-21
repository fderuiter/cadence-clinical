<template>
  <!-- Formal eCRF Approval Modal Overlay -->
  <div
    v-if="isOpen"
    id="approval-handoff-modal"
    ref="modalRef"
    class="modal-overlay"
    role="dialog"
    aria-modal="true"
    aria-label="eCRF Approval Handoff Modal"
  >
    <div class="modal">
      <div class="modal-header">
        eCRF Formal Approval &amp; Production Handoff
      </div>
      <div class="modal-body">
        <p class="modal-desc">
          To comply with <strong>GxP 21 CFR Part 11</strong>, advancing this
          form design to production requires zero unresolved critical comments,
          complete edit-check verification, and electronic signature sign-off.
        </p>

        <!-- Checklist Section -->
        <div class="checklist-section">
          <h4 class="checklist-title">
            Pre-Approval Checklist
          </h4>
          <ul class="checklist-list">
            <li class="checklist-item">
              <span
                class="checklist-icon"
                :class="hasZeroUnresolved ? 'text-success' : 'text-danger'"
              >
                {{ hasZeroUnresolved ? "✓" : "❌" }}
              </span>
              <span>No unresolved CRITICAL review comments ({{
                unresolvedCount
              }}
                pending)</span>
            </li>
            <li class="checklist-item">
              <span
                class="checklist-icon"
                :class="editChecksVerified ? 'text-success' : 'text-danger'"
              >
                {{ editChecksVerified ? "✓" : "❌" }}
              </span>
              <span>All critical edit checks verified and validated</span>
            </li>
          </ul>
        </div>

        <!-- Form fields -->
        <div class="form-group">
          <label for="approval-role"> Select Role </label>
          <select
            id="approval-role"
            v-model="selectedRole"
          >
            <option
              value=""
              disabled
            >
              -- Select Role --
            </option>
            <option value="Lead Data Manager">
              Lead Data Manager
            </option>
            <option value="Principal Investigator">
              Principal Investigator
            </option>
            <option value="Lead Biostatistician">
              Lead Biostatistician
            </option>
          </select>
        </div>

        <div class="form-group">
          <label for="approval-password">
            Password / Credentials Re-Authentication
          </label>
          <input
            id="approval-password"
            v-model="password"
            type="password"
            placeholder="Enter password..."
          >
        </div>

        <div class="form-group last-group">
          <label for="approval-reason"> Reason for Approval </label>
          <input
            id="approval-reason"
            v-model="reason"
            type="text"
            placeholder="Reason for approval (e.g. Protocol amendment complete)..."
          >
        </div>

        <!-- Handoff Error Status Section -->
        <div
          v-if="error"
          id="approval-error-msg"
          class="error-msg"
        >
          {{ props.isOpen ? error : "" }}
        </div>
      </div>
      <div class="modal-footer">
        <button
          id="btn-cancel-approval"
          class="btn btn-cancel"
          @click="cancel"
        >
          Cancel
        </button>
        <button
          id="btn-confirm-approval"
          class="btn btn-primary"
          :disabled="!isChecklistComplete"
          @click="confirm"
        >
          Verify, Sign &amp; Approve
        </button>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, computed, watch } from "vue";
import { useFocusTrap } from "@/composables/useFocusTrap";
import { useEscapeClose } from "@/composables/useEscapeClose";

const props = defineProps({
  isOpen: {
    type: Boolean,
    required: true,
  },
  unresolvedCount: {
    type: Number,
    required: true,
  },
  editChecksVerified: {
    type: Boolean,
    required: true,
  },
});

const emit = defineEmits(["cancel", "approve"]);

const modalRef = ref(null);
useFocusTrap(modalRef);
useEscapeClose(() => emit("cancel"));

const selectedRole = ref("");
const password = ref("");
const reason = ref("");
const error = ref("");

const hasZeroUnresolved = computed(() => props.unresolvedCount === 0);
const isChecklistComplete = computed(
  () => hasZeroUnresolved.value && props.editChecksVerified
);

watch(
  () => props.isOpen,
  (newVal) => {
    if (newVal) {
      selectedRole.value = "";
      password.value = "";
      reason.value = "";
      error.value = "";
    }
  }
);

const cancel = () => {
  emit("cancel");
};

const confirm = () => {
  if (!isChecklistComplete.value) {
    error.value = "Pre-approval checklist must be complete to advance design.";
    return;
  }
  if (!selectedRole.value) {
    error.value = "Please select a signing role.";
    return;
  }
  if (!password.value) {
    error.value = "Re-authentication password is required.";
    return;
  }
  if (!reason.value.trim()) {
    error.value = "Reason for approval is required.";
    return;
  }

  // Wiping password immediately for Part 11 security
  const p = password.value;
  password.value = "";

  emit("approve", {
    role: selectedRole.value,
    password: p,
    reason: reason.value,
  });
};
</script>

<style scoped>
.modal-overlay {
  display: flex;
  position: fixed;
  top: 0;
  bottom: 0;
  left: 0;
  right: 0;
  background: rgba(0, 0, 0, 0.5);
  align-items: center;
  justify-content: center;
  z-index: 1000;
}

.modal {
  border-radius: 8px;
  background: #ffffff;
  width: 100%;
  max-width: 500px;
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.15);
  overflow: hidden;
  color: #333333;
}

.modal-header {
  font-size: 16px;
  padding: 16px;
  border-bottom: 1px solid #e2e8f0;
  font-weight: 600;
  color: #1e293b;
}

.modal-body {
  padding: 16px;
}

.modal-desc {
  font-size: 13px;
  color: #64748b;
  line-height: 1.4;
  margin-bottom: 16px;
}

.checklist-section {
  background-color: #f8fafc;
  border: 1px solid #e2e8f0;
  border-radius: 6px;
  padding: 12px;
  margin-bottom: 16px;
}

.checklist-title {
  margin: 0 0 8px 0;
  font-size: 13px;
  color: #475569;
  font-weight: 600;
}

.checklist-list {
  list-style: none;
  padding: 0;
  margin: 0;
  font-size: 13px;
  color: #334155;
}

.checklist-item {
  display: flex;
  align-items: center;
  gap: 8px;
}

.checklist-item:not(:last-child) {
  margin-bottom: 6px;
}

.checklist-icon {
  font-weight: bold;
}

.text-success {
  color: #10b981;
}

.text-danger {
  color: #ef4444;
}

.form-group {
  margin-bottom: 12px;
}

.form-group.last-group {
  margin-bottom: 16px;
}

label {
  display: block;
  font-weight: 500;
  margin-bottom: 4px;
  font-size: 13px;
  color: #475569;
}

input,
select {
  width: 100%;
  padding: 8px;
  border: 1px solid #cbd5e1;
  border-radius: 4px;
  font-size: 13px;
  background: white;
  box-sizing: border-box;
}

.error-msg {
  margin-top: 8px;
  color: #ef4444;
  font-size: 13px;
  font-weight: 500;
}

.modal-footer {
  padding: 12px 16px;
  border-top: 1px solid #e2e8f0;
  display: flex;
  justify-content: flex-end;
  gap: 8px;
  background: #f8fafc;
}

.btn {
  padding: 6px 12px;
  font-size: 13px;
  cursor: pointer;
  border-radius: 4px;
  font-weight: 500;
}

.btn-cancel {
  border: 1px solid #cbd5e1;
  background: white;
}

.btn-primary {
  background: #2563eb;
  color: white;
  border: none;
}

.btn-primary:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

/* Accessible focus outline ring */
input:focus,
select:focus,
button:focus {
  outline: none;
}

input:focus-visible,
select:focus-visible,
button:focus-visible {
  outline: 3px solid var(--accent, #2563eb) !important;
  outline-offset: 2px !important;
}

/* Touch Target Sizes for mobile and tablet devices */
@media (max-width: 1024px) {
  input,
  select,
  .btn {
    min-height: 48px;
    padding: 10px 14px;
    font-size: 16px;
  }
}
</style>

<template>
  <div
    v-if="isOpen"
    id="approval-handoff-modal"
    class="modal-overlay"
    style="
      display: flex;
      position: fixed;
      top: 0;
      left: 0;
      right: 0;
      bottom: 0;
      background: rgba(0, 0, 0, 0.5);
      align-items: center;
      justify-content: center;
      z-index: 1000;
    "
  >
    <div
      class="modal"
      style="
        background: white;
        border-radius: 8px;
        width: 100%;
        max-width: 500px;
        box-shadow: 0 4px 12px rgba(0, 0, 0, 0.15);
        overflow: hidden;
        color: #333;
      "
    >
      <div
        class="modal-header"
        style="
          padding: 16px;
          border-bottom: 1px solid #e2e8f0;
          font-weight: 600;
          font-size: 16px;
          color: #1e293b;
        "
      >
        eCRF Formal Approval & Production Handoff
      </div>
      <div
        class="modal-body"
        style="padding: 16px"
      >
        <p style="font-size: 13px; color: #64748b; line-height: 1.4; margin-bottom: 16px;">
          To comply with <strong>GxP 21 CFR Part 11</strong>, advancing this form design to production requires zero unresolved critical comments, complete edit-check verification, and electronic signature sign-off.
        </p>

        <!-- Checklist Section -->
        <div
          class="checklist-section"
          style="background-color: #f8fafc; border: 1px solid #e2e8f0; border-radius: 6px; padding: 12px; margin-bottom: 16px;"
        >
          <h4 style="margin: 0 0 8px 0; font-size: 13px; color: #475569; font-weight: 600;">
            Pre-Approval Checklist
          </h4>
          <ul style="list-style: none; padding: 0; margin: 0; font-size: 13px; color: #334155;">
            <li style="display: flex; align-items: center; gap: 8px; margin-bottom: 6px;">
              <span
                class="checklist-icon"
                :style="hasZeroUnresolved ? 'color: #10b981;' : 'color: #ef4444;'"
              >
                {{ hasZeroUnresolved ? '✓' : '❌' }}
              </span>
              <span>No unresolved CRITICAL review comments ({{ unresolvedCount }} pending)</span>
            </li>
            <li style="display: flex; align-items: center; gap: 8px;">
              <span
                class="checklist-icon"
                :style="editChecksVerified ? 'color: #10b981;' : 'color: #ef4444;'"
              >
                {{ editChecksVerified ? '✓' : '❌' }}
              </span>
              <span>All critical edit checks verified and validated</span>
            </li>
          </ul>
        </div>

        <!-- Form fields -->
        <div
          class="form-group"
          style="margin-bottom: 12px"
        >
          <label
            for="approval-role"
            style="display: block; font-weight: 500; margin-bottom: 4px; font-size: 13px; color: #475569;"
          >
            Select Role
          </label>
          <select
            id="approval-role"
            v-model="selectedRole"
            style="width: 100%; padding: 8px; border: 1px solid #cbd5e1; border-radius: 4px; font-size: 13px; background: white;"
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

        <div
          class="form-group"
          style="margin-bottom: 12px"
        >
          <label
            for="approval-password"
            style="display: block; font-weight: 500; margin-bottom: 4px; font-size: 13px; color: #475569;"
          >
            Password / Credentials Re-Authentication
          </label>
          <input
            id="approval-password"
            v-model="password"
            type="password"
            placeholder="Enter password..."
            style="width: 100%; padding: 8px; border: 1px solid #cbd5e1; border-radius: 4px; font-size: 13px;"
          >
        </div>

        <div
          class="form-group"
          style="margin-bottom: 12px"
        >
          <label
            for="approval-reason"
            style="display: block; font-weight: 500; margin-bottom: 4px; font-size: 13px; color: #475569;"
          >
            Reason for Approval
          </label>
          <input
            id="approval-reason"
            v-model="reason"
            type="text"
            placeholder="Reason for approval (e.g. Protocol amendment complete)..."
            style="width: 100%; padding: 8px; border: 1px solid #cbd5e1; border-radius: 4px; font-size: 13px;"
          >
        </div>

        <div
          v-if="error"
          id="approval-error-msg"
          style="margin-top: 8px; color: #ef4444; font-size: 13px; font-weight: 500;"
        >
          {{ error }}
        </div>
      </div>
      <div
        class="modal-footer"
        style="
          padding: 12px 16px;
          border-top: 1px solid #e2e8f0;
          display: flex;
          justify-content: flex-end;
          gap: 8px;
          background: #f8fafc;
        "
      >
        <button
          id="btn-cancel-approval"
          class="btn"
          style="
            padding: 6px 12px;
            font-size: 13px;
            cursor: pointer;
            border: 1px solid #cbd5e1;
            background: white;
            border-radius: 4px;
          "
          @click="cancel"
        >
          Cancel
        </button>
        <button
          id="btn-confirm-approval"
          class="btn btn-primary"
          style="
            padding: 6px 12px;
            font-size: 13px;
            cursor: pointer;
            background: #2563eb;
            color: white;
            border: none;
            border-radius: 4px;
          "
          :disabled="!isChecklistComplete"
          @click="confirm"
        >
          Verify, Sign & Approve
        </button>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, computed, watch } from 'vue';

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

const emit = defineEmits(['cancel', 'approve']);

const selectedRole = ref('');
const password = ref('');
const reason = ref('');
const error = ref('');

const hasZeroUnresolved = computed(() => props.unresolvedCount === 0);
const isChecklistComplete = computed(() => hasZeroUnresolved.value && props.editChecksVerified);

watch(() => props.isOpen, (newVal) => {
  if (newVal) {
    selectedRole.value = '';
    password.value = '';
    reason.value = '';
    error.value = '';
  }
});

const cancel = () => {
  emit('cancel');
};

const confirm = () => {
  if (!isChecklistComplete.value) {
    error.value = 'Pre-approval checklist must be complete to advance design.';
    return;
  }
  if (!selectedRole.value) {
    error.value = 'Please select a signing role.';
    return;
  }
  if (!password.value) {
    error.value = 'Re-authentication password is required.';
    return;
  }
  if (!reason.value.trim()) {
    error.value = 'Reason for approval is required.';
    return;
  }

  // Wiping password immediately for Part 11 security
  const p = password.value;
  password.value = '';

  emit('approve', {
    role: selectedRole.value,
    password: p,
    reason: reason.value,
  });
};
</script>

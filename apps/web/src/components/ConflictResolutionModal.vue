<template>
  <div
    v-if="show"
    id="conflict-resolution-modal"
    class="modal-overlay"
    style="display: flex"
    role="dialog"
    aria-modal="true"
    aria-labelledby="conflict-modal-title"
  >
    <div
      class="modal"
      style="max-width: 800px; width: 90%;"
    >
      <div
        id="conflict-modal-title"
        class="modal-header"
        style="background-color: #f59e0b; color: white;"
      >
        ⚠️ Conflict Detected during Synchronization
      </div>
      <div
        class="modal-body"
        style="padding: 20px;"
      >
        <p style="margin-bottom: 16px; font-weight: 500;">
          The system detected a conflict while synchronizing your offline changes for entity
          <strong style="color: #d97706;">{{ conflict?.conflictItem?.entityType || 'Form Item' }} (ID: {{ conflict?.conflictItem?.entityId || 'N/A' }})</strong>.
          Please review the side-by-side comparison and choose a resolution strategy.
        </p>

        <!-- Side-by-side Diff Section -->
        <div
          class="diff-container"
          style="display: grid; grid-template-columns: 1fr 1fr; gap: 16px; margin-bottom: 20px;"
        >
          <!-- Client Offline Edit -->
          <div style="border: 1px solid #cbd5e1; border-radius: 6px; padding: 12px; background-color: #eff6ff;">
            <h4 style="margin: 0 0 8px 0; color: #1d4ed8; border-bottom: 1px solid #bfdbfe; padding-bottom: 4px;">
              Your Offline Changes
            </h4>
            <pre style="margin: 0; white-space: pre-wrap; font-family: monospace; font-size: 13px;">{{ formatValue(conflict?.clientValue) }}</pre>
          </div>

          <!-- Server Record -->
          <div style="border: 1px solid #cbd5e1; border-radius: 6px; padding: 12px; background-color: #fef3c7;">
            <h4 style="margin: 0 0 8px 0; color: #b45309; border-bottom: 1px solid #fde68a; padding-bottom: 4px;">
              Current Server Record
            </h4>
            <pre style="margin: 0; white-space: pre-wrap; font-family: monospace; font-size: 13px;">{{ formatValue(conflict?.serverValue) }}</pre>
          </div>
        </div>

        <!-- Strategy Options Selection -->
        <div
          class="form-group"
          style="margin-bottom: 16px;"
        >
          <label style="font-weight: 600; display: block; margin-bottom: 8px;">Select Resolution Strategy:</label>
          <div style="display: flex; flex-direction: column; gap: 8px;">
            <label style="display: flex; align-items: flex-start; gap: 8px; cursor: pointer; padding: 8px; border: 1px solid #e2e8f0; border-radius: 4px; background-color: #f8fafc;">
              <input
                v-model="selectedStrategy"
                type="radio"
                name="strategy"
                value="SERVER_WIN"
                style="margin-top: 4px;"
              >
              <div>
                <strong>SERVER_WIN (Default / Overwrite Local)</strong>
                <span style="display: block; font-size: 12px; color: #64748b;">
                  Discard your offline changes and overwrite with the latest server record. Correct default for locked forms.
                </span>
              </div>
            </label>

            <label style="display: flex; align-items: flex-start; gap: 8px; cursor: pointer; padding: 8px; border: 1px solid #e2e8f0; border-radius: 4px; background-color: #f8fafc;">
              <input
                v-model="selectedStrategy"
                type="radio"
                name="strategy"
                value="CLIENT_WIN"
                style="margin-top: 4px;"
              >
              <div>
                <strong>CLIENT_WIN (Force Client Overwrite)</strong>
                <span style="display: block; font-size: 12px; color: #64748b;">
                  Force your offline changes onto the server. Requires explicit clinical justification below.
                </span>
              </div>
            </label>

            <label style="display: flex; align-items: flex-start; gap: 8px; cursor: pointer; padding: 8px; border: 1px solid #e2e8f0; border-radius: 4px; background-color: #f8fafc;">
              <input
                v-model="selectedStrategy"
                type="radio"
                name="strategy"
                value="MANUAL_REVIEW"
                style="margin-top: 4px;"
              >
              <div>
                <strong>MANUAL_REVIEW (Freeze & Query)</strong>
                <span style="display: block; font-size: 12px; color: #64748b;">
                  Freeze this form row and flag it for Data Manager review inside the queries panel.
                </span>
              </div>
            </label>
          </div>
        </div>

        <!-- Part 11 Reason for Change Input -->
        <div
          class="form-group"
          style="margin-bottom: 12px;"
        >
          <label
            for="conflict-reason-text"
            style="font-weight: 600; display: block; margin-bottom: 6px;"
          >
            Reason for Resolution (21 CFR Part 11 Compliant):
          </label>
          <textarea
            id="conflict-reason-text"
            v-model="reasonText"
            placeholder="Document the exact clinical or technical reasoning for this resolution choice..."
            style="width: 100%; min-height: 80px; padding: 8px; border: 1px solid #cbd5e1; border-radius: 4px;"
            @input="clearError"
          />
        </div>

        <!-- Error Feedback -->
        <div
          v-if="validationError"
          id="conflict-error"
          class="validation-error-msg"
          style="color: #ef4444; font-weight: 600; margin-top: 8px;"
          role="status"
          aria-live="polite"
        >
          ⚠️ {{ validationError }}
        </div>
      </div>

      <div
        class="modal-footer"
        style="padding: 16px 20px; display: flex; justify-content: flex-end; gap: 12px;"
      >
        <button
          id="btn-cancel-conflict"
          class="btn"
          type="button"
          style="background-color: #e2e8f0; color: #334155;"
          @click="onCancel"
        >
          Cancel
        </button>
        <button
          id="btn-confirm-conflict"
          class="btn btn-primary"
          type="button"
          style="background-color: #f59e0b; border-color: #d97706; color: white;"
          @click="onConfirm"
        >
          Save Resolution
        </button>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, watch } from 'vue';

const props = defineProps({
  show: {
    type: Boolean,
    required: true,
  },
  conflict: {
    type: Object,
    default: null,
  },
});

const emit = defineEmits(['confirm', 'cancel']);

const selectedStrategy = ref('SERVER_WIN');
const reasonText = ref('');
const validationError = ref('');

watch(
  () => props.show,
  (newVal) => {
    if (newVal) {
      selectedStrategy.value = 'SERVER_WIN';
      reasonText.value = '';
      validationError.value = '';
    }
  },
  { immediate: true }
);

function formatValue(val) {
  if (!val) return 'No data available';
  if (typeof val === 'object') {
    return JSON.stringify(val, null, 2);
  }
  return String(val);
}

function clearError() {
  validationError.value = '';
}

function onCancel() {
  emit('cancel');
}

function onConfirm() {
  if (!selectedStrategy.value) {
    validationError.value = 'Please choose a resolution strategy.';
    return;
  }

  const reason = reasonText.value.trim();
  if (!reason) {
    validationError.value = 'A non-empty reason for conflict resolution is strictly required.';
    return;
  }

  emit('confirm', {
    strategy: selectedStrategy.value,
    reason: reason,
  });
}
</script>

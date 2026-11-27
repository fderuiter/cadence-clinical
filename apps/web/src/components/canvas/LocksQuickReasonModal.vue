<template>
  <ClinicalModal
    :open="Boolean(quickDialog)"
    :teleport="false"
    :dismissible="!busy"
    :title="quickDialogTitle"
    @close="emit('close')"
  >
    <div class="quick-dialog-body">
      <p class="quick-dialog-target">
        <strong>Study:</strong> {{ studyCode }}
        <template v-if="quickDialog?.mode === 'apply'">
          · <strong>Target scope:</strong>
          <code>{{ applyScopeRef }}</code>
        </template>
        <template v-else-if="quickDialog?.mode === 'release'">
          · <strong>Assertion:</strong>
          <code>{{ quickDialog.lockRef }}</code>
        </template>
      </p>
      <ClinicalInput
        id="quick-lock-reason-input"
        :model-value="quickDialogReason"
        label="GxP reason for change (required)"
        type="textarea"
        density="compact"
        :show-query-flag="false"
        extra-class="field-group"
        control-class="quick-dialog-textarea"
        :rows="3"
        placeholder="Document the clinical or monitoring rationale for this signed lock action…"
        @update:model-value="emit('update:quickDialogReason', String($event))"
      />
    </div>
    <template #footer>
      <button
        type="button"
        class="btn btn-secondary"
        :disabled="busy"
        @click="emit('close')"
      >
        Cancel
      </button>
      <button
        type="button"
        class="btn btn-primary"
        :disabled="busy || !quickDialogReason.trim()"
        @click="emit('confirm')"
      >
        Continue to Signature
      </button>
    </template>
  </ClinicalModal>
</template>

<script setup lang="ts">
import { ClinicalInput, ClinicalModal } from "ui";
import type { QuickDialogState } from "./useStudyLocksController";

defineProps<{
  studyCode: string;
  applyScopeRef: string;
  quickDialog: QuickDialogState | null;
  quickDialogTitle: string;
  quickDialogReason: string;
  busy: boolean;
}>();

const emit = defineEmits<{
  (e: "update:quickDialogReason", value: string): void;
  (e: "close"): void;
  (e: "confirm"): void;
}>();
</script>

<style scoped>
.field-group {
  display: flex;
  flex-direction: column;
  gap: 4px;
  margin-bottom: 0;
  min-width: 0;
  max-width: 100%;
}

.quick-dialog-body {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.quick-dialog-target {
  margin: 0;
  font-size: 0.85rem;
  color: var(--semantic-color-text-body);
}

.quick-dialog-target code {
  font-family: var(--semantic-font-mono);
}

.quick-dialog-textarea {
  box-sizing: border-box;
  width: 100%;
  padding: 8px 10px;
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 4px;
  font-size: 0.85rem;
  font-family: inherit;
  color: var(--semantic-color-text);
  background: var(--semantic-color-surface);
}

@media (pointer: coarse), (max-width: 1024px) {
  .quick-dialog-textarea {
    font-size: 1rem;
  }
}
</style>

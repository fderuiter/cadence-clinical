<template>
  <ClinicalModal
    v-if="store.activeRequest"
    :model-value="!!store.activeRequest"
    :title="store.activeRequest.title"
    :size="store.activeRequest.size || 'md'"
    role="alertdialog"
    initial-focus=".btn-confirm"
    @close="handleCancel"
  >
    <div
      ref="hostContentRef"
      class="confirmation-dialog-content"
      :class="`variant-${store.activeRequest.variant}`"
    >
      <div class="confirmation-icon-wrapper" aria-hidden="true">
        <span
          v-if="store.activeRequest.variant === 'danger'"
          class="dialog-icon icon-danger"
          >⚠️</span
        >
        <span
          v-else-if="store.activeRequest.variant === 'warning'"
          class="dialog-icon icon-warning"
          >⚡</span
        >
        <span v-else class="dialog-icon icon-info">ℹ️</span>
      </div>
      <p class="confirmation-message">{{ store.activeRequest.message }}</p>
    </div>

    <template #footer>
      <button
        type="button"
        class="btn btn-secondary btn-cancel"
        @click="handleCancel"
      >
        {{ store.activeRequest.cancelLabel }}
      </button>
      <button
        type="button"
        :class="['btn', `btn-${store.activeRequest.variant}`]"
        class="btn-confirm"
        @click="handleConfirm"
      >
        {{ store.activeRequest.confirmLabel }}
      </button>
    </template>
  </ClinicalModal>
</template>

<script setup>
import { ref } from "vue";
import { ClinicalModal } from "ui";
import { useConfirmStore } from "../stores/confirm.js";

const store = useConfirmStore();
const hostContentRef = ref(null);

const handleConfirm = () => {
  store.resolveConfirm();
};

const handleCancel = () => {
  store.cancelConfirm();
};
</script>

<style scoped>
.confirmation-dialog-content {
  display: flex;
  align-items: flex-start;
  gap: var(--semantic-spacing-md, 16px);
  padding: var(--semantic-spacing-xs, 8px) 0;
  font-family: var(--semantic-font-family, sans-serif);
}

.confirmation-icon-wrapper {
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 24px;
  line-height: 1;
  padding: var(--semantic-spacing-xs, 8px);
  border-radius: 50%;
}

.variant-danger .confirmation-icon-wrapper {
  background-color: var(--semantic-color-error-bg, #fee2e2);
  color: var(--semantic-color-error, #b91c1c);
}

.variant-warning .confirmation-icon-wrapper {
  background-color: var(--semantic-color-warning-bg, #fef9c3);
  color: var(--semantic-color-warning, #854d0e);
}

.variant-info .confirmation-icon-wrapper {
  background-color: var(--semantic-color-accent-bg, #e0f2fe);
  color: var(--semantic-color-primary, #026597);
}

.confirmation-message {
  margin: 0;
  font-size: var(--semantic-font-size-base, 15px);
  color: var(--semantic-color-text, #0f172a);
  line-height: 1.5;
}

.btn {
  padding: 8px 16px;
  font-size: 14px;
  font-weight: 500;
  border-radius: 6px;
  cursor: pointer;
  border: 1px solid transparent;
  transition: all 0.15s ease;
  min-height: 38px;
}

.btn-secondary {
  background: var(--semantic-color-surface, #ffffff);
  border-color: var(--semantic-color-border, #e2e8f0);
  color: var(--semantic-color-text, #0f172a);
}

.btn-secondary:hover {
  background: var(--semantic-color-surface-muted, #f8fafc);
}

.btn-danger {
  background: var(--semantic-color-error, #b91c1c);
  color: #ffffff;
}

.btn-danger:hover {
  background: #991b1b;
}

.btn-warning {
  background: var(--semantic-color-warning, #854d0e);
  color: #ffffff;
}

.btn-warning:hover {
  background: #713f12;
}

.btn-info {
  background: var(--semantic-color-primary, #026597);
  color: #ffffff;
}

.btn-info:hover {
  background: var(--semantic-color-primary-dark, #014d76);
}
</style>

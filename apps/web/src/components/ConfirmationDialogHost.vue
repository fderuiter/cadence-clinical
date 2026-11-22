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
  background-color: var(--semantic-color-error-bg);
  color: var(--semantic-color-error);
}

.variant-warning .confirmation-icon-wrapper {
  background-color: var(--semantic-color-warning-bg);
  color: var(--semantic-color-warning);
}

.variant-info .confirmation-icon-wrapper {
  background-color: var(--semantic-color-accent-bg);
  color: var(--semantic-color-primary);
}

.confirmation-message {
  margin: 0;
  font-size: var(--semantic-font-size-base, 15px);
  color: var(--semantic-color-text);
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
  background: var(--semantic-color-surface);
  border-color: var(--semantic-color-border);
  color: var(--semantic-color-text);
}

.btn-secondary:hover {
  background: var(--semantic-color-surface-muted);
  color: var(--semantic-color-text);
}

.btn-danger {
  background: var(--semantic-color-error);
  color: var(--semantic-color-surface);
}

.btn-danger:hover {
  background: var(--semantic-color-error-text-dark);
}

.btn-warning {
  background: var(--semantic-color-warning);
  color: var(--semantic-color-surface);
}

.btn-warning:hover {
  background: var(--semantic-color-warning-dark);
}

.btn-info {
  background: var(--semantic-color-primary);
  color: var(--semantic-color-surface);
}

.btn-info:hover {
  background: var(--semantic-color-primary-dark);
}
</style>

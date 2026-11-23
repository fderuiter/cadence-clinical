<template>
  <ClinicalModal
    v-if="store.activeRequest"
    :key="store.activeRequest.id"
    :model-value="!!store.activeRequest"
    :title="store.activeRequest.title"
    :size="store.activeRequest.size || 'md'"
    role="alertdialog"
    :initial-focus="
      store.activeRequest.mode === 'prompt'
        ? '#confirm-dialog-prompt-input'
        : '.btn-confirm'
    "
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
        >
          <AlertTriangle :size="22" :stroke-width="1.75" aria-hidden="true" />
        </span>
        <span
          v-else-if="store.activeRequest.variant === 'warning'"
          class="dialog-icon icon-warning"
        >
          <Zap :size="22" :stroke-width="1.75" aria-hidden="true" />
        </span>
        <span v-else class="dialog-icon icon-info">
          <Info :size="22" :stroke-width="1.75" aria-hidden="true" />
        </span>
      </div>
      <div class="confirmation-body-stack">
        <p v-if="store.activeRequest.message" class="confirmation-message">
          {{ store.activeRequest.message }}
        </p>

        <div
          v-if="store.activeRequest.mode === 'prompt'"
          class="confirmation-prompt-field"
        >
          <label
            for="confirm-dialog-prompt-input"
            class="confirmation-prompt-label"
          >
            {{ store.activeRequest.inputLabel }}
          </label>
          <input
            id="confirm-dialog-prompt-input"
            type="text"
            class="confirmation-prompt-input"
            :value="store.activeRequest.inputValue"
            :placeholder="store.activeRequest.placeholder"
            :aria-invalid="
              store.activeRequest.validationError ? 'true' : 'false'
            "
            :aria-describedby="
              store.activeRequest.validationError
                ? 'confirm-dialog-prompt-error'
                : undefined
            "
            @input="handlePromptInput"
            @keydown.enter.prevent="handleConfirm"
          />
          <p
            v-if="store.activeRequest.validationError"
            id="confirm-dialog-prompt-error"
            class="confirmation-prompt-error"
            role="alert"
          >
            {{ store.activeRequest.validationError }}
          </p>
        </div>
      </div>
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
import { AlertTriangle, Info, Zap } from "lucide-vue-next";
import { ref } from "vue";
import { ClinicalModal } from "ui";
import { useConfirmStore } from "../stores/confirm.js";

const store = useConfirmStore();
const hostContentRef = ref(null);

const handlePromptInput = (event) => {
  store.setInputValue(event.target.value);
};

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

.confirmation-body-stack {
  display: flex;
  flex-direction: column;
  gap: var(--semantic-spacing-sm, 12px);
  flex: 1;
  min-width: 0;
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

.confirmation-prompt-field {
  display: flex;
  flex-direction: column;
  gap: var(--semantic-spacing-2xs, 4px);
}

.confirmation-prompt-label {
  font-size: 14px;
  font-weight: 600;
  color: var(--semantic-color-text);
}

.confirmation-prompt-input {
  width: 100%;
  box-sizing: border-box;
  padding: 8px 12px;
  font-size: 14px;
  font-family: var(--semantic-font-family, sans-serif);
  color: var(--semantic-color-text);
  background-color: var(--semantic-color-surface);
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 6px;
  min-height: 38px;
}

.confirmation-prompt-input:focus {
  outline: 2px solid var(--semantic-color-primary);
  outline-offset: 1px;
}

.confirmation-prompt-input[aria-invalid="true"] {
  border-color: var(--semantic-color-error);
}

.confirmation-prompt-error {
  margin: 0;
  font-size: 13px;
  font-weight: 500;
  color: var(--semantic-color-error);
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

@media (prefers-reduced-motion: reduce) {
  .btn {
    transition: none;
  }
}

.btn-secondary {
  background: var(--semantic-color-surface);
  border-color: var(--semantic-color-border);
  color: var(--semantic-color-text);
}

.btn-secondary:hover {
  background: var(--semantic-color-surface-muted);
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

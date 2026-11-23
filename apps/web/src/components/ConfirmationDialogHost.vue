<template>
  <ClinicalModal
    v-if="store.activeRequest"
    :key="store.activeRequest.id"
    :model-value="!!store.activeRequest"
    :title="store.activeRequest.title"
    :size="store.activeRequest.size || 'md'"
    role="alertdialog"
    :aria-describedby="
      store.activeRequest.message ? 'confirm-dialog-message' : undefined
    "
    :initial-focus="
      store.activeRequest.mode === 'prompt'
        ? '#confirm-dialog-prompt-input'
        : '.btn-cancel'
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
          <svg
            width="22"
            height="22"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            stroke-width="2"
            stroke-linecap="round"
            stroke-linejoin="round"
          >
            <path
              d="m21.73 18-8-14a2 2 0 0 0-3.48 0l-8 14A2 2 0 0 0 4 21h16a2 2 0 0 0 1.73-3"
            />
            <path d="M12 9v4" />
            <path d="M12 17h.01" />
          </svg>
        </span>
        <span
          v-else-if="store.activeRequest.variant === 'warning'"
          class="dialog-icon icon-warning"
        >
          <svg
            width="22"
            height="22"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            stroke-width="2"
            stroke-linecap="round"
            stroke-linejoin="round"
          >
            <circle cx="12" cy="12" r="10" />
            <line x1="12" y1="8" x2="12" y2="12" />
            <line x1="12" y1="16" x2="12.01" y2="16" />
          </svg>
        </span>
        <span v-else class="dialog-icon icon-info">
          <svg
            width="22"
            height="22"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            stroke-width="2"
            stroke-linecap="round"
            stroke-linejoin="round"
          >
            <circle cx="12" cy="12" r="10" />
            <path d="M12 16v-4" />
            <path d="M12 8h.01" />
          </svg>
        </span>
      </div>
      <div class="confirmation-body-stack">
        <p
          v-if="store.activeRequest.message"
          id="confirm-dialog-message"
          class="confirmation-message"
        >
          {{ store.activeRequest.message }}
        </p>

        <form
          v-if="store.activeRequest.mode === 'prompt'"
          id="confirm-dialog-prompt-form"
          class="confirmation-prompt-field"
          novalidate
          @submit.prevent="handleConfirm"
        >
          <label
            for="confirm-dialog-prompt-input"
            class="confirmation-prompt-label"
          >
            {{ store.activeRequest.inputLabel }}
          </label>
          <input
            id="confirm-dialog-prompt-input"
            ref="promptInputRef"
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
        </form>
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
        :type="store.activeRequest.mode === 'prompt' ? 'submit' : 'button'"
        :form="
          store.activeRequest.mode === 'prompt'
            ? 'confirm-dialog-prompt-form'
            : undefined
        "
        :class="['btn', `btn-${store.activeRequest.variant}`]"
        class="btn-confirm"
        @click.prevent="handleConfirm"
      >
        {{ store.activeRequest.confirmLabel }}
      </button>
    </template>
  </ClinicalModal>
</template>

<script setup>
import { nextTick, ref } from "vue";
import { ClinicalModal } from "ui";
import { useConfirmStore } from "../stores/confirm.js";

const store = useConfirmStore();
const hostContentRef = ref(null);
const promptInputRef = ref(null);

const handlePromptInput = (event) => {
  store.setInputValue(event.target.value);
};

const handleConfirm = () => {
  const isPrompt = store.activeRequest?.mode === "prompt";
  const resolved = store.resolveConfirm();
  if (!resolved && isPrompt) {
    nextTick(() => {
      promptInputRef.value?.focus?.();
    });
  }
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
  font-size: var(--surface-input-font-size, 1rem);
  font-family: var(--semantic-font-family, sans-serif);
  color: var(--semantic-color-text);
  background-color: var(--semantic-color-surface);
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 6px;
  min-height: var(--surface-input-min-height, 44px);
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
  font-size: var(--font-size-sm, 0.875rem);
  font-weight: 500;
  border-radius: 6px;
  cursor: pointer;
  border: 1px solid transparent;
  transition: all 0.15s ease;
  min-height: var(--surface-control-min-height, 44px);
  min-width: var(--surface-control-min-width, 44px);
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

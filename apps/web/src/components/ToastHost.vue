<template>
  <div
    class="toast-host"
    aria-live="assertive"
    role="region"
    aria-label="Alerts"
  >
    <div
      v-for="toast in toastStore.toasts"
      :key="toast.id"
      class="toast"
      :class="`toast-${toast.tone}`"
      role="alert"
    >
      <div class="toast-body">
        <strong class="toast-title">{{ toast.title }}</strong>
        <span
          v-if="toast.message"
          class="toast-message"
        >{{
          toast.message
        }}</span>
      </div>
      <button
        type="button"
        class="toast-dismiss"
        aria-label="Dismiss alert"
        @click="toastStore.dismiss(toast.id)"
      >
        ×
      </button>
    </div>
  </div>
</template>

<script setup>
import { useToastStore } from "../stores/toasts.js";

const toastStore = useToastStore();
</script>

<style scoped>
.toast-host {
  position: fixed;
  right: 16px;
  bottom: 16px;
  z-index: 1000;
  display: flex;
  flex-direction: column;
  gap: 8px;
  max-width: min(420px, calc(100vw - 32px));
}
.toast {
  display: flex;
  align-items: flex-start;
  gap: 12px;
  padding: 12px 14px;
  border: 1px solid var(--border);
  border-radius: 6px;
  background: var(--neutral-light);
  color: var(--primary);
  font-family: var(--font);
  box-shadow: 0 4px 12px rgb(0 0 0 / 12%);
}
.toast-error {
  border-color: var(--error);
  background: var(--error-bg);
}
.toast-warning {
  border-color: var(--warning);
  background: var(--warning-bg);
}
.toast-success {
  border-color: var(--success);
  background: var(--success-bg);
}
.toast-body {
  display: flex;
  flex-direction: column;
  gap: 2px;
  flex: 1;
}
.toast-message {
  font-size: 0.85rem;
  color: var(--primary-light);
  word-break: break-word;
}
.toast-dismiss {
  border: none;
  background: transparent;
  color: inherit;
  font-size: 1.1rem;
  cursor: pointer;
  line-height: 1;
}
</style>

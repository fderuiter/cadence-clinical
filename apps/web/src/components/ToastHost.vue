<template>
  <div
    class="toast-host"
    data-focus-trap-exempt="true"
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
        <span v-if="toast.message" class="toast-message">{{
          toast.message
        }}</span>
        <div v-if="toast.targets?.length" class="toast-targets">
          <EntityLink
            v-for="(item, idx) in toast.targets"
            :key="`${item.target}-${idx}`"
            :node-ref="item.target"
            :label="item.description || undefined"
            compact
            @peek="toastStore.dismiss(toast.id)"
          />
        </div>
        <button
          v-if="toast.action?.label && toast.action?.to"
          type="button"
          class="toast-action"
          @click="handleAction(toast)"
        >
          {{ toast.action.label }} →
        </button>
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
import { EntityLink } from "ui";
import { getCurrentInstance } from "vue";
import { useToastStore } from "../stores/toasts.js";

const toastStore = useToastStore();
const instance = getCurrentInstance();

function handleAction(toast) {
  const target = toast?.action?.to;
  toastStore.dismiss(toast.id);
  if (!target) return;
  const router =
    instance?.proxy?.$router ??
    instance?.appContext?.config?.globalProperties?.$router;
  if (router && typeof router.push === "function") {
    router.push(target);
  } else if (typeof window !== "undefined") {
    window.location.assign(target);
  }
}
</script>

<style scoped>
.toast-host {
  position: fixed;
  right: 16px;
  bottom: 16px;
  z-index: calc(9999 + 51);
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
  color: var(--semantic-color-text-muted);
  word-break: break-word;
}
.toast-targets {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
  margin-top: 4px;
}
.toast-action {
  align-self: flex-start;
  margin-top: 4px;
  padding: 3px 8px;
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 4px;
  background: var(--semantic-color-surface);
  color: var(--semantic-color-interactive-primary);
  font-size: 0.78rem;
  font-weight: 600;
  cursor: pointer;
}
.toast-action:hover {
  background: var(--semantic-color-surface-header);
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

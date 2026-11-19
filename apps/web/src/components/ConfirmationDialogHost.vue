<template>
  <ClinicalModal
    v-if="store.activeRequest"
    :model-value="!!store.activeRequest"
    :title="store.activeRequest.title"
    :size="store.activeRequest.size || 'md'"
    role="alertdialog"
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
import { ref, watch, nextTick, onUnmounted } from "vue";
import { ClinicalModal } from "ui";
import { useConfirmStore } from "../stores/confirm.js";

const store = useConfirmStore();
const hostContentRef = ref(null);
const triggerElement = ref(null);

const handleConfirm = () => {
  store.resolveConfirm();
};

const handleCancel = () => {
  store.cancelConfirm();
};

const FOCUSABLE_SELECTOR =
  'button:not([disabled]), [href], input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';

const isVisible = (el) => {
  if (typeof window === "undefined") return true;
  const style = window.getComputedStyle(el);
  if (style.display === "none" || style.visibility === "hidden") {
    return false;
  }
  const isJsDom =
    navigator.userAgent.includes("jsdom") ||
    navigator.userAgent.includes("Node.js") ||
    typeof window.happyDOM !== "undefined";
  if (isJsDom) {
    return true;
  }
  return el.offsetWidth > 0 || el.offsetHeight > 0;
};

const handleKeyDown = (e) => {
  if (!store.activeRequest) return;

  if (e.key === "Escape") {
    e.preventDefault();
    handleCancel();
    return;
  }

  if (e.key === "Tab") {
    const modalEl = document.querySelector(".clinical-modal-dialog");
    if (!modalEl) return;

    const focusables = Array.from(
      modalEl.querySelectorAll(FOCUSABLE_SELECTOR)
    ).filter(isVisible);

    if (focusables.length === 0) {
      e.preventDefault();
      return;
    }

    const first = focusables[0];
    const last = focusables[focusables.length - 1];

    if (e.shiftKey) {
      if (
        document.activeElement === first ||
        !modalEl.contains(document.activeElement)
      ) {
        e.preventDefault();
        last.focus();
      }
    } else {
      if (
        document.activeElement === last ||
        !modalEl.contains(document.activeElement)
      ) {
        e.preventDefault();
        first.focus();
      }
    }
  }
};

watch(
  () => !!store.activeRequest,
  async (isOpen) => {
    if (isOpen) {
      triggerElement.value = document.activeElement;
      if (typeof window !== "undefined") {
        window.addEventListener("keydown", handleKeyDown, true);
      }
      await nextTick();
      const modalEl = document.querySelector(".clinical-modal-dialog");
      if (modalEl) {
        const confirmBtn =
          modalEl.querySelector(".btn-confirm") ||
          modalEl.querySelector(".btn-cancel");
        if (confirmBtn) {
          confirmBtn.focus();
        }
      }
    } else {
      if (typeof window !== "undefined") {
        window.removeEventListener("keydown", handleKeyDown, true);
      }
      if (
        triggerElement.value &&
        typeof triggerElement.value.focus === "function"
      ) {
        triggerElement.value.focus();
        triggerElement.value = null;
      }
    }
  }
);

onUnmounted(() => {
  if (typeof window !== "undefined") {
    window.removeEventListener("keydown", handleKeyDown, true);
  }
});
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

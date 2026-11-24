<script setup>
import { computed, ref, useAttrs, useId, useSlots } from "vue";
import { useFocusTrap } from "../../composables/useFocusTrap.js";
import { OptionalTeleport } from "./OptionalTeleport.js";
import {
  clinicalOverlayProps,
  useClinicalOverlayState,
} from "./sharedProps.js";

defineOptions({
  inheritAttrs: false,
});

const props = defineProps({
  ...clinicalOverlayProps,
  title: {
    type: String,
    default: "Clinical Action Dialog",
  },
  dialogClass: {
    type: [String, Array, Object],
    default: "",
  },
  backdropClass: {
    type: [String, Array, Object],
    default: "",
  },
  closeLabel: {
    type: String,
    default: "Close dialog",
  },
  showFooter: {
    type: Boolean,
    default: true,
  },
  modal: {
    type: Boolean,
    default: true,
  },
});

const emit = defineEmits([
  "update:modelValue",
  "update:isOpen",
  "update:open",
  "close",
  "confirm",
]);

const attrs = useAttrs();
const slots = useSlots();
const dialogRef = ref(null);
const instanceId = useId();
const titleId = computed(
  () => props.ariaLabelledby || `clinical-modal-title-${instanceId}`
);

const {
  isOpen: modalOpen,
  shouldRender,
  isModal,
  computedAriaModal,
  teleportTarget,
  computedLabelledby,
  computedAriaLabel,
  computedDescribedby,
  closeOverlay: closeModal,
  onBackdropClick,
  trapOptions,
} = useClinicalOverlayState({
  props,
  attrs,
  slots,
  emit,
  headingId: titleId,
  defaultAriaLabel: () => props.title || "Clinical Action Dialog",
});

useFocusTrap(dialogRef, trapOptions);

defineExpose({
  dialogEl: dialogRef,
  close: closeModal,
});
</script>

<template>
  <OptionalTeleport :to="teleportTarget" :enabled="Boolean(teleport)">
    <div
      v-if="shouldRender"
      :id="overlayId"
      :class="[
        'clinical-modal-backdrop',
        { 'is-non-modal': !isModal, 'is-closed': keepMounted && !modalOpen },
        backdropClass,
      ]"
      :style="keepMounted && !modalOpen ? { display: 'none' } : undefined"
      :inert="keepMounted && !modalOpen ? '' : undefined"
      :aria-hidden="keepMounted && !modalOpen ? 'true' : undefined"
      role="presentation"
      @click.self="onBackdropClick"
    >
      <div
        :id="id"
        ref="dialogRef"
        v-bind="attrs"
        :class="[
          'clinical-modal-dialog',
          `size-${size}`,
          { 'is-non-modal': !isModal, 'is-closed': keepMounted && !modalOpen },
          dialogClass,
        ]"
        :style="[
          keepMounted && !modalOpen ? { display: 'none' } : null,
          attrs.style,
        ]"
        :role="role"
        :aria-modal="computedAriaModal"
        :aria-hidden="keepMounted && !modalOpen ? 'true' : undefined"
        :inert="keepMounted && !modalOpen ? '' : undefined"
        :aria-labelledby="computedLabelledby"
        :aria-label="computedAriaLabel"
        :aria-describedby="computedDescribedby"
        tabindex="-1"
      >
        <div v-if="showHeader" :class="['modal-header', headerClass]">
          <slot name="header" :title-id="titleId" :close="closeModal">
            <h2 :id="titleId" class="modal-title">
              {{ title }}
            </h2>
          </slot>
          <button
            v-if="showClose"
            type="button"
            class="modal-close-btn"
            :aria-label="closeLabel"
            :disabled="!dismissible"
            @click="closeModal"
          >
            <svg
              width="18"
              height="18"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              stroke-width="2"
              stroke-linecap="round"
              stroke-linejoin="round"
              aria-hidden="true"
              focusable="false"
            >
              <path d="M18 6 6 18" />
              <path d="m6 6 12 12" />
            </svg>
          </button>
        </div>

        <div :class="['modal-body', bodyClass]">
          <slot />
        </div>

        <div v-if="showFooter" :class="['modal-footer', footerClass]">
          <slot name="footer" :close="closeModal">
            <button
              type="button"
              class="btn btn-secondary"
              :disabled="!dismissible"
              @click="closeModal"
            >
              Cancel
            </button>
            <button
              type="button"
              class="btn btn-primary"
              @click="emit('confirm')"
            >
              Confirm
            </button>
          </slot>
        </div>
      </div>
    </div>
  </OptionalTeleport>
</template>

<style scoped>
.clinical-modal-backdrop {
  position: fixed;
  inset: 0;
  box-sizing: border-box;
  background: rgba(15, 23, 42, 0.6);
  backdrop-filter: blur(2px);
  display: flex;
  justify-content: center;
  align-items: center;
  z-index: 9999;
  padding: 16px;
  overflow-x: hidden;
  overflow-y: auto;
  -webkit-overflow-scrolling: touch;
}

.clinical-modal-backdrop.is-non-modal {
  background: transparent;
  backdrop-filter: none;
  pointer-events: none;
  padding: 0;
  overflow: visible;
}

.clinical-modal-dialog {
  background: var(--semantic-color-surface);
  border-radius: var(--radius-md);
  box-shadow: var(--shadow-xl);
  border: 1px solid var(--semantic-color-border);
  display: flex;
  flex-direction: column;
  max-height: 90vh;
  max-height: 90dvh;
  width: 100%;
  min-width: 0;
  box-sizing: border-box;
  overflow: hidden;
  pointer-events: auto;
}

.clinical-modal-dialog.is-non-modal {
  overflow: visible;
}

.clinical-modal-dialog:focus:not(:focus-visible) {
  outline: none;
}

.size-sm {
  max-width: 400px;
}
.size-md {
  max-width: 560px;
}
.size-lg {
  max-width: 800px;
}
.size-xl {
  max-width: 1100px;
}

.modal-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 8px;
  flex-shrink: 0;
  padding: 16px 20px;
  background-color: var(--semantic-color-surface);
  color: var(--semantic-color-text);
  border-bottom: 1px solid var(--semantic-color-border);
}

.modal-title {
  margin: 0;
  font-size: 16px;
  font-weight: 600;
  color: var(--semantic-color-text);
  min-width: 0;
  overflow-wrap: anywhere;
}

.modal-close-btn {
  flex-shrink: 0;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  background: transparent;
  border: none;
  font-size: 18px;
  color: var(--semantic-color-text-secondary);
  cursor: pointer;
  padding: 6px;
  border-radius: var(--radius-xs);
}

.modal-close-btn:hover:not(:disabled) {
  background: var(--semantic-color-surface-header);
  color: var(--semantic-color-text);
}

.modal-close-btn:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.modal-body {
  flex: 1 1 auto;
  min-height: 0;
  min-width: 0;
  padding: 20px;
  overflow-x: hidden;
  overflow-y: auto;
  -webkit-overflow-scrolling: touch;
  font-size: 14px;
  color: var(--semantic-color-text);
}

.modal-body.has-form-layout {
  padding: 0;
  display: flex;
  flex-direction: column;
  overflow: hidden;
}

.modal-body.has-form-layout :deep(.clinical-modal-form) {
  display: flex;
  flex-direction: column;
  flex: 1 1 auto;
  min-height: 0;
  min-width: 0;
  overflow: hidden;
}

.modal-body.has-form-layout :deep(.clinical-modal-form > .modal-body) {
  flex: 1 1 auto;
  min-height: 0;
  min-width: 0;
  padding: 20px;
  overflow-x: hidden;
  overflow-y: auto;
  -webkit-overflow-scrolling: touch;
}

.modal-body.has-form-layout :deep(.clinical-modal-form > .modal-footer) {
  display: flex;
  flex-wrap: wrap;
  justify-content: flex-end;
  align-items: center;
  flex-shrink: 0;
  gap: 12px;
  padding: 14px 20px;
  border-top: 1px solid var(--semantic-color-border);
  background: var(--semantic-color-surface-muted);
}

.modal-footer {
  display: flex;
  flex-wrap: wrap;
  justify-content: flex-end;
  align-items: center;
  flex-shrink: 0;
  gap: 12px;
  padding: 14px 20px;
  border-top: 1px solid var(--semantic-color-border);
  background: var(--semantic-color-surface-muted);
  border-bottom-left-radius: var(--radius-md);
  border-bottom-right-radius: var(--radius-md);
}

.btn {
  padding: 8px 16px;
  font-size: 14px;
  font-weight: 500;
  border-radius: var(--radius-sm);
  cursor: pointer;
  border: 1px solid transparent;
  transition: all 0.15s ease;
}

.btn-secondary {
  background: var(--semantic-color-surface);
  border-color: var(--semantic-color-border-control);
  color: var(--semantic-color-text);
}

.btn-secondary:hover {
  background: var(--semantic-color-surface-header);
}

.btn-primary {
  background: var(--semantic-color-primary);
  border-color: var(--semantic-color-primary-dark);
  color: var(--semantic-color-surface);
}

.btn-primary:hover {
  background: var(--semantic-color-primary-dark);
}

@media (max-width: 1279px) {
  .modal-close-btn {
    min-width: 44px;
    min-height: 44px;
  }

  .modal-footer .btn,
  .modal-footer :deep(.btn),
  .modal-body.has-form-layout :deep(.clinical-modal-form > .modal-footer .btn) {
    min-height: 44px;
  }
}

@media (max-width: 1024px), (pointer: coarse) {
  .clinical-modal-dialog
    :deep(
      input:not([type="checkbox"]):not([type="radio"]):not([type="range"]):not(
          [type="file"]
        )
    ),
  .clinical-modal-dialog :deep(select),
  .clinical-modal-dialog :deep(textarea) {
    font-size: max(1rem, 16px);
  }
}

@media (max-width: 480px) {
  .clinical-modal-backdrop:not(.is-non-modal) {
    align-items: flex-end;
    padding: 0;
  }

  .clinical-modal-dialog:not(.is-non-modal) {
    width: 100%;
    max-width: 100%;
    max-height: 90vh;
    max-height: 90dvh;
    margin: 0;
    border-bottom-left-radius: 0;
    border-bottom-right-radius: 0;
    border-top-left-radius: var(--radius-xl, 16px);
    border-top-right-radius: var(--radius-xl, 16px);
    padding-bottom: env(safe-area-inset-bottom, 0px);
  }

  .modal-header {
    padding: 12px 16px;
  }

  .modal-body:not(.has-form-layout),
  .modal-body.has-form-layout :deep(.clinical-modal-form > .modal-body) {
    padding: 16px;
  }

  .modal-footer,
  .modal-body.has-form-layout :deep(.clinical-modal-form > .modal-footer) {
    gap: 8px;
    padding: 12px 16px calc(12px + env(safe-area-inset-bottom, 0px));
    border-bottom-left-radius: 0;
    border-bottom-right-radius: 0;
  }
}

@media (max-height: 480px) {
  .clinical-modal-backdrop:not(.is-non-modal) {
    align-items: flex-start;
    padding: 8px;
  }

  .clinical-modal-dialog:not(.is-non-modal) {
    display: block;
    max-height: calc(100vh - 16px);
    max-height: calc(100dvh - 16px);
    margin: auto 0;
    overflow-x: hidden;
    overflow-y: auto;
    -webkit-overflow-scrolling: touch;
  }

  .modal-body,
  .modal-body.has-form-layout,
  .modal-body.has-form-layout :deep(.clinical-modal-form),
  .modal-body.has-form-layout :deep(.clinical-modal-form > .modal-body) {
    display: block;
    overflow: visible;
  }
}

@media (max-width: 480px) and (max-height: 480px) {
  .clinical-modal-backdrop:not(.is-non-modal) {
    align-items: flex-end;
    padding: 0;
  }

  .clinical-modal-dialog:not(.is-non-modal) {
    width: 100%;
    max-width: 100%;
    max-height: 90vh;
    max-height: 90dvh;
    margin: 0;
  }
}

@media (prefers-reduced-motion: reduce) {
  .clinical-modal-backdrop,
  .clinical-modal-dialog,
  .modal-close-btn,
  .btn {
    animation: none;
    transition: none;
  }
}
</style>

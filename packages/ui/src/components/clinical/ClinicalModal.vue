<script setup>
import { X } from "lucide-vue-next";
import { Teleport, computed, h, ref, useAttrs, useId, useSlots } from "vue";
import { useFocusTrap } from "../../composables/useFocusTrap.js";
import { clinicalOverlayProps } from "./sharedProps.js";

const OptionalTeleport = (tpProps, { slots: tpSlots }) => {
  const children = tpSlots.default?.() || [];
  return tpProps.enabled
    ? h(Teleport, { to: tpProps.to }, children)
    : (children[0] ?? null);
};

defineOptions({
  inheritAttrs: false,
});

const props = defineProps({
  ...clinicalOverlayProps,
  role: {
    type: String,
    default: "dialog",
  },
  modal: {
    type: Boolean,
    default: true,
  },
  modelValue: {
    type: Boolean,
    default: undefined,
  },
  isOpen: {
    type: Boolean,
    default: false,
  },
  open: {
    type: Boolean,
    default: undefined,
  },
  title: {
    type: String,
    default: "Clinical Action Dialog",
  },
  ariaLabel: {
    type: String,
    default: undefined,
  },
  ariaLabelledby: {
    type: String,
    default: undefined,
  },
  ariaDescribedby: {
    type: String,
    default: undefined,
  },
  id: {
    type: String,
    default: undefined,
  },
  overlayId: {
    type: String,
    default: undefined,
  },
  closeButtonId: {
    type: String,
    default: undefined,
  },
  closeButtonClass: {
    type: [String, Array, Object],
    default: "",
  },
  dialogClass: {
    type: [String, Array, Object],
    default: "",
  },
  backdropClass: {
    type: [String, Array, Object],
    default: "",
  },
  headerClass: {
    type: [String, Array, Object],
    default: "",
  },
  bodyClass: {
    type: [String, Array, Object],
    default: "",
  },
  footerClass: {
    type: [String, Array, Object],
    default: "",
  },
  closeLabel: {
    type: String,
    default: "Close dialog",
  },
  showHeader: {
    type: Boolean,
    default: true,
  },
  showCloseButton: {
    type: Boolean,
    default: true,
  },
  showFooter: {
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

const modalOpen = computed(() => {
  if (props.modelValue !== undefined) return props.modelValue;
  if (props.open !== undefined) return props.open;
  return props.isOpen;
});

const teleportTarget = computed(() =>
  typeof props.teleport === "string" ? props.teleport : "body"
);

const computedLabelledby = computed(() => {
  if (props.ariaLabelledby) return props.ariaLabelledby;
  if (props.ariaLabel || attrs["aria-label"]) return undefined;
  if (props.showHeader && (props.title || slots.header)) return titleId.value;
  return undefined;
});

const computedAriaLabel = computed(() => {
  if (props.ariaLabel) return props.ariaLabel;
  if (attrs["aria-label"]) return String(attrs["aria-label"]);
  if (!computedLabelledby.value) {
    return props.title || "Clinical Action Dialog";
  }
  return undefined;
});

const computedAriaDescribedby = computed(() => {
  if (props.ariaDescribedby) return props.ariaDescribedby;
  if (attrs["aria-describedby"]) return String(attrs["aria-describedby"]);
  return undefined;
});

const keepMountedBackdropStyle = computed(() => {
  if (!props.keepMounted) return undefined;
  return { display: modalOpen.value ? "flex" : "none" };
});

const closeModal = () => {
  if (!props.dismissible) return;
  emit("update:modelValue", false);
  emit("update:isOpen", false);
  emit("update:open", false);
  emit("close");
};

const onBackdropClick = () => {
  if (!props.modal || !props.dismissible || !props.closeOnBackdrop) return;
  closeModal();
};

useFocusTrap(dialogRef, {
  isOpen: () => modalOpen.value && props.modal,
  modal: () => props.modal,
  escapeDeactivates: () => props.dismissible,
  initialFocus: () => props.initialFocus,
  restoreFocus: props.restoreFocus,
  onEscape: closeModal,
});
</script>

<template>
  <OptionalTeleport :enabled="Boolean(teleport)" :to="teleportTarget">
    <div
      v-if="keepMounted || modalOpen"
      :id="overlayId"
      :class="[
        'clinical-modal-backdrop',
        {
          active: modalOpen,
          'is-closed': !modalOpen,
          'is-non-modal': !modal,
        },
        modal ? backdropClass : '',
      ]"
      :style="keepMountedBackdropStyle"
      :inert="!modalOpen || undefined"
      :aria-hidden="!modalOpen ? 'true' : undefined"
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
          {
            active: modalOpen,
            'is-closed': !modalOpen,
            'is-non-modal': !modal,
          },
          dialogClass,
        ]"
        :role="role"
        :aria-modal="modalOpen && modal ? 'true' : undefined"
        :aria-hidden="!modalOpen ? 'true' : undefined"
        :inert="!modalOpen || undefined"
        :aria-labelledby="computedLabelledby"
        :aria-label="computedAriaLabel"
        :aria-describedby="computedAriaDescribedby"
        tabindex="-1"
      >
        <div v-if="showHeader" :class="['modal-header', headerClass]">
          <slot name="header" :title-id="titleId" :close="closeModal">
            <h2 :id="titleId" class="modal-title">
              {{ title }}
            </h2>
          </slot>
          <button
            v-if="showCloseButton"
            :id="closeButtonId"
            type="button"
            :class="['modal-close-btn', 'btn-close', closeButtonClass]"
            :aria-label="closeLabel"
            :disabled="!dismissible"
            @click="closeModal"
          >
            <X :size="18" :stroke-width="1.75" aria-hidden="true" />
          </button>
        </div>

        <div :class="['modal-body', bodyClass]">
          <slot :close="closeModal" :title-id="titleId" />
        </div>

        <footer v-if="showFooter" :class="['modal-footer', footerClass]">
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
        </footer>
      </div>
    </div>
  </OptionalTeleport>
</template>

<style scoped>
.clinical-modal-backdrop {
  position: fixed;
  inset: 0;
  width: 100vw;
  height: var(--overlay-viewport-height-fallback, 100vh);
  height: var(--overlay-viewport-height, 100dvh);
  background: rgba(15, 23, 42, 0.6);
  backdrop-filter: blur(2px);
  display: flex;
  justify-content: center;
  align-items: center;
  z-index: 9999;
  box-sizing: border-box;
  padding: max(16px, var(--safe-area-inset-top, env(safe-area-inset-top, 0px)))
    max(16px, var(--safe-area-inset-right, env(safe-area-inset-right, 0px)))
    max(16px, var(--safe-area-inset-bottom, env(safe-area-inset-bottom, 0px)))
    max(16px, var(--safe-area-inset-left, env(safe-area-inset-left, 0px)));
}

.clinical-modal-backdrop.is-non-modal {
  background: transparent;
  backdrop-filter: none;
  pointer-events: none;
  padding: 0;
}

.clinical-modal-dialog {
  background: var(--semantic-color-surface);
  border-radius: var(--radius-md);
  box-shadow: var(--shadow-xl);
  border: 1px solid var(--semantic-color-border);
  display: flex;
  flex-direction: column;
  max-height: var(--overlay-sheet-max-height-fallback, 90vh);
  max-height: var(--overlay-sheet-max-height, 90dvh);
  width: 100%;
  min-height: 0;
  overflow: hidden;
  outline: none;
  box-sizing: border-box;
  pointer-events: auto;
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
.size-full {
  max-width: min(1400px, 96vw);
}

.modal-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 12px;
  padding: 14px 20px;
  flex-shrink: 0;
  background-color: var(--semantic-color-surface);
  color: var(--semantic-color-text);
  border-bottom: 1px solid var(--semantic-color-border);
}

.modal-title {
  margin: 0;
  font-size: 16px;
  font-weight: 600;
  color: var(--semantic-color-text);
}

.modal-close-btn {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-width: var(--touch-target-staff-min, 44px);
  min-height: var(--touch-target-staff-min, 44px);
  background: transparent;
  border: none;
  font-size: 18px;
  color: var(--semantic-color-text-secondary);
  cursor: pointer;
  padding: 6px;
  border-radius: var(--radius-xs);
  flex-shrink: 0;
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
  padding: 20px;
  overflow-y: auto;
  -webkit-overflow-scrolling: touch;
  overscroll-behavior: contain;
  font-size: 14px;
  color: var(--semantic-color-text);
}

.modal-footer {
  display: flex;
  justify-content: flex-end;
  align-items: center;
  flex-wrap: wrap;
  gap: 12px;
  padding: 14px 20px;
  flex-shrink: 0;
  border-top: 1px solid var(--semantic-color-border);
  background: var(--semantic-color-surface-muted);
  border-bottom-left-radius: var(--radius-md);
  border-bottom-right-radius: var(--radius-md);
}

.btn {
  min-height: var(--touch-target-staff-min, 44px);
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

.btn-secondary:hover:not(:disabled) {
  background: var(--semantic-color-surface-header);
}

.btn-primary {
  background: var(--semantic-color-primary);
  border-color: var(--semantic-color-primary-dark);
  color: var(--semantic-color-surface);
}

.btn-primary:hover:not(:disabled) {
  background: var(--semantic-color-primary-dark);
}

/* 320px to 480px: Safe-area bottom sheet capped at 90dvh */
@media (max-width: 480px) {
  .clinical-modal-backdrop {
    align-items: flex-end;
    padding: var(--safe-area-inset-top, env(safe-area-inset-top, 0px)) 0 0 0;
  }

  .clinical-modal-dialog {
    width: 100%;
    max-width: 100%;
    max-height: var(--overlay-sheet-max-height-fallback, 90vh);
    max-height: var(--overlay-sheet-max-height, 90dvh);
    border-radius: var(--radius-xl, 16px) var(--radius-xl, 16px) 0 0;
    border-bottom: none;
    padding-bottom: var(
      --safe-area-inset-bottom,
      env(safe-area-inset-bottom, 0px)
    );
  }

  .modal-header {
    padding: 14px 16px;
  }

  .modal-body {
    padding: 16px;
  }

  .modal-footer {
    padding: 12px 16px;
    border-bottom-left-radius: 0;
    border-bottom-right-radius: 0;
  }
}

.modal-body.modal-content-passthrough {
  padding: 0;
  display: flex;
  flex-direction: column;
  overflow: hidden;
}

.modal-body.modal-content-passthrough > :deep(form) {
  display: flex;
  flex-direction: column;
  flex: 1 1 auto;
  min-height: 0;
}

/* Extreme short viewports (e.g. 320x256 at 400% WCAG SC 1.4.10 zoom): allow full sheet scroll */
@media (max-height: 360px) {
  .clinical-modal-backdrop {
    align-items: center;
    padding: 8px;
  }

  .clinical-modal-dialog {
    display: block;
    max-height: var(--overlay-sheet-max-height-fallback, 90vh);
    max-height: var(--overlay-sheet-max-height, 90dvh);
    overflow-y: auto;
    -webkit-overflow-scrolling: touch;
  }

  .modal-body,
  .modal-body.modal-content-passthrough,
  .modal-body.modal-content-passthrough > :deep(form) {
    display: block;
    overflow-y: visible;
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

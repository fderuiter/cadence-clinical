<script setup>
import { computed, ref, useAttrs, useId, useSlots } from "vue";
import { useFocusTrap } from "../../composables/useFocusTrap.js";
import {
  MaybeTeleport,
  buildClinicalOverlayFocusTrapOptions,
  clinicalOverlayOpenProps,
  clinicalOverlayProps,
  resolveClinicalOverlayAria,
  resolveClinicalOverlayOpen,
  resolveClinicalTeleportTarget,
  validateClinicalOverlayRole,
} from "./sharedProps.js";

defineOptions({
  inheritAttrs: false,
});

const props = defineProps({
  ...clinicalOverlayProps,
  ...clinicalOverlayOpenProps,
  role: {
    type: String,
    default: "dialog",
    validator: validateClinicalOverlayRole,
  },
  title: {
    type: String,
    default: "Clinical Action Dialog",
  },
  ariaDescribedby: {
    type: String,
    default: undefined,
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
  footerClass: {
    type: [String, Array, Object],
    default: "",
  },
  closeLabel: {
    type: String,
    default: "Close dialog",
  },
  showClose: {
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

const modalOpen = computed(() => resolveClinicalOverlayOpen(props));
const teleportTarget = computed(() => resolveClinicalTeleportTarget(props));

const overlayAria = computed(() =>
  resolveClinicalOverlayAria({
    props,
    attrs,
    headingId: titleId.value,
    canUseHeading: Boolean(props.showHeader && (props.title || slots.header)),
    fallbackLabel: props.title || "Clinical Action Dialog",
  })
);
const computedLabelledby = computed(() => overlayAria.value.labelledby);
const computedAriaLabel = computed(() => overlayAria.value.ariaLabel);

const computedDescribedby = computed(() => {
  if (props.ariaDescribedby) return props.ariaDescribedby;
  if (attrs["aria-describedby"]) return String(attrs["aria-describedby"]);
  return undefined;
});

const closeModal = () => {
  if (!props.dismissible) return;
  emit("update:modelValue", false);
  emit("update:isOpen", false);
  emit("update:open", false);
  emit("close");
};

const onBackdropClick = () => {
  if (!props.dismissible || !props.closeOnBackdrop) return;
  closeModal();
};

useFocusTrap(
  dialogRef,
  buildClinicalOverlayFocusTrapOptions(props, {
    isOpen: modalOpen,
    onEscape: closeModal,
  })
);
</script>

<template>
  <MaybeTeleport :to="teleportTarget" :disabled="!teleport">
    <div
      v-if="modalOpen"
      :id="overlayId"
      :class="['clinical-modal-backdrop', backdropClass]"
      role="presentation"
      @click.self="onBackdropClick"
    >
      <div
        :id="id"
        ref="dialogRef"
        v-bind="attrs"
        :class="['clinical-modal-dialog', `size-${size}`, dialogClass]"
        :role="role"
        aria-modal="true"
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
            ✕
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
  </MaybeTeleport>
</template>

<style scoped>
.clinical-modal-backdrop {
  position: fixed;
  inset: 0;
  box-sizing: border-box;
  background: var(--semantic-color-backdrop);
  backdrop-filter: blur(2px);
  display: flex;
  justify-content: center;
  align-items: center;
  z-index: 9999;
  padding: 16px;
  overflow-y: auto;
  -webkit-overflow-scrolling: touch;
}

.clinical-modal-dialog {
  background: var(--semantic-color-surface);
  border-radius: var(--radius-md);
  box-shadow: var(--shadow-xl);
  border: 1px solid var(--semantic-color-border);
  display: flex;
  flex-direction: column;
  max-height: 90vh;
  width: 100%;
  min-width: 0;
  box-sizing: border-box;
  overflow: hidden;
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
  background: transparent;
  border: none;
  font-size: 18px;
  color: var(--semantic-color-text-secondary);
  cursor: pointer;
  padding: 4px 8px;
  border-radius: var(--radius-xs);
}

.modal-close-btn:disabled,
.modal-close-btn[aria-disabled="true"] {
  cursor: not-allowed;
  opacity: 0.5;
  color: var(--semantic-color-text-muted);
}

.modal-close-btn:not(:disabled):not([aria-disabled="true"]):hover {
  background: var(--semantic-color-surface-header);
  color: var(--semantic-color-text);
}

.modal-body {
  flex: 1 1 auto;
  min-height: 0;
  padding: 20px;
  overflow-y: auto;
  -webkit-overflow-scrolling: touch;
  font-size: 14px;
  color: var(--semantic-color-text);
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
  transition:
    background-color 0.15s ease,
    border-color 0.15s ease,
    color 0.15s ease;
}

.btn:disabled,
.btn[aria-disabled="true"] {
  cursor: not-allowed;
  opacity: 0.6;
}

.btn-secondary {
  background: var(--semantic-color-surface);
  border-color: var(--semantic-color-border-control);
  color: var(--semantic-color-text);
}

.btn-secondary:disabled,
.btn-secondary[aria-disabled="true"] {
  background: var(--semantic-color-surface-muted);
  border-color: var(--semantic-color-border);
  color: var(--semantic-color-text-muted);
}

.btn-secondary:not(:disabled):not([aria-disabled="true"]):hover {
  background: var(--semantic-color-surface-header);
}

.btn-primary {
  background: var(--semantic-color-primary);
  border-color: var(--semantic-color-primary-dark);
  color: var(--semantic-color-text-on-primary);
}

.btn-primary:not(:disabled):not([aria-disabled="true"]):hover {
  background: var(--semantic-color-primary-dark);
}

@media (max-width: 480px) {
  .clinical-modal-backdrop {
    padding: 8px;
  }

  .clinical-modal-dialog {
    max-height: calc(100vh - 16px);
  }

  .modal-header {
    padding: 12px 16px;
  }

  .modal-body {
    padding: 16px;
  }

  .modal-footer {
    gap: 8px;
    padding: 12px 16px;
  }
}

@media (max-height: 480px) {
  .clinical-modal-backdrop {
    align-items: flex-start;
    padding: 8px;
  }

  .clinical-modal-dialog {
    display: block;
    max-height: calc(100vh - 16px);
    margin: auto 0;
    overflow-x: hidden;
    overflow-y: auto;
    -webkit-overflow-scrolling: touch;
  }

  .modal-body {
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

<script setup>
import { computed, ref, useAttrs, useId, useSlots } from "vue";
import { X } from "lucide-vue-next";
import { useFocusTrap } from "../../composables/useFocusTrap.js";
import { clinicalOverlayProps } from "./sharedProps.js";

defineOptions({
  inheritAttrs: false,
});

const props = defineProps({
  ...clinicalOverlayProps,
  role: {
    type: String,
    default: "dialog",
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
  id: {
    type: String,
    default: undefined,
  },
  overlayId: {
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

useFocusTrap(dialogRef, {
  isOpen: modalOpen,
  escapeDeactivates: () => props.dismissible,
  initialFocus: () => props.initialFocus,
  restoreFocus: props.restoreFocus,
  onEscape: closeModal,
});
</script>

<template>
  <Teleport :to="teleportTarget" :disabled="!teleport">
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
        tabindex="-1"
      >
        <div v-if="showHeader" :class="['modal-header', headerClass]">
          <slot name="header" :title-id="titleId" :close="closeModal">
            <h2 :id="titleId" class="modal-title">
              {{ title }}
            </h2>
          </slot>
          <button
            type="button"
            class="modal-close-btn"
            :aria-label="closeLabel"
            :disabled="!dismissible"
            @click="closeModal"
          >
            <X :size="20" :stroke-width="1.75" aria-hidden="true" />
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
  </Teleport>
</template>

<style scoped>
.clinical-modal-backdrop {
  position: fixed;
  top: 0;
  left: 0;
  width: 100vw;
  height: 100vh;
  background: rgba(15, 23, 42, 0.6);
  backdrop-filter: blur(2px);
  display: flex;
  justify-content: center;
  align-items: center;
  z-index: 9999;
  padding: 16px;
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
  padding: 16px 20px;
  background-color: var(--semantic-color-surface);
  color: var(--semantic-color-text);
  border-bottom: 1px solid var(--semantic-color-border);
}

.modal-title {
  margin: 0;
  font-size: var(--font-size-md, 1rem);
  font-weight: 600;
  color: var(--semantic-color-text);
}

.modal-close-btn {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-width: var(--surface-control-min-width, 44px);
  min-height: var(--surface-control-min-height, 44px);
  background: transparent;
  border: none;
  font-size: var(--font-size-lg, 1.125rem);
  color: var(--semantic-color-text-secondary);
  cursor: pointer;
  padding: 6px;
  border-radius: var(--radius-xs);
}

.modal-close-btn:hover {
  background: var(--semantic-color-surface-header);
  color: var(--semantic-color-text);
}

.modal-body {
  padding: 20px;
  overflow-y: auto;
  font-size: var(--font-size-sm, 0.875rem);
  color: var(--semantic-color-text);
}

.modal-footer {
  display: flex;
  flex-wrap: wrap;
  justify-content: flex-end;
  gap: 12px;
  padding: 14px 20px;
  border-top: 1px solid var(--semantic-color-border);
  background: var(--semantic-color-surface-muted);
  border-bottom-left-radius: var(--radius-md);
  border-bottom-right-radius: var(--radius-md);
}

.modal-footer :deep(button) {
  min-height: var(--surface-control-min-height, 44px);
  min-width: var(--surface-control-min-width, 44px);
}

.btn {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-height: var(--surface-control-min-height, 44px);
  min-width: var(--surface-control-min-width, 44px);
  padding: 8px 16px;
  font-size: var(--font-size-sm, 0.875rem);
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

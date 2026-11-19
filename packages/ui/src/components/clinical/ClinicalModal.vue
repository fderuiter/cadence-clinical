<script setup>
import { computed, ref, useAttrs, useId } from "vue";
import { useFocusTrap } from "../../composables/useFocusTrap.js";

defineOptions({
  inheritAttrs: false,
});

const props = defineProps({
  modelValue: {
    type: Boolean,
    default: false,
  },
  title: {
    type: String,
    default: "Clinical Action Dialog",
  },
  size: {
    type: String,
    default: "md", // sm, md, lg, xl
  },
  role: {
    type: String,
    default: "dialog",
  },
  dismissible: {
    type: Boolean,
    default: true,
  },
  closeOnBackdrop: {
    type: Boolean,
    default: true,
  },
  initialFocus: {
    type: [String, Object, Function],
    default: null,
  },
  restoreFocus: {
    type: [Boolean, Function],
    default: true,
  },
  teleport: {
    type: [Boolean, String],
    default: "body",
  },
  id: {
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
  closeLabel: {
    type: String,
    default: "Close dialog",
  },
});

const emit = defineEmits(["update:modelValue", "close", "confirm"]);

const attrs = useAttrs();
const dialogRef = ref(null);
const instanceId = useId();
const titleId = `clinical-modal-title-${instanceId}`;

const teleportTarget = computed(() =>
  typeof props.teleport === "string" ? props.teleport : "body"
);

const closeModal = () => {
  if (!props.dismissible) return;
  emit("update:modelValue", false);
  emit("close");
};

const onBackdropClick = () => {
  if (!props.dismissible || !props.closeOnBackdrop) return;
  closeModal();
};

useFocusTrap(dialogRef, {
  isOpen: () => props.modelValue,
  escapeDeactivates: () => props.dismissible,
  initialFocus: () => props.initialFocus,
  restoreFocus: props.restoreFocus,
  onEscape: closeModal,
});
</script>

<template>
  <Teleport :to="teleportTarget" :disabled="!teleport">
    <div
      v-if="modelValue"
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
        :aria-labelledby="attrs['aria-label'] ? undefined : titleId"
        :aria-label="
          attrs['aria-label'] ? String(attrs['aria-label']) : undefined
        "
        tabindex="-1"
      >
        <div class="modal-header">
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
            ✕
          </button>
        </div>

        <div class="modal-body">
          <slot />
        </div>

        <div class="modal-footer">
          <slot name="footer">
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
  background: var(--color-surface, #ffffff);
  border-radius: 8px;
  box-shadow:
    0 20px 25px -5px rgba(0, 0, 0, 0.1),
    0 10px 10px -5px rgba(0, 0, 0, 0.04);
  border: 1px solid var(--color-border, #e2e8f0);
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
  border-bottom: 1px solid var(--color-border, #e2e8f0);
}

.modal-title {
  margin: 0;
  font-size: 16px;
  font-weight: 600;
  color: var(--color-text, #0f172a);
}

.modal-close-btn {
  background: transparent;
  border: none;
  font-size: 18px;
  color: var(--color-text-muted, #475569);
  cursor: pointer;
  padding: 4px 8px;
  border-radius: 4px;
}

.modal-close-btn:hover {
  background: #f1f5f9;
  color: var(--color-text, #0f172a);
}

.modal-body {
  padding: 20px;
  overflow-y: auto;
  font-size: 14px;
  color: var(--color-text, #0f172a);
}

.modal-footer {
  display: flex;
  justify-content: flex-end;
  gap: 12px;
  padding: 14px 20px;
  border-top: 1px solid var(--color-border, #e2e8f0);
  background: var(--color-surface-muted, #f8fafc);
  border-bottom-left-radius: 8px;
  border-bottom-right-radius: 8px;
}

.btn {
  padding: 8px 16px;
  font-size: 14px;
  font-weight: 500;
  border-radius: 6px;
  cursor: pointer;
  border: 1px solid transparent;
  transition: all 0.15s ease;
}

.btn-secondary {
  background: var(--color-surface, #ffffff);
  border-color: var(--color-border, #e2e8f0);
  color: var(--color-text, #0f172a);
}

.btn-secondary:hover {
  background: #f1f5f9;
}

.btn-primary {
  background: var(--color-primary, #026597);
  color: #ffffff;
}

.btn-primary:hover {
  background: var(--color-primary-dark, #014d76);
}
</style>

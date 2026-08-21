<script setup>
import { nextTick, onMounted, onUnmounted, ref, watch } from "vue";

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
});

const emit = defineEmits(["update:modelValue", "close", "confirm"]);

const dialogRef = ref(null);
const previousActiveElement = ref(null);

const closeModal = () => {
  emit("update:modelValue", false);
  emit("close");
};

const handleKeyDown = (e) => {
  if (e.key === "Escape" && props.modelValue) {
    closeModal();
  }
};

watch(
  () => props.modelValue,
  async (isOpen) => {
    if (isOpen) {
      previousActiveElement.value = document.activeElement;
      await nextTick();
      if (dialogRef.value) {
        dialogRef.value.focus();
      }
    } else if (previousActiveElement.value) {
      previousActiveElement.value.focus();
    }
  }
);

onMounted(() => {
  window.addEventListener("keydown", handleKeyDown);
});

onUnmounted(() => {
  window.removeEventListener("keydown", handleKeyDown);
});
</script>

<template>
  <Teleport to="body">
    <div
      v-if="modelValue"
      class="clinical-modal-backdrop"
      role="presentation"
      @click.self="closeModal"
    >
      <div
        ref="dialogRef"
        :class="['clinical-modal-dialog', `size-${size}`]"
        role="dialog"
        aria-modal="true"
        :aria-label="title"
        tabindex="-1"
      >
        <div class="modal-header">
          <h2 class="modal-title">
            {{ title }}
          </h2>
          <button
            class="modal-close-btn"
            aria-label="Close dialog"
            @click="closeModal"
          >
            ✕
          </button>
        </div>

        <div class="modal-body">
          <slot></slot>
        </div>

        <div class="modal-footer">
          <slot name="footer">
            <button
              class="btn btn-secondary"
              @click="closeModal"
            >
              Cancel
            </button>
            <button
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

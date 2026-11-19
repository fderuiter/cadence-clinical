<script setup>
import { ref } from "vue";
import { useFocusTrap } from "../../composables/useFocusTrap.js";

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
});

const emit = defineEmits(["update:modelValue", "close", "confirm"]);

const dialogRef = ref(null);

const closeModal = () => {
  emit("update:modelValue", false);
  emit("close");
};

useFocusTrap(dialogRef, {
  isOpen: () => props.modelValue,
  onEscape: closeModal,
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
        :role="role"
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
          <slot />
        </div>

        <div class="modal-footer">
          <slot name="footer">
            <button class="btn btn-secondary" @click="closeModal">
              Cancel
            </button>
            <button class="btn btn-primary" @click="emit('confirm')">
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
  border-bottom: 1px solid var(--semantic-color-border);
}

.modal-title {
  margin: 0;
  font-size: 16px;
  font-weight: 600;
  color: var(--semantic-color-text);
}

.modal-close-btn {
  background: transparent;
  border: none;
  font-size: 18px;
  color: var(--semantic-color-text-muted);
  cursor: pointer;
  padding: 4px 8px;
  border-radius: var(--radius-xs);
}

.modal-close-btn:hover {
  background: var(--semantic-color-surface-header);
  color: var(--semantic-color-text);
}

.modal-body {
  padding: 20px;
  overflow-y: auto;
  font-size: 14px;
  color: var(--semantic-color-text);
}

.modal-footer {
  display: flex;
  justify-content: flex-end;
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
</style>

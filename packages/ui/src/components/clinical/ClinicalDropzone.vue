<script setup>
import { computed, onBeforeUnmount, ref, useId, watch } from "vue";
import { isClinicalIconName } from "../../icons/clinicalCoreIcons.js";
import ClinicalIcon from "./ClinicalIcon.vue";

const props = defineProps({
  inputId: {
    type: String,
    default: "",
  },
  accept: {
    type: String,
    default: "",
  },
  multiple: {
    type: Boolean,
    default: false,
  },
  disabled: {
    type: Boolean,
    default: false,
  },
  hasSelection: {
    type: Boolean,
    default: false,
  },
  icon: {
    type: String,
    default: "",
  },
  promptText: {
    type: String,
    default: "",
  },
  hintText: {
    type: String,
    default: "",
  },
  buttonLabel: {
    type: String,
    default: "Browse files",
  },
  buttonContext: {
    type: String,
    default: "",
  },
  buttonClass: {
    type: String,
    default: "",
  },
  inputAriaLabel: {
    type: String,
    default: "",
  },
  describedBy: {
    type: String,
    default: "",
  },
  removable: {
    type: Boolean,
    default: false,
  },
  removeLabel: {
    type: String,
    default: "Remove file",
  },
  removeContext: {
    type: String,
    default: "",
  },
  removeButtonClass: {
    type: String,
    default: "",
  },
});

const emit = defineEmits([
  "files-selected",
  "file-selected",
  "change",
  "remove",
]);

const fallbackUid = useId();
const dropzoneRef = ref(null);
const fileInputRef = ref(null);
const isDragging = ref(false);

const resolvedInputId = computed(
  () => props.inputId || `clinical-dropzone-input-${fallbackUid}`
);
const isSemanticIcon = computed(() => isClinicalIconName(props.icon));

const openFilePicker = () => {
  if (props.disabled) return;
  fileInputRef.value?.click();
};

const resetInput = () => {
  if (fileInputRef.value) {
    fileInputRef.value.value = "";
  }
};

const emitFiles = (fileList) => {
  const files = fileList ? Array.from(fileList) : [];
  if (files.length === 0) return;
  emit("files-selected", files);
  emit("file-selected", files[0]);
};

const onInputChange = (event) => {
  const files = event.target?.files ? Array.from(event.target.files) : [];
  if (event.target && "value" in event.target) {
    try {
      event.target.value = "";
    } catch {
      // Ignore synthetic event targets with read-only value descriptors
    }
  }
  emit("change", event);
  if (files.length > 0) {
    emit("files-selected", files);
    emit("file-selected", files[0]);
  }
};

const onDragEnter = (event) => {
  event?.preventDefault?.();
  if (props.disabled) return;
  isDragging.value = true;
};

const onDragOver = (event) => {
  event?.preventDefault?.();
  if (props.disabled) return;
  isDragging.value = true;
};

const onDragLeave = (event) => {
  event?.preventDefault?.();
  isDragging.value = false;
};

const onDrop = (event) => {
  event?.preventDefault?.();
  isDragging.value = false;
  if (props.disabled) return;
  emitFiles(event?.dataTransfer?.files);
};

const detachDragListeners = (el) => {
  if (!el?.removeEventListener) return;
  el.removeEventListener("dragenter", onDragEnter);
  el.removeEventListener("dragover", onDragOver);
  el.removeEventListener("dragleave", onDragLeave);
  el.removeEventListener("drop", onDrop);
};

watch(
  dropzoneRef,
  (el, prevEl) => {
    detachDragListeners(prevEl);
    if (!el?.addEventListener) return;
    el.addEventListener("dragenter", onDragEnter);
    el.addEventListener("dragover", onDragOver);
    el.addEventListener("dragleave", onDragLeave);
    el.addEventListener("drop", onDrop);
  },
  { immediate: true }
);

onBeforeUnmount(() => {
  detachDragListeners(dropzoneRef.value);
});

const onRemove = () => {
  if (props.disabled) return;
  resetInput();
  emit("remove");
};

defineExpose({
  openFilePicker,
  resetInput,
  fileInputRef,
});
</script>

<template>
  <div
    ref="dropzoneRef"
    class="clinical-dropzone"
    :class="{
      'is-dragging': isDragging,
      'has-file': hasSelection,
      'is-disabled': disabled,
    }"
  >
    <input
      :id="resolvedInputId"
      ref="fileInputRef"
      type="file"
      class="clinical-dropzone-input"
      tabindex="-1"
      :accept="accept || undefined"
      :multiple="multiple"
      :disabled="disabled"
      :aria-label="inputAriaLabel || buttonLabel"
      @change="onInputChange"
    />

    <div class="clinical-dropzone-body">
      <span v-if="icon" class="clinical-dropzone-icon" aria-hidden="true">
        <ClinicalIcon v-if="isSemanticIcon" :name="icon" :size="20" />
        <template v-else>{{ icon }}</template>
      </span>

      <div v-if="$slots.prompt || promptText" class="clinical-dropzone-prompt">
        <slot name="prompt">
          <p class="clinical-dropzone-title">{{ promptText }}</p>
        </slot>
      </div>

      <p v-if="$slots.hint || hintText" class="clinical-dropzone-hint">
        <slot name="hint">{{ hintText }}</slot>
      </p>

      <div
        v-if="hasSelection && $slots.selected"
        class="clinical-dropzone-selected"
        role="status"
        aria-live="polite"
      >
        <slot name="selected" />
      </div>

      <div class="clinical-dropzone-actions">
        <button
          type="button"
          :class="buttonClass || 'clinical-dropzone-browse-btn'"
          :disabled="disabled"
          :aria-describedby="describedBy || undefined"
          @click="openFilePicker"
        >
          {{ buttonLabel
          }}<span v-if="buttonContext" class="sr-only">
            {{ buttonContext }}</span
          >
        </button>

        <button
          v-if="removable && hasSelection"
          type="button"
          :class="removeButtonClass || 'clinical-dropzone-remove-btn'"
          :disabled="disabled"
          @click="onRemove"
        >
          {{ removeLabel
          }}<span v-if="removeContext" class="sr-only">
            {{ removeContext }}</span
          >
        </button>

        <slot
          name="actions"
          :open-file-picker="openFilePicker"
          :reset-input="resetInput"
        />
      </div>
    </div>
  </div>
</template>

<style scoped>
.clinical-dropzone {
  border: 2px dashed var(--semantic-color-border-control);
  border-radius: var(--radius-md);
  padding: var(--spacing-lg);
  text-align: center;
  background-color: var(--semantic-color-surface-muted);
  transition:
    border-color 0.15s ease,
    background-color 0.15s ease;
}

.clinical-dropzone.is-dragging {
  border-color: var(--semantic-color-primary);
  background-color: var(--semantic-color-primary-light);
}

.clinical-dropzone.has-file {
  border-style: solid;
  border-color: var(--semantic-color-success);
  background-color: var(--semantic-color-success-bg-soft);
}

.clinical-dropzone.is-disabled {
  opacity: 0.6;
}

.clinical-dropzone-input {
  display: none;
}

.clinical-dropzone-body {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: var(--spacing-xs);
}

.clinical-dropzone-icon {
  font-size: var(--font-size-2xl);
  line-height: 1.1;
}

.clinical-dropzone-title {
  margin: 0;
  font-size: var(--font-size-sm);
  font-weight: 600;
  color: var(--semantic-color-text);
}

.clinical-dropzone-hint {
  margin: 0;
  font-size: var(--font-size-xs);
  color: var(--semantic-color-text-muted);
}

.clinical-dropzone-selected {
  width: 100%;
  margin-top: var(--spacing-2xs);
  color: var(--semantic-color-text);
}

.clinical-dropzone-actions {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: center;
  gap: var(--spacing-xs);
  margin-top: var(--spacing-2xs);
}

.clinical-dropzone-browse-btn {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-height: 36px;
  padding: var(--spacing-xs) var(--spacing-md);
  border-radius: var(--radius-sm);
  border: 1px solid var(--semantic-color-primary-dark);
  background-color: var(--semantic-color-surface);
  color: var(--semantic-color-primary-dark);
  font-family: var(--font-sans);
  font-size: var(--font-size-xs);
  font-weight: 600;
  cursor: pointer;
}

.clinical-dropzone-browse-btn:hover:not(:disabled) {
  background-color: var(--semantic-color-primary-light);
}

.clinical-dropzone-remove-btn {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-height: 36px;
  padding: var(--spacing-xs) var(--spacing-sm);
  border-radius: var(--radius-sm);
  border: 1px solid var(--semantic-color-error-border);
  background-color: var(--semantic-color-surface);
  color: var(--semantic-color-error-dark);
  font-family: var(--font-sans);
  font-size: var(--font-size-xs);
  font-weight: 600;
  cursor: pointer;
}

.clinical-dropzone-remove-btn:hover:not(:disabled) {
  background-color: var(--semantic-color-error-bg-soft);
}

.clinical-dropzone-browse-btn:focus-visible,
.clinical-dropzone-remove-btn:focus-visible {
  outline: 2px solid var(--semantic-color-primary);
  outline-offset: 2px;
}

.clinical-dropzone-browse-btn:disabled,
.clinical-dropzone-remove-btn:disabled {
  cursor: not-allowed;
  opacity: 0.6;
}

@media (prefers-reduced-motion: reduce) {
  .clinical-dropzone {
    transition: none;
  }
}
</style>

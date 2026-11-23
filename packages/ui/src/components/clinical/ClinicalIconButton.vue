<script setup>
const props = defineProps({
  label: {
    type: String,
    required: true,
    validator: (value) => typeof value === "string" && value.trim().length > 0,
  },
  icon: {
    type: String,
    default: "",
  },
  type: {
    type: String,
    default: "button",
  },
  variant: {
    type: String,
    default: "unstyled",
  },
  size: {
    type: String,
    default: "md",
  },
  disabled: {
    type: Boolean,
    default: false,
  },
  title: {
    type: String,
    default: "",
  },
});

const emit = defineEmits(["click"]);

const handleClick = (event) => {
  if (props.disabled) {
    event.preventDefault();
    return;
  }
  emit("click", event);
};
</script>

<template>
  <button
    :type="type"
    class="clinical-icon-btn"
    :class="[
      variant !== 'unstyled' ? `clinical-icon-btn-${variant}` : '',
      variant !== 'unstyled' ? `clinical-icon-btn-${size}` : '',
    ]"
    :aria-label="label"
    :disabled="disabled"
    :title="title || undefined"
    @click="handleClick"
  >
    <span class="clinical-icon-btn-glyph" aria-hidden="true">
      <slot>{{ icon }}</slot>
    </span>
  </button>
</template>

<style scoped>
.clinical-icon-btn-glyph {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  line-height: inherit;
  pointer-events: none;
}

.clinical-icon-btn-ghost,
.clinical-icon-btn-secondary,
.clinical-icon-btn-danger {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  border-radius: var(--radius-sm);
  font-family: var(--font-sans);
  cursor: pointer;
  transition:
    background-color 0.15s ease,
    border-color 0.15s ease,
    color 0.15s ease;
}

.clinical-icon-btn-ghost {
  background: transparent;
  border: 1px solid transparent;
  color: var(--semantic-color-text-secondary);
}

.clinical-icon-btn-ghost:hover:not(:disabled) {
  background-color: var(--semantic-color-surface-muted);
  color: var(--semantic-color-text);
}

.clinical-icon-btn-secondary {
  background-color: var(--semantic-color-surface);
  border: 1px solid var(--semantic-color-border-control);
  color: var(--semantic-color-text);
}

.clinical-icon-btn-secondary:hover:not(:disabled) {
  background-color: var(--semantic-color-surface-muted);
}

.clinical-icon-btn-danger {
  background-color: var(--semantic-color-surface);
  border: 1px solid var(--semantic-color-error-border);
  color: var(--semantic-color-error-dark);
}

.clinical-icon-btn-danger:hover:not(:disabled) {
  background-color: var(--semantic-color-error-bg-soft);
}

.clinical-icon-btn-sm {
  min-width: 28px;
  min-height: 28px;
  padding: var(--spacing-2xs);
  font-size: var(--font-size-xs);
}

.clinical-icon-btn-md {
  min-width: 34px;
  min-height: 34px;
  padding: var(--spacing-xs);
  font-size: var(--font-size-sm);
}

.clinical-icon-btn-lg {
  min-width: var(--touch-target-min);
  min-height: var(--touch-target-min);
  padding: var(--spacing-sm);
  font-size: var(--font-size-md);
}

.clinical-icon-btn:focus-visible {
  outline: 2px solid var(--semantic-color-primary);
  outline-offset: 2px;
}

.clinical-icon-btn:disabled {
  opacity: 0.55;
  cursor: not-allowed;
}

@media (prefers-reduced-motion: reduce) {
  .clinical-icon-btn-ghost,
  .clinical-icon-btn-secondary,
  .clinical-icon-btn-danger {
    transition: none;
  }
}
</style>

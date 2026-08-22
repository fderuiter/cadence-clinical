<script setup>
import { computed } from "vue";

const props = defineProps({
  label: {
    type: String,
    default: "AI Assist",
  },
  icon: {
    type: String,
    default: "✨",
  },
  loading: {
    type: Boolean,
    default: false,
  },
  loadingText: {
    type: String,
    default: "Generating...",
  },
  disabled: {
    type: Boolean,
    default: false,
  },
  variant: {
    type: String,
    default: "secondary", // 'primary', 'secondary', 'subtle', 'outline'
  },
  size: {
    type: String,
    default: "md", // 'sm', 'md', 'lg'
  },
  requiredRoles: {
    type: [Array, String],
    default: () => [],
  },
  userRoles: {
    type: [Array, String],
    default: () => [],
  },
  unauthorizedTooltip: {
    type: String,
    default: "Your role does not permit AI actions for this clinical workspace.",
  },
  ariaLabel: {
    type: String,
    default: "",
  },
});

const emit = defineEmits(["click"]);

const normalizedRequiredRoles = computed(() => {
  if (!props.requiredRoles) return [];
  if (Array.isArray(props.requiredRoles)) {
    return props.requiredRoles.map((r) => String(r).toLowerCase().trim());
  }
  return [String(props.requiredRoles).toLowerCase().trim()];
});

const normalizedUserRoles = computed(() => {
  if (!props.userRoles) return [];
  if (Array.isArray(props.userRoles)) {
    return props.userRoles.map((r) => String(r).toLowerCase().trim());
  }
  return [String(props.userRoles).toLowerCase().trim()];
});

const isAuthorized = computed(() => {
  if (normalizedRequiredRoles.value.length === 0) return true;
  if (normalizedUserRoles.value.length === 0) return false;
  return normalizedRequiredRoles.value.some((role) =>
    normalizedUserRoles.value.includes(role)
  );
});

const isDisabled = computed(() => {
  return props.disabled || props.loading || !isAuthorized.value;
});

const computedAriaLabel = computed(() => {
  if (props.ariaLabel) return props.ariaLabel;
  if (props.loading) return props.loadingText || "Generating AI suggestion...";
  if (!isAuthorized.value) return props.unauthorizedTooltip;
  return props.label;
});

const handleClick = (e) => {
  if (isDisabled.value) {
    e.preventDefault();
    return;
  }
  emit("click", e);
};
</script>

<template>
  <button
    type="button"
    class="ai-action-btn"
    :class="[
      `ai-btn-${variant}`,
      `ai-btn-${size}`,
      { 'is-loading': loading, 'is-unauthorized': !isAuthorized },
    ]"
    :disabled="isDisabled"
    :aria-busy="loading ? 'true' : 'false'"
    :aria-disabled="isDisabled ? 'true' : 'false'"
    :aria-label="computedAriaLabel"
    :title="!isAuthorized ? unauthorizedTooltip : ''"
    @click="handleClick"
  >
    <template v-if="loading">
      <span class="ai-spinner" aria-hidden="true">⏳</span>
      <span class="ai-loading-text">{{ loadingText }}</span>
    </template>
    <template v-else>
      <span class="ai-icon" aria-hidden="true">{{ icon }}</span>
      <span class="ai-label">{{ label }}</span>
    </template>
  </button>
</template>

<style scoped>
.ai-action-btn {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: var(--spacing-xs, 8px);
  font-family: var(--font-sans, inherit);
  font-weight: 500;
  border-radius: var(--radius-md, 6px);
  border: 1px solid transparent;
  cursor: pointer;
  transition: all 0.15s ease-in-out;
  outline: none;
  white-space: nowrap;
  user-select: none;
}

.ai-action-btn:focus-visible {
  box-shadow: 0 0 0 2px var(--color-surface, #ffffff), 0 0 0 4px var(--color-primary, #026597);
}

/* Sizes */
.ai-btn-sm {
  padding: 4px 10px;
  font-size: var(--font-size-xs, 12px);
  min-height: 28px;
}

.ai-btn-md {
  padding: 6px 14px;
  font-size: var(--font-size-sm, 14px);
  min-height: 36px;
}

.ai-btn-lg {
  padding: 10px 18px;
  font-size: var(--font-size-base, 15px);
  min-height: 44px;
}

/* Variants */
.ai-btn-primary {
  background: var(--color-primary, #026597);
  color: #ffffff;
  border-color: var(--color-primary-dark, #014d76);
}

.ai-btn-primary:hover:not(:disabled) {
  background: var(--color-primary-dark, #014d76);
}

.ai-btn-secondary {
  background: var(--color-surface, #ffffff);
  border-color: var(--color-border, #e2e8f0);
  color: var(--color-text, #0f172a);
}

.ai-btn-secondary:hover:not(:disabled) {
  background: var(--color-surface-muted, #f8fafc);
  border-color: var(--color-primary, #026597);
  color: var(--color-primary, #026597);
}

.ai-btn-subtle {
  background: var(--color-primary-light, #e0f2fe);
  color: var(--color-primary-dark, #014d76);
  border-color: transparent;
}

.ai-btn-subtle:hover:not(:disabled) {
  background: #bae6fd;
}

.ai-btn-outline {
  background: transparent;
  border-color: var(--color-primary, #026597);
  color: var(--color-primary, #026597);
}

.ai-btn-outline:hover:not(:disabled) {
  background: var(--color-primary-light, #e0f2fe);
}

/* Disabled & Loading States */
.ai-action-btn:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

.ai-action-btn.is-unauthorized {
  opacity: 0.45;
  filter: grayscale(0.8);
}

.ai-spinner {
  display: inline-block;
  animation: ai-spin 1.2s infinite linear;
}

@keyframes ai-spin {
  0% { transform: rotate(0deg); }
  100% { transform: rotate(360deg); }
}

.ai-icon {
  font-size: 1.1em;
}
</style>

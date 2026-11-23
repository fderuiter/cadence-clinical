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
    default:
      "Your role does not permit AI actions for this clinical workspace.",
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
  const visibleText = props.loading
    ? props.loadingText || "Generating AI suggestion..."
    : props.label;
  if (props.ariaLabel) {
    const normalizedVisible = String(visibleText || "")
      .trim()
      .toLowerCase();
    const normalizedOverride = String(props.ariaLabel).trim().toLowerCase();
    return normalizedVisible && normalizedOverride.includes(normalizedVisible)
      ? props.ariaLabel
      : `${visibleText} — ${props.ariaLabel}`;
  }
  if (!isAuthorized.value && props.unauthorizedTooltip) {
    return `${visibleText} — ${props.unauthorizedTooltip}`;
  }
  return visibleText;
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
  transition:
    background-color 0.15s ease-in-out,
    border-color 0.15s ease-in-out,
    color 0.15s ease-in-out,
    box-shadow 0.15s ease-in-out,
    opacity 0.15s ease-in-out;
  outline: none;
  white-space: nowrap;
  user-select: none;
}

.ai-action-btn:focus-visible {
  box-shadow:
    0 0 0 2px var(--color-surface),
    0 0 0 4px var(--color-primary);
}

/* Sizes */
.ai-btn-sm {
  padding: 4px 10px;
  font-size: var(--font-size-xs, 0.8125rem);
  min-height: 28px;
}

.ai-btn-md {
  padding: 6px 14px;
  font-size: var(--font-size-sm, 0.875rem);
  min-height: 36px;
}

.ai-btn-lg {
  padding: 10px 18px;
  font-size: var(--font-size-base, 0.9375rem);
  min-height: 44px;
}

/* Variants */
.ai-btn-primary {
  background: var(--color-primary);
  color: var(--semantic-color-surface);
  border-color: var(--color-primary-dark);
}

.ai-btn-primary:hover:not(:disabled) {
  background: var(--color-primary-dark);
}

.ai-btn-secondary {
  background: var(--color-surface);
  border-color: var(--semantic-color-border-control);
  color: var(--color-text);
}

.ai-btn-secondary:hover:not(:disabled) {
  background: var(--color-surface-muted);
  border-color: var(--color-primary);
  color: var(--color-primary);
}

.ai-btn-subtle {
  background: var(--color-primary-light);
  color: var(--color-primary-dark);
  border-color: var(--semantic-color-border-control);
}

.ai-btn-subtle:hover:not(:disabled) {
  background: var(--semantic-color-info-border-accent);
}

.ai-btn-outline {
  background: transparent;
  border-color: var(--color-primary);
  color: var(--color-primary);
}

.ai-btn-outline:hover:not(:disabled) {
  background: var(--color-primary-light);
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
  0% {
    transform: rotate(0deg);
  }
  100% {
    transform: rotate(360deg);
  }
}

.ai-icon {
  font-size: 1.1em;
}
</style>

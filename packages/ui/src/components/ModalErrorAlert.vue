<script setup>
import { computed, ref, watch, onMounted } from "vue";
import { AlertTriangle } from "lucide-vue-next";

const props = defineProps({
  error: {
    type: [Object, String, Error, null, undefined],
    default: null,
  },
  errorMessage: {
    type: String,
    default: "",
  },
  title: {
    type: String,
    default: "",
  },
  retryLabel: {
    type: String,
    default: "Retry",
  },
  dismissLabel: {
    type: String,
    default: "Dismiss",
  },
  showRetry: {
    type: Boolean,
    default: true,
  },
  showDismiss: {
    type: Boolean,
    default: true,
  },
  onRetry: {
    type: Function,
    default: null,
  },
  onDismiss: {
    type: Function,
    default: null,
  },
  id: {
    type: String,
    default: null,
  },
});

const emit = defineEmits(["retry", "dismiss", "clear-error"]);

const alertRef = ref(null);

const displayMessage = computed(() => {
  if (!props.error && !props.errorMessage) return "";
  if (props.errorMessage) return props.errorMessage;
  if (typeof props.error === "string") return props.error;
  if (props.error && typeof props.error === "object") {
    return props.error.message || props.error.detail || String(props.error);
  }
  return String(props.error);
});

const hasError = computed(() => {
  return !!(props.error || props.errorMessage);
});

function handleRetry(event) {
  emit("retry", event);
}

function handleDismiss(event) {
  emit("dismiss", event);
  emit("clear-error", event);
}

watch(hasError, (newVal) => {
  if (newVal && alertRef.value) {
    alertRef.value.focus();
  }
});

onMounted(() => {
  if (hasError.value && alertRef.value) {
    alertRef.value.focus();
  }
});
</script>

<template>
  <div
    v-if="hasError"
    :id="id"
    ref="alertRef"
    class="modal-error-alert error-alert error-banner alert-box error-box error-msg"
    role="alert"
    aria-live="assertive"
    tabindex="-1"
  >
    <div class="alert-content">
      <span class="error-icon" aria-hidden="true">
        <AlertTriangle
          :size="16"
          :stroke-width="1.75"
          aria-hidden="true"
          focusable="false"
          class="clinical-icon"
        />
      </span>
      <div class="error-text-container">
        <strong v-if="title" class="alert-title">{{ title }}</strong>
        <span class="alert-message">
          <slot>{{ displayMessage }}</slot>
        </span>
      </div>
    </div>
    <div v-if="showRetry || showDismiss" class="alert-actions">
      <button
        v-if="showRetry"
        type="button"
        class="btn-alert-action btn-retry"
        data-testid="modal-error-retry-btn"
        @click="handleRetry"
      >
        {{ retryLabel }}
      </button>
      <button
        v-if="showDismiss"
        type="button"
        class="btn-alert-action btn-dismiss"
        data-testid="modal-error-dismiss-btn"
        @click="handleDismiss"
      >
        {{ dismissLabel }}
      </button>
    </div>
  </div>
</template>

<style scoped>
.modal-error-alert {
  display: flex;
  flex-direction: column;
  gap: 10px;
  padding: 12px 16px;
  background-color: var(--semantic-color-error-bg-soft);
  border: 1px solid var(--semantic-color-error-border-soft);
  border-radius: var(--radius-sm);
  color: var(--semantic-color-error-text-dark);
  font-size: 0.875rem;
  line-height: 1.4;
  margin-top: 10px;
  margin-bottom: 10px;
}

.modal-error-alert:focus {
  outline: 2px solid var(--semantic-color-error);
  outline-offset: 1px;
}

.alert-content {
  display: flex;
  align-items: flex-start;
  gap: 10px;
}

.error-icon {
  font-size: 1.1rem;
  flex-shrink: 0;
  line-height: 1.2;
}

.error-text-container {
  display: flex;
  flex-direction: column;
  gap: 2px;
  flex: 1;
  word-break: break-word;
}

.alert-title {
  font-weight: 600;
  color: var(--semantic-color-error-text-deep);
}

.alert-message {
  color: var(--semantic-color-error-text-dark);
}

.alert-actions {
  display: flex;
  align-items: center;
  justify-content: flex-end;
  gap: 8px;
  margin-top: 4px;
}

.btn-alert-action {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-height: var(--semantic-control-target-min, 24px);
  min-width: var(--semantic-control-target-min, 24px);
  padding: 4px 10px;
  font-size: 0.8rem;
  font-weight: 600;
  border-radius: var(--radius-xs);
  cursor: pointer;
  transition:
    background-color 0.15s ease,
    border-color 0.15s ease,
    color 0.15s ease;
  box-sizing: border-box;
}

.btn-retry {
  background-color: var(--semantic-color-error);
  color: var(--semantic-color-text-on-status);
  border: 1px solid var(--semantic-color-error);
}

.btn-retry:hover:not(:disabled) {
  background-color: var(--semantic-color-error-text-dark);
  border-color: var(--semantic-color-error-text-dark);
}

.btn-dismiss {
  background-color: var(--semantic-color-surface);
  color: var(--semantic-color-error-text-deep);
  border: 1px solid var(--semantic-color-error);
}

.btn-dismiss:hover:not(:disabled) {
  background-color: var(--semantic-color-error-bg);
}
</style>

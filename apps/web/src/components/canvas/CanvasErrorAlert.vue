<template>
  <div ref="alertRootRef" class="canvas-error-alert" role="alert">
    <p class="canvas-error-message">{{ message }}</p>
    <div v-if="showRetry || showExplain" class="canvas-error-actions">
      <button
        v-if="showRetry"
        type="button"
        class="canvas-error-btn"
        data-testid="canvas-error-retry"
        @click="handleRetryClick"
      >
        {{ retryLabel }}
      </button>
      <button
        v-if="showExplain"
        type="button"
        class="canvas-error-btn"
        data-testid="canvas-error-explain"
        @click="$emit('explain')"
      >
        {{ explainLabel }}
      </button>
    </div>
    <SupportDetails
      :detail="detail"
      :code="code"
      :reference="reference"
      :summary-label="summaryLabel"
      :detail-id="detailId"
    />
  </div>
</template>

<script setup lang="ts">
import { ref } from "vue";
import { handoffRetryFocus } from "ui";
import SupportDetails from "./SupportDetails.vue";

const props = withDefaults(
  defineProps<{
    message: string;
    detail?: string | null;
    code?: string | null;
    reference?: string | null;
    summaryLabel?: string;
    detailId?: string;
    retryLabel?: string;
    explainLabel?: string;
    showRetry?: boolean;
    showExplain?: boolean;
    onRetry?: () => unknown | Promise<unknown>;
  }>(),
  {
    detail: null,
    code: null,
    reference: null,
    summaryLabel: "Support details",
    detailId: undefined,
    retryLabel: "Retry",
    explainLabel: "Explain",
    showRetry: true,
    showExplain: true,
    onRetry: undefined,
  }
);

defineEmits<{
  (e: "explain"): void;
}>();

const alertRootRef = ref<HTMLElement | null>(null);

function handleRetryClick() {
  void handoffRetryFocus(() => props.onRetry?.(), {
    alertElement: alertRootRef,
    errorTargets: ['[data-testid="canvas-error-retry"]', ".btn-retry"],
  });
}
</script>

<style scoped>
.canvas-error-alert {
  display: grid;
  gap: var(--spacing-xs);
  padding: var(--spacing-sm);
  border: 1px solid var(--semantic-color-error-border);
  border-radius: var(--radius-sm);
  background: var(--semantic-color-error-bg-soft);
  color: var(--semantic-color-text);
}

.canvas-error-message {
  margin: 0;
  font-size: var(--font-size-sm);
  line-height: 1.45;
  color: var(--semantic-color-error-text-dark);
}

.canvas-error-actions {
  display: flex;
  flex-wrap: wrap;
  gap: var(--spacing-xs);
  align-items: center;
}

.canvas-error-btn {
  padding: var(--spacing-2xs) var(--spacing-sm);
  border-radius: var(--radius-xs);
  border: 1px solid var(--semantic-color-border-muted);
  background: var(--semantic-color-surface);
  color: var(--semantic-color-text);
  font: inherit;
  font-size: var(--font-size-xs);
  font-weight: 600;
  cursor: pointer;
}

.canvas-error-btn:hover {
  background: var(--semantic-color-surface-subtle);
}

.canvas-error-btn:focus-visible {
  outline: 2px solid var(--semantic-color-accent);
  outline-offset: 2px;
}
</style>

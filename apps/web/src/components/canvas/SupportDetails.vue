<template>
  <details v-if="hasDetails" class="support-details">
    <summary class="support-details-summary">{{ summaryLabel }}</summary>
    <div class="support-details-body">
      <p v-if="detail" class="support-details-text">{{ detail }}</p>
      <p v-if="code" class="support-details-code">
        Diagnostic code: <code>{{ code }}</code>
      </p>
      <p v-if="reference" class="support-details-code">
        Reference: <code>{{ reference }}</code>
      </p>
    </div>
  </details>
</template>

<script setup lang="ts">
import { computed } from "vue";

const props = withDefaults(
  defineProps<{
    detail?: string | null;
    code?: string | null;
    reference?: string | null;
    summaryLabel?: string;
  }>(),
  {
    detail: null,
    code: null,
    reference: null,
    summaryLabel: "Support details",
  }
);

const hasDetails = computed(() =>
  Boolean(props.detail?.trim() || props.code?.trim() || props.reference?.trim())
);
</script>

<style scoped>
.support-details {
  margin-top: 6px;
  font-size: 0.8125rem;
  color: var(--semantic-color-text-muted);
}

.support-details-summary {
  cursor: pointer;
  display: inline-flex;
  align-items: center;
  min-height: var(--surface-control-min-height, 44px);
  min-width: var(--surface-control-min-width, 44px);
  padding: 4px 2px;
  font-weight: 500;
  color: var(--semantic-color-text-muted);
}

.support-details-summary:focus-visible {
  outline: 2px solid var(--semantic-color-accent);
  outline-offset: 2px;
  border-radius: 3px;
}

.support-details-body {
  margin-top: 4px;
  padding: 6px 10px;
  border-left: 2px solid var(--semantic-color-border-muted);
  background: var(--semantic-color-surface-subtle);
  border-radius: 0 4px 4px 0;
}

.support-details-text,
.support-details-code {
  margin: 2px 0;
  word-break: break-word;
}

.support-details-code code {
  font-family: var(--font-mono);
  font-size: 0.75rem;
}
</style>

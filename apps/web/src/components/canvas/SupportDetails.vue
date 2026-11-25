<template>
  <details v-if="hasDetails" class="support-details">
    <summary class="support-details-summary">{{ summaryLabel }}</summary>
    <div class="support-details-body">
      <p v-if="detail" :id="detailId" class="support-details-text">
        {{ detail }}
      </p>
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
    detailId?: string;
  }>(),
  {
    detail: null,
    code: null,
    reference: null,
    summaryLabel: "Support details",
    detailId: undefined,
  }
);

const hasDetails = computed(() =>
  Boolean(props.detail?.trim() || props.code?.trim() || props.reference?.trim())
);
</script>

<style scoped>
.support-details {
  margin-top: 6px;
  font-size: var(--font-size-xs);
  color: var(--semantic-color-text-muted);
}

.support-details-summary {
  cursor: pointer;
  display: inline-flex;
  align-items: center;
  min-height: var(--semantic-control-target-min, 24px);
  padding: 3px 2px;
  font-weight: 500;
  color: var(--semantic-color-text-muted);
  border-radius: 3px;
  box-sizing: border-box;
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
  font-size: var(--font-size-2xs);
}
</style>

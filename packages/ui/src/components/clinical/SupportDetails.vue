<script setup>
import { computed, useSlots } from "vue";

const props = defineProps({
  detail: {
    type: String,
    default: null,
  },
  code: {
    type: String,
    default: null,
  },
  reference: {
    type: String,
    default: null,
  },
  entries: {
    type: Array,
    default: () => [],
  },
  summaryLabel: {
    type: String,
    default: "Support details",
  },
  detailId: {
    type: String,
    default: undefined,
  },
});

const slots = useSlots();

const normalizedEntries = computed(() =>
  (Array.isArray(props.entries) ? props.entries : []).filter(
    (item) =>
      item &&
      item.value !== null &&
      item.value !== undefined &&
      String(item.value).trim() !== ""
  )
);

const hasDetails = computed(() =>
  Boolean(
    (props.detail && String(props.detail).trim()) ||
    (props.code && String(props.code).trim()) ||
    (props.reference && String(props.reference).trim()) ||
    normalizedEntries.value.length > 0 ||
    slots.default
  )
);
</script>

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
      <dl
        v-if="normalizedEntries.length > 0"
        class="support-details-list"
        aria-label="Technical support metadata"
      >
        <div
          v-for="item in normalizedEntries"
          :key="item.key || item.label"
          class="support-details-row"
        >
          <dt class="support-details-term">{{ item.label }}</dt>
          <dd class="support-details-value">
            <code
              v-if="item.mono !== false"
              :class="
                item.key === 'hash' || item.key === 'sha256_hash'
                  ? 'hash-code'
                  : undefined
              "
              :title="String(item.value)"
              >{{ item.value }}</code
            >
            <span v-else>{{ item.value }}</span>
          </dd>
        </div>
      </dl>
      <slot />
    </div>
  </details>
</template>

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
  gap: 6px;
  font-weight: 500;
  color: var(--semantic-color-text-muted);
  user-select: none;
}

.support-details-summary:focus-visible {
  outline: 2px solid var(--semantic-color-accent, var(--semantic-color-primary));
  outline-offset: 2px;
  border-radius: 3px;
}

.support-details-body {
  margin-top: 6px;
  padding: 8px 10px;
  border-left: 2px solid
    var(--semantic-color-border-muted, var(--semantic-color-border));
  background: var(
    --semantic-color-surface-subtle,
    var(--semantic-color-surface-muted)
  );
  border-radius: 0 4px 4px 0;
}

.support-details-text,
.support-details-code {
  margin: 2px 0;
  word-break: break-word;
}

.support-details-code code,
.support-details-value code {
  font-family: var(--font-mono, ui-monospace, SFMono-Regular, Menlo, monospace);
  font-size: 0.75rem;
  word-break: break-all;
}

.support-details-list {
  margin: 0;
  display: grid;
  gap: 4px;
}

.support-details-row {
  display: grid;
  grid-template-columns: minmax(110px, auto) 1fr;
  gap: 8px;
  align-items: baseline;
}

.support-details-term {
  font-weight: 600;
  font-size: 0.75rem;
  color: var(--semantic-color-text-muted);
}

.support-details-value {
  margin: 0;
  color: var(--semantic-color-text);
}
</style>

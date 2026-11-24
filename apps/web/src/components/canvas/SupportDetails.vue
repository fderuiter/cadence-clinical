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
      <dl v-if="normalizedEntries.length" class="support-details-list">
        <div
          v-for="item in normalizedEntries"
          :key="item.key || item.label"
          class="support-details-row"
        >
          <dt class="support-details-term">{{ item.label }}</dt>
          <dd class="support-details-value">
            <code
              v-if="item.mono !== false"
              :class="{ 'hash-code': isHashEntry(item) }"
              :title="isHashEntry(item) ? String(item.value) : undefined"
              >{{ item.value }}</code
            >
            <span v-else>{{ item.value }}</span>
          </dd>
        </div>
      </dl>
    </div>
  </details>
</template>

<script setup lang="ts">
import { computed } from "vue";

export interface SupportDetailEntry {
  key?: string;
  label: string;
  value: string | number | null | undefined;
  mono?: boolean;
}

const props = withDefaults(
  defineProps<{
    detail?: string | null;
    code?: string | null;
    reference?: string | null;
    entries?: SupportDetailEntry[];
    summaryLabel?: string;
    detailId?: string;
  }>(),
  {
    detail: null,
    code: null,
    reference: null,
    entries: () => [],
    summaryLabel: "Support details",
    detailId: undefined,
  }
);

const normalizedEntries = computed(() =>
  (props.entries ?? []).filter(
    (entry) =>
      entry &&
      entry.value !== null &&
      entry.value !== undefined &&
      String(entry.value).trim() !== ""
  )
);

function isHashEntry(item: SupportDetailEntry): boolean {
  const key = String(item?.key ?? "").toLowerCase();
  const label = String(item?.label ?? "").toLowerCase();
  const val = String(item?.value ?? "");
  return (
    key.includes("hash") || label.includes("hash") || val.startsWith("sha256:")
  );
}

const hasDetails = computed(() =>
  Boolean(
    props.detail?.trim() ||
    props.code?.trim() ||
    props.reference?.trim() ||
    normalizedEntries.value.length > 0
  )
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
  display: inline-block;
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
  font-size: 0.75rem;
}
</style>

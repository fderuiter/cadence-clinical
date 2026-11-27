<template>
  <li :id="`record-form-${summary.form_id}`" class="forms-item">
    <div class="forms-item-main">
      <component
        :is="linkComponent"
        :id="`record-form-link-${summary.form_id}`"
        class="forms-link"
        v-bind="linkBindings"
        @click="emit('select', $event)"
      >
        {{ title }}
      </component>
      <span
        v-for="badge in badges"
        :key="badge.key"
        :class="badge.classes"
        :data-complete="badge.dataComplete"
        :data-testid="badge.testId"
      >
        {{ badge.label }}
      </span>
    </div>
    <span class="forms-meta">
      Version {{ summary.version }} · Collected {{ summary.collected_at }}
    </span>
  </li>
</template>

<script setup lang="ts">
import type { EcrfFormSummary } from "../../api/ecrfFormsApi";

export interface FormStatusBadgeView {
  key: string;
  classes: string;
  label: string;
  dataComplete?: "true" | "false";
  testId?: string;
}

defineProps<{
  summary: EcrfFormSummary;
  title: string;
  linkComponent: unknown;
  linkBindings: Record<string, unknown>;
  badges: FormStatusBadgeView[];
}>();

const emit = defineEmits<{
  (e: "select", event: MouseEvent): void;
}>();
</script>

<style scoped>
.cb-badge,
.forms-meta {
  font-variant-numeric: tabular-nums;
}

.forms-item {
  display: flex;
  flex-direction: column;
  gap: 4px;
  padding: 10px 12px;
  border: 1px solid var(--semantic-color-border);
  border-radius: 8px;
  background: var(--semantic-color-surface);
  font-size: 0.85rem;
  min-width: 0;
  max-width: 100%;
  box-sizing: border-box;
  overflow-wrap: anywhere;
}

.forms-item-main {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
}

.forms-link {
  position: relative;
  font-weight: 600;
  color: var(--semantic-color-interactive-primary);
  text-decoration: underline;
}

.forms-meta {
  color: var(--semantic-color-text-secondary);
  font-size: 0.78rem;
}
</style>

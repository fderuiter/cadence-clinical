<template>
  <ClinicalFieldLayout
    :id="id"
    :label="label"
    :query="query"
    :grid-span="gridSpan"
    :error="error"
    :can-manage-queries="canManageQueries"
    :query-label="queryLabel"
    tag="div"
    extra-class="clinical-lookup-container"
  >
    <template #default="{ id: slotId, errorId, hasError }">
      <input
        :id="slotId"
        type="text"
        :name="slotId"
        :value="modelValue"
        autocomplete="off"
        v-bind="attributes"
        :aria-describedby="
          [
            status !== 'none' ? `lookup-status-${id}` : '',
            hasError ? errorId : '',
          ]
            .filter(Boolean)
            .join(' ') || undefined
        "
        :aria-invalid="status === 'invalid' || hasError ? 'true' : undefined"
        @input="
          $emit('update:modelValue', $event.target.value);
          $emit('input', $event.target.value);
        "
        @change="$emit('change', $event.target.value, $event.target)"
      />
    </template>

    <template #after-input>
      <!-- Lookup Status Indicator -->
      <div
        v-if="status !== 'none'"
        :id="`lookup-status-${id}`"
        class="lookup-status-indicator"
        :class="stateClass"
        role="status"
        aria-live="polite"
      >
        <span class="lookup-status-icon" aria-hidden="true">{{
          statusIcon
        }}</span>
        <span class="lookup-status-text">{{ ariaLiveMessage }}</span>
      </div>
      <div
        v-else
        :id="`lookup-status-${id}`"
        class="lookup-status-indicator"
        role="status"
        aria-live="polite"
        style="display: none"
      ></div>
    </template>
  </ClinicalFieldLayout>
</template>

<script setup lang="ts">
import { computed } from "vue";
import ClinicalFieldLayout from "./ClinicalFieldLayout.vue";

interface Props {
  // Status message explanation helper
  statusMessage?: string;
  // Active lookup status
  status?: "none" | "loading" | "valid" | "invalid" | "degraded";
  // Field identifier
  id: string;
  // Label for field
  label: string;
  // Active model value
  modelValue?: string | number;
  // Validation error string
  error?: string | null;
  // Grid width span
  gridSpan?: number | string;
  // Query details
  query?: any;
  // Custom element attributes mapping
  attributes?: Record<string, any>;
  // Query permission check
  canManageQueries?: boolean;
  // Formatted query status label
  queryLabel?: string;
}

const props = withDefaults(defineProps<Props>(), {
  statusMessage: "",
  status: "none",
  modelValue: "",
  error: null,
  gridSpan: 12,
  query: null,
  attributes: () => ({}),
  canManageQueries: false,
  queryLabel: "",
});

defineEmits<{
  (e: "update:modelValue", value: string | number): void;
  (e: "input", value: string | number): void;
  (e: "change", value: string | number, target: any): void;
}>();

const stateClass = computed(() => {
  if (props.status === "loading") return "lookup-loading";
  if (props.status === "valid") return "lookup-valid";
  if (props.status === "invalid") return "lookup-invalid";
  if (props.status === "degraded") return "lookup-degraded";
  return "";
});

const statusIcon = computed(() => {
  if (props.status === "loading") return "⏳";
  if (props.status === "valid") return "✅";
  if (props.status === "invalid") return "❌";
  if (props.status === "degraded") return "⚠️";
  return "";
});

const ariaLiveMessage = computed(() => {
  if (props.statusMessage) return props.statusMessage;
  if (props.status === "loading") return "Searching terminology database...";
  if (props.status === "valid") return "Code is valid.";
  if (props.status === "invalid")
    return "Invalid code. Please check and try again.";
  if (props.status === "degraded")
    return "Terminology service degraded. Validation offline.";
  return "";
});
</script>

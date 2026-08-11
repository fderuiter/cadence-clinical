<template>
  <ClinicalFieldLayout
    :id="id"
    :label="label"
    :query="query"
    :grid-span="gridSpan"
    :error="error"
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

<script setup>
import { computed } from "vue";
import ClinicalFieldLayout from "./ClinicalFieldLayout.vue";

const props = defineProps({
  // Status message explanation helper
  statusMessage: {
    type: String,
    default: "",
  },
  // Active lookup status
  status: {
    type: String,
    default: "none", // 'none', 'loading', 'valid', 'invalid', 'degraded'
  },
  // Field identifier
  id: {
    type: String,
    required: true,
  },
  // Label for field
  label: {
    type: String,
    required: true,
  },
  // Active model value
  modelValue: {
    type: [String, Number],
    default: "",
  },
  // Validation error string
  error: {
    type: String,
    default: null,
  },
  // Grid width span
  gridSpan: {
    type: [Number, String],
    default: 12,
  },
  // Query details
  query: {
    type: Object,
    default: null,
  },
  // Custom element attributes mapping
  attributes: {
    type: Object,
    default: () => ({}),
  },
});

defineEmits(["update:modelValue", "input", "change"]);

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

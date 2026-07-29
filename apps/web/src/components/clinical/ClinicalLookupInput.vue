<template>
  <div
    :id="`field-container-${id}`"
    class="clinical-input clinical-lookup-container"
    :class="{ 'has-error': showError, [`grid-span-${gridSpan}`]: true }"
    :style="`grid-column: span ${gridSpan};`"
    v-bind="attributes"
  >
    <label :for="id">{{ label }}</label>
    <div class="input-wrapper">
      <input
        :id="id"
        type="text"
        :name="id"
        :value="modelValue"
        autocomplete="off"
        @input="$emit('update:modelValue', $event.target.value); $emit('input', $event.target.value)"
        @change="$emit('change', $event.target.value, $event.target)"
      />

      <!-- Query Flag -->
      <ClinicalQueryFlag
        :id="id"
        :query="query"
        :is-open="isQueryOpen"
        @click="isQueryOpen = !isQueryOpen"
      />
    </div>

    <!-- Lookup Status Indicator -->
    <div
      v-if="status !== 'none'"
      :id="`lookup-status-${id}`"
      class="lookup-status-indicator"
      :class="stateClass"
      role="status"
      aria-live="polite"
    >
      <span class="lookup-status-icon" aria-hidden="true">{{ statusIcon }}</span>
      <span class="lookup-status-text">{{ ariaLiveMessage }}</span>
    </div>
    <div
      v-else
      :id="`lookup-status-${id}`"
      class="lookup-status-indicator"
      role="status"
      aria-live="polite"
      style="display: none;"
    ></div>

    <!-- Validation Error -->
    <div v-if="showError" class="validation-error-msg">
      {{ error }}
    </div>

    <!-- Query Panel -->
    <ClinicalQueryPanel
      v-if="isQueryOpen"
      :id="id"
      :query="query"
      @close-panel="isQueryOpen = false"
      @create-query="$emit('create-query', $event)"
      @respond-query="$emit('respond-query', $event)"
      @close-query="$emit('close-query')"
      @reopen-query="$emit('reopen-query')"
    />
  </div>
</template>

<script setup>
import { ref, computed } from "vue";
import ClinicalQueryFlag from "./ClinicalQueryFlag.vue";
import ClinicalQueryPanel from "./ClinicalQueryPanel.vue";

const props = defineProps({
  id: {
    type: String,
    required: true,
  },
  label: {
    type: String,
    required: true,
  },
  modelValue: {
    type: [String, Number],
    default: "",
  },
  query: {
    type: Object,
    default: null,
  },
  gridSpan: {
    type: [Number, String],
    default: 12,
  },
  error: {
    type: String,
    default: null,
  },
  status: {
    type: String,
    default: "none", // 'none', 'loading', 'valid', 'invalid', 'degraded'
  },
  statusMessage: {
    type: String,
    default: "",
  },
  attributes: {
    type: Object,
    default: () => ({}),
  },
});

defineEmits([
  "update:modelValue",
  "input",
  "change",
  "create-query",
  "respond-query",
  "close-query",
  "reopen-query",
]);

const isQueryOpen = ref(false);

const showError = computed(() => {
  return props.error && props.modelValue !== "";
});

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
  if (props.status === "invalid") return "Invalid code. Please check and try again.";
  if (props.status === "degraded") return "Terminology service degraded. Validation offline.";
  return "";
});
</script>

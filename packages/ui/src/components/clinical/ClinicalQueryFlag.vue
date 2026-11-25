<template>
  <button
    :id="`query-flag-${id}`"
    class="query-flag"
    :class="`query-status-${statusClass}`"
    type="button"
    :aria-expanded="isOpen ? 'true' : 'false'"
    :aria-controls="`query-panel-${id}`"
    :aria-label="ariaLabel"
    @click="$emit('click')"
  >
    <component
      :is="iconComponent"
      :size="16"
      :stroke-width="1.75"
      aria-hidden="true"
      focusable="false"
      class="clinical-icon"
    />
  </button>
</template>

<script setup>
import { computed } from "vue";
import { AlertTriangle, MessageSquare } from "lucide-vue-next";

const props = defineProps({
  id: {
    type: String,
    required: true,
  },
  query: {
    type: Object,
    default: null,
  },
  isOpen: {
    type: Boolean,
    default: false,
  },
  fieldLabel: {
    type: String,
    default: "",
  },
});

defineEmits(["click"]);

const statusClass = computed(() => {
  const status =
    props.query && props.query.status
      ? props.query.status.toUpperCase()
      : "NONE";
  return status.toLowerCase();
});

const ariaLabel = computed(() => {
  const status =
    props.query && props.query.status
      ? props.query.status.toUpperCase()
      : "NONE";
  const cleanField = props.fieldLabel ? props.fieldLabel.trim() : "";
  if (status === "NONE") {
    return cleanField
      ? `No active queries for ${cleanField}. Click to create.`
      : "No active queries. Click to create.";
  }
  return cleanField
    ? `Query status for ${cleanField}: ${status}`
    : `Query status: ${status}`;
});

const iconComponent = computed(() => {
  const status =
    props.query && props.query.status
      ? props.query.status.toUpperCase()
      : "NONE";
  return status === "NONE" ? MessageSquare : AlertTriangle;
});
</script>

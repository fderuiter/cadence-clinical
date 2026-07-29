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
    {{ icon }}
  </button>
</template>

<script setup>
import { computed } from "vue";

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
});

defineEmits(["click"]);

const statusClass = computed(() => {
  const status = props.query && props.query.status ? props.query.status.toUpperCase() : "NONE";
  return status.toLowerCase();
});

const ariaLabel = computed(() => {
  const status = props.query && props.query.status ? props.query.status.toUpperCase() : "NONE";
  return status === "NONE"
    ? "No active queries. Click to create."
    : `Query status: ${status}`;
});

const icon = computed(() => {
  const status = props.query && props.query.status ? props.query.status.toUpperCase() : "NONE";
  return status === "NONE" ? "💬" : "⚠️";
});
</script>

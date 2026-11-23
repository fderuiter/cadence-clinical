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
    <span class="query-flag-glyph" aria-hidden="true">
      <MessageSquare
        v-if="isNoneStatus"
        :size="16"
        :stroke-width="1.75"
        aria-hidden="true"
      />
      <AlertTriangle
        v-else
        :size="16"
        :stroke-width="1.75"
        aria-hidden="true"
      />
    </span>
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

const normalizedStatus = computed(() => {
  return props.query && props.query.status
    ? props.query.status.toUpperCase()
    : "NONE";
});

const isNoneStatus = computed(() => normalizedStatus.value === "NONE");

const statusClass = computed(() => normalizedStatus.value.toLowerCase());

const ariaLabel = computed(() => {
  const status = normalizedStatus.value;
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
</script>

<style scoped>
.query-flag {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-width: var(--surface-control-min-width, 44px);
  min-height: var(--surface-control-min-height, 44px);
  flex-shrink: 0;
}

.query-flag-glyph {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  line-height: 1;
}
</style>

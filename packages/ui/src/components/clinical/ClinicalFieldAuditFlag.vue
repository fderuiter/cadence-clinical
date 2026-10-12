<template>
  <button
    :id="`audit-flag-${id}`"
    class="audit-flag"
    type="button"
    :aria-expanded="isOpen ? 'true' : 'false'"
    :aria-controls="`audit-modal-${id}`"
    :aria-label="ariaLabel"
    :title="ariaLabel"
    @click="handleClick"
  >
    <span class="audit-flag-icon" aria-hidden="true">📜</span>
  </button>
</template>

<script setup>
import { computed } from "vue";

const props = defineProps({
  id: {
    type: String,
    required: true,
  },
  isOpen: {
    type: Boolean,
    default: false,
  },
  hasHistory: {
    type: Boolean,
    default: true,
  },
});

const emit = defineEmits(["click", "open-audit"]);

const ariaLabel = computed(() => {
  return `View audit history for ${props.id}`;
});

function handleClick(event) {
  emit("click", event);
  emit("open-audit", props.id);
}
</script>

<style scoped>
.audit-flag {
  border: none;
  background: transparent;
  font-size: 1rem;
  cursor: pointer;
  padding: 4px;
  border-radius: 4px;
  transition: background-color 0.2s ease, opacity 0.2s ease;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  line-height: 1;
}

.audit-flag:hover {
  background-color: var(--color-surface-muted, #f1f5f9);
}

.audit-flag:focus-visible {
  outline: 2px solid var(--color-primary, #026597);
  outline-offset: 1px;
}
</style>

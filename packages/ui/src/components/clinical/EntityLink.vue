<template>
  <span
    v-if="parsed.masked"
    class="entity-link entity-link--masked"
    :data-node-kind="parsed.kind"
    :data-mask-reason="parsed.maskReason"
  >
    <EyeOff :size="16" :stroke-width="1.75" aria-hidden="true" class="entity-link__icon" />
    <span class="entity-link__label">{ label || parsed.displayLabel }</span>
  </span>
  <button
    v-else
    type="button"
    class="entity-link entity-link--interactive"
    :class="{ 'entity-link--compact': compact }"
    :data-node-ref="parsed.raw"
    :data-node-kind="parsed.kind"
    :aria-label="`Inspect ${resolvedLabel}`"
    @click="handleClick"
  >
    <ExternalLink :size="16" :stroke-width="1.75" aria-hidden="true" class="entity-link__icon" />
    <span class="entity-link__label">{ resolvedLabel }</span>
    <span v-if="showRefCode" class="entity-link__code">{ parsed.shortRef }</span>
  </button>
</template>

<script setup>
import { computed } from "vue";
import { ExternalLink, EyeOff } from "lucide-vue-next";
import { openEntityPeek, parseNodeRef } from "../../utils/entityPeekRegistry.js";

const props = defineProps({
  nodeRef: {
    type: String,
    required: true,
  },
  label: {
    type: String,
    default: "",
  },
  compact: {
    type: Boolean,
    default: false,
  },
  showCode: {
    type: Boolean,
    default: true,
  },
});

const emit = defineEmits(["peek", "select"]);

const parsed = computed(() => parseNodeRef(props.nodeRef));
const resolvedLabel = computed(() => props.label || parsed.value.displayLabel);
const showRefCode = computed(
  () => props.showCode && Boolean(parsed.value.shortRef) && parsed.value.shortRef !== resolvedLabel.value
);

function handleClick(event) {
  if (parsed.value.masked) {
    return;
  }
  const payload = {
    nodeRef: parsed.value.raw,
    label: resolvedLabel.value,
    parsed: parsed.value,
    originalEvent: event,
  };
  openEntityPeek(parsed.value.raw, { label: resolvedLabel.value });
  emit("peek", payload);
  emit("select", parsed.value.raw);
}
</script>

<style scoped>
.entity-link {
  display: inline-flex;
  align-items: center;
  gap: 0.375rem;
  min-height: 2.75rem;
  padding: 0.25rem 0.625rem;
  border-radius: 0.375rem;
  border: 1px solid var(--color-border);
  background: transparent;
  color: var(--color-text);
  font-family: var(--font-sans);
  font-size: 0.8125rem;
  line-height: 1.25;
  text-align: left;
  vertical-align: middle;
}

.entity-link--compact {
  min-height: 2.75rem;
  padding: 0.25rem 0.5rem;
}

.entity-link--interactive {
  cursor: pointer;
  transition: border-color 120ms ease;
}

.entity-link--interactive:hover,
.entity-link--interactive:focus-visible {
  border-color: var(--color-primary);
  outline: none;
}

.entity-link--masked {
  color: var(--text-muted);
  cursor: default;
}

.entity-link__icon {
  flex-shrink: 0;
}

.entity-link__label {
  font-weight: 500;
}

.entity-link__code {
  font-family: var(--font-mono);
  font-size: 0.75rem;
  color: var(--text-muted);
}
</style>

<template>
  <span class="ai-label" :title="title">
    <span class="ai-mark" aria-hidden="true">AI</span>
    <span class="ai-text">{{ text }}</span>
  </span>
</template>

<script setup lang="ts">
import { computed } from "vue";

/**
 * The AI label (UX-8, ADR-2635): every place AI output appears carries it, in the colour reserved for AI and with the
 * word "AI", so it never relies on colour alone. It names the model when the kernel recorded one (K-PROV-3) and the
 * task configuration once proposals record theirs (K-AI-5, #5385).
 */
const props = defineProps<{
  /** What the AI did, for example "Proposed by an AI agent". */
  label?: string;
  /** The model the kernel recorded for this output. */
  model?: string | null;
  /** The task configuration version, once recorded. */
  configuration?: string | null;
}>();

const text = computed(() => {
  const parts = [props.label ?? "Produced by an AI agent"];
  if (props.model) parts.push(props.model);
  if (props.configuration) parts.push(`configuration ${props.configuration}`);
  return parts.join(" · ");
});

const title = computed(
  () =>
    `${text.value}. A person reviews AI output before it changes anything in the study.`
);
</script>

<style scoped>
.ai-label {
  display: inline-flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 4px 6px;
  min-width: 0;
  max-width: 100%;
  box-sizing: border-box;
  padding: 1px 8px 1px 2px;
  border: 1px solid var(--semantic-color-ai-border);
  border-radius: 12px;
  background: var(--semantic-color-ai-bg);
  color: var(--semantic-color-ai);
  font-size: var(--font-size-2xs);
  line-height: 1.6;
}

.ai-mark {
  flex-shrink: 0;
  padding: 0 6px;
  border-radius: 999px;
  background: var(--semantic-color-ai);
  color: var(--semantic-color-surface);
  font-weight: 700;
  letter-spacing: 0.04em;
}

.ai-text {
  min-width: 0;
  max-width: 100%;
  overflow-wrap: anywhere;
}
</style>

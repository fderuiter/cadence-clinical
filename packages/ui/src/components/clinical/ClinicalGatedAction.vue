<template>
  <div
    class="clinical-gated-action"
    :class="{ 'clinical-gated-action--blocked': !permitted }"
    :data-gated-state="permitted ? 'permitted' : 'blocked'"
  >
    <div class="clinical-gated-action__controls">
      <slot
        :disabled="!permitted"
        :permitted="permitted"
        :aria-disabled="permitted ? undefined : 'true'"
        :explanation-id="permitted ? undefined : explanationDomId"
      >
        <button
          v-if="label"
          type="button"
          class="clinical-gated-action__fallback-btn"
          :disabled="!permitted"
          :aria-disabled="permitted ? undefined : 'true'"
          :aria-describedby="!permitted ? explanationDomId : undefined"
          @click="permitted && $emit('action')"
        >
          <Lock
            v-if="!permitted"
            :size="16"
            :stroke-width="1.75"
            aria-hidden="true"
          />
          <span>{{ label }}</span>
        </button>
      </slot>

      <button
        v-if="!permitted && !inline && hasExplanation"
        type="button"
        class="clinical-gated-action__why-btn"
        :aria-expanded="String(expanded)"
        :aria-controls="explanationDomId"
        @click="expanded = !expanded"
      >
        <Lock :size="16" :stroke-width="1.75" aria-hidden="true" />
        <span>{{ whyButtonLabel }}</span>
        <ChevronDown
          v-if="expanded"
          :size="16"
          :stroke-width="1.75"
          aria-hidden="true"
        />
        <ChevronRight
          v-else
          :size="16"
          :stroke-width="1.75"
          aria-hidden="true"
        />
      </button>
    </div>

    <div
      v-if="!permitted && hasExplanation && (inline || expanded)"
      :id="explanationDomId"
      class="clinical-gated-action__explanation"
    >
      <ClinicalExplanation
        :explanation="explanation"
        :fallback-reason="cleanReason"
        :code="code"
        compact
        @peek="$emit('peek', $event)"
      />
    </div>
  </div>
</template>

<script setup>
import { computed, ref } from "vue";
import { ChevronDown, ChevronRight, Lock } from "lucide-vue-next";
import ClinicalExplanation from "./ClinicalExplanation.vue";
import { stripRawPermissionTokens } from "../../utils/entityPeekRegistry.js";

let nextInstanceId = 0;

const props = defineProps({
  permitted: {
    type: Boolean,
    default: true,
  },
  explanation: {
    type: Object,
    default: null,
  },
  reason: {
    type: String,
    default: "",
  },
  code: {
    type: String,
    default: "",
  },
  label: {
    type: String,
    default: "",
  },
  whyLabel: {
    type: String,
    default: "Why blocked",
  },
  inline: {
    type: Boolean,
    default: false,
  },
  defaultExpanded: {
    type: Boolean,
    default: false,
  },
});

defineEmits(["action", "peek"]);

nextInstanceId += 1;
const explanationDomId = `clinical-gated-exp-${nextInstanceId}`;
const expanded = ref(props.defaultExpanded);

const cleanReason = computed(() =>
  stripRawPermissionTokens(
    props.reason ||
      props.explanation?.claim ||
      "This clinical action is currently unavailable."
  )
);

const whyButtonLabel = computed(() => props.whyLabel || "Why blocked");

const hasExplanation = computed(
  () => Boolean(props.explanation) || Boolean(cleanReason.value)
);
</script>

<style scoped>
.clinical-gated-action {
  display: inline-flex;
  flex-direction: column;
  gap: 0.5rem;
  max-width: 100%;
}

.clinical-gated-action__controls {
  display: inline-flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 0.5rem;
}

.clinical-gated-action__fallback-btn,
.clinical-gated-action__why-btn {
  display: inline-flex;
  align-items: center;
  gap: 0.375rem;
  min-height: 2.75rem;
  padding: 0.375rem 0.75rem;
  border-radius: 0.375rem;
  border: 1px solid var(--color-border-control);
  background: transparent;
  color: var(--color-text);
  font-family: var(--font-sans);
  font-size: var(--font-size-xs);
  font-weight: 500;
  cursor: pointer;
}

.clinical-gated-action__fallback-btn:disabled {
  opacity: 0.65;
  cursor: not-allowed;
}

.clinical-gated-action__explanation {
  min-width: min(22rem, 100%);
}
</style>

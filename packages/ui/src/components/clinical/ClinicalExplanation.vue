<template>
  <section
    v-if="hasContent"
    class="clinical-explanation"
    :class="{ 'clinical-explanation--compact': compact }"
    aria-live="polite"
  >
    <div class="clinical-explanation__header">
      <ShieldAlert :size="16" :stroke-width="1.75" aria-hidden="true" class="clinical-explanation__lead-icon" />
      <p class="clinical-explanation__claim">{ displayClaim }</p>
      <span v-if="metaBadge" class="clinical-explanation__badge">{ metaBadge }</span>
    </div>

    <div v-if="subjectRef" class="clinical-explanation__subject">
      <span class="clinical-explanation__caption">Governing record:</span>
      <EntityLink :node-ref="subjectRef" compact @peek="$emit('peek', $event)" />
    </div>

    <ul v-if="conditionsList.length > 0" class="clinical-explanation__conditions" aria-label="Evaluated conditions">
      <li
        v-for="(cond, idx) in conditionsList"
        :key="`${cond.code || 'cond'}-${idx}`"
        class="clinical-explanation__condition"
        :data-condition-met="String(Boolean(cond.met))"
      >
        <CheckCircle2
          v-if="cond.met"
          :size="16"
          :stroke-width="1.75"
          aria-hidden="true"
          class="clinical-explanation__status-icon clinical-explanation__status-icon--met"
        />
        <XCircle
          v-else
          :size="16"
          :stroke-width="1.75"
          aria-hidden="true"
          class="clinical-explanation__status-icon clinical-explanation__status-icon--unmet"
        />
        <div class="clinical-explanation__condition-body">
          <span class="clinical-explanation__condition-status">
            { cond.met ? "Met:" : "Required:" }
          </span>
          <span class="clinical-explanation__condition-text">{ cleanText(cond.description) }</span>
          <div v-if="cond.evidence && cond.evidence.length > 0" class="clinical-explanation__evidence">
            <EntityLink
              v-for="(ev, evIdx) in cond.evidence"
              :key="`${ev}-${evIdx}`"
              :node-ref="String(ev)"
              compact
              @peek="$emit('peek', $event)"
            />
          </div>
        </div>
      </li>
    </ul>

    <div v-if="nextActionsList.length > 0" class="clinical-explanation__next-actions">
      <span class="clinical-explanation__caption">How to resolve:</span>
      <ul class="clinical-explanation__actions-list">
        <li
          v-for="(act, idx) in nextActionsList"
          :key="`next-${idx}`"
          class="clinical-explanation__action-item"
        >
          <span class="clinical-explanation__action-text">{ cleanText(act.description) }</span>
          <EntityLink
            v-if="act.target"
            :node-ref="String(act.target)"
            compact
            @peek="$emit('peek', $event)"
          />
        </li>
      </ul>
    </div>

    <details v-if="!compact && stepsList.length > 0" class="clinical-explanation__steps">
      <summary class="clinical-explanation__steps-summary">
        <Info :size="16" :stroke-width="1.75" aria-hidden="true" />
        <span>Rule evaluation steps ({ stepsList.length })</span>
      </summary>
      <ol class="clinical-explanation__steps-list">
        <li v-for="(step, idx) in stepsList" :key="`step-${idx}`" class="clinical-explanation__step-item">
          <span>{ cleanText(step.statement) }</span>
          <div v-if="step.evidence && step.evidence.length > 0" class="clinical-explanation__evidence">
            <EntityLink
              v-for="(ev, evIdx) in step.evidence"
              :key="`step-ev-${ev}-${evIdx}`"
              :node-ref="String(ev)"
              compact
              @peek="$emit('peek', $event)"
            />
          </div>
        </li>
      </ol>
    </details>
  </section>
</template>

<script setup>
import { computed } from "vue";
import { CheckCircle2, Info, ShieldAlert, XCircle } from "lucide-vue-next";
import EntityLink from "./EntityLink.vue";
import { stripRawPermissionTokens } from "../../utils/entityPeekRegistry.js";

const props = defineProps({
  explanation: {
    type: Object,
    default: null,
  },
  fallbackReason: {
    type: String,
    default: "",
  },
  code: {
    type: String,
    default: "",
  },
  compact: {
    type: Boolean,
    default: false,
  },
});

defineEmits(["peek"]);

function cleanText(val) {
  return stripRawPermissionTokens(val || "");
}

const displayClaim = computed(() => {
  const raw = props.explanation?.claim || props.fallbackReason || "";
  return cleanText(raw);
});

const subjectRef = computed(() => props.explanation?.subject || null);

const conditionsList = computed(() =>
  Array.isArray(props.explanation?.conditions) ? props.explanation.conditions : []
);

const stepsList = computed(() =>
  Array.isArray(props.explanation?.steps) ? props.explanation.steps : []
);

const nextActionsList = computed(() => {
  if (Array.isArray(props.explanation?.next_actions)) {
    return props.explanation.next_actions;
  }
  if (Array.isArray(props.explanation?.nextActions)) {
    return props.explanation.nextActions;
  }
  return [];
});

const metaBadge = computed(() => props.code || props.explanation?.producer || "");

const hasContent = computed(
  () =>
    Boolean(displayClaim.value) ||
    conditionsList.value.length > 0 ||
    nextActionsList.value.length > 0
);
</script>

<style scoped>
.clinical-explanation {
  display: flex;
  flex-direction: column;
  gap: 0.625rem;
  padding: 0.75rem;
  border: 1px solid var(--color-border);
  border-radius: 0.5rem;
  background: var(--color-surface);
  color: var(--color-text);
  font-family: var(--font-sans);
  font-size: 0.875rem;
}

.clinical-explanation--compact {
  padding: 0.625rem;
  gap: 0.5rem;
}

.clinical-explanation__header {
  display: flex;
  align-items: flex-start;
  gap: 0.5rem;
  flex-wrap: wrap;
}

.clinical-explanation__lead-icon {
  flex-shrink: 0;
  margin-top: 0.125rem;
}

.clinical-explanation__claim {
  margin: 0;
  flex: 1 1 12rem;
  font-weight: 600;
  line-height: 1.4;
}

.clinical-explanation__badge {
  font-family: var(--font-mono);
  font-size: 0.75rem;
  color: var(--text-muted);
  padding: 0.125rem 0.375rem;
  border: 1px solid var(--color-border);
  border-radius: 0.25rem;
}

.clinical-explanation__subject,
.clinical-explanation__evidence {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 0.375rem;
}

.clinical-explanation__caption {
  font-size: 0.75rem;
  font-weight: 600;
  color: var(--text-muted);
}

.clinical-explanation__conditions,
.clinical-explanation__actions-list,
.clinical-explanation__steps-list {
  margin: 0;
  padding: 0;
  list-style: none;
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.clinical-explanation__condition {
  display: flex;
  align-items: flex-start;
  gap: 0.5rem;
}

.clinical-explanation__status-icon {
  flex-shrink: 0;
  margin-top: 0.125rem;
}

.clinical-explanation__status-icon--met {
  color: var(--color-success);
}

.clinical-explanation__status-icon--unmet {
  color: currentColor;
}

.clinical-explanation__condition-body {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
}

.clinical-explanation__condition-status {
  font-weight: 600;
  margin-right: 0.25rem;
}

.clinical-explanation__action-item {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 0.5rem;
}

.clinical-explanation__steps-summary {
  display: inline-flex;
  align-items: center;
  gap: 0.375rem;
  min-height: 2.75rem;
  cursor: pointer;
  font-size: 0.8125rem;
  color: var(--text-muted);
}
</style>

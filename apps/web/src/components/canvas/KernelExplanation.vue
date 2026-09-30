<template>
  <div class="explanation">
    <p class="explanation-claim">{{ explanation.claim }}</p>
    <ol v-if="explanation.steps.length" class="explanation-steps">
      <li v-for="(step, index) in explanation.steps" :key="`step-${index}`">
        {{ step.claim }}
        <span v-if="step.evidence.length" class="explanation-evidence">
          <code v-for="ref in step.evidence" :key="ref">{{ ref }}</code>
        </span>
      </li>
    </ol>
    <template v-if="explanation.conditions.length">
      <h4 class="explanation-heading">Conditions checked</h4>
      <ul class="explanation-conditions">
        <li
          v-for="condition in explanation.conditions"
          :key="condition.code"
          :class="condition.met ? 'condition-met' : 'condition-unmet'"
        >
          <span class="condition-state">{{
            condition.met ? "Met" : "Not met"
          }}</span>
          {{ condition.description }}
          <span v-if="condition.evidence.length" class="explanation-evidence">
            <code v-for="ref in condition.evidence" :key="ref">{{ ref }}</code>
          </span>
        </li>
      </ul>
    </template>
    <template v-if="explanation.next_actions.length">
      <h4 class="explanation-heading">What would change this</h4>
      <ul class="explanation-next">
        <li
          v-for="(action, index) in explanation.next_actions"
          :key="`next-${index}`"
        >
          {{ action.description }}
        </li>
      </ul>
    </template>
  </div>
</template>

<script setup lang="ts">
import type { KernelExplanation } from "../../api/kernelApi";

/**
 * Renders one of the kernel's explanations (ADR-2482) as the kernel produced it: the claim, the chain of steps,
 * every condition checked with whether it held, and what would change the outcome. The Canvas never composes or
 * rewords an explanation (K-EXP-1); it only lays it out.
 */
defineProps<{ explanation: KernelExplanation }>();
</script>

<style scoped>
.explanation {
  display: flex;
  flex-direction: column;
  gap: 6px;
  font-size: 0.85rem;
}

.explanation-claim {
  margin: 0;
  font-weight: 600;
}

.explanation-heading {
  margin: 4px 0 0;
  font-size: 0.8rem;
  color: var(--neutral-dark);
}

.explanation-steps,
.explanation-conditions,
.explanation-next {
  margin: 0;
  padding-left: 18px;
}

.condition-state {
  font-weight: 600;
  margin-right: 4px;
}

.condition-unmet .condition-state {
  color: var(--error);
}

.explanation-evidence {
  display: inline-flex;
  flex-wrap: wrap;
  gap: 4px;
  margin-left: 4px;
}

.explanation-evidence code {
  font-size: 0.75rem;
  overflow-wrap: anywhere;
}
</style>

<template>
  <section
    id="inspector-state-change"
    class="state-change"
    aria-labelledby="inspector-state-change-title"
  >
    <h3 id="inspector-state-change-title">Change state</h3>
    <p v-if="!changes.targets.length" id="state-change-final" class="note">
      {{ finalNote }}
    </p>
    <template v-else>
      <label for="state-change-target">New state</label>
      <select id="state-change-target" v-model="target">
        <option value="" disabled>Choose a state</option>
        <option v-for="code in changes.targets" :key="code" :value="code">
          {{ participantStateLabel(code) }}
        </option>
      </select>
      <label for="state-change-reason">Reason</label>
      <textarea
        id="state-change-reason"
        v-model="reason"
        rows="2"
        placeholder="Why the state changes (recorded in the audit trail)"
      />
      <button
        id="btn-change-state"
        type="button"
        class="btn btn-secondary"
        :disabled="busy || !target || !reason.trim()"
        @click="submit"
      >
        {{
          target
            ? `Move to ${participantStateLabel(target).toLowerCase()}`
            : "Move"
        }}
      </button>
      <p v-if="target === 'ENROLLED'" id="state-change-enrol-note" class="note">
        Execution enrols a participant only at a site whose activation documents
        are complete.
      </p>
    </template>
    <p
      v-if="changes.unblindingWithheld"
      id="state-change-unblinding-note"
      class="note"
    >
      Unblinding isn't offered here: it needs its own unblinding workflow.
    </p>
  </section>
</template>

<script setup lang="ts">
import { computed, ref, watch } from "vue";
import type { SubjectSummary } from "../../api/executionApi";
import {
  participantStateChanges,
  participantStateLabel,
} from "../../canvas/lifecycle";
import { useStudyCanvasStore } from "../../stores/studyCanvas";

/**
 * Moves the inspected participant to another state. It offers the transitions Execution's graph allows from
 * the current state (less unblinding); Execution checks the transition again, gates enrolment on site
 * activation and records the reason.
 */
const props = defineProps<{ participant: SubjectSummary }>();

const canvas = useStudyCanvasStore();
const target = ref("");
const reason = ref("");
const busy = ref(false);

const changes = computed(() =>
  participantStateChanges(props.participant.status)
);
const finalNote = computed(
  () =>
    `${participantStateLabel(props.participant.status)} is a final state; it doesn't change.`
);

watch(
  () => [props.participant.id, props.participant.status],
  () => {
    target.value = "";
    reason.value = "";
  }
);

async function submit() {
  const state = target.value;
  const why = reason.value.trim();
  if (!state || !why) return;
  busy.value = true;
  try {
    await canvas.changeParticipantState(props.participant, state, why);
  } finally {
    busy.value = false;
  }
}
</script>

<style scoped>
.state-change {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.state-change h3 {
  margin: 0;
  font-size: 0.95rem;
}

.state-change select,
.state-change textarea {
  padding: 6px 8px;
  border: 1px solid var(--border);
  border-radius: 4px;
}

.note {
  margin: 0;
  color: var(--neutral-dark);
  font-size: 0.85rem;
}
</style>

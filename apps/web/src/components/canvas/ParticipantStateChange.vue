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

    <div v-if="canOpenRandomize" class="workflow-callout">
      <p class="note">
        Randomization assigns a treatment arm and kit through the study's
        approved randomization scheme.
      </p>
      <button
        id="btn-open-randomize-drawer"
        type="button"
        class="btn btn-primary"
        @click="randomizeOpen = true"
      >
        Randomize participant
      </button>
    </div>
    <RandomizeParticipantDrawer
      v-if="canManage && (participant.status === 'ENROLLED' || randomizeOpen)"
      :is-open="randomizeOpen"
      :study-code="studyCode"
      :participant="participant"
      :username="authStore.identity?.username ?? undefined"
      @close="randomizeOpen = false"
    />

    <div v-if="changes.unblindingWithheld" class="workflow-callout">
      <template v-if="canEmergencyUnblind">
        <p class="note">
          Emergency unblinding permanently breaks the treatment blind and
          requires a 50-character clinical justification.
        </p>
        <button
          id="btn-emergency-unblind"
          type="button"
          class="btn btn-danger-outline"
          @click="unblindOpen = true"
        >
          Emergency unblinding
        </button>
        <EmergencyUnblindModal
          :is-open="unblindOpen"
          :participant="participant"
          @close="unblindOpen = false"
        />
      </template>
      <p v-else id="state-change-unblinding-note" class="note">
        Emergency unblinding requires a Principal Investigator or Authorized ER
        Physician and a 50-character clinical justification.
      </p>
    </div>
  </section>
</template>

<script setup lang="ts">
import { computed, ref, watch } from "vue";
import type { SubjectSummary } from "../../api/executionApi";
import {
  canManageParticipants,
  canUnblindParticipant,
} from "../../canvas/authority";
import {
  participantStateChanges,
  participantStateLabel,
} from "../../canvas/lifecycle";
import { useAuthStore } from "../../stores/auth";
import { useStudyCanvasStore } from "../../stores/studyCanvas";
import EmergencyUnblindModal from "./EmergencyUnblindModal.vue";
import RandomizeParticipantDrawer from "./RandomizeParticipantDrawer.vue";

/**
 * Moves the inspected participant to another state. It offers the transitions Execution's graph allows from
 * the current state (less randomization and unblinding, which have dedicated signed/audited workflows).
 */
const props = defineProps<{ participant: SubjectSummary }>();

const authStore = useAuthStore();
const canvas = useStudyCanvasStore();
const target = ref("");
const reason = ref("");
const busy = ref(false);
const randomizeOpen = ref(false);
const unblindOpen = ref(false);

const studyCode = computed(
  () => canvas.studyCode || props.participant.study_id || ""
);
const changes = computed(() =>
  participantStateChanges(props.participant.status)
);
const canManage = computed(() => canManageParticipants(authStore));
const canOpenRandomize = computed(
  () => props.participant.status === "ENROLLED" && canManage.value
);
const canEmergencyUnblind = computed(() => canUnblindParticipant(authStore));
const finalNote = computed(
  () =>
    `${participantStateLabel(props.participant.status)} is a final state; it doesn't change.`
);

watch(
  () => props.participant.id,
  () => {
    randomizeOpen.value = false;
    unblindOpen.value = false;
  }
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

.workflow-callout {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  gap: 6px;
  padding-top: 6px;
  border-top: 1px solid var(--border);
}

.btn-danger-outline {
  background: var(--danger-bg);
  border: 1px solid var(--danger);
  color: var(--danger);
}

.note {
  margin: 0;
  color: var(--neutral-dark);
  font-size: 0.85rem;
}
</style>

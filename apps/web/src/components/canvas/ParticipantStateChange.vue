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
      <template v-if="target === 'WITHDRAWN'">
        <label for="state-change-withdrawal-kind">Withdrawal category</label>
        <select id="state-change-withdrawal-kind" v-model="withdrawalKind">
          <option value="" disabled>Choose a withdrawal category</option>
          <option
            v-for="item in WITHDRAWAL_KINDS"
            :key="item.code"
            :value="item.code"
          >
            {{ item.label }}
          </option>
        </select>
      </template>
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
        :disabled="
          busy ||
          !target ||
          !reason.trim() ||
          (target === 'WITHDRAWN' && !withdrawalKind)
        "
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
      <p
        v-if="target === 'WITHDRAWN'"
        id="state-change-withdraw-note"
        class="note"
      >
        Withdrawing a participant requires a step-up electronic signature and
        closes new form capture.
      </p>
    </template>
    <p
      v-if="changes.unblindingWithheld"
      id="state-change-unblinding-note"
      class="note"
    >
      Unblinding isn't offered here: it needs its own unblinding workflow.
    </p>
    <SignatureCaptureModal
      v-if="withdrawModalOpen"
      :is-open="withdrawModalOpen"
      :username="signerUsername"
      :signer-name="signerFullName"
      :reasons="WITHDRAW_SIGNING_REASONS"
      :action-url="WITHDRAW_ENROLLMENT_ACTION"
      :semantic-action="WITHDRAW_ENROLLMENT_SEMANTIC_ACTION"
      :on-sign="executeSignedWithdrawal"
      @cancel="withdrawModalOpen = false"
      @success="withdrawModalOpen = false"
    />
  </section>
</template>

<script setup lang="ts">
import { computed, ref, watch } from "vue";
import type { SubjectSummary } from "../../api/executionApi";
import {
  participantStateChanges,
  participantStateLabel,
  WITHDRAW_ENROLLMENT_ACTION,
  WITHDRAW_ENROLLMENT_SEMANTIC_ACTION,
  WITHDRAWAL_KINDS,
} from "../../canvas/lifecycle";
import { useAuthStore } from "../../stores/auth.js";
import { useStudyCanvasStore } from "../../stores/studyCanvas";
import SignatureCaptureModal from "../SignatureCaptureModal.vue";

const WITHDRAW_SIGNING_REASONS = ["Withdrawn"];

/**
 * Moves the inspected participant to another state. It offers the transitions Execution's graph allows from
 * the current state (less unblinding); Execution checks the transition again, gates enrolment on site
 * activation and records the reason. Withdrawing collects a structured withdrawal category and Part 11 step-up signature.
 */
const props = defineProps<{ participant: SubjectSummary }>();

const authStore = useAuthStore();
const canvas = useStudyCanvasStore();
const target = ref("");
const withdrawalKind = ref("");
const reason = ref("");
const busy = ref(false);
const withdrawModalOpen = ref(false);

const signerUsername = computed(
  () => authStore?.user?.email || authStore?.user?.username || "investigator"
);
const signerFullName = computed(
  () =>
    authStore?.user?.name || authStore?.user?.full_name || signerUsername.value
);

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
    withdrawalKind.value = "";
    reason.value = "";
    withdrawModalOpen.value = false;
  }
);

watch(target, (next) => {
  if (next !== "WITHDRAWN") {
    withdrawalKind.value = "";
    withdrawModalOpen.value = false;
  }
});

async function submit() {
  const state = target.value;
  const why = reason.value.trim();
  if (!state || !why) return;
  if (state === "WITHDRAWN") {
    if (!withdrawalKind.value) return;
    withdrawModalOpen.value = true;
    return;
  }
  busy.value = true;
  try {
    await canvas.changeParticipantState(props.participant, state, why);
  } finally {
    busy.value = false;
  }
}

async function executeSignedWithdrawal(sigToken: string) {
  const why = reason.value.trim();
  const kind = withdrawalKind.value;
  if (!why || !kind) return false;
  busy.value = true;
  try {
    const ok = await canvas.changeParticipantState(
      props.participant,
      "WITHDRAWN",
      why,
      {
        withdrawalKind: kind,
        sigToken,
      }
    );
    if (!ok) {
      return false;
    }
    withdrawModalOpen.value = false;
    return { kind: "signed" as const };
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

<template>
  <section
    id="inspector-state-change"
    class="state-change"
    aria-labelledby="inspector-state-change-title"
  >
    <div
      v-if="effectiveCanManageParticipants && canRandomize"
      id="participant-randomize-section"
      class="rtsm-action-card"
    >
      <h3 id="participant-randomize-title">Randomize participant</h3>
      <p class="note">
        Allocates the next slot from the approved randomization scheme with an
        electronic signature.
      </p>
      <div
        v-for="(factor, idx) in stratFactors"
        :key="idx"
        class="strat-factor-row"
      >
        <input
          :id="`strat-factor-key-${idx}`"
          v-model="factor.key"
          type="text"
          placeholder="Factor (e.g. age_group)"
          aria-label="Stratification factor name"
        />
        <input
          :id="`strat-factor-val-${idx}`"
          v-model="factor.value"
          type="text"
          placeholder="Level (e.g. >=65)"
          aria-label="Stratification factor level"
        />
        <button
          :id="`btn-remove-strat-factor-${idx}`"
          type="button"
          class="btn btn-secondary btn-sm"
          @click="removeStratFactor(idx)"
        >
          Remove
        </button>
      </div>
      <button
        id="btn-add-strat-factor"
        type="button"
        class="btn btn-secondary btn-sm"
        @click="addStratFactor"
      >
        + Add stratification factor
      </button>
      <label for="randomize-reason">Reason</label>
      <textarea
        id="randomize-reason"
        v-model="randomizeReason"
        rows="2"
        placeholder="Why the participant is randomized (recorded in the audit trail)"
      />
      <button
        id="btn-randomize-participant"
        type="button"
        class="btn btn-primary"
        :disabled="busy || !randomizeReason.trim()"
        @click="randomizeModalOpen = true"
      >
        Sign &amp; randomize participant
      </button>
    </div>

    <template v-if="effectiveCanManageParticipants">
      <h3 id="inspector-state-change-title" tabindex="-1">Change state</h3>
      <p
        v-if="!changes.targets.length"
        id="state-change-final"
        class="note"
        tabindex="-1"
      >
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
        <p
          v-if="target === 'ENROLLED'"
          id="state-change-enrol-note"
          class="note"
        >
          Execution enrols a participant only at a site whose activation
          documents are complete.
        </p>
      </template>
    </template>
    <h3 v-else id="inspector-state-change-title" class="sr-only" tabindex="-1">
      Emergency unblinding
    </h3>

    <template v-if="changes.unblindingWithheld">
      <div
        v-if="canUnblind"
        id="emergency-unblind-section"
        class="rtsm-action-card rtsm-action-card--danger"
      >
        <div class="unblind-header">
          <h4>Emergency unblinding</h4>
          <button
            id="btn-toggle-emergency-unblind"
            type="button"
            class="btn btn-secondary btn-sm"
            @click="toggleEmergencyUnblind"
          >
            {{ unblindOpen ? "Cancel" : "Emergency unblind" }}
          </button>
        </div>
        <template v-if="unblindOpen">
          <p class="note">
            Breaking the blind is irreversible and recorded in the audit trail.
            Provide a clinical justification of at least 50 characters.
          </p>
          <label for="emergency-unblind-reason">Clinical justification</label>
          <textarea
            id="emergency-unblind-reason"
            v-model="unblindReason"
            rows="3"
            placeholder="Document the acute clinical emergency requiring immediate treatment arm disclosure..."
          />
          <p id="emergency-unblind-char-count" class="note">
            {{ unblindReasonLength }} / 50 minimum characters
          </p>
          <button
            id="btn-confirm-emergency-unblind"
            type="button"
            class="btn btn-primary"
            :disabled="busy || unblindReasonLength < 50"
            @click="submitEmergencyUnblind"
          >
            Confirm emergency unblinding
          </button>
        </template>
      </div>
      <p v-else id="state-change-unblinding-note" class="note">
        Unblinding isn't offered here: emergency unblinding requires a Principal
        Investigator or Authorized ER Physician.
      </p>
    </template>

    <SignatureCaptureModal
      v-if="effectiveCanManageParticipants && canRandomize"
      :is-open="randomizeModalOpen"
      fallback-focus="#state-change-target"
      :username="authStore.user?.username || ''"
      :signer-name="signerFullName"
      signer-role="Site CRC"
      :reasons="RANDOMIZE_SIGNING_REASONS"
      :action-url="randomizeSubjectAction(participant.id)"
      :semantic-action="RANDOMIZE_SUBJECT_SEMANTIC_ACTION"
      :on-sign="handleRandomizeSigned"
      @cancel="randomizeModalOpen = false"
    />
  </section>
</template>

<script setup lang="ts">
import { computed, ref, watch } from "vue";
import { handoffFocus, useInlineFocusHandoff } from "ui";
import type { SubjectSummary } from "../../api/executionApi";
import { canUnblindParticipant } from "../../canvas/authority";
import {
  RANDOMIZE_SUBJECT_SEMANTIC_ACTION,
  canRandomizeParticipant,
  participantStateChanges,
  participantStateLabel,
  randomizeSubjectAction,
} from "../../canvas/lifecycle";
import { useAuthStore } from "../../stores/auth.js";
import { useStudyCanvasStore } from "../../stores/studyCanvas";
import {
  changeParticipantStateMutation,
  randomizeParticipantMutation,
} from "../../stores/studyCanvasParticipantMutations";
import SignatureCaptureModal from "../SignatureCaptureModal.vue";

/**
 * Moves the inspected participant to another state, dispatches step-up randomization from ENROLLED,
 * and offers emergency unblinding to Principal Investigators and Authorized ER Physicians (#4943).
 */
const props = withDefaults(
  defineProps<{
    participant: SubjectSummary;
    canManageParticipants?: boolean;
  }>(),
  {
    canManageParticipants: true,
  }
);

const RANDOMIZE_SIGNING_REASONS = [
  "I verify participant eligibility and authorize randomization",
  "INVESTIGATOR_SIGNATURE",
  "APPROVAL",
];

const authStore = useAuthStore();
const signerFullName = computed(() => {
  const identity = authStore.user;
  if (!identity) return "";
  const full = [identity.firstName, identity.lastName]
    .filter(Boolean)
    .join(" ")
    .trim();
  return full || identity.username || "";
});
const canvas = useStudyCanvasStore();
const target = ref("");
const reason = ref("");
const busy = ref(false);

const stratFactors = ref<Array<{ key: string; value: string }>>([]);
const randomizeReason = ref("");
const randomizeModalOpen = ref(false);

const unblindOpen = ref(false);
const unblindReason = ref("");
const unblindHandoff = useInlineFocusHandoff({
  openTarget: "#emergency-unblind-reason",
  closeFallback: [
    "#btn-toggle-emergency-unblind",
    "#state-change-final",
    "#inspector-state-change-title",
  ],
  scrollIfOffscreen: true,
});

const changes = computed(() =>
  participantStateChanges(props.participant.status)
);
const effectiveCanManageParticipants = computed(
  () => props.canManageParticipants ?? true
);
const canRandomize = computed(() =>
  canRandomizeParticipant(props.participant.status)
);
const canUnblind = computed(() => canUnblindParticipant(authStore));
const unblindReasonLength = computed(() => unblindReason.value.trim().length);

const finalNote = computed(
  () =>
    `${participantStateLabel(props.participant.status)} is a final state; it doesn't change.`
);

watch([() => props.participant.id, () => props.participant.status], () => {
  target.value = "";
  reason.value = "";
  stratFactors.value = [];
  randomizeReason.value = "";
  randomizeModalOpen.value = false;
  unblindOpen.value = false;
  unblindReason.value = "";
});

function addStratFactor() {
  stratFactors.value.push({ key: "", value: "" });
}

function removeStratFactor(index: number) {
  stratFactors.value.splice(index, 1);
  const remaining = stratFactors.value.length;
  void handoffFocus(
    remaining > 0
      ? [
          `#strat-factor-key-${Math.min(index, remaining - 1)}`,
          "#btn-add-strat-factor",
        ]
      : ["#btn-add-strat-factor", "#inspector-state-change-title"]
  );
}

function toggleEmergencyUnblind() {
  if (unblindOpen.value) {
    unblindOpen.value = false;
    void unblindHandoff.restoreTrigger([
      "#btn-toggle-emergency-unblind",
      "#state-change-final",
      "#inspector-state-change-title",
    ]);
  } else {
    unblindHandoff.rememberTrigger("#btn-toggle-emergency-unblind");
    unblindOpen.value = true;
    void unblindHandoff.handoff("#emergency-unblind-reason", {
      scrollIfOffscreen: true,
    });
  }
}

async function submit() {
  const state = target.value;
  const why = reason.value.trim();
  if (!state || !why) return;
  busy.value = true;
  try {
    const ok = await changeParticipantStateMutation(
      canvas,
      props.participant,
      state,
      why
    );
    if (ok) {
      busy.value = false;
      void handoffFocus([
        "#state-change-final",
        "#state-change-target",
        "#btn-toggle-emergency-unblind",
        "#inspector-state-change-title",
      ]);
    }
  } finally {
    busy.value = false;
  }
}

async function handleRandomizeSigned(sigToken: string, signingReason: string) {
  const why = randomizeReason.value.trim() || signingReason;
  const factorRecord: Record<string, string> = {};
  for (const row of stratFactors.value) {
    const k = row.key.trim();
    const v = row.value.trim();
    if (k && v) {
      factorRecord[k] = v;
    }
  }
  busy.value = true;
  try {
    const randomized = await randomizeParticipantMutation(
      canvas,
      props.participant,
      Object.keys(factorRecord).length > 0 ? factorRecord : null,
      why,
      sigToken
    );
    if (!randomized) {
      return {
        status: "FAILED",
        message: "Participant randomization could not be completed.",
      };
    }
    randomizeModalOpen.value = false;
    randomizeReason.value = "";
    stratFactors.value = [];
    busy.value = false;
    void handoffFocus([
      "#state-change-target",
      "#state-change-final",
      "#btn-toggle-emergency-unblind",
      "#inspector-state-change-title",
    ]);
    return { kind: "signed" };
  } finally {
    busy.value = false;
  }
}

async function submitEmergencyUnblind() {
  const why = unblindReason.value.trim();
  if (!canUnblind.value || why.length < 50) return;
  busy.value = true;
  try {
    const ok = await changeParticipantStateMutation(
      canvas,
      props.participant,
      "UNBLINDED",
      why
    );
    if (ok) {
      unblindOpen.value = false;
      unblindReason.value = "";
      busy.value = false;
      void unblindHandoff.handoff([
        "#state-change-final",
        "#btn-toggle-emergency-unblind",
        "#inspector-state-change-title",
      ]);
    }
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

.state-change h3,
.state-change h4 {
  margin: 0;
  font-size: 0.95rem;
}

.state-change select,
.state-change textarea,
.state-change input[type="text"] {
  padding: 6px 8px;
  border: 1px solid var(--border);
  border-radius: 4px;
}

.rtsm-action-card {
  display: flex;
  flex-direction: column;
  gap: 6px;
  padding: 10px;
  border: 1px solid var(--border);
  border-radius: 6px;
}

.rtsm-action-card--danger {
  border-color: var(--warning);
}

.strat-factor-row {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
  align-items: center;
}

.strat-factor-row input {
  flex: 1 1 110px;
  min-width: 0;
}

.unblind-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 8px;
}

.note {
  margin: 0;
  color: var(--neutral-dark);
  font-size: 0.85rem;
}
</style>

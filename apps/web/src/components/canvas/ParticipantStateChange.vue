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
        @click="activeSignatureFlow = 'randomize'"
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
        <label for="state-change-reason">{{
          isWithdrawTarget ? "Clinical justification" : "Reason"
        }}</label>
        <textarea
          id="state-change-reason"
          v-model="reason"
          :rows="isWithdrawTarget ? 3 : 2"
          :aria-describedby="
            isWithdrawTarget
              ? 'state-change-withdraw-note state-change-withdraw-char-count'
              : undefined
          "
          :placeholder="
            isWithdrawTarget
              ? 'Document the clinical or participant withdrawal justification (minimum 50 characters)...'
              : 'Why the state changes (recorded in the audit trail)'
          "
        />
        <template v-if="isWithdrawTarget">
          <p id="state-change-withdraw-note" class="note">
            Withdrawing a participant requires an electronic signature and a
            clinical justification of at least 50 characters.
          </p>
          <p id="state-change-withdraw-char-count" class="note">
            {{ stateReasonLength }} /
            {{ MIN_CLINICAL_JUSTIFICATION_LENGTH }} minimum characters
          </p>
        </template>
        <button
          id="btn-change-state"
          type="button"
          class="btn btn-secondary"
          :disabled="isChangeStateDisabled"
          @click="submit"
        >
          {{ changeStateButtonLabel }}
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
          <p id="emergency-unblind-note" class="note">
            Breaking the blind is irreversible and requires an electronic
            signature with a clinical justification of at least 50 characters.
          </p>
          <label for="emergency-unblind-reason">Clinical justification</label>
          <textarea
            id="emergency-unblind-reason"
            v-model="unblindReason"
            rows="3"
            aria-describedby="emergency-unblind-note emergency-unblind-char-count"
            placeholder="Document the acute clinical emergency requiring immediate treatment arm disclosure..."
          />
          <p id="emergency-unblind-char-count" class="note">
            {{ unblindReasonLength }} /
            {{ MIN_CLINICAL_JUSTIFICATION_LENGTH }} minimum characters
          </p>
          <button
            id="btn-confirm-emergency-unblind"
            type="button"
            class="btn btn-primary"
            :disabled="
              busy || unblindReasonLength < MIN_CLINICAL_JUSTIFICATION_LENGTH
            "
            @click="submitEmergencyUnblind"
          >
            Sign &amp; confirm emergency unblinding
          </button>
        </template>
      </div>
      <p v-else id="state-change-unblinding-note" class="note">
        Unblinding isn't offered here: emergency unblinding requires a Principal
        Investigator or Authorized ER Physician.
      </p>
    </template>

    <SignatureCaptureModal
      v-if="hasStepUpCapableAction && activeSignatureFlow !== null"
      :is-open="activeSignatureFlow !== null"
      :fallback-focus="signatureModalFallbackFocus"
      :username="authStore.user?.username || ''"
      :signer-name="signerFullName"
      :signer-role="signatureModalSignerRole"
      :reasons="signatureModalReasons"
      :action-url="signatureModalActionUrl"
      :semantic-action="signatureModalSemanticAction"
      :on-sign="handleSignatureCompleted"
      @cancel="activeSignatureFlow = null"
    />
  </section>
</template>

<script lang="ts">
import { defineLazyOverlay } from "../../utils/lazyOverlay";

const SignatureCaptureModal = defineLazyOverlay(
  () => import("../SignatureCaptureModal.vue")
);
</script>

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
import {
  MIN_CLINICAL_JUSTIFICATION_LENGTH,
  UNBLIND_SUBJECT_SEMANTIC_ACTION,
  WITHDRAW_SUBJECT_SEMANTIC_ACTION,
  requiresStepUpSignatureForState,
  subjectStateAction,
} from "../../canvas/participantSignatures";
import { useAuthStore } from "../../stores/auth.js";
import { useStudyCanvasStore } from "../../stores/studyCanvas";
import {
  changeParticipantStateMutation,
  randomizeParticipantMutation,
} from "../../stores/studyCanvasParticipantMutations";

/**
 * Moves the inspected participant to another state, dispatches step-up randomization from ENROLLED,
 * and enforces Part 11 step-up electronic signatures with >= 50-character clinical justifications on
 * participant withdrawal and emergency unblinding (#4943).
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

type SignatureFlow = "randomize" | "withdraw" | "unblind";

const RANDOMIZE_SIGNING_REASONS = [
  "I verify participant eligibility and authorize randomization",
  "INVESTIGATOR_SIGNATURE",
  "APPROVAL",
];

const WITHDRAW_SIGNING_REASONS = [
  "I verify clinical justification and authorize participant withdrawal",
  "INVESTIGATOR_SIGNATURE",
  "APPROVAL",
];

const UNBLIND_SIGNING_REASONS = [
  "I authorize emergency treatment arm unblinding for acute clinical care",
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
const activeSignatureFlow = ref<SignatureFlow | null>(null);

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
const stateReasonLength = computed(() => reason.value.trim().length);
const unblindReasonLength = computed(() => unblindReason.value.trim().length);
const isWithdrawTarget = computed(() => target.value === "WITHDRAWN");

const isChangeStateDisabled = computed(() => {
  if (busy.value || !target.value || !reason.value.trim()) {
    return true;
  }
  if (
    requiresStepUpSignatureForState(target.value) &&
    stateReasonLength.value < MIN_CLINICAL_JUSTIFICATION_LENGTH
  ) {
    return true;
  }
  return false;
});

const changeStateButtonLabel = computed(() => {
  if (!target.value) {
    return "Move";
  }
  const label = participantStateLabel(target.value).toLowerCase();
  return requiresStepUpSignatureForState(target.value)
    ? `Sign & move to ${label}`
    : `Move to ${label}`;
});

const hasStepUpCapableAction = computed(
  () =>
    (effectiveCanManageParticipants.value &&
      (canRandomize.value || changes.value.targets.includes("WITHDRAWN"))) ||
    (changes.value.unblindingWithheld && canUnblind.value)
);

const signatureModalSignerRole = computed(() => {
  if (activeSignatureFlow.value === "randomize") {
    return "Site CRC";
  }
  if (activeSignatureFlow.value === "unblind") {
    return "Investigator";
  }
  return "";
});

const signatureModalReasons = computed(() => {
  if (activeSignatureFlow.value === "unblind") {
    return UNBLIND_SIGNING_REASONS;
  }
  if (activeSignatureFlow.value === "withdraw") {
    return WITHDRAW_SIGNING_REASONS;
  }
  return RANDOMIZE_SIGNING_REASONS;
});

const signatureModalActionUrl = computed(() =>
  activeSignatureFlow.value === "randomize"
    ? randomizeSubjectAction(props.participant.id)
    : subjectStateAction(props.participant.id)
);

const signatureModalFallbackFocus = computed(() => {
  if (activeSignatureFlow.value === "unblind") {
    return "#btn-toggle-emergency-unblind";
  }
  if (activeSignatureFlow.value === "withdraw") {
    return "#state-change-final";
  }
  return "#state-change-target";
});

const signatureModalSemanticAction = computed(() => {
  if (activeSignatureFlow.value === "unblind") {
    return UNBLIND_SUBJECT_SEMANTIC_ACTION;
  }
  if (activeSignatureFlow.value === "withdraw") {
    return WITHDRAW_SUBJECT_SEMANTIC_ACTION;
  }
  return RANDOMIZE_SUBJECT_SEMANTIC_ACTION;
});

const finalNote = computed(
  () =>
    `${participantStateLabel(props.participant.status)} is a final state; it doesn't change.`
);

watch([() => props.participant.id, () => props.participant.status], () => {
  target.value = "";
  reason.value = "";
  stratFactors.value = [];
  randomizeReason.value = "";
  activeSignatureFlow.value = null;
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
  if (requiresStepUpSignatureForState(state)) {
    if (why.length < MIN_CLINICAL_JUSTIFICATION_LENGTH) return;
    activeSignatureFlow.value = "withdraw";
    return;
  }
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

async function handleSignatureCompleted(
  sigToken: string,
  signingReason: string
) {
  if (activeSignatureFlow.value === "unblind") {
    return handleUnblindSigned(sigToken, signingReason);
  }
  if (activeSignatureFlow.value === "withdraw") {
    return handleWithdrawSigned(sigToken, signingReason);
  }
  return handleRandomizeSigned(sigToken, signingReason);
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
    activeSignatureFlow.value = null;
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

async function handleWithdrawSigned(sigToken: string, signingReason: string) {
  const why = reason.value.trim() || signingReason;
  busy.value = true;
  try {
    const ok = await changeParticipantStateMutation(
      canvas,
      props.participant,
      "WITHDRAWN",
      why,
      sigToken
    );
    if (!ok) {
      return {
        status: "FAILED",
        message: "Participant withdrawal could not be completed.",
      };
    }
    activeSignatureFlow.value = null;
    target.value = "";
    reason.value = "";
    busy.value = false;
    void handoffFocus([
      "#state-change-final",
      "#state-change-target",
      "#btn-toggle-emergency-unblind",
      "#inspector-state-change-title",
    ]);
    return { kind: "signed" };
  } finally {
    busy.value = false;
  }
}

async function handleUnblindSigned(sigToken: string, signingReason: string) {
  const why = unblindReason.value.trim() || signingReason;
  busy.value = true;
  try {
    const ok = await changeParticipantStateMutation(
      canvas,
      props.participant,
      "UNBLINDED",
      why,
      sigToken
    );
    if (!ok) {
      return {
        status: "FAILED",
        message: "Emergency unblinding could not be completed.",
      };
    }
    activeSignatureFlow.value = null;
    unblindOpen.value = false;
    unblindReason.value = "";
    busy.value = false;
    void unblindHandoff.handoff([
      "#state-change-final",
      "#btn-toggle-emergency-unblind",
      "#inspector-state-change-title",
    ]);
    return { kind: "signed" };
  } finally {
    busy.value = false;
  }
}

function submitEmergencyUnblind() {
  const why = unblindReason.value.trim();
  if (!canUnblind.value || why.length < MIN_CLINICAL_JUSTIFICATION_LENGTH) {
    return;
  }
  activeSignatureFlow.value = "unblind";
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
  font-size: var(--font-size-base);
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
  font-size: var(--font-size-sm);
}
</style>

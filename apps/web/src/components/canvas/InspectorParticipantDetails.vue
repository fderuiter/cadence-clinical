<template>
  <dl class="details">
    <dt>State</dt>
    <dd id="inspector-state">
      {{ participantStateLabel(participant.status) }}
    </dd>
    <dt>Study</dt>
    <dd class="inspector-mono">{{ participant.study_id }}</dd>
    <dt>Site</dt>
    <dd
      id="inspector-site"
      :class="{ 'inspector-mono': Boolean(participant.site_id) }"
    >
      {{ participant.site_id || "Not assigned" }}
    </dd>
    <dt>Protocol version</dt>
    <dd class="numeric">
      {{ participant.active_protocol_version || "Not recorded" }}
    </dd>
    <dt>Enrollment number</dt>
    <dd class="numeric">
      {{ participant.enrollment_index }}
    </dd>
    <template v-if="assignedKitDisplay">
      <dt>Assigned kit</dt>
      <dd id="inspector-assigned-kit" class="inspector-mono">
        {{ assignedKitDisplay }}
      </dd>
    </template>
    <template v-if="participant.treatment_group">
      <dt>Treatment arm</dt>
      <dd id="inspector-treatment-group">
        {{ participant.treatment_group }}
      </dd>
    </template>
  </dl>

  <ParticipantSiteAssignment
    v-if="canManageParticipants && !participant.site_id"
    :participant="participant"
    :study-code="studyCode"
  />

  <section
    v-if="canManageParticipants"
    id="inspector-consent"
    class="consent-panel"
    aria-labelledby="inspector-consent-title"
  >
    <div class="consent-header">
      <h3 id="inspector-consent-title">Informed consent</h3>
      <button
        id="btn-toggle-consent-form"
        type="button"
        class="btn btn-secondary btn-sm"
        @click="consentFormOpen = !consentFormOpen"
      >
        {{ consentFormOpen ? "Cancel" : "Record consent" }}
      </button>
    </div>
    <form
      v-if="consentFormOpen"
      id="inspector-consent-form"
      class="consent-form"
      @submit.prevent="submitConsent"
    >
      <label for="consent-version-tag">Protocol version</label>
      <input
        id="consent-version-tag"
        v-model="consentVersionTag"
        type="text"
        placeholder="1.0"
      />
      <label for="consent-version-index">Version number</label>
      <input
        id="consent-version-index"
        v-model.number="consentVersionIndex"
        type="number"
        min="1"
      />
      <label for="consent-signed-date">Signed date</label>
      <input id="consent-signed-date" v-model="consentSignedDate" type="date" />
      <label class="consent-checkbox" for="consent-paper-override">
        <input
          id="consent-paper-override"
          v-model="consentPaperOverride"
          type="checkbox"
        />
        Verified paper ICF override
      </label>
      <label for="consent-reason">Reason</label>
      <textarea
        id="consent-reason"
        v-model="consentReason"
        rows="2"
        placeholder="Why consent is recorded (recorded in the audit trail)"
      />
      <button
        id="btn-record-consent"
        type="submit"
        class="btn btn-primary"
        :disabled="
          consentBusy ||
          !consentVersionTag.trim() ||
          (!consentSignedDate && !consentPaperOverride) ||
          !consentReason.trim()
        "
      >
        Save informed consent
      </button>
    </form>
  </section>

  <ParticipantStateChange
    v-if="showParticipantStateActions"
    :participant="participant"
    :can-manage-participants="Boolean(canManageParticipants)"
  />
</template>

<script setup lang="ts">
import { computed, ref, watch } from "vue";
import { canUnblindParticipant } from "../../canvas/authority";
import {
  participantStateChanges,
  participantStateLabel,
} from "../../canvas/lifecycle";
import { useAuthStore } from "../../stores/auth";
import {
  useStudyCanvasStore,
  type CanvasParticipant,
} from "../../stores/studyCanvas";
import { recordParticipantConsentMutation } from "../../stores/studyCanvasParticipantMutations";
import ParticipantSiteAssignment from "./ParticipantSiteAssignment.vue";
import ParticipantStateChange from "./ParticipantStateChange.vue";

const props = defineProps<{
  studyCode: string;
  participant: CanvasParticipant;
  canManageParticipants?: boolean;
  canUnblindParticipants?: boolean;
}>();

const authStore = useAuthStore();
const canvas = useStudyCanvasStore();

const effectiveCanUnblind = computed(
  () => props.canUnblindParticipants ?? canUnblindParticipant(authStore)
);
const showParticipantStateActions = computed(() => {
  if (props.canManageParticipants) return true;
  return (
    effectiveCanUnblind.value &&
    participantStateChanges(props.participant.status).unblindingWithheld
  );
});

const assignedKitDisplay = computed(
  () =>
    props.participant.investigational_product_id ||
    props.participant.kit_number ||
    ""
);

const consentFormOpen = ref(false);
const consentVersionTag = ref("1.0");
const consentVersionIndex = ref(1);
const consentSignedDate = ref(new Date().toISOString().slice(0, 10));
const consentPaperOverride = ref(false);
const consentReason = ref("");
const consentBusy = ref(false);

watch(
  () => [props.participant.id, props.participant.active_protocol_version],
  () => {
    consentFormOpen.value = false;
    consentVersionTag.value =
      props.participant.active_protocol_version || "1.0";
    consentVersionIndex.value = 1;
    consentSignedDate.value = new Date().toISOString().slice(0, 10);
    consentPaperOverride.value = false;
    consentReason.value = "";
  },
  { immediate: true }
);

async function submitConsent() {
  const tag = consentVersionTag.value.trim();
  const reason = consentReason.value.trim();
  if (
    !tag ||
    (!consentSignedDate.value && !consentPaperOverride.value) ||
    !reason
  ) {
    return;
  }
  consentBusy.value = true;
  try {
    const ok = await recordParticipantConsentMutation(
      canvas,
      props.participant,
      {
        versionTag: tag,
        versionIndex: Math.max(1, Number(consentVersionIndex.value) || 1),
        icfSignedDate: consentSignedDate.value
          ? `${consentSignedDate.value}T00:00:00Z`
          : null,
        isPaperOverride: consentPaperOverride.value,
        reason,
      }
    );
    if (ok) {
      consentFormOpen.value = false;
      consentReason.value = "";
    }
  } finally {
    consentBusy.value = false;
  }
}
</script>

<style scoped>
.inspector-mono {
  font-family: var(--semantic-font-mono);
}

.numeric {
  font-variant-numeric: tabular-nums;
}

.consent-panel {
  display: flex;
  flex-direction: column;
  gap: 8px;
  padding-top: 10px;
  border-top: 1px solid var(--border);
}

.consent-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 8px;
}

.consent-header h3 {
  margin: 0;
  font-size: 0.95rem;
}

.consent-form {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.consent-form input[type="text"],
.consent-form input[type="number"],
.consent-form input[type="date"],
.consent-form textarea {
  padding: 6px 8px;
  border: 1px solid var(--border);
  border-radius: 4px;
}

.consent-checkbox {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  font-size: 0.86rem;
}
</style>

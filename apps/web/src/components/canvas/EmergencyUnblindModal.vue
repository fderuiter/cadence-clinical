<template>
  <ClinicalModal
    id="emergency-unblind-modal"
    :model-value="isOpen"
    title="Emergency unblinding"
    size="md"
    :dismissible="!busy"
    @update:model-value="onModelUpdate"
    @close="onClose"
  >
    <form
      id="emergency-unblind-form"
      class="unblind-form"
      @submit.prevent="confirmUnblind"
    >
      <p class="warning-banner">
        Breaking the treatment blind for
        <strong>{{ participant.subject_id }}</strong> is an irreversible
        clinical safety action recorded permanently in the 21 CFR Part 11 audit
        trail.
      </p>

      <div class="field">
        <label for="emergency-unblind-reason">
          Clinical emergency justification (at least
          {{ EMERGENCY_UNBLIND_MIN_REASON_LENGTH }} characters)
        </label>
        <textarea
          id="emergency-unblind-reason"
          v-model="reason"
          rows="4"
          placeholder="Document the acute clinical emergency, suspected serious adverse event, and why knowledge of the treatment assignment is required for immediate medical management."
          @blur="touched = true"
        />
        <span id="emergency-unblind-count" class="char-count">
          {{ trimmedLength }} / {{ EMERGENCY_UNBLIND_MIN_REASON_LENGTH }}
          characters minimum
        </span>
      </div>

      <p
        v-if="showLengthValidation"
        id="emergency-unblind-validation"
        class="error-note"
        role="alert"
      >
        Emergency unblinding requires a clinical reason of at least
        {{ EMERGENCY_UNBLIND_MIN_REASON_LENGTH }} characters.
      </p>
    </form>

    <template #footer>
      <button
        id="btn-cancel-emergency-unblind"
        type="button"
        class="btn btn-secondary"
        :disabled="busy"
        @click="onClose"
      >
        Cancel
      </button>
      <button
        id="btn-confirm-emergency-unblind"
        type="button"
        class="btn btn-danger"
        :disabled="busy || !hasMinimumLength"
        @click="confirmUnblind"
      >
        {{ busy ? "Unblinding..." : `Unblind ${participant.subject_id}` }}
      </button>
    </template>
  </ClinicalModal>
</template>

<script setup lang="ts">
import { computed, ref, watch } from "vue";
import { ClinicalModal, useEscapeClose } from "ui";
import type { SubjectSummary } from "../../api/executionApi";
import { EMERGENCY_UNBLIND_MIN_REASON_LENGTH } from "../../canvas/rtsm";
import { useStudyCanvasStore } from "../../stores/studyCanvas";

const props = defineProps<{
  isOpen: boolean;
  participant: SubjectSummary;
}>();

const emit = defineEmits<{
  close: [];
  unblinded: [];
}>();

const canvas = useStudyCanvasStore();
const reason = ref("");
const touched = ref(false);
const busy = ref(false);

useEscapeClose(onClose, {
  isOpen: () => props.isOpen && !busy.value,
});

const trimmedLength = computed(() => reason.value.trim().length);
const hasMinimumLength = computed(
  () => trimmedLength.value >= EMERGENCY_UNBLIND_MIN_REASON_LENGTH
);
const showLengthValidation = computed(
  () => (touched.value || trimmedLength.value > 0) && !hasMinimumLength.value
);

watch(
  () => props.isOpen,
  (open) => {
    if (open) {
      reason.value = "";
      touched.value = false;
    }
  }
);

function onClose() {
  if (busy.value) return;
  emit("close");
}

function onModelUpdate(open: boolean) {
  if (!open) onClose();
}

async function confirmUnblind() {
  touched.value = true;
  if (!hasMinimumLength.value) return;
  busy.value = true;
  try {
    const ok = await canvas.changeParticipantState(
      props.participant,
      "UNBLINDED",
      reason.value.trim()
    );
    if (ok) {
      reason.value = "";
      emit("unblinded");
      emit("close");
    }
  } finally {
    busy.value = false;
  }
}
</script>

<style scoped>
.unblind-form {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.warning-banner {
  margin: 0;
  padding: 10px 12px;
  border-radius: 6px;
  border: 1px solid var(--danger);
  background: var(--danger-bg);
  color: var(--danger);
  font-size: 0.88rem;
}

.field {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.field label {
  font-size: 0.85rem;
  font-weight: 600;
}

.field textarea {
  padding: 8px;
  border: 1px solid var(--border);
  border-radius: 4px;
  background: var(--bg-card);
  color: var(--text-main);
}

.char-count {
  color: var(--neutral-dark);
  font-size: 0.8rem;
  font-variant-numeric: tabular-nums;
}

.error-note {
  margin: 0;
  color: var(--danger);
  font-size: 0.85rem;
}

.btn-danger {
  background: var(--danger);
  border-color: var(--danger);
  color: var(--bg-card);
}
</style>

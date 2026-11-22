<template>
  <form
    id="add-participant-form"
    class="add-participant"
    aria-labelledby="add-participant-title"
    @submit.prevent="submit"
  >
    <h3 id="add-participant-title">Add a participant</h3>
    <p class="hint">
      The participant starts in screening. No name, birth date or other identity
      details are recorded here.
    </p>
    <label for="add-participant-code">Participant code</label>
    <input
      id="add-participant-code"
      v-model="subjectCode"
      type="text"
      maxlength="255"
      autocomplete="off"
      required
    />
    <label for="add-participant-site">Site (optional)</label>
    <input
      id="add-participant-site"
      v-model="siteId"
      type="text"
      maxlength="255"
      autocomplete="off"
    />
    <label for="add-participant-reason">Reason</label>
    <textarea
      id="add-participant-reason"
      v-model="reason"
      rows="2"
      placeholder="Why this participant is being added (recorded in the audit trail)"
      required
    />
    <div class="buttons">
      <button
        id="btn-confirm-add-participant"
        type="submit"
        class="btn btn-primary"
        :disabled="busy || !subjectCode.trim() || !reason.trim()"
      >
        Add participant
      </button>
      <button
        id="btn-cancel-add-participant"
        type="button"
        class="btn btn-secondary"
        @click="emit('close')"
      >
        Cancel
      </button>
    </div>
  </form>
</template>

<script setup lang="ts">
import { onMounted, ref } from "vue";
import { handoffFocus } from "ui";
import type { SubjectSummary } from "../../api/executionApi";
import { useStudyCanvasStore } from "../../stores/studyCanvas";

/**
 * Adds a participant to the open study through Execution, which starts it in screening and audits the reason.
 * Codes are sent as typed: Execution compares them exactly.
 */
const props = defineProps<{ defaultSiteId: string }>();
const emit = defineEmits<{
  added: [participant: SubjectSummary | null];
  close: [];
}>();

const canvas = useStudyCanvasStore();
const subjectCode = ref("");
const siteId = ref(props.defaultSiteId);
const reason = ref("");
const busy = ref(false);

onMounted(() => {
  void handoffFocus("#add-participant-code", { scrollIfOffscreen: true });
});

async function submit() {
  const code = subjectCode.value.trim();
  const why = reason.value.trim();
  if (!code || !why || busy.value) return;
  busy.value = true;
  try {
    const result = await canvas.addParticipant(code, siteId.value.trim(), why);
    // When adding fails nothing was created: keep the form so the user can correct it.
    if (result.added) emit("added", result.participant);
  } finally {
    busy.value = false;
  }
}
</script>

<style scoped>
.add-participant {
  display: flex;
  flex-direction: column;
  gap: 6px;
  max-width: 420px;
  padding: 12px;
  border: 1px solid var(--border);
  border-radius: 8px;
}

.add-participant h3 {
  margin: 0;
  font-size: 1rem;
}

.hint {
  margin: 0;
  color: var(--neutral-dark);
  font-size: 0.85rem;
}

.add-participant input,
.add-participant textarea {
  padding: 6px 8px;
  border: 1px solid var(--border);
  border-radius: 4px;
}

.buttons {
  display: flex;
  gap: 6px;
}
</style>

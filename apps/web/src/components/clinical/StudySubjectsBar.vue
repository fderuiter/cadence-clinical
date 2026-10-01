<template>
  <form
    id="study-subjects-bar"
    class="study-subjects-bar"
    aria-label="Study"
    @submit.prevent="submit"
  >
    <label for="study-subjects-code">Study</label>
    <input
      id="study-subjects-code"
      v-model="studyCode"
      type="text"
      autocomplete="off"
      placeholder="Study code, e.g. CAD-101"
    >
    <button
      id="btn-load-study-subjects"
      type="submit"
      class="btn btn-secondary"
      :disabled="!studyCode.trim() || store.subjectsLoading"
    >
      Show subjects
    </button>
    <span
      id="study-subjects-status"
      class="status"
      role="status"
    >
      {{ statusText }}
    </span>
  </form>
</template>

<script setup>
import { computed, ref, watch } from "vue";
import { useClinicalStore } from "../../stores/clinical";

/**
 * Chooses the study whose subjects the eCRF lists, and reports how the load went (#4828).
 * The study comes from `?studyId=` when the screen is opened from a study; otherwise the user types it.
 */
const emit = defineEmits(["loaded"]);
const store = useClinicalStore();
const studyCode = ref(store.activeStudyId);

watch(
  () => store.activeStudyId,
  (value) => {
    studyCode.value = value;
  }
);

const statusText = computed(() => {
  if (store.subjectsLoading) return "Loading subjects...";
  if (store.subjectsError) return "Subjects couldn't be loaded.";
  if (!store.activeStudyId) return "Enter a study code to list its subjects.";
  const shown = store.subjects.length;
  const total = store.subjectsTotal;
  if (total === 0) return "No subjects in this study yet.";
  const noun = total === 1 ? "subject" : "subjects";
  return shown < total
    ? `Showing the first ${shown} of ${total} ${noun}.`
    : `${total} ${noun}.`;
});

async function submit() {
  const code = studyCode.value.trim();
  if (!code) return;
  store.activeStudyId = code;
  if (await store.loadSubjects(code)) emit("loaded");
}
</script>

<style scoped>
.study-subjects-bar {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: var(--spacing-sm);
  margin-bottom: var(--spacing-md);
}

.study-subjects-bar label {
  font-weight: bold;
}

.study-subjects-bar input {
  flex: 0 1 16rem;
  min-width: 0;
  padding: 8px;
  border: 1px solid var(--border);
  border-radius: 4px;
}

.study-subjects-bar .status {
  color: var(--text-muted, #64748b);
  font-size: 0.9rem;
}
</style>

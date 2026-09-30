<template>
  <section
    id="participants-section"
    class="participants"
    aria-labelledby="participants-title"
    :aria-busy="canvas.participantsLoading"
  >
    <header class="participants-header">
      <h2 id="participants-title">Participants</h2>
      <label for="participants-status">State</label>
      <select id="participants-status" :value="status" @change="onStatusChange">
        <option value="">Any state</option>
        <option
          v-for="state in PARTICIPANT_STATES"
          :key="state.code"
          :value="state.code"
        >
          {{ state.label }}
        </option>
      </select>
      <span v-if="siteId" id="participants-site-filter" class="filter-chip">
        Site {{ siteId }}
        <button
          id="btn-clear-site-filter"
          type="button"
          :aria-label="`Show participants at every site, not only ${siteId}`"
          @click="emit('filter', { status, siteId: '' })"
        >
          ×
        </button>
      </span>
      <button
        v-if="!adding"
        id="btn-add-participant"
        type="button"
        class="btn btn-secondary"
        @click="adding = true"
      >
        Add participant
      </button>
    </header>

    <AddParticipantForm
      v-if="adding"
      :default-site-id="siteId"
      @added="onAdded"
      @close="adding = false"
    />

    <p id="participants-status-line" class="status-line" role="status">
      {{ statusLine }}
    </p>

    <table v-if="canvas.participants.length" class="participants-table">
      <thead>
        <tr>
          <th scope="col">Participant</th>
          <th scope="col">State</th>
          <th scope="col">Site</th>
          <th scope="col" class="numeric">Enrollment</th>
        </tr>
      </thead>
      <tbody>
        <tr
          v-for="participant in canvas.participants"
          :key="participant.id"
          :class="{ selected: participant.id === selectedId }"
        >
          <th scope="row">
            <button
              :id="`participant-${participant.subject_id}`"
              type="button"
              class="row-select"
              :aria-pressed="participant.id === selectedId"
              @click="emit('select', participant)"
            >
              {{ participant.subject_id }}
            </button>
          </th>
          <td>{{ participantStateLabel(participant.status) }}</td>
          <td>{{ participant.site_id || "Not assigned" }}</td>
          <td class="numeric">{{ participant.enrollment_index }}</td>
        </tr>
      </tbody>
    </table>

    <button
      v-if="canvas.hasMoreParticipants"
      id="btn-more-participants"
      type="button"
      class="btn btn-secondary"
      :disabled="canvas.participantsLoading"
      @click="
        canvas.loadParticipants(canvas.participantsFilter, { append: true })
      "
    >
      Show more
    </button>
  </section>
</template>

<script setup lang="ts">
import { computed, ref } from "vue";
import type { SubjectSummary } from "../../api/executionApi";
import {
  PARTICIPANT_STATES,
  participantStateLabel,
} from "../../canvas/lifecycle";
import { useStudyCanvasStore } from "../../stores/studyCanvas";
import AddParticipantForm from "./AddParticipantForm.vue";

/**
 * The study's participants in enrollment order, from Execution, filtered by state and site. The filter lives
 * in the canvas URL, so the parent owns it; selecting a participant fills the inspector.
 */
const props = defineProps<{
  status: string;
  siteId: string;
  selectedId: string;
}>();
const emit = defineEmits<{
  select: [participant: SubjectSummary];
  filter: [filter: { status: string; siteId: string }];
}>();

const canvas = useStudyCanvasStore();
const adding = ref(false);

/** Closes the form and selects the new participant when the list could place it. */
function onAdded(participant: SubjectSummary | null) {
  adding.value = false;
  if (participant) emit("select", participant);
}

const statusLine = computed(() => {
  if (canvas.participantsLoading && !canvas.participants.length) {
    return "Loading participants...";
  }
  if (canvas.participantsError) return "Participants couldn't be loaded.";
  const total = canvas.participantsTotal;
  if (total === 0) {
    return props.status || props.siteId
      ? "No participants match these filters."
      : `No participants in ${canvas.studyCode} yet.`;
  }
  const shown = canvas.participants.length;
  const noun = total === 1 ? "participant" : "participants";
  return shown < total
    ? `Showing ${shown} of ${total} ${noun}.`
    : `${total} ${noun}.`;
});

function onStatusChange(event: Event) {
  const value = (event.target as HTMLSelectElement).value;
  emit("filter", { status: value, siteId: props.siteId });
}
</script>

<style scoped>
.participants {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.participants-header {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
}

.participants-header h2 {
  margin: 0 auto 0 0;
  font-size: 1rem;
}

.participants-header select {
  padding: 6px;
  border: 1px solid var(--border);
  border-radius: 4px;
}

.filter-chip {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  padding: 2px 8px;
  border-radius: 999px;
  background: var(--accent-bg);
  font-size: 0.85rem;
}

.filter-chip button {
  border: 0;
  background: transparent;
  color: inherit;
  cursor: pointer;
}

.status-line {
  margin: 0;
  color: var(--neutral-dark);
  font-size: 0.85rem;
}

.participants-table {
  width: 100%;
  border-collapse: collapse;
}

.participants-table th,
.participants-table td {
  padding: 6px 8px;
  border-bottom: 1px solid var(--border);
  text-align: left;
}

.participants-table thead th {
  color: var(--neutral-dark);
  font-size: 0.8rem;
  font-weight: 600;
}

.participants-table tr.selected {
  background: var(--accent-bg);
}

.row-select {
  padding: 0;
  border: 0;
  background: transparent;
  color: var(--accent);
  font-family: ui-monospace, SFMono-Regular, Menlo, monospace;
  cursor: pointer;
}

.numeric {
  font-variant-numeric: tabular-nums;
  text-align: right;
}

.participants-table th.numeric {
  text-align: right;
}
</style>

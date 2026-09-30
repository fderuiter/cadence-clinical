<template>
  <section
    id="participant-record"
    class="record"
    aria-labelledby="participant-record-title"
    :aria-busy="record.loading"
  >
    <header class="record-header">
      <h2 id="participant-record-title">{{ participant.subject_id }}</h2>
      <span class="record-meta">
        {{ participantStateLabel(participant.status) }} ·
        {{ participant.site_id ? `Site ${participant.site_id}` : "No site" }}
      </span>
    </header>

    <p v-if="record.loading" class="status-line" role="status">
      Loading {{ participant.subject_id }}'s data...
    </p>
    <p
      v-else-if="record.error"
      id="record-error"
      class="status-line"
      role="status"
    >
      {{ participant.subject_id }}'s data couldn't be loaded.
      <button
        id="btn-retry-record"
        type="button"
        class="btn btn-secondary"
        @click="emit('retry')"
      >
        Try again
      </button>
    </p>

    <template v-else>
      <section aria-labelledby="record-observations-title">
        <h3 id="record-observations-title">Observations</h3>
        <p
          v-if="!record.observations.length"
          id="record-observations-empty"
          class="status-line"
        >
          No observations recorded for {{ participant.subject_id }} yet.
        </p>
        <template v-for="group in visitGroups" v-else :key="group.visit">
          <h4 class="visit-title">{{ group.visit }}</h4>
          <table class="record-table">
            <thead>
              <tr>
                <th scope="col">Test</th>
                <th scope="col" class="numeric">Result</th>
                <th scope="col">Date</th>
                <th scope="col">Source verified</th>
              </tr>
            </thead>
            <tbody>
              <tr
                v-for="observation in group.observations"
                :key="observation.id"
                :class="{ selected: observation.id === selectedObservationId }"
              >
                <th scope="row">
                  <button
                    :id="`observation-${observation.id}`"
                    type="button"
                    class="row-select"
                    :aria-pressed="observation.id === selectedObservationId"
                    @click="emit('select-observation', observation)"
                  >
                    {{ observation.test_name }}
                  </button>
                </th>
                <td class="numeric">
                  {{ result(observation) }}
                  <span
                    v-if="observation.is_out_of_range"
                    class="flag"
                    title="Out of range"
                    >Out of range</span
                  >
                </td>
                <td>{{ observation.observation_date }}</td>
                <td>
                  {{ observation.is_sdv_verified ? "Verified" : "Not yet" }}
                </td>
              </tr>
            </tbody>
          </table>
        </template>
      </section>

      <QueriesPanel
        :subject-code="participant.subject_id"
        :queries="record.queries"
        :observations="record.observations"
        :can-manage="canManage"
      />

      <section aria-labelledby="record-forms-title">
        <h3 id="record-forms-title">Forms</h3>
        <p
          v-if="!record.forms.length"
          id="record-forms-empty"
          class="status-line"
        >
          No forms submitted for {{ participant.subject_id }} yet.
        </p>
        <table v-else id="record-forms" class="record-table">
          <thead>
            <tr>
              <th scope="col">Form</th>
              <th scope="col">Visit</th>
              <th scope="col">Status</th>
              <th scope="col">Protocol version</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="form in record.forms" :key="form.id">
              <th scope="row">{{ form.form_id }}</th>
              <td>{{ form.visit_id || "Not recorded" }}</td>
              <td>
                {{ FORM_STATUS[form.status] ?? form.status
                }}{{ form.is_readonly ? ", locked" : "" }}
              </td>
              <td>{{ form.protocol_version || "Not recorded" }}</td>
            </tr>
          </tbody>
        </table>
        <p class="status-line">
          Forms open here once form definitions are served for the protocol
          version (#4727).
        </p>
      </section>
    </template>
  </section>
</template>

<script setup lang="ts">
import { computed } from "vue";
import type { Observation, SubjectSummary } from "../../api/executionApi";
import { participantStateLabel } from "../../canvas/lifecycle";
import { useStudyCanvasStore } from "../../stores/studyCanvas";
import QueriesPanel from "./QueriesPanel.vue";

/**
 * One participant's data as Execution holds it: observations grouped by visit, queries and form submissions.
 * Selecting an observation fills the inspector, where source data verification happens.
 */
defineProps<{
  participant: SubjectSummary;
  selectedObservationId: string;
  canManage: boolean;
}>();
const emit = defineEmits<{
  "select-observation": [observation: Observation];
  retry: [];
}>();

const canvas = useStudyCanvasStore();
const record = computed(() => canvas.record);

const FORM_STATUS: Record<string, string> = {
  DRAFT: "Draft",
  COMPLETED: "Completed",
  APPROVED: "Approved",
};

const NO_VISIT = "No visit recorded";

// Visits in the order their first observation appears, observations in date order within a visit.
const visitGroups = computed(() => {
  const groups = new Map<string, Observation[]>();
  const sorted = [...record.value.observations].sort((a, b) =>
    a.observation_date.localeCompare(b.observation_date)
  );
  for (const observation of sorted) {
    const visit = observation.visit_id || NO_VISIT;
    const list = groups.get(visit) ?? [];
    list.push(observation);
    groups.set(visit, list);
  }
  return [...groups].map(([visit, observations]) => ({ visit, observations }));
});

function result(observation: Observation): string {
  const value = observation.value_string ?? observation.value;
  if (value === null || value === undefined || value === "") return "No value";
  return observation.unit ? `${value} ${observation.unit}` : String(value);
}
</script>

<style scoped>
.record {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.record-header {
  display: flex;
  flex-wrap: wrap;
  align-items: baseline;
  gap: 12px;
}

.record-header h2 {
  margin: 0;
  font-size: 1.1rem;
  font-family: ui-monospace, SFMono-Regular, Menlo, monospace;
}

.record-meta,
.status-line {
  color: var(--neutral-dark);
  font-size: 0.85rem;
}

.status-line {
  margin: 0;
}

h3 {
  margin: 0 0 8px;
  font-size: 1rem;
}

.visit-title {
  margin: 12px 0 4px;
  color: var(--neutral-dark);
  font-size: 0.8rem;
  text-transform: uppercase;
  letter-spacing: 0.04em;
}

.record-table {
  width: 100%;
  border-collapse: collapse;
}

.record-table th,
.record-table td {
  padding: 6px 8px;
  border-bottom: 1px solid var(--border);
  text-align: left;
}

.record-table thead th {
  color: var(--neutral-dark);
  font-size: 0.8rem;
  font-weight: 600;
}

.record-table tr.selected {
  background: var(--accent-bg);
}

.row-select {
  padding: 0;
  border: 0;
  background: transparent;
  color: var(--accent);
  cursor: pointer;
  text-align: left;
}

.numeric {
  font-variant-numeric: tabular-nums;
}

.flag {
  margin-left: 6px;
  padding: 0 6px;
  border-radius: 999px;
  background: var(--warning-bg);
  color: var(--warning);
  font-size: 0.75rem;
}
</style>

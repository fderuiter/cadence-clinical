<template>
  <section
    id="participant-record"
    class="record"
    aria-labelledby="participant-record-title"
    :aria-busy="record.loading"
  >
    <header class="record-header">
      <h2 id="participant-record-title">
        {{ participant.subject_id }}
      </h2>
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
      <QueriesPanel
        :study-code="canvas.studyCode"
        :subject-code="participant.subject_id"
        :enrollment-id="record.enrollmentId"
        :can-manage="canManage"
      />

      <section aria-labelledby="record-forms-title">
        <h3 id="record-forms-title">Forms</h3>
        <p id="record-forms-empty" class="status-line">
          Observations and forms are kept as kernel form instances. Open
          {{ participant.subject_id }}'s case report form on the
          <router-link
            id="record-open-ecrf"
            :to="{
              path: '/ecrf',
              query: {
                studyId: canvas.studyCode,
                subjectId: participant.subject_id,
              },
            }"
            >eCRF screen</router-link
          >.
        </p>
      </section>
    </template>
  </section>
</template>

<script setup lang="ts">
import { computed } from "vue";
import type { SubjectSummary } from "../../api/executionApi";
import { participantStateLabel } from "../../canvas/lifecycle";
import { useStudyCanvasStore } from "../../stores/studyCanvas";
import QueriesPanel from "./QueriesPanel.vue";

/**
 * One participant's data: the eCRF queries on the participant's kernel enrollment (#5461), and where the participant's
 * forms open. Values and forms are kernel form instances (ADR-2622), entered and read on the eCRF screen.
 */
defineProps<{
  participant: SubjectSummary;
  canManage: boolean;
}>();
const emit = defineEmits<{
  retry: [];
}>();

const canvas = useStudyCanvasStore();
const record = computed(() => canvas.record);
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
</style>

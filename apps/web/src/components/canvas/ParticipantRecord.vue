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

    <dl
      v-if="isPostRandomized"
      id="record-allocation-summary"
      class="allocation-summary"
    >
      <dt>Treatment allocation</dt>
      <dd id="record-allocation-arm">
        {{ allocationLabel }}
      </dd>
      <template v-if="assignedKitLabel">
        <dt>Assigned kit</dt>
        <dd id="record-allocation-kit">
          {{ assignedKitLabel }}
        </dd>
      </template>
    </dl>

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
      <ParticipantFormsPanel
        :subject-code="participant.subject_id"
        :enrollment-id="record.enrollmentId"
        :form-location="formLocation"
        :new-form-location="newFormLocation"
      />

      <QueriesPanel
        :study-code="canvas.studyCode"
        :subject-code="participant.subject_id"
        :enrollment-id="record.enrollmentId"
        :can-manage="canManage"
        :form-location="formLocation"
        :observation-location="observationLocation"
      />
    </template>
  </section>
</template>

<script setup lang="ts">
import { computed, watch } from "vue";
import type { RouteLocationRaw } from "vue-router";
import type { SubjectSummary } from "../../api/executionApi";
import { participantStateLabel } from "../../canvas/lifecycle";
import {
  isPostRandomizationStatus,
  useStudyCanvasStore,
} from "../../stores/studyCanvas";
import ParticipantFormsPanel from "./ParticipantFormsPanel.vue";
import QueriesPanel from "./QueriesPanel.vue";

/**
 * One participant's data inside Clinical Canvas: opened case report forms and queries on the participant's kernel
 * enrollment (ADR-2622, #5461), linking directly to forms and observations in the zoom path.
 */
const props = defineProps<{
  participant: SubjectSummary;
  canManage: boolean;
  formLocation: (formId: string) => RouteLocationRaw;
  newFormLocation: RouteLocationRaw;
  observationLocation: (
    observationId: string,
    formId: string
  ) => RouteLocationRaw;
}>();
const emit = defineEmits<{
  retry: [];
}>();

const canvas = useStudyCanvasStore();
const record = computed(() => canvas.record);

const isPostRandomized = computed(
  () =>
    isPostRandomizationStatus(props.participant.status) ||
    Boolean(canvas.allocations[props.participant.subject_id])
);
const subjectDetail = computed(
  () => canvas.subjectDetails[props.participant.subject_id] ?? null
);
const localAllocation = computed(
  () => canvas.allocations[props.participant.subject_id] ?? null
);
const allocationLabel = computed(() =>
  subjectDetail.value?.treatment_group
    ? subjectDetail.value.treatment_group
    : "Randomized, arm masked"
);
const assignedKitLabel = computed(
  () =>
    subjectDetail.value?.investigational_product_id ||
    localAllocation.value?.kitNumber ||
    ""
);

watch(
  () => [
    canvas.studyCode,
    props.participant.subject_id,
    props.participant.status,
  ],
  () => {
    if (
      isPostRandomizationStatus(props.participant.status) ||
      canvas.allocations[props.participant.subject_id]
    ) {
      canvas.loadSubjectDetail(props.participant);
    }
  },
  { immediate: true }
);
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

.allocation-summary {
  display: flex;
  flex-wrap: wrap;
  align-items: baseline;
  gap: 6px 16px;
  margin: 0;
  padding: 10px 12px;
  border: 1px solid var(--border);
  border-radius: 6px;
  background: var(--neutral-light);
  font-size: 0.88rem;
}

.allocation-summary dt {
  color: var(--neutral-dark);
  font-weight: 600;
}

.allocation-summary dd {
  margin: 0 12px 0 0;
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

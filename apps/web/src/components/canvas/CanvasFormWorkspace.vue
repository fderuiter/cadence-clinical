<template>
  <section
    id="canvas-form-workspace"
    class="canvas-form-workspace"
    aria-labelledby="canvas-form-workspace-title"
  >
    <router-link id="canvas-form-back" class="back" :to="backLocation">
      {{ backLabel }}
    </router-link>

    <header class="workspace-header">
      <h2 id="canvas-form-workspace-title">
        {{ workspaceTitle }}
      </h2>
      <span v-if="participant" class="workspace-meta">
        {{ participant.subject_id }} ·
        {{ participant.site_id ? `Site ${participant.site_id}` : "No site" }}
      </span>
    </header>

    <template v-if="!formId && recordLoading">
      <p id="canvas-form-loading-enrollment" class="status-line" role="status">
        Loading {{ participant?.subject_id || "participant" }}'s enrollment…
      </p>
    </template>

    <template v-else-if="!formId && !enrollmentId">
      <p id="canvas-form-no-enrollment" class="status-line" role="status">
        The kernel has no enrollment for
        {{ participant?.subject_id || "this participant" }} that you may see, so
        forms can't be opened here yet.
      </p>
    </template>

    <template v-else>
      <ParticipantFormsPanel
        v-if="!formId && enrollmentId"
        :subject-code="participant?.subject_id || 'participant'"
        :enrollment-id="enrollmentId"
        :form-location="formLocation"
        :new-form-location="null"
      />

      <EcrfFormPanel
        :form-id="formId"
        :enrollment-context="enrollmentContext"
        :can-manage-queries="canManageQueries"
        :can-verify-source="canVerifySource"
        :form-location="formLocation"
        :observation-location="observationLocation"
        @form-loaded="emit('form-loaded', $event)"
        @closed="emit('closed')"
      />
    </template>
  </section>
</template>

<script setup lang="ts">
import { computed, ref, watch } from "vue";
import type { RouteLocationRaw } from "vue-router";
import type { EcrfForm } from "../../api/ecrfFormsApi";
import type { SubjectSummary } from "../../api/executionApi";
import {
  findStudyScheduleOptions,
  type StudyScheduleOptions,
} from "../../canvas/kernelStudy";
import EcrfFormPanel, {
  type CanvasFormEnrollmentContext,
} from "../ecrf/EcrfFormPanel.vue";
import ParticipantFormsPanel from "./ParticipantFormsPanel.vue";

const props = defineProps<{
  studyCode: string;
  participant: SubjectSummary | null;
  enrollmentId: string | null;
  recordLoading?: boolean;
  /** UUID of an existing form to load, or null when opening a new form. */
  formId: string | null;
  formLabel?: string;
  canManageQueries: boolean;
  canVerifySource?: boolean;
  backLocation: RouteLocationRaw;
  backLabel: string;
  formLocation: (formId: string) => RouteLocationRaw;
  observationLocation: (
    observationId: string,
    formId: string
  ) => RouteLocationRaw;
}>();

const emit = defineEmits<{
  (e: "form-loaded", loaded: EcrfForm): void;
  (e: "closed"): void;
}>();

const scheduleOptions = ref<StudyScheduleOptions | null>(null);
let scheduleReq = 0;

watch(
  () => props.studyCode,
  async (code) => {
    const req = ++scheduleReq;
    scheduleOptions.value = null;
    if (!code) return;
    try {
      const loaded = await findStudyScheduleOptions(code);
      if (req === scheduleReq) scheduleOptions.value = loaded;
    } catch {
      if (req === scheduleReq) scheduleOptions.value = null;
    }
  },
  { immediate: true }
);

const workspaceTitle = computed(() => {
  if (props.formLabel) return props.formLabel;
  return props.formId ? "Case report form" : "Forms";
});

const enrollmentContext = computed<CanvasFormEnrollmentContext | null>(() => {
  if (!props.enrollmentId) return null;
  return {
    enrollmentId: props.enrollmentId,
    subjectCode: props.participant?.subject_id,
    collectionSpecs: scheduleOptions.value?.collectionSpecs ?? [],
    plannedActivities: scheduleOptions.value?.plannedActivities ?? [],
  };
});
</script>

<style scoped>
.canvas-form-workspace {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.back {
  align-self: flex-start;
  color: var(--semantic-color-interactive-primary);
  font-size: 0.85rem;
}

.workspace-header {
  display: flex;
  flex-wrap: wrap;
  align-items: baseline;
  gap: 12px;
}

.workspace-header h2 {
  margin: 0;
  font-size: 1.1rem;
}

.workspace-meta,
.status-line {
  margin: 0;
  color: var(--semantic-color-text-secondary);
  font-size: 0.85rem;
}
</style>

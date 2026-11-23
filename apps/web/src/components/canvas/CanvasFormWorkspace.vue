<template>
  <div id="canvas-form-workspace" class="canvas-form-workspace">
    <router-link id="canvas-form-back" class="back" :to="backLocation">
      {{ backLabel }}
    </router-link>

    <header class="workspace-header">
      <div class="workspace-title-group">
        <h2 id="canvas-form-workspace-title">
          {{ workspaceTitle }}
        </h2>
        <span v-if="participant" class="workspace-meta">
          {{ participant.subject_id }} ·
          {{ participant.site_id ? `Site ${participant.site_id}` : "No site" }}
        </span>
      </div>
      <router-link
        v-if="isDemoBuild() && handoffLocation"
        id="workspace-handoff-standalone"
        data-testid="workspace-ecrf-handoff"
        class="btn btn-secondary workspace-handoff-btn"
        :to="handoffLocation"
      >
        Open standalone form view
      </router-link>
    </header>

    <template v-if="!formId && recordLoading">
      <p id="canvas-form-loading-enrollment" class="status-line" role="status">
        Loading {{ participant?.subject_id || "participant" }}'s enrollment…
      </p>
      <div
        v-if="canvas.scheduleLoading"
        id="ecrf-schedule-skeleton"
        data-testid="ecrf-schedule-skeleton"
      >
        <SkeletonLoader
          type="row"
          :count="3"
          :columns="2"
          aria-label="Loading protocol schedule options…"
        />
      </div>
    </template>

    <template v-else-if="!formId && !enrollmentId">
      <p id="canvas-form-no-enrollment" class="status-line" role="status">
        There is no enrollment for
        {{ participant?.subject_id || "this participant" }} visible to your
        account, so forms can't be opened here yet.
      </p>
    </template>

    <template v-else>
      <ParticipantFormsPanel
        v-if="!formId && enrollmentId"
        :study-code="studyCode"
        :subject-code="participant?.subject_id || 'participant'"
        :enrollment-id="enrollmentId"
        :schedule="scheduleForStudy"
        :form-location="formLocation"
        :new-form-location="null"
        :new-form-cell-location="resolvedNewFormCellLocation"
      />

      <EcrfFormPanel
        :form-id="formId"
        :collection-spec-id="collectionSpecId"
        :planned-activity-id="plannedActivityId"
        :enrollment-context="enrollmentContext"
        :schedule-loading="!formId && canvas.scheduleLoading"
        :schedule-error="!formId ? canvas.scheduleError : null"
        :can-manage-queries="canManageQueries"
        :can-answer-queries="canAnswerQueries"
        :can-verify-source="canVerifySource"
        :highlight-item-id="highlightItemId"
        :form-location="formLocation"
        :observation-location="observationLocation"
        @retry-schedule="canvas.retryScheduleOptions(studyCode)"
        @dirty-change="emit('dirty-change', $event)"
        @form-loaded="emit('form-loaded', $event)"
        @closed="emit('closed')"
      />
    </template>
  </div>
</template>

<script setup lang="ts">
import { computed, onUnmounted, watch } from "vue";
import type { RouteLocationRaw } from "vue-router";
import type { EcrfForm } from "../../api/ecrfFormsApi";
import type { SubjectSummary } from "../../api/executionApi";
import { formatNodeRef, tryNodeRef } from "../../canvas/nodeRef";
import { useStudyCanvasStore } from "../../stores/studyCanvas";
import { isDemoBuild } from "../../utils/demoMode";
import SkeletonLoader from "../common/SkeletonLoader.vue";
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
  collectionSpecId?: string | null;
  plannedActivityId?: string | null;
  formLabel?: string;
  canManageQueries: boolean;
  canAnswerQueries?: boolean;
  canVerifySource?: boolean;
  highlightItemId?: string | null;
  backLocation: RouteLocationRaw;
  backLabel: string;
  handoffLocation?: RouteLocationRaw | null;
  formLocation: (formId: string, itemId?: string | null) => RouteLocationRaw;
  newFormCellLocation?: (
    collectionSpecId: string,
    plannedActivityId?: string | null
  ) => RouteLocationRaw;
  observationLocation: (
    observationId: string,
    formId: string,
    itemId?: string | null
  ) => RouteLocationRaw;
}>();

const emit = defineEmits<{
  (e: "dirty-change", dirty: boolean): void;
  (e: "form-loaded", loaded: EcrfForm): void;
  (e: "closed"): void;
}>();

const canvas = useStudyCanvasStore();

onUnmounted(() => {
  emit("dirty-change", false);
});

watch(
  () => [props.studyCode, props.formId] as const,
  ([code, formId]) => {
    if (code && !formId) {
      void canvas.loadScheduleOptions(code);
    }
  },
  { immediate: true }
);

const workspaceTitle = computed(() => {
  if (props.formLabel) return props.formLabel;
  return props.formId ? "Case report form" : "Forms";
});

const scheduleForStudy = computed(() =>
  Object.prototype.hasOwnProperty.call(
    canvas.scheduleOptionsByStudy,
    props.studyCode
  )
    ? canvas.scheduleOptionsByStudy[props.studyCode]
    : canvas.scheduleOptions
);

const resolvedNewFormCellLocation = computed(() => {
  if (props.newFormCellLocation) return props.newFormCellLocation;
  return (specId: string, visitId?: string | null): RouteLocationRaw => {
    const node = props.participant
      ? tryNodeRef("participant", props.participant.id)
      : null;
    const query: Record<string, string> = {
      stage: "conduct",
      form: "new",
    };
    if (node) {
      query.participant = formatNodeRef(node);
    }
    if (specId) {
      query.spec = specId;
    }
    if (visitId) {
      query.visit = visitId;
    }
    return {
      name: "study",
      params: { studyCode: props.studyCode },
      query,
    };
  };
});

const enrollmentContext = computed<CanvasFormEnrollmentContext | null>(() => {
  if (!props.enrollmentId) return null;
  return {
    enrollmentId: props.enrollmentId,
    subjectCode: props.participant?.subject_id,
    collectionSpecs: scheduleForStudy.value?.collectionSpecs ?? [],
    plannedActivities: scheduleForStudy.value?.plannedActivities ?? [],
    scheduleLoading: !props.formId && canvas.scheduleLoading,
    scheduleError: !props.formId ? canvas.scheduleError : null,
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
  align-items: center;
  justify-content: space-between;
  gap: 12px;
}

.workspace-title-group {
  display: flex;
  flex-wrap: wrap;
  align-items: baseline;
  gap: 12px;
}

.workspace-handoff-btn {
  font-size: 0.82rem;
  padding: 4px 10px;
  text-decoration: none;
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

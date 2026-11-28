<template>
  <div id="canvas-form-workspace" class="canvas-form-workspace">
    <router-link id="canvas-form-back" class="back" :to="backLocation">
      {{ backLabel }}
    </router-link>

    <header class="workspace-header">
      <div class="workspace-title-group">
        <h2 id="canvas-form-workspace-title" tabindex="-1">
          {{ workspaceTitle }}
        </h2>
        <span v-if="participant" class="workspace-meta">
          {{ participant.subject_id }} ·
          {{ participant.site_id ? `Site ${participant.site_id}` : "No site" }}
        </span>
      </div>
      <div class="workspace-header-actions">
        <button
          v-if="hasPreselectedSpec && enrollmentId && !recordLoading"
          id="btn-switch-visit-or-form"
          ref="switcherTriggerRef"
          data-testid="workspace-switch-visit-or-form"
          type="button"
          class="btn btn-secondary workspace-switcher-btn"
          aria-haspopup="dialog"
          :aria-expanded="switcherOpen ? 'true' : 'false'"
          @click="openSwitcher"
        >
          <CalendarRange
            class="workspace-switcher-icon"
            :size="16"
            :stroke-width="1.75"
            aria-hidden="true"
          />
          <span>Switch visit or form</span>
        </button>
        <router-link
          v-if="isDemoBuild() && handoffLocation"
          id="workspace-handoff-standalone"
          data-testid="workspace-ecrf-handoff"
          class="btn btn-secondary workspace-handoff-btn"
          :to="handoffLocation"
        >
          Open standalone form view
        </router-link>
      </div>
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
        v-if="!formId && enrollmentId && !hasPreselectedSpec"
        :study-code="studyCode"
        :site-code="participant?.site_id"
        :subject-code="participant?.subject_id || 'participant'"
        :enrollment-id="enrollmentId"
        :schedule="effectiveSchedule"
        :form-location="formLocation"
        :new-form-location="null"
        :new-form-cell-location="resolvedNewFormCellLocation"
        @forms-loaded="onFormsLoaded"
      />

      <ClinicalDrawer
        v-if="hasPreselectedSpec && enrollmentId"
        id="workspace-visit-switcher-drawer"
        v-model="switcherOpen"
        :teleport="false"
        mobile-bottom-sheet
        size="xl"
        title="Switch visit or form"
        :subtitle="participant?.subject_id || undefined"
        close-label="Close visit and form switcher"
        :restore-focus="resolveSwitcherRestoreFocus"
      >
        <ParticipantFormsPanel
          v-if="switcherOpen"
          :study-code="studyCode"
          :site-code="participant?.site_id"
          :subject-code="participant?.subject_id || 'participant'"
          :enrollment-id="enrollmentId"
          :schedule="effectiveSchedule"
          :form-location="formLocation"
          :new-form-location="null"
          :new-form-cell-location="resolvedNewFormCellLocation"
          @forms-loaded="onFormsLoaded"
          @select-form="onDrawerSelectForm"
          @select-new-cell="onDrawerSelectNewCell"
        />
      </ClinicalDrawer>

      <EcrfFormPanel
        ref="ecrfFormPanelRef"
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
        @retry-schedule="onRetrySchedule"
        @dirty-change="emit('dirty-change', $event)"
        @form-loaded="emit('form-loaded', $event)"
        @closed="emit('closed')"
      />
    </template>
  </div>
</template>

<script setup lang="ts">
import { CalendarRange } from "lucide-vue-next";
import { ClinicalDrawer } from "ui";
import { computed, onUnmounted, ref, watch } from "vue";
import type { RouteLocationRaw } from "vue-router";
import {
  ecrfFormsApi,
  type EcrfForm,
  type EcrfFormSummary,
} from "../../api/ecrfFormsApi";
import type { SubjectSummary } from "../../api/executionApi";
import { formatNodeRef, tryNodeRef } from "../../canvas/nodeRef";
import { useScopedNavigation } from "../../composables/useScopedNavigation";
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
const { scopedStudyLocation } = useScopedNavigation(() => props.studyCode);
const loadedEnrollmentForms = ref<EcrfFormSummary[]>([]);
const switcherOpen = ref(false);
const switcherTriggerRef = ref<HTMLButtonElement | null>(null);
const suppressSwitcherRestore = ref(false);
const ecrfFormPanelRef = ref<InstanceType<typeof EcrfFormPanel> | null>(null);
let enrollmentFormsSeq = 0;

const hasPreselectedSpec = computed(
  () => !props.formId && Boolean(props.collectionSpecId)
);

function openSwitcher() {
  suppressSwitcherRestore.value = false;
  switcherOpen.value = true;
}

function resolveSwitcherRestoreFocus() {
  if (suppressSwitcherRestore.value) return null;
  return switcherTriggerRef.value;
}

function onFormsLoaded(forms: EcrfFormSummary[]) {
  loadedEnrollmentForms.value = forms;
}

function onDrawerSelectForm() {
  suppressSwitcherRestore.value = true;
  switcherOpen.value = false;
}

function onDrawerSelectNewCell() {
  suppressSwitcherRestore.value = true;
  switcherOpen.value = false;
  void ecrfFormPanelRef.value?.focusNewFormOpener?.();
}

watch(
  () =>
    [props.formId, props.collectionSpecId, props.plannedActivityId] as const,
  () => {
    if (switcherOpen.value) {
      switcherOpen.value = false;
    }
  }
);

watch(
  () => [props.enrollmentId, props.formId, hasPreselectedSpec.value] as const,
  ([enrollmentId, formId, preselected]) => {
    const seq = ++enrollmentFormsSeq;
    if (!enrollmentId) {
      loadedEnrollmentForms.value = [];
      return;
    }
    if (
      !formId &&
      preselected &&
      typeof ecrfFormsApi?.listForEnrollment === "function"
    ) {
      void ecrfFormsApi
        .listForEnrollment(enrollmentId)
        .then((result) => {
          if (seq === enrollmentFormsSeq) {
            loadedEnrollmentForms.value = result ?? [];
          }
        })
        .catch(() => {
          if (seq === enrollmentFormsSeq) {
            loadedEnrollmentForms.value = [];
          }
        });
    }
  },
  { immediate: true }
);

onUnmounted(() => {
  emit("dirty-change", false);
});

watch(
  () => [props.studyCode, props.participant?.site_id, props.formId] as const,
  ([code, siteId, formId]) => {
    if (code && !formId) {
      void canvas.loadScheduleOptions(code, { siteCode: siteId ?? null });
    }
  },
  { immediate: true }
);

watch(
  () => [props.enrollmentId, props.formId] as const,
  ([enrollmentId, formId]) => {
    if (
      enrollmentId &&
      !formId &&
      typeof canvas.loadEnrollmentSchedule === "function"
    ) {
      void canvas.loadEnrollmentSchedule(enrollmentId);
    }
  },
  { immediate: true }
);

function onRetrySchedule() {
  void canvas.retryScheduleOptions(
    props.studyCode,
    props.participant?.site_id ?? null
  );
  if (
    props.enrollmentId &&
    typeof canvas.loadEnrollmentSchedule === "function"
  ) {
    void canvas.loadEnrollmentSchedule(props.enrollmentId, { force: true });
  }
}

const workspaceTitle = computed(() => {
  if (props.formLabel) return props.formLabel;
  return props.formId ? "Case report form" : "Forms";
});

const scheduleForStudy = computed(() =>
  canvas.getScheduleFor(props.studyCode, props.participant?.site_id ?? null)
);

const enrollmentSchedule = computed(() => {
  if (!props.enrollmentId || !canvas.enrollmentSchedulesById) return null;
  return canvas.enrollmentSchedulesById[props.enrollmentId] ?? null;
});

const effectiveSchedule = computed(
  () => enrollmentSchedule.value ?? scheduleForStudy.value
);

const resolvedNewFormCellLocation = computed(() => {
  if (props.newFormCellLocation) return props.newFormCellLocation;
  return (specId: string, visitId?: string | null): RouteLocationRaw => {
    const node = props.participant
      ? tryNodeRef("participant", props.participant.id)
      : null;
    return scopedStudyLocation({
      stage: "conduct",
      section: "",
      form: "new",
      ...(props.participant?.site_id
        ? { site: props.participant.site_id }
        : {}),
      ...(node ? { participant: formatNodeRef(node) } : {}),
      ...(specId ? { spec: specId } : {}),
      ...(visitId ? { visit: visitId } : {}),
    });
  };
});

const enrollmentContext = computed<CanvasFormEnrollmentContext | null>(() => {
  if (!props.enrollmentId) return null;
  const active = effectiveSchedule.value;
  return {
    enrollmentId: props.enrollmentId,
    subjectCode: props.participant?.subject_id,
    governanceStatus: active?.governanceStatus,
    guidance: active?.guidance ?? null,
    collectionSpecs: active?.collectionSpecs ?? [],
    plannedActivities: active?.plannedActivities ?? [],
    scheduleLoading: !props.formId && canvas.scheduleLoading,
    scheduleError: !props.formId ? canvas.scheduleError : null,
    existingForms: loadedEnrollmentForms.value,
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

.workspace-header-actions {
  display: inline-flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
}

.workspace-switcher-btn {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  min-height: 44px;
  padding: 6px 12px;
  font-size: 0.84rem;
}

.workspace-switcher-icon {
  flex: 0 0 auto;
}

.workspace-handoff-btn {
  display: inline-flex;
  align-items: center;
  min-height: 44px;
  font-size: 0.82rem;
  padding: 6px 12px;
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

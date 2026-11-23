<template>
  <div class="ecrf-form-open">
    <p class="ecrf-form-hint">
      <template v-if="enrollmentContext">
        Choose a form specification and visit for
        {{ enrollmentContext.subjectCode || "this participant" }}, then enter
        when the values were collected.
      </template>
      <template v-else>
        Each form is opened once for a participant's visit and then saved a page
        at a time. Select an opened form below, or open a new one for a
        participant.
      </template>
    </p>
    <form
      v-if="!enrollmentContext"
      class="ecrf-form-reuse-block"
      @submit.prevent="submitReuse"
    >
      <div class="ecrf-form-row">
        <label for="ecrf-existing-form-select">Opened form</label>
        <select
          id="ecrf-existing-form-select"
          v-model="selectedExistingFormId"
          @change="onSelectExistingForm"
        >
          <option value="">
            {{
              existingFormsLoading
                ? "Loading opened forms…"
                : availableEnrollmentForms.length > 0
                  ? "Select an opened form…"
                  : "No opened forms listed yet"
            }}
          </option>
          <option
            v-for="existing in availableEnrollmentForms"
            :key="existing.form_id"
            :value="existing.form_id"
          >
            {{ formatExistingFormLabel(existing) }}
          </option>
        </select>
        <button
          id="ecrf-form-load"
          class="btn btn-secondary"
          type="submit"
          :disabled="loading || !reuseId"
        >
          Load form
        </button>
      </div>
      <details class="ecrf-direct-id-disclosure">
        <summary class="ecrf-direct-id-summary">Enter form ID directly</summary>
        <div class="ecrf-form-row">
          <label for="ecrf-form-id">Form reference ID</label>
          <input
            id="ecrf-form-id"
            v-model.trim="reuseId"
            type="text"
            autocomplete="off"
            placeholder="Paste form reference ID"
          />
        </div>
      </details>
    </form>

    <div
      v-if="isScheduleLoading"
      id="ecrf-schedule-skeleton"
      data-testid="ecrf-schedule-skeleton"
      role="status"
      aria-label="Loading form schedule options"
    >
      <SkeletonLoader :lines="3" />
    </div>

    <div
      v-else-if="effectiveScheduleError"
      id="ecrf-schedule-error"
      data-testid="ecrf-schedule-error"
      class="ecrf-form-outcome ecrf-form-outcome--failed"
      role="alert"
    >
      <span>Form schedule options could not be loaded.</span>
      <SupportDetails :detail="effectiveScheduleError" />
      <button
        id="btn-retry-schedule"
        class="btn btn-secondary"
        type="button"
        @click="emit('retry-schedule')"
      >
        Retry
      </button>
    </div>

    <form
      v-else
      id="ecrf-form-new"
      class="ecrf-form-grid"
      @submit.prevent="submitOpenNew"
    >
      <template v-if="!enrollmentContext">
        <label for="ecrf-site-select">Study site</label>
        <select
          id="ecrf-site-select"
          v-model="selectedStandaloneSiteRef"
          @focus="ensureStandaloneSitesLoaded"
          @change="onStandaloneSiteChange"
        >
          <option value="">
            {{
              standaloneSitesLoading
                ? "Loading study sites…"
                : "Select study site…"
            }}
          </option>
          <option
            v-for="site in standaloneSiteOptions"
            :key="site.ref"
            :value="site.ref"
          >
            {{ site.label }}
          </option>
        </select>

        <label for="ecrf-enrollment-select">Participant</label>
        <select
          id="ecrf-enrollment-select"
          v-model="selectedStandaloneEnrollmentId"
          :disabled="standaloneEnrollmentsLoading"
          @change="onStandaloneEnrollmentChange"
        >
          <option value="">
            {{
              standaloneEnrollmentsLoading
                ? "Loading participants…"
                : "Select participant…"
            }}
          </option>
          <option
            v-for="enr in standaloneEnrollmentOptions"
            :key="enr.id"
            :value="enr.id"
          >
            {{ enr.label }}
          </option>
        </select>

        <details class="ecrf-direct-id-disclosure ecrf-grid-span">
          <summary class="ecrf-direct-id-summary">
            Enter enrollment ID directly
          </summary>
          <div class="ecrf-form-row">
            <label for="ecrf-enrollment-id">Participant enrollment ID</label>
            <input
              id="ecrf-enrollment-id"
              v-model.trim="localEnrollmentId"
              type="text"
              autocomplete="off"
              placeholder="Paste enrollment reference ID"
            />
          </div>
        </details>
      </template>
      <label for="ecrf-collection-spec">Collection spec</label>
      <select
        v-if="enrollmentContext && structuredSpecs.length > 0"
        id="ecrf-collection-spec"
        v-model="localCollectionSpecId"
      >
        <option value="">Select form specification…</option>
        <option v-for="spec in structuredSpecs" :key="spec.id" :value="spec.id">
          {{ spec.name ? `${spec.name} (${spec.id})` : spec.id }}
        </option>
      </select>
      <input
        v-else
        id="ecrf-collection-spec"
        v-model.trim="localCollectionSpecId"
        type="text"
        autocomplete="off"
      />
      <label for="ecrf-planned-activity">Visit (planned activity)</label>
      <select
        v-if="enrollmentContext && hasProtocolSchedule"
        id="ecrf-planned-activity"
        v-model="localPlannedActivityId"
      >
        <option value="">Unscheduled</option>
        <option
          v-for="activity in structuredActivities"
          :key="activity.id"
          :value="activity.id"
        >
          {{ activityLabel(activity) }}
        </option>
      </select>
      <input
        v-else
        id="ecrf-planned-activity"
        v-model.trim="localPlannedActivityId"
        type="text"
        autocomplete="off"
        placeholder="None for an unscheduled form"
      />
      <label for="ecrf-collected-at">Collected at</label>
      <input
        id="ecrf-collected-at"
        v-model="collectedAt"
        type="datetime-local"
      />
      <button
        id="ecrf-form-open"
        class="btn btn-primary"
        type="submit"
        :disabled="opening || !canOpen"
      >
        {{ opening ? "Opening…" : "Open form" }}
      </button>
    </form>

    <p v-if="loading" role="status">Loading form…</p>
    <p v-if="loadError" class="ecrf-form-error" role="alert">
      The form could not be loaded: {{ loadError }}
      <button
        id="ecrf-form-retry"
        class="btn btn-secondary"
        type="button"
        :disabled="loading"
        @click="emit('retry-load')"
      >
        Try again
      </button>
    </p>
    <p v-if="openError" class="ecrf-form-error" role="alert">
      The form could not be opened: {{ openError }}
    </p>
  </div>
</template>

<script lang="ts">
import type {
  StudyCollectionSpecOption,
  StudyPlannedActivityOption,
} from "../../canvas/kernelStudy";

export interface CanvasFormEnrollmentContext {
  enrollmentId: string;
  subjectCode?: string;
  collectionSpecs?: StudyCollectionSpecOption[];
  plannedActivities?: StudyPlannedActivityOption[];
  scheduleLoading?: boolean;
  scheduleError?: string | null;
}
</script>

<script setup lang="ts">
import { computed, ref, watch } from "vue";
import { getActivePinia } from "pinia";
import { ecrfFormsApi, type EcrfFormSummary } from "../../api/ecrfFormsApi";
import { kernelApi, type KernelChild } from "../../api/kernelApi";
import { parseNodeRef } from "../../canvas/nodeRef";
import { getNodeDisplay, seedNodeSummary } from "../../canvas/nodeResolver";
import SupportDetails from "../canvas/SupportDetails.vue";
import SkeletonLoader from "../common/SkeletonLoader.vue";

const props = defineProps<{
  formId?: string | null;
  enrollmentId?: string | null;
  collectionSpecId?: string | null;
  plannedActivityId?: string | null;
  enrollmentContext?: CanvasFormEnrollmentContext | null;
  scheduleLoading?: boolean;
  scheduleError?: string | null;
  loading: boolean;
  loadError: string | null;
  opening: boolean;
  openError: string | null;
}>();

const emit = defineEmits<{
  (e: "load-form", formId: string): void;
  (
    e: "open-form",
    payload: {
      enrollmentId: string;
      collectionSpecId: string;
      plannedActivityId: string | null;
      collectedAt: string;
    }
  ): void;
  (e: "retry-load"): void;
  (e: "retry-schedule"): void;
  (e: "enrollment-change"): void;
}>();

const reuseId = ref(props.formId ?? "");
const localEnrollmentId = ref(
  props.enrollmentContext?.enrollmentId ?? props.enrollmentId ?? ""
);
const localCollectionSpecId = ref(props.collectionSpecId ?? "");
const localPlannedActivityId = ref(props.plannedActivityId ?? "");
const collectedAt = ref("");

function getActiveWorkspaceContext(): {
  studyCode: string | null;
  subjectCode: string | null;
} {
  try {
    const stores = (
      getActivePinia() as unknown as {
        _s?: Map<string, Record<string, unknown>>;
      } | null
    )?._s;
    const canvas = stores?.get("studyCanvas");
    const clinicalScope = stores?.get("clinicalScope");
    const clinical = stores?.get("clinical");
    const studyCode =
      (typeof canvas?.studyCode === "string" && canvas.studyCode) ||
      (typeof clinicalScope?.activeStudyId === "string" &&
        clinicalScope.activeStudyId) ||
      (typeof clinical?.activeStudyId === "string" && clinical.activeStudyId) ||
      null;
    const subjectCode =
      (typeof canvas?.subjectCode === "string" && canvas.subjectCode) ||
      (typeof clinicalScope?.activeSubjectId === "string" &&
        clinicalScope.activeSubjectId) ||
      (typeof clinical?.activeSubjectId === "string" &&
        clinical.activeSubjectId) ||
      null;
    return { studyCode, subjectCode };
  } catch {
    return { studyCode: null, subjectCode: null };
  }
}

const selectedExistingFormId = ref("");
const availableEnrollmentForms = ref<EcrfFormSummary[]>([]);
const existingFormsLoading = ref(false);

const standaloneSitesLoaded = ref(false);
const standaloneSitesLoading = ref(false);
const standaloneSites = ref<Array<{ ref: string; label: string }>>([]);
const selectedStandaloneSiteRef = ref("");
const standaloneEnrollmentsLoading = ref(false);
const standaloneSiteEnrollments = ref<KernelChild[]>([]);
const selectedStandaloneEnrollmentId = ref(localEnrollmentId.value);

const UUID_ONLY_RE =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

const standaloneSiteOptions = computed(() => standaloneSites.value);

const standaloneEnrollmentOptions = computed(() => {
  const opts = standaloneSiteEnrollments.value
    .filter(
      (child) =>
        Boolean(child.node.ref) &&
        !child.node.mask &&
        child.node.ref!.startsWith("enrollment:")
    )
    .map((child) => {
      const parsed = parseNodeRef(child.node.ref!);
      const id = parsed?.id ?? child.node.ref!;
      return {
        id,
        label:
          child.node.code ||
          child.node.title ||
          getNodeDisplay(child.node.ref).label,
      };
    });

  const currentId = localEnrollmentId.value.trim();
  if (currentId && !opts.some((o) => o.id === currentId)) {
    const subjectLabel =
      getActiveWorkspaceContext().subjectCode ||
      getNodeDisplay(`enrollment:${currentId}`).label;
    opts.unshift({
      id: currentId,
      label:
        subjectLabel && !subjectLabel.startsWith("Participant #")
          ? `Participant ${subjectLabel}`
          : subjectLabel || "Selected participant",
    });
  }
  return opts;
});

function formatExistingFormLabel(summary: EcrfFormSummary): string {
  const base = summary.collection_title || summary.collection_spec_id;
  return summary.planned_activity_id
    ? `${base} · ${summary.planned_activity_id}`
    : base;
}

function onSelectExistingForm() {
  if (selectedExistingFormId.value) {
    reuseId.value = selectedExistingFormId.value;
  }
}

async function refreshAvailableFormsForEnrollment(targetEnrollmentId: string) {
  availableEnrollmentForms.value = [];
  selectedExistingFormId.value = "";
  if (
    props.enrollmentContext ||
    !UUID_ONLY_RE.test(targetEnrollmentId) ||
    typeof ecrfFormsApi.listForEnrollment !== "function"
  ) {
    return;
  }
  existingFormsLoading.value = true;
  try {
    const summaries = await ecrfFormsApi.listForEnrollment(targetEnrollmentId);
    availableEnrollmentForms.value = Array.isArray(summaries) ? summaries : [];
    for (const summary of availableEnrollmentForms.value) {
      seedNodeSummary(`collection_instance:${summary.form_id}`, {
        kind: "collection_instance",
        title: formatExistingFormLabel(summary),
      });
    }
  } catch {
    availableEnrollmentForms.value = [];
  } finally {
    existingFormsLoading.value = false;
  }
}

async function ensureStandaloneSitesLoaded() {
  if (
    standaloneSitesLoaded.value ||
    standaloneSitesLoading.value ||
    typeof kernelApi.listStudies !== "function" ||
    typeof kernelApi.getChildren !== "function"
  ) {
    return;
  }
  standaloneSitesLoading.value = true;
  try {
    const studies = await kernelApi.listStudies();
    const activeCode = getActiveWorkspaceContext().studyCode;
    const matchedStudy =
      studies.find((s) => !s.mask && activeCode && s.code === activeCode) ??
      studies.find((s) => !s.mask);
    if (!matchedStudy?.ref) return;
    const children = await kernelApi.getChildren(matchedStudy.ref);
    standaloneSites.value = (children ?? [])
      .filter(
        (c) =>
          Boolean(c.node.ref) &&
          !c.node.mask &&
          c.node.ref!.startsWith("study_site:")
      )
      .map((c) => {
        seedNodeSummary(c.node.ref!, {
          kind: c.node.kind,
          code: c.node.code,
          title: c.node.title,
          status: c.node.status,
        });
        return {
          ref: c.node.ref as string,
          label:
            c.node.code || c.node.title || getNodeDisplay(c.node.ref).label,
        };
      });
    standaloneSitesLoaded.value = true;
  } catch {
    standaloneSites.value = [];
  } finally {
    standaloneSitesLoading.value = false;
  }
}

async function onStandaloneSiteChange() {
  standaloneSiteEnrollments.value = [];
  selectedStandaloneEnrollmentId.value = "";
  if (
    !selectedStandaloneSiteRef.value ||
    typeof kernelApi.getChildren !== "function"
  ) {
    return;
  }
  standaloneEnrollmentsLoading.value = true;
  try {
    const children = await kernelApi.getChildren(
      selectedStandaloneSiteRef.value
    );
    standaloneSiteEnrollments.value = children ?? [];
    for (const child of standaloneSiteEnrollments.value) {
      if (child.node.ref && !child.node.mask) {
        seedNodeSummary(child.node.ref, {
          kind: child.node.kind,
          code: child.node.code,
          title: child.node.title,
          status: child.node.status,
        });
      }
    }
  } catch {
    standaloneSiteEnrollments.value = [];
  } finally {
    standaloneEnrollmentsLoading.value = false;
  }
}

function onStandaloneEnrollmentChange() {
  localEnrollmentId.value = selectedStandaloneEnrollmentId.value;
}

watch(
  localEnrollmentId,
  (next, prev) => {
    selectedStandaloneEnrollmentId.value = next;
    if (prev !== undefined && next !== prev) {
      emit("enrollment-change");
    }
    void refreshAvailableFormsForEnrollment(next.trim());
  },
  { immediate: true }
);

const isScheduleLoading = computed(
  () =>
    !props.formId &&
    Boolean(props.scheduleLoading || props.enrollmentContext?.scheduleLoading)
);
const effectiveScheduleError = computed(() =>
  !props.formId
    ? (props.scheduleError ?? props.enrollmentContext?.scheduleError ?? null)
    : null
);

const structuredSpecs = computed(
  () => props.enrollmentContext?.collectionSpecs ?? []
);
const hasProtocolSchedule = computed(
  () => (props.enrollmentContext?.plannedActivities?.length ?? 0) > 0
);
const structuredActivities = computed(() => {
  const all = props.enrollmentContext?.plannedActivities ?? [];
  if (!all.length) return [];
  const selectedSpec = structuredSpecs.value.find(
    (s) => s.id === localCollectionSpecId.value
  );
  if (!selectedSpec || !Array.isArray(selectedSpec.plannedActivityIds)) {
    return all;
  }
  if (selectedSpec.plannedActivityIds.length === 0) return [];
  const scheduled = new Set(selectedSpec.plannedActivityIds);
  return all.filter((a) => scheduled.has(a.id));
});

function activityLabel(activity: StudyPlannedActivityOption): string {
  const base = activity.name
    ? `${activity.name} (${activity.id})`
    : activity.id;
  return activity.studyDay !== null
    ? `${base} · Day ${activity.studyDay}`
    : base;
}

watch(
  () => props.formId,
  (next) => {
    reuseId.value = next ?? "";
  }
);

watch(
  () => props.enrollmentContext?.enrollmentId ?? props.enrollmentId ?? "",
  (next) => {
    localEnrollmentId.value = next;
  }
);

watch(
  () => props.collectionSpecId,
  (next) => {
    localCollectionSpecId.value = next ?? "";
  }
);

watch(
  () => props.plannedActivityId,
  (next) => {
    localPlannedActivityId.value = next ?? "";
  }
);

watch(structuredActivities, (allowed) => {
  if (
    localPlannedActivityId.value &&
    hasProtocolSchedule.value &&
    !allowed.some((a) => a.id === localPlannedActivityId.value)
  ) {
    localPlannedActivityId.value = "";
  }
});

const canOpen = computed(
  () =>
    localEnrollmentId.value.length > 0 &&
    localCollectionSpecId.value.length > 0 &&
    collectedAt.value.length > 0
);

function submitReuse() {
  if (reuseId.value) emit("load-form", reuseId.value);
}

function submitOpenNew() {
  if (!canOpen.value) return;
  emit("open-form", {
    enrollmentId: localEnrollmentId.value,
    collectionSpecId: localCollectionSpecId.value,
    plannedActivityId: localPlannedActivityId.value || null,
    collectedAt: new Date(collectedAt.value).toISOString(),
  });
}
</script>

<style scoped>
.ecrf-form-hint {
  font-size: 0.85rem;
  color: var(--semantic-color-text-secondary);
}
.ecrf-form-row {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
  margin: 8px 0;
}
.ecrf-form-grid {
  display: grid;
  grid-template-columns: minmax(140px, max-content) minmax(0, 1fr);
  gap: 6px 12px;
  align-items: center;
  min-width: 0;
}
.ecrf-form-grid > * {
  min-width: 0;
  max-width: 100%;
}
.ecrf-form-grid button {
  grid-column: 2;
  justify-self: start;
}
.ecrf-form-reuse-block {
  display: flex;
  flex-direction: column;
  gap: 4px;
  margin: 8px 0;
}
.ecrf-grid-span {
  grid-column: 1 / -1;
}
.ecrf-direct-id-disclosure {
  font-size: 0.82rem;
  color: var(--semantic-color-text-secondary);
}
.ecrf-direct-id-summary {
  cursor: pointer;
  font-weight: 500;
  color: var(--semantic-color-text-secondary);
}
@media (max-width: 639px) {
  .ecrf-form-grid {
    grid-template-columns: minmax(0, 1fr);
  }
  .ecrf-form-grid button {
    grid-column: 1;
    justify-self: stretch;
  }
}
.ecrf-form-error {
  color: var(--semantic-color-error);
  font-size: 0.8rem;
  margin: 2px 0 0;
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
}
.ecrf-form-outcome {
  border-radius: 6px;
  padding: 8px 12px;
  font-size: 0.85rem;
  display: flex;
  gap: 8px;
  align-items: center;
  flex-wrap: wrap;
}
.ecrf-form-outcome--failed {
  background-color: var(--semantic-color-error-bg-soft);
  color: var(--semantic-color-error-text-dark);
}
</style>

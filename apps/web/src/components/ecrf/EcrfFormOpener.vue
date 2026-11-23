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
        at a time. Reuse a form by its id, or open a new one.
      </template>
    </p>
    <form
      v-if="!enrollmentContext"
      class="ecrf-form-row"
      @submit.prevent="submitReuse"
    >
      <label for="ecrf-form-id">Form id</label>
      <input
        id="ecrf-form-id"
        v-model.trim="reuseId"
        type="text"
        autocomplete="off"
        placeholder="Form id (UUID)"
      />
      <button
        id="ecrf-form-load"
        class="btn btn-secondary"
        type="submit"
        :disabled="loading || !reuseId"
      >
        Load form
      </button>
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
      <span>
        Form schedule options could not be loaded:
        {{ effectiveScheduleError }}
      </span>
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
        <label for="ecrf-enrollment-id">Enrollment id</label>
        <input
          id="ecrf-enrollment-id"
          v-model.trim="localEnrollmentId"
          type="text"
          autocomplete="off"
        />
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
    emit("enrollment-change");
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

<template>
  <section
    id="study-queries-section"
    class="study-queries"
    aria-labelledby="study-queries-title"
    :aria-busy="queries.loading || studyLoading"
  >
    <header class="study-queries-header">
      <h2 id="study-queries-title">Queries</h2>
      <div class="study-queries-filters" data-testid="study-queries-filters">
        <label for="study-queries-status">Status</label>
        <select
          id="study-queries-status"
          :value="statusFilter"
          @change="onStatusChange"
        >
          <option value="">All statuses</option>
          <option value="open">Open</option>
          <option value="answered">Answered</option>
          <option value="closed">Closed</option>
          <option value="cancelled">Cancelled</option>
        </select>

        <label for="study-queries-site">Site</label>
        <input
          id="study-queries-site"
          :value="siteFilter"
          type="text"
          autocomplete="off"
          placeholder="All sites"
          list="study-queries-sites"
          @input="onSiteInput"
        />
        <datalist id="study-queries-sites">
          <option v-for="code in knownSites" :key="code" :value="code" />
        </datalist>

        <label for="study-queries-participant">Participant</label>
        <input
          id="study-queries-participant"
          :value="participantFilter"
          type="text"
          autocomplete="off"
          placeholder="All participants"
          list="study-queries-participants"
          @input="onParticipantInput"
        />
        <datalist id="study-queries-participants">
          <option v-for="code in knownParticipants" :key="code" :value="code" />
        </datalist>

        <label for="study-queries-changed">Value changed</label>
        <select
          id="study-queries-changed"
          :value="changedFilter"
          @change="onChangedChange"
        >
          <option value="">All queries</option>
          <option value="true">Value changed</option>
          <option value="false">Unchanged</option>
        </select>

        <button
          v-if="hasActiveFilter"
          id="btn-clear-query-filters"
          type="button"
          class="btn btn-secondary"
          @click="clearFilters"
        >
          Clear filters
        </button>
      </div>
    </header>

    <p v-if="studyLoading" class="status-line" role="status">
      Looking up {{ studyCode }}…
    </p>
    <p
      v-else-if="studyError"
      id="study-queries-error"
      class="status-line"
      role="alert"
    >
      {{ studyCode }}'s queries couldn't be loaded: {{ studyError }}
    </p>
    <p v-else-if="!studyId" id="study-queries-no-study" class="status-line">
      The kernel has no study {{ studyCode }} that you may see, so its queries
      cannot be listed here.
    </p>
    <p v-else-if="queries.loading" class="status-line" role="status">
      Loading queries for {{ studyCode }}…
    </p>
    <p
      v-else-if="queries.loadError"
      id="study-queries-error"
      class="status-line"
      role="alert"
    >
      {{ studyCode }}'s queries couldn't be loaded: {{ queries.loadError }}
    </p>
    <p
      v-else-if="queries.queries.length === 0"
      id="study-queries-empty"
      class="status-line"
    >
      {{
        hasActiveFilter
          ? "No queries match these filters."
          : `No queries on visible enrollments in ${studyCode}.`
      }}
    </p>
    <FormQueryList
      v-else
      :queries="queries.queries"
      :can-manage="canManage"
      :busy="queries.busy"
      :study-code="studyCode"
      :act="queries.act"
      :act-many="queries.actMany"
      show-form
      show-context
    />

    <p
      id="study-queries-outcome"
      class="status-line"
      aria-live="polite"
      role="status"
    >
      {{ queries.outcome?.message ?? "" }}
    </p>
    <p v-if="!canManage" id="study-queries-note" class="status-line">
      A CRA or data manager closes, reopens and cancels queries.
    </p>
  </section>
</template>

<script setup lang="ts">
import { computed, reactive, ref, watch } from "vue";
import type { FormQueryStatus } from "../../api/ecrfQueriesApi";
import { findKernelStudyRef } from "../../canvas/kernelStudy";
import { parseNodeRef } from "../../canvas/nodeRef";
import { useFormQueries } from "../../composables/useFormQueries";
import FormQueryList from "../ecrf/FormQueryList.vue";

export interface StudyQueriesFilterState {
  status: string;
  site: string;
  participant: string;
  changed: string;
}

const VALID_STATUSES = new Set<FormQueryStatus>([
  "open",
  "answered",
  "closed",
  "cancelled",
]);

const props = defineProps<{
  studyCode: string;
  canManage: boolean;
  status?: string;
  site?: string;
  participant?: string;
  changed?: string;
}>();

const emit = defineEmits<{
  filter: [next: StudyQueriesFilterState];
}>();

const statusFilter = ref(props.status ?? "");
const siteFilter = ref(props.site ?? "");
const participantFilter = ref(props.participant ?? "");
const changedFilter = ref(props.changed ?? "");

watch(
  () => [props.status, props.site, props.participant, props.changed],
  ([status, site, participant, changed]) => {
    if (status !== undefined) statusFilter.value = status;
    if (site !== undefined) siteFilter.value = site;
    if (participant !== undefined) participantFilter.value = participant;
    if (changed !== undefined) changedFilter.value = changed;
  }
);

const studyId = ref<string | null>(null);
const studyLoading = ref(false);
const studyError = ref<string | null>(null);
let studyLookup = 0;

const queries = reactive(useFormQueries());
const seenSites = ref<string[]>([]);
const seenParticipants = ref<string[]>([]);

const knownSites = computed(() => [...new Set(seenSites.value)].sort());
const knownParticipants = computed(() =>
  [...new Set(seenParticipants.value)].sort()
);

const hasActiveFilter = computed(() =>
  Boolean(
    statusFilter.value ||
    siteFilter.value.trim() ||
    participantFilter.value.trim() ||
    changedFilter.value
  )
);

function syncFilterUrl() {
  emit("filter", {
    status: statusFilter.value,
    site: siteFilter.value.trim(),
    participant: participantFilter.value.trim(),
    changed: changedFilter.value,
  });
}

function onStatusChange(event: Event) {
  statusFilter.value = (event.target as HTMLSelectElement).value;
  syncFilterUrl();
}

function onSiteInput(event: Event) {
  siteFilter.value = (event.target as HTMLInputElement).value;
  syncFilterUrl();
}

function onParticipantInput(event: Event) {
  participantFilter.value = (event.target as HTMLInputElement).value;
  syncFilterUrl();
}

function onChangedChange(event: Event) {
  changedFilter.value = (event.target as HTMLSelectElement).value;
  syncFilterUrl();
}

function clearFilters() {
  statusFilter.value = "";
  siteFilter.value = "";
  participantFilter.value = "";
  changedFilter.value = "";
  syncFilterUrl();
}

async function refreshQueries() {
  if (!studyId.value) return;
  const normalizedStatus = VALID_STATUSES.has(
    statusFilter.value as FormQueryStatus
  )
    ? (statusFilter.value as FormQueryStatus)
    : "";
  const valueChanged =
    changedFilter.value === "true"
      ? true
      : changedFilter.value === "false"
        ? false
        : null;
  await queries.load({
    studyId: studyId.value,
    filters: {
      status: normalizedStatus,
      site: siteFilter.value.trim() || undefined,
      participant: participantFilter.value.trim() || undefined,
      valueChanged,
    },
  });
  for (const q of queries.queries) {
    if (q.site_code && !seenSites.value.includes(q.site_code)) {
      seenSites.value.push(q.site_code);
    }
    if (q.subject_code && !seenParticipants.value.includes(q.subject_code)) {
      seenParticipants.value.push(q.subject_code);
    }
  }
}

watch(
  () => props.studyCode,
  async (code) => {
    const request = ++studyLookup;
    studyId.value = null;
    studyError.value = null;
    seenSites.value = [];
    seenParticipants.value = [];
    queries.clear();
    if (!code) return;
    studyLoading.value = true;
    try {
      const refText = await findKernelStudyRef(code);
      if (request !== studyLookup) return;
      const parsed = refText ? parseNodeRef(refText) : null;
      studyId.value = parsed?.kind === "study" ? parsed.id : null;
      if (studyId.value) {
        await refreshQueries();
      }
    } catch (error) {
      if (request !== studyLookup) return;
      studyError.value = error instanceof Error ? error.message : String(error);
    } finally {
      if (request === studyLookup) studyLoading.value = false;
    }
  },
  { immediate: true }
);

watch([statusFilter, siteFilter, participantFilter, changedFilter], () => {
  if (studyId.value && !studyLoading.value) {
    void refreshQueries();
  }
});
</script>

<style scoped>
.study-queries {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.study-queries-header {
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.study-queries-header h2 {
  margin: 0;
  font-size: 1.15rem;
}

.study-queries-filters {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
  font-size: 0.85rem;
}

.study-queries-filters select,
.study-queries-filters input {
  padding: 5px 8px;
  border: 1px solid var(--border);
  border-radius: 6px;
  background: #fff;
  font-size: 0.85rem;
}

.status-line {
  margin: 0;
  color: var(--neutral-dark, #475569);
  font-size: 0.85rem;
}

.status-line:empty {
  display: none;
}
</style>

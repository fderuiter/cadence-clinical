<template>
  <section
    id="study-queries-section"
    class="study-queries"
    aria-labelledby="study-queries-title"
    :aria-busy="queries.loading || queries.refreshing || studyLoading"
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

        <label for="study-queries-owner">Owner</label>
        <select
          id="study-queries-owner"
          :value="ownerFilter"
          @change="onOwnerChange"
        >
          <option value="">All owners</option>
          <option value="site">Site</option>
          <option value="monitor">Monitor</option>
          <option value="data_manager">Data manager</option>
        </select>

        <label for="study-queries-origin">Origin</label>
        <select
          id="study-queries-origin"
          :value="originFilter"
          @change="onOriginChange"
        >
          <option value="">All origins</option>
          <option value="manual">Manual</option>
          <option value="system">System</option>
        </select>

        <label for="study-queries-priority">Priority</label>
        <select
          id="study-queries-priority"
          :value="priorityFilter"
          @change="onPriorityChange"
        >
          <option value="">All priorities</option>
          <option value="high">High</option>
          <option value="normal">Normal</option>
          <option value="low">Low</option>
        </select>

        <label for="study-queries-site">Site</label>
        <input
          id="study-queries-site"
          ref="siteInputRef"
          :value="siteInput"
          type="text"
          autocomplete="off"
          placeholder="All sites"
          list="study-queries-sites"
          @focus="siteFocused = true"
          @blur="onSiteBlur"
          @input="onSiteInput"
        />
        <datalist id="study-queries-sites">
          <option v-for="code in knownSites" :key="code" :value="code" />
        </datalist>

        <label for="study-queries-participant">Participant</label>
        <input
          id="study-queries-participant"
          ref="participantInputRef"
          :value="participantInput"
          type="text"
          autocomplete="off"
          placeholder="All participants"
          list="study-queries-participants"
          @focus="participantFocused = true"
          @blur="onParticipantBlur"
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
    <div
      v-else-if="studyError"
      id="study-queries-error"
      class="status-line"
      role="alert"
    >
      <p class="status-line">{{ studyCode }}'s queries couldn't be loaded.</p>
      <SupportDetails :detail="studyError" />
    </div>
    <p v-else-if="!studyId" id="study-queries-no-study" class="status-line">
      Study {{ studyCode }} wasn't found or isn't visible to your account, so
      its queries cannot be listed here.
    </p>
    <p v-else-if="queries.loading" class="status-line" role="status">
      Loading queries for {{ studyCode }}…
    </p>
    <template v-else>
      <p
        v-if="queries.refreshing"
        id="study-queries-refreshing"
        class="status-line"
        role="status"
        aria-live="polite"
      >
        Refreshing queries for {{ studyCode }}…
      </p>
      <div
        v-if="queries.loadError"
        id="study-queries-error"
        class="status-line"
        role="alert"
      >
        <p class="status-line">{{ studyCode }}'s queries couldn't be loaded.</p>
        <SupportDetails :detail="queries.loadError" />
      </div>
      <p
        v-if="queries.queries.length === 0 && !queries.loadError"
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
        v-else-if="queries.queries.length > 0"
        :queries="queries.queries"
        :can-manage="canManage"
        :can-answer="canAnswer"
        :busy="queries.busy"
        :study-code="studyCode"
        :form-to="queryFormLocation"
        :observation-to="queryObservationLocation"
        :act="queries.act"
        :act-many="queries.actMany"
        show-form
        show-context
      />
    </template>

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
import { computed, onBeforeUnmount, reactive, ref, watch } from "vue";
import type { RouteLocationRaw } from "vue-router";
import type {
  FormQueryOrigin,
  FormQueryOwnerRole,
  FormQueryPriority,
  FormQueryResponse,
  FormQueryStatus,
} from "../../api/ecrfQueriesApi";
import {
  collectionInstanceRef,
  observationNodeRef,
} from "../../canvas/nodeRef";
import { useFormQueries } from "../../composables/useFormQueries";
import { useKernelStudyRef } from "../../composables/useKernelStudyRef";
import FormQueryList from "../ecrf/FormQueryList.vue";
import SupportDetails from "./SupportDetails.vue";

export interface StudyQueriesFilterState {
  status: string;
  owner: string;
  origin: string;
  priority: string;
  site: string;
  participant: string;
  changed: string;
}

const FILTER_DELAY_MS = 250;

const VALID_STATUSES = new Set<FormQueryStatus>([
  "open",
  "answered",
  "closed",
  "cancelled",
]);
const VALID_OWNERS = new Set<FormQueryOwnerRole>([
  "site",
  "monitor",
  "data_manager",
]);
const VALID_ORIGINS = new Set<FormQueryOrigin>(["manual", "system"]);
const VALID_PRIORITIES = new Set<FormQueryPriority>(["low", "normal", "high"]);

const props = withDefaults(
  defineProps<{
    studyCode: string;
    canManage: boolean;
    canAnswer?: boolean;
    status?: string;
    owner?: string;
    origin?: string;
    priority?: string;
    site?: string;
    participant?: string;
    changed?: string;
  }>(),
  {
    canAnswer: undefined,
  }
);

const emit = defineEmits<{
  filter: [next: StudyQueriesFilterState];
}>();

const siteInputRef = ref<HTMLInputElement | null>(null);
const participantInputRef = ref<HTMLInputElement | null>(null);
const siteFocused = ref(false);
const participantFocused = ref(false);

const statusFilter = ref(props.status ?? "");
const ownerFilter = ref(props.owner ?? "");
const originFilter = ref(props.origin ?? "");
const priorityFilter = ref(props.priority ?? "");
const siteInput = ref(props.site ?? "");
const participantInput = ref(props.participant ?? "");
const appliedSite = ref((props.site ?? "").trim());
const appliedParticipant = ref((props.participant ?? "").trim());
const changedFilter = ref(props.changed ?? "");

let filterTimer: ReturnType<typeof setTimeout> | undefined;
let queryAbortController: AbortController | null = null;

function abortActiveQueryRead() {
  if (queryAbortController) {
    queryAbortController.abort();
    queryAbortController = null;
  }
}

function isSiteFocused(): boolean {
  return (
    siteFocused.value ||
    (typeof document !== "undefined" &&
      siteInputRef.value !== null &&
      document.activeElement === siteInputRef.value)
  );
}

function isParticipantFocused(): boolean {
  return (
    participantFocused.value ||
    (typeof document !== "undefined" &&
      participantInputRef.value !== null &&
      document.activeElement === participantInputRef.value)
  );
}

watch(
  () => [
    props.status,
    props.owner,
    props.origin,
    props.priority,
    props.site,
    props.participant,
    props.changed,
  ],
  ([status, owner, origin, priority, site, participant, changed]) => {
    if (status !== undefined) statusFilter.value = status;
    if (owner !== undefined) ownerFilter.value = owner;
    if (origin !== undefined) originFilter.value = origin;
    if (priority !== undefined) priorityFilter.value = priority;
    if (site !== undefined) {
      appliedSite.value = site.trim();
      if (!isSiteFocused()) {
        siteInput.value = site;
      }
    }
    if (participant !== undefined) {
      appliedParticipant.value = participant.trim();
      if (!isParticipantFocused()) {
        participantInput.value = participant;
      }
    }
    if (changed !== undefined) changedFilter.value = changed;
  }
);

const queries = reactive(useFormQueries());
const seenSites = ref<string[]>([]);
const seenParticipants = ref<string[]>([]);

const { studyId, studyLoading, studyError } = useKernelStudyRef(
  () => props.studyCode,
  {
    onReset: () => {
      clearTimeout(filterTimer);
      filterTimer = undefined;
      abortActiveQueryRead();
      seenSites.value = [];
      seenParticipants.value = [];
      queries.clear();
    },
    onResolved: async () => {
      if (studyId.value) {
        await refreshQueries();
      }
    },
  }
);

const knownSites = computed(() => [...new Set(seenSites.value)].sort());
const knownParticipants = computed(() =>
  [...new Set(seenParticipants.value)].sort()
);

const hasActiveFilter = computed(() =>
  Boolean(
    statusFilter.value ||
    ownerFilter.value ||
    originFilter.value ||
    priorityFilter.value ||
    siteInput.value.trim() ||
    participantInput.value.trim() ||
    changedFilter.value
  )
);

function queryFormLocation(query: FormQueryResponse): RouteLocationRaw {
  const formRef = collectionInstanceRef(query.form_id) ?? query.form_id;
  return {
    name: "study",
    params: { studyCode: props.studyCode },
    query: {
      stage: "data",
      section: "queries",
      ...(query.site_code ? { site: query.site_code } : {}),
      form: formRef,
      item: query.item_id,
    },
  };
}

function queryObservationLocation(
  query: FormQueryResponse
): RouteLocationRaw | null {
  const obsRef = observationNodeRef(query.observation_id);
  if (!obsRef) return null;
  const formRef = collectionInstanceRef(query.form_id);
  return {
    name: "study",
    params: { studyCode: props.studyCode },
    query: {
      stage: "data",
      section: "queries",
      ...(query.site_code ? { site: query.site_code } : {}),
      ...(formRef ? { form: formRef } : {}),
      ...(query.item_id ? { item: query.item_id } : {}),
      observation: obsRef,
    },
  };
}

function syncFilterUrl() {
  emit("filter", {
    status: statusFilter.value,
    owner: ownerFilter.value,
    origin: originFilter.value,
    priority: priorityFilter.value,
    site: appliedSite.value,
    participant: appliedParticipant.value,
    changed: changedFilter.value,
  });
}

function flushTextFilters(): boolean {
  clearTimeout(filterTimer);
  filterTimer = undefined;
  const nextSite = siteInput.value.trim();
  const nextParticipant = participantInput.value.trim();
  const changed =
    nextSite !== appliedSite.value ||
    nextParticipant !== appliedParticipant.value;
  appliedSite.value = nextSite;
  appliedParticipant.value = nextParticipant;
  if (!isSiteFocused()) siteInput.value = nextSite;
  if (!isParticipantFocused()) participantInput.value = nextParticipant;
  return changed;
}

function scheduleTextFilterSync() {
  clearTimeout(filterTimer);
  filterTimer = setTimeout(() => {
    if (flushTextFilters()) {
      syncFilterUrl();
    }
  }, FILTER_DELAY_MS);
}

function onSiteBlur() {
  siteFocused.value = false;
  if (!filterTimer) {
    siteInput.value = appliedSite.value;
  }
}

function onParticipantBlur() {
  participantFocused.value = false;
  if (!filterTimer) {
    participantInput.value = appliedParticipant.value;
  }
}

function onStatusChange(event: Event) {
  statusFilter.value = (event.target as HTMLSelectElement).value;
  flushTextFilters();
  syncFilterUrl();
}

function onOwnerChange(event: Event) {
  ownerFilter.value = (event.target as HTMLSelectElement).value;
  flushTextFilters();
  syncFilterUrl();
}

function onOriginChange(event: Event) {
  originFilter.value = (event.target as HTMLSelectElement).value;
  flushTextFilters();
  syncFilterUrl();
}

function onPriorityChange(event: Event) {
  priorityFilter.value = (event.target as HTMLSelectElement).value;
  flushTextFilters();
  syncFilterUrl();
}

function onSiteInput(event: Event) {
  siteInput.value = (event.target as HTMLInputElement).value;
  scheduleTextFilterSync();
}

function onParticipantInput(event: Event) {
  participantInput.value = (event.target as HTMLInputElement).value;
  scheduleTextFilterSync();
}

function onChangedChange(event: Event) {
  changedFilter.value = (event.target as HTMLSelectElement).value;
  flushTextFilters();
  syncFilterUrl();
}

function clearFilters() {
  clearTimeout(filterTimer);
  filterTimer = undefined;
  statusFilter.value = "";
  ownerFilter.value = "";
  originFilter.value = "";
  priorityFilter.value = "";
  siteInput.value = "";
  participantInput.value = "";
  appliedSite.value = "";
  appliedParticipant.value = "";
  changedFilter.value = "";
  syncFilterUrl();
}

onBeforeUnmount(() => {
  clearTimeout(filterTimer);
  abortActiveQueryRead();
});

async function refreshQueries() {
  if (!studyId.value) return;
  abortActiveQueryRead();
  const controller = new AbortController();
  queryAbortController = controller;
  const normalizedStatus = VALID_STATUSES.has(
    statusFilter.value as FormQueryStatus
  )
    ? (statusFilter.value as FormQueryStatus)
    : "";
  const normalizedOwner = VALID_OWNERS.has(
    ownerFilter.value as FormQueryOwnerRole
  )
    ? (ownerFilter.value as FormQueryOwnerRole)
    : "";
  const normalizedOrigin = VALID_ORIGINS.has(
    originFilter.value as FormQueryOrigin
  )
    ? (originFilter.value as FormQueryOrigin)
    : "";
  const normalizedPriority = VALID_PRIORITIES.has(
    priorityFilter.value as FormQueryPriority
  )
    ? (priorityFilter.value as FormQueryPriority)
    : "";
  const valueChanged =
    changedFilter.value === "true"
      ? true
      : changedFilter.value === "false"
        ? false
        : null;
  await queries.load(
    {
      studyId: studyId.value,
      filters: {
        status: normalizedStatus,
        ...(normalizedOwner ? { owner: normalizedOwner } : {}),
        ...(normalizedOrigin ? { origin: normalizedOrigin } : {}),
        ...(normalizedPriority ? { priority: normalizedPriority } : {}),
        site: appliedSite.value || undefined,
        participant: appliedParticipant.value || undefined,
        valueChanged,
      },
    },
    { signal: controller.signal }
  );
  if (controller.signal.aborted) return;
  if (queryAbortController === controller) {
    queryAbortController = null;
  }
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
  [
    statusFilter,
    ownerFilter,
    originFilter,
    priorityFilter,
    appliedSite,
    appliedParticipant,
    changedFilter,
  ],
  () => {
    if (studyId.value && !studyLoading.value) {
      void refreshQueries();
    }
  }
);
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
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 6px;
  background: var(--semantic-color-surface);
  font-size: 0.85rem;
}

.status-line {
  margin: 0;
  color: var(--semantic-color-text-secondary);
  font-size: 0.85rem;
}

.status-line:empty {
  display: none;
}
</style>

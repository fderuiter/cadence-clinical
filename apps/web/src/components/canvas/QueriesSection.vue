<template>
  <section
    id="study-queries-section"
    class="study-queries"
    aria-labelledby="study-queries-title"
    :aria-busy="queries.loading || queries.refreshing || studyLoading"
  >
    <header class="study-queries-header">
      <div class="study-queries-title-row">
        <h2 id="study-queries-title" tabindex="-1">Queries</h2>
        <button
          v-if="canManage && studyId && !slaFormOpen"
          id="btn-run-sla-check"
          type="button"
          class="btn btn-secondary study-queries-sla-trigger"
          :disabled="slaRunning"
          @click="openSlaCheck"
        >
          <svg
            width="16"
            height="16"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            stroke-width="1.75"
            stroke-linecap="round"
            stroke-linejoin="round"
            aria-hidden="true"
          >
            <path d="M12 6v6l4 2" />
            <path d="M16 21.16a10 10 0 1 1 5-13.516" />
            <path d="M20 11.5v6" />
            <path d="M20 21.5h.01" />
          </svg>
          <span>Run 14-day SLA check</span>
        </button>
      </div>

      <form
        v-if="canManage && studyId && slaFormOpen"
        id="study-queries-sla-form"
        class="study-queries-sla-form"
        aria-label="Run 14-day SLA check"
        @submit.prevent="confirmSlaCheck"
      >
        <label for="study-queries-sla-reason">
          Reason for running 14-day SLA check
        </label>
        <input
          id="study-queries-sla-reason"
          v-model="slaReason"
          type="text"
          autocomplete="off"
          placeholder="Scheduled monitoring review"
          :disabled="slaRunning"
        />
        <div class="study-queries-sla-actions">
          <button
            id="btn-confirm-sla-check"
            type="submit"
            class="btn btn-primary"
            :disabled="slaRunning || !slaReason.trim()"
          >
            {{ slaRunning ? "Running SLA check…" : "Confirm SLA check" }}
          </button>
          <button
            id="btn-cancel-sla-check"
            type="button"
            class="btn btn-secondary"
            :disabled="slaRunning"
            @click="cancelSlaCheck"
          >
            Back
          </button>
        </div>
      </form>

      <ClinicalLiveRegion
        id="study-queries-sla-outcome"
        test-id="study-queries-sla-outcome"
        variant="inline"
        :active="Boolean(slaOutcome)"
        :politeness="slaOutcome?.error ? 'assertive' : 'polite'"
        :tone="slaOutcome?.error ? 'error' : 'success'"
        :live-class="[
          'study-queries-sla-outcome',
          { 'study-queries-sla-outcome--error': Boolean(slaOutcome?.error) },
        ]"
        :message="slaOutcome?.message || ''"
      />

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

        <label for="study-queries-escalated">SLA</label>
        <select
          id="study-queries-escalated"
          :value="escalatedFilter"
          @change="onEscalatedChange"
        >
          <option value="">All SLA states</option>
          <option value="true">Escalated (&gt;= 14d)</option>
          <option value="false">Within SLA</option>
        </select>

        <label for="study-queries-assignee">Assignee</label>
        <input
          id="study-queries-assignee"
          ref="assigneeInputRef"
          :value="assigneeInput"
          type="text"
          autocomplete="off"
          placeholder="All assignees"
          list="study-queries-assignees"
          @focus="assigneeFocused = true"
          @blur="onAssigneeBlur"
          @input="onAssigneeInput"
        />
        <datalist id="study-queries-assignees">
          <option v-for="actor in knownAssignees" :key="actor" :value="actor" />
        </datalist>

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
      <ClinicalLiveRegion
        id="study-queries-filter-status"
        test-id="study-queries-filter-status"
        variant="strip"
        :tone="hasActiveFilter ? 'info' : 'neutral'"
        :active="
          Boolean(studyId && !studyLoading && !studyError && !queries.loadError)
        "
        :polite-message="queryFilterPoliteMessage"
      >
        <span class="tabular-nums">{{ queryFilterSummaryText }}</span>
        <template v-if="hasActiveFilter" #actions>
          <button
            type="button"
            class="btn btn-secondary btn-sm"
            @click="clearFilters"
          >
            Reset filters
          </button>
        </template>
      </ClinicalLiveRegion>
    </header>

    <section
      v-if="digestsSupported && studyId && !studyLoading && !studyError"
      id="study-queries-digests"
      data-testid="study-queries-digests"
      class="study-digests"
      aria-labelledby="study-digests-title"
      :aria-busy="digestsLoading"
    >
      <div class="study-digests-header">
        <h3 id="study-digests-title">Aging digests (14-day SLA)</h3>
        <span class="study-digests-count">{{ digests.length }}</span>
      </div>
      <p v-if="digestsLoading" class="status-line" role="status">
        Loading aging digests…
      </p>
      <p v-else-if="digestsError" class="study-digests-error" role="alert">
        Aging digests couldn't be loaded: {{ digestsError }}
      </p>
      <p
        v-else-if="digests.length === 0"
        id="study-digests-empty"
        class="status-line"
      >
        No daily aging digests recorded for your visible scope yet.
      </p>
      <div v-else class="study-digests-grid">
        <article
          v-for="digest in digests"
          :key="digest.digest_id"
          class="study-digest-card"
          data-testid="aging-digest-card"
          :data-scope="digest.scope_key"
        >
          <div class="study-digest-head">
            <span
              class="study-digest-scope"
              :data-scope-kind="digest.study_site_id ? 'site' : 'study'"
            >
              <template v-if="digest.study_site_id">
                Site
                <span class="clinical-code">{{
                  digest.site_code || digest.scope_key
                }}</span>
                · Site investigator
              </template>
              <template v-else> Study-wide · Study lead </template>
            </span>
            <span class="study-digest-meta">
              {{ digest.digest_date }} · {{ digest.escalated_count }} escalated
            </span>
          </div>
          <p v-if="digest.entries.length === 0" class="status-line">
            No overdue open queries in this scope.
          </p>
          <ClinicalDataTable
            v-else
            :title="
              digest.study_site_id
                ? `Aging digest entries for site ${digest.site_code || digest.scope_key}`
                : 'Study-wide aging digest entries'
            "
            :aria-label="
              digest.study_site_id
                ? `Aging digest entries for site ${digest.site_code || digest.scope_key}`
                : 'Study-wide aging digest entries'
            "
            hide-header
            plain
            wrapper-class="study-digest-table-wrap"
            table-class="study-digest-table"
            :columns="digestColumns"
            :data="digest.entries"
            :row-key="(entry, idx) => `${entry.query_id}-${idx}`"
            :row-attrs="
              (entry) => ({ 'data-testid': `digest-entry-${entry.query_id}` })
            "
            external-pagination
          >
            <template #cell-subject="{ row: entry }">
              <span
                v-if="isRedactedDigestEntry(entry)"
                class="study-digest-redacted"
                data-testid="digest-entry-redacted"
              >
                <svg
                  width="14"
                  height="14"
                  viewBox="0 0 24 24"
                  fill="none"
                  stroke="currentColor"
                  stroke-width="1.75"
                  stroke-linecap="round"
                  stroke-linejoin="round"
                  aria-hidden="true"
                >
                  <path
                    d="M10.733 5.076a10.744 10.744 0 0 1 11.205 6.575 1 1 0 0 1 0 .696 10.747 10.747 0 0 1-1.444 2.49"
                  />
                  <path d="M14.084 14.158a3 3 0 0 1-4.242-4.242" />
                  <path
                    d="M17.479 17.499a10.75 10.75 0 0 1-15.417-5.151 1 1 0 0 1 0-.696 10.75 10.75 0 0 1 4.446-5.143"
                  />
                  <path d="m2 2 20 20" />
                </svg>
                <span>Redacted (blinded treatment item)</span>
              </span>
              <span v-else class="clinical-code">{{
                entry.subject_code || "—"
              }}</span>
            </template>
            <template #cell-site="{ row: entry }">
              <span
                v-if="isRedactedDigestEntry(entry)"
                class="study-digest-muted"
                >—</span
              >
              <span v-else class="clinical-code">{{
                entry.site_code || digest.site_code || "—"
              }}</span>
            </template>
            <template #cell-item="{ row: entry }">
              <span
                v-if="isRedactedDigestEntry(entry)"
                class="study-digest-muted"
                >—</span
              >
              <span v-else class="clinical-code">{{ entry.item_id }}</span>
            </template>
            <template #cell-owner="{ row: entry }">
              <span
                v-if="isRedactedDigestEntry(entry)"
                class="study-digest-muted"
                >—</span
              >
              <template v-else>{{
                formatOwnerLabel(entry.owner_role)
              }}</template>
            </template>
            <template #cell-age="{ row: entry }">
              {{ entry.age_days }}d
            </template>
            <template #cell-form="{ row: entry }">
              <span
                v-if="isRedactedDigestEntry(entry)"
                class="study-digest-muted"
                >Masked</span
              >
              <router-link
                v-else
                :id="`digest-open-form-${entry.query_id}`"
                class="study-digest-link"
                :to="digestEntryFormLocation(digest, entry)"
              >
                Open
                <span class="clinical-code">{{
                  entry.collection_spec_id || "form"
                }}</span>
              </router-link>
            </template>
          </ClinicalDataTable>
        </article>
      </div>
    </section>

    <p v-if="studyLoading" class="status-line" role="status">
      Looking up {{ studyCode }}…
    </p>
    <CanvasErrorAlert
      v-else-if="studyError"
      id="study-queries-error"
      :message="`${studyCode}'s queries couldn't be loaded.`"
      :detail="studyError"
      @retry="reloadStudy()"
      @explain="explainLoadError"
    />
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
      <CanvasErrorAlert
        v-if="queries.loadError"
        id="study-queries-error"
        :message="`${studyCode}'s queries couldn't be loaded.`"
        :detail="queries.loadError"
        @retry="refreshQueries()"
        @explain="explainLoadError"
      />
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
        :reassign="queries.reassign"
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
      A CRA or data manager closes, reopens, reassigns, and cancels queries.
    </p>
  </section>
</template>

<script setup lang="ts">
import { computed, onBeforeUnmount, reactive, ref, watch } from "vue";
import type { RouteLocationRaw } from "vue-router";
import {
  ClinicalDataTable,
  ClinicalLiveRegion,
  handoffFocus,
  useStatusAnnouncer,
} from "ui";
import { ApiError } from "../../api/client";
import {
  describeQueryError,
  ecrfQueriesApi,
  type FormQueryAgingDigestEntry,
  type FormQueryAgingDigestResponse,
  type FormQueryOrigin,
  type FormQueryOwnerRole,
  type FormQueryPriority,
  type FormQueryResponse,
  type FormQueryStatus,
} from "../../api/ecrfQueriesApi";
import {
  collectionInstanceRef,
  observationNodeRef,
} from "../../canvas/nodeRef";
import { useFormQueries } from "../../composables/useFormQueries";
import { useKernelStudyRef } from "../../composables/useKernelStudyRef";
import { useStudyCanvasStore } from "../../stores/studyCanvas";
import FormQueryList from "../ecrf/FormQueryList.vue";
import CanvasErrorAlert from "./CanvasErrorAlert.vue";

export interface StudyQueriesFilterState {
  status: string;
  owner: string;
  origin: string;
  priority: string;
  escalated: string;
  assignee: string;
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

const OWNER_LABELS: Record<string, string> = {
  site: "Site",
  monitor: "Monitor",
  data_manager: "Data manager",
};

const digestColumns = [
  { key: "subject", label: "Subject" },
  { key: "site", label: "Site" },
  { key: "item", label: "Item" },
  { key: "owner", label: "Owner" },
  { key: "age", label: "Age", cellClass: "study-digest-num" },
  { key: "form", label: "Form" },
];

const props = withDefaults(
  defineProps<{
    studyCode: string;
    canManage: boolean;
    canAnswer?: boolean;
    status?: string;
    owner?: string;
    origin?: string;
    priority?: string;
    escalated?: string;
    assignee?: string;
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
  explain: [];
}>();

const canvasStore = useStudyCanvasStore();

const siteInputRef = ref<HTMLInputElement | null>(null);
const participantInputRef = ref<HTMLInputElement | null>(null);
const assigneeInputRef = ref<HTMLInputElement | null>(null);
const siteFocused = ref(false);
const participantFocused = ref(false);
const assigneeFocused = ref(false);

const statusFilter = ref(props.status ?? "");
const ownerFilter = ref(props.owner ?? "");
const originFilter = ref(props.origin ?? "");
const priorityFilter = ref(props.priority ?? "");
const escalatedFilter = ref(props.escalated ?? "");
const assigneeInput = ref(props.assignee ?? "");
const appliedAssignee = ref((props.assignee ?? "").trim());
const siteInput = ref(props.site ?? "");
const participantInput = ref(props.participant ?? "");
const appliedSite = ref((props.site ?? "").trim());
const appliedParticipant = ref((props.participant ?? "").trim());
const changedFilter = ref(props.changed ?? "");

const digests = ref<FormQueryAgingDigestResponse[]>([]);
const digestsLoading = ref(false);
const digestsError = ref<string | null>(null);

const slaFormOpen = ref(false);
const slaReason = ref("");
const slaRunning = ref(false);
const slaOutcome = ref<{ message: string; error: boolean } | null>(null);
let pendingSlaAttempt: { reason: string; key: string } | null = null;

const digestsSupported = computed(
  () => typeof ecrfQueriesApi.listDigests === "function"
);

let filterTimer: ReturnType<typeof setTimeout> | undefined;
let queryAbortController: AbortController | null = null;
let digestAbortController: AbortController | null = null;

function abortActiveQueryRead() {
  if (queryAbortController) {
    queryAbortController.abort();
    queryAbortController = null;
  }
}

function abortActiveDigestRead() {
  if (digestAbortController) {
    digestAbortController.abort();
    digestAbortController = null;
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

function isAssigneeFocused(): boolean {
  return (
    assigneeFocused.value ||
    (typeof document !== "undefined" &&
      assigneeInputRef.value !== null &&
      document.activeElement === assigneeInputRef.value)
  );
}

watch(
  () => [
    props.status,
    props.owner,
    props.origin,
    props.priority,
    props.escalated,
    props.assignee,
    props.site,
    props.participant,
    props.changed,
  ],
  ([
    status,
    owner,
    origin,
    priority,
    escalated,
    assignee,
    site,
    participant,
    changed,
  ]) => {
    if (status !== undefined) statusFilter.value = status;
    if (owner !== undefined) ownerFilter.value = owner;
    if (origin !== undefined) originFilter.value = origin;
    if (priority !== undefined) priorityFilter.value = priority;
    if (escalated !== undefined) escalatedFilter.value = escalated;
    if (assignee !== undefined) {
      appliedAssignee.value = assignee.trim();
      if (!isAssigneeFocused()) {
        assigneeInput.value = assignee;
      }
    }
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
const seenAssignees = ref<string[]>([]);

const {
  studyId,
  studyLoading,
  studyError,
  reload: reloadStudy,
} = useKernelStudyRef(() => props.studyCode, {
  onReset: () => {
    clearTimeout(filterTimer);
    filterTimer = undefined;
    abortActiveQueryRead();
    abortActiveDigestRead();
    seenSites.value = [];
    seenParticipants.value = [];
    seenAssignees.value = [];
    digests.value = [];
    digestsError.value = null;
    digestsLoading.value = false;
    slaFormOpen.value = false;
    slaOutcome.value = null;
    queries.clear();
  },
  onResolved: async () => {
    if (studyId.value) {
      const queriesPromise = refreshQueries();
      void refreshDigests();
      await queriesPromise;
    }
  },
});

const knownSites = computed(() => [...new Set(seenSites.value)].sort());
const knownParticipants = computed(() =>
  [...new Set(seenParticipants.value)].sort()
);
const knownAssignees = computed(() => [...new Set(seenAssignees.value)].sort());

const hasActiveFilter = computed(() =>
  Boolean(
    statusFilter.value ||
    ownerFilter.value ||
    originFilter.value ||
    priorityFilter.value ||
    escalatedFilter.value ||
    assigneeInput.value.trim() ||
    siteInput.value.trim() ||
    participantInput.value.trim() ||
    changedFilter.value
  )
);

const queryFilterSummaryText = computed(() => {
  if (studyLoading.value) {
    return `Looking up ${props.studyCode}...`;
  }
  if (queries.loading || queries.refreshing) {
    return `Loading queries for ${props.studyCode}...`;
  }
  const count = queries.queries.length;
  const noun = count === 1 ? "query" : "queries";
  if (hasActiveFilter.value) {
    return count === 0
      ? "No queries match these filters."
      : `Showing ${count} matching ${noun} in ${props.studyCode}`;
  }
  return count === 0
    ? `No queries on visible enrollments in ${props.studyCode}.`
    : `Showing ${count} ${noun} in ${props.studyCode}`;
});

const { politeMessage: queryFilterPoliteMessage } = useStatusAnnouncer({
  debounceMs: 220,
  statusSource: () => queryFilterSummaryText.value,
});

function formatOwnerLabel(role: string): string {
  return OWNER_LABELS[role] ?? role;
}

function isRedactedDigestEntry(entry: FormQueryAgingDigestEntry): boolean {
  return entry.item_id === "masked";
}

function digestEntryFormLocation(
  digest: FormQueryAgingDigestResponse,
  entry: FormQueryAgingDigestEntry
): RouteLocationRaw {
  const formRef = collectionInstanceRef(entry.form_id) ?? entry.form_id;
  const siteCode = entry.site_code || digest.site_code;
  return {
    name: "study",
    params: { studyCode: props.studyCode },
    query: {
      stage: "data",
      section: "queries",
      ...(siteCode ? { site: siteCode } : {}),
      form: formRef,
      item: entry.item_id,
    },
  };
}

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
    escalated: escalatedFilter.value,
    assignee: appliedAssignee.value,
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
  const nextAssignee = assigneeInput.value.trim();
  const changed =
    nextSite !== appliedSite.value ||
    nextParticipant !== appliedParticipant.value ||
    nextAssignee !== appliedAssignee.value;
  appliedSite.value = nextSite;
  appliedParticipant.value = nextParticipant;
  appliedAssignee.value = nextAssignee;
  if (!isSiteFocused()) siteInput.value = nextSite;
  if (!isParticipantFocused()) participantInput.value = nextParticipant;
  if (!isAssigneeFocused()) assigneeInput.value = nextAssignee;
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

function onAssigneeBlur() {
  assigneeFocused.value = false;
  if (!filterTimer) {
    assigneeInput.value = appliedAssignee.value;
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

function onEscalatedChange(event: Event) {
  escalatedFilter.value = (event.target as HTMLSelectElement).value;
  flushTextFilters();
  syncFilterUrl();
}

function onAssigneeInput(event: Event) {
  assigneeInput.value = (event.target as HTMLInputElement).value;
  scheduleTextFilterSync();
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
  escalatedFilter.value = "";
  assigneeInput.value = "";
  appliedAssignee.value = "";
  siteInput.value = "";
  participantInput.value = "";
  appliedSite.value = "";
  appliedParticipant.value = "";
  changedFilter.value = "";
  syncFilterUrl();
  void handoffFocus("#study-queries-status");
}

function openSlaCheck() {
  slaFormOpen.value = true;
  slaReason.value = "";
  slaOutcome.value = null;
  void handoffFocus("#study-queries-sla-reason", { scrollIfOffscreen: true });
}

function cancelSlaCheck() {
  slaFormOpen.value = false;
  slaReason.value = "";
  void handoffFocus(["#btn-run-sla-check", "#study-queries-title"]);
}

async function confirmSlaCheck() {
  if (!studyId.value || slaRunning.value) return;
  const reason = slaReason.value.trim();
  if (!reason) return;
  if (!pendingSlaAttempt || pendingSlaAttempt.reason !== reason) {
    pendingSlaAttempt = { reason, key: globalThis.crypto.randomUUID() };
  }
  slaRunning.value = true;
  slaOutcome.value = null;
  try {
    const result = await ecrfQueriesApi.escalate({
      studyId: studyId.value,
      reason,
      idempotencyKey: pendingSlaAttempt.key,
    });
    pendingSlaAttempt = null;
    const escalatedCount = Number(result.escalated_queries ?? 0);
    const digestCount = Array.isArray(result.digests)
      ? result.digests.length
      : 0;
    if (Array.isArray(result.digests)) {
      digests.value = result.digests;
      digestsError.value = null;
    }
    slaFormOpen.value = false;
    slaReason.value = "";
    slaOutcome.value = {
      message: `14-day SLA check completed: ${escalatedCount} overdue ${escalatedCount === 1 ? "query" : "queries"} escalated; ${digestCount} aging ${digestCount === 1 ? "digest" : "digests"} recorded.`,
      error: false,
    };
    void refreshDigests();
    await refreshQueries();
    void handoffFocus(["#btn-run-sla-check", "#study-queries-title"]);
  } catch (error) {
    if (error instanceof ApiError && error.status !== null) {
      pendingSlaAttempt = null;
    }
    slaOutcome.value = {
      message: describeQueryError(error),
      error: true,
    };
  } finally {
    slaRunning.value = false;
  }
}

onBeforeUnmount(() => {
  clearTimeout(filterTimer);
  abortActiveQueryRead();
  abortActiveDigestRead();
});

async function refreshDigests() {
  if (!studyId.value || typeof ecrfQueriesApi.listDigests !== "function") {
    return;
  }
  abortActiveDigestRead();
  const controller = new AbortController();
  digestAbortController = controller;
  digestsLoading.value = true;
  digestsError.value = null;
  try {
    const result = await ecrfQueriesApi.listDigests(
      studyId.value,
      {
        site: appliedSite.value || undefined,
      },
      { signal: controller.signal }
    );
    if (controller.signal.aborted) return;
    digests.value = result;
  } catch (error) {
    if (controller.signal.aborted) return;
    digestsError.value = describeQueryError(error);
  } finally {
    if (digestAbortController === controller) {
      digestAbortController = null;
      digestsLoading.value = false;
    }
  }
}

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
  const escalated =
    escalatedFilter.value === "true"
      ? true
      : escalatedFilter.value === "false"
        ? false
        : null;
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
        ...(escalated !== null ? { escalated } : {}),
        ...(appliedAssignee.value ? { assignee: appliedAssignee.value } : {}),
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
    if (
      q.assignee &&
      q.assignee !== "masked" &&
      !seenAssignees.value.includes(q.assignee)
    ) {
      seenAssignees.value.push(q.assignee);
    }
  }
}

function explainLoadError() {
  emit("explain");
  canvasStore.requestExplain({
    targetNodeRef: studyId.value ? `study:${studyId.value}` : null,
  });
}

watch(
  [
    statusFilter,
    ownerFilter,
    originFilter,
    priorityFilter,
    escalatedFilter,
    appliedAssignee,
    appliedSite,
    appliedParticipant,
    changedFilter,
  ],
  ([, , , , , , nextSite], [, , , , , , prevSite]) => {
    if (studyId.value && !studyLoading.value) {
      void refreshQueries();
      if (nextSite !== prevSite) {
        void refreshDigests();
      }
    }
  }
);
</script>

<style scoped>
.study-queries {
  display: flex;
  flex-direction: column;
  gap: 12px;
  min-width: 0;
  max-width: 100%;
  box-sizing: border-box;
}

.study-queries-header {
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.study-queries-title-row {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
}

.study-queries-header h2 {
  margin: 0;
  font-size: 1.15rem;
}

.study-queries-sla-trigger {
  display: inline-flex;
  align-items: center;
  gap: 6px;
}

.study-queries-sla-form {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
  padding: 10px 12px;
  border: 1px solid var(--border);
  border-radius: 8px;
  background: var(--semantic-color-surface);
  font-size: 0.85rem;
}

.study-queries-sla-form input {
  flex: 1 1 16rem;
  padding: 6px 8px;
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 6px;
  background: var(--semantic-color-surface);
  font-size: 0.85rem;
}

.study-queries-sla-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}

.study-queries-sla-outcome {
  margin: 0;
  padding: 8px 12px;
  border-radius: 6px;
  background-color: var(--semantic-color-success-bg-soft);
  color: var(--semantic-color-success-dark);
  font-size: 0.85rem;
  font-variant-numeric: tabular-nums;
}

.study-queries-sla-outcome--error {
  background-color: var(--semantic-color-error-bg-soft);
  color: var(--semantic-color-error-text-dark);
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

@media (pointer: coarse) {
  .study-queries-filters select,
  .study-queries-filters input,
  .study-queries-sla-form input {
    font-size: 1rem;
    min-height: 2.75rem;
  }
}

.study-digests {
  display: flex;
  flex-direction: column;
  gap: 8px;
  min-width: 0;
  max-width: 100%;
  box-sizing: border-box;
  padding: 10px 12px;
  border: 1px solid var(--border);
  border-radius: 8px;
  background: var(--semantic-color-surface);
}

.study-digests-header {
  display: flex;
  align-items: center;
  gap: 8px;
}

.study-digests-header h3 {
  margin: 0;
  font-size: 0.95rem;
}

.study-digests-count {
  padding: 0 8px;
  border-radius: 999px;
  background: var(--neutral-light);
  color: var(--semantic-color-text-dark);
  font-size: 0.75rem;
  font-weight: 600;
  font-variant-numeric: tabular-nums;
}

.study-digests-error {
  margin: 0;
  color: var(--semantic-color-error-text-dark);
  font-size: 0.85rem;
}

.study-digests-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(min(22rem, 100%), 1fr));
  gap: 10px;
}

.study-digest-card {
  display: flex;
  flex-direction: column;
  gap: 8px;
  min-width: 0;
  max-width: 100%;
  box-sizing: border-box;
  padding: 10px;
  border: 1px solid var(--border);
  border-radius: 6px;
  font-size: 0.82rem;
}

.study-digest-head {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 6px;
}

.study-digest-scope {
  padding: 2px 8px;
  border-radius: 999px;
  background: var(--neutral-light);
  color: var(--semantic-color-text-dark);
  font-size: 0.75rem;
  font-weight: 600;
}

.study-digest-meta {
  color: var(--semantic-color-text-secondary);
  font-size: 0.75rem;
  font-variant-numeric: tabular-nums;
}

:deep(.study-digest-table-wrap) {
  overflow-x: auto;
}

:deep(.study-digest-table) {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.8rem;
}

:deep(.study-digest-table th),
:deep(.study-digest-table td) {
  padding: 5px 6px;
  border-bottom: 1px solid var(--border);
  text-align: left;
}

.study-digest-num {
  font-variant-numeric: tabular-nums;
}

.clinical-code {
  font-family: var(--semantic-font-mono);
}

.study-digest-redacted {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  padding: 2px 8px;
  border-radius: 999px;
  border: 1px solid var(--border);
  background: var(--neutral-light);
  color: var(--semantic-color-text-secondary);
  font-size: 0.75rem;
  font-weight: 600;
}

.study-digest-muted {
  color: var(--semantic-color-text-secondary);
  font-size: 0.75rem;
}

.study-digest-link {
  color: var(--semantic-color-interactive-primary);
  text-decoration: underline;
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

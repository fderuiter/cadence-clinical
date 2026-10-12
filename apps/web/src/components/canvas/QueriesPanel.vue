<template>
  <section
    id="record-queries"
    class="queries"
    aria-labelledby="record-queries-title"
  >
    <h3 id="record-queries-title">
      Queries
    </h3>
    <p
      v-if="!queries.length"
      id="record-queries-empty"
      class="status-line"
    >
      No queries on {{ subjectCode }}.
    </p>
    <template v-else>
      <div
        v-if="selectableQueries.length > 0"
        class="batch-controls"
        role="group"
        aria-label="Batch query actions"
      >
        <label class="select-all-label">
          <input
            id="select-all-queries"
            type="checkbox"
            :checked="allSelected"
            aria-label="Select all queries"
            @change="toggleSelectAll"
          >
          <span>Select All</span>
        </label>
        <div class="batch-buttons">
          <button
            id="btn-batch-respond"
            type="button"
            class="btn btn-secondary"
            :disabled="selectedRespondableQueries.length === 0"
            @click="startBatchAction('respond')"
          >
            Batch Answer
          </button>
          <button
            v-if="canManage"
            id="btn-batch-close"
            type="button"
            class="btn btn-secondary"
            :disabled="selectedCloseableQueries.length === 0"
            @click="startBatchAction('close')"
          >
            Batch Close
          </button>
          <button
            v-if="canManage"
            id="btn-batch-cancel"
            type="button"
            class="btn btn-secondary"
            :disabled="selectedCancellableQueries.length === 0"
            @click="startBatchAction('cancel')"
          >
            Batch Cancel
          </button>
        </div>
      </div>

      <div
        v-if="batchAction"
        class="batch-form"
        role="group"
        :aria-label="
          batchAction === 'respond'
            ? 'Batch answer queries'
            : batchAction === 'close'
              ? 'Batch close queries'
              : 'Batch cancel queries'
        "
      >
        <label for="batch-query-reason">{{
          batchAction === "respond"
            ? `Answer for batch answering ${selectedRespondableQueries.length} query/queries`
            : batchAction === "close"
              ? `Reason for batch closing ${selectedCloseableQueries.length} query/queries`
              : `Reason for batch cancelling ${selectedCancellableQueries.length} query/queries`
        }}</label>
        <textarea
          id="batch-query-reason"
          v-model="batchReason"
          rows="2"
        />
        <div class="query-buttons">
          <button
            :id="`btn-confirm-batch-${batchAction}`"
            type="button"
            class="btn btn-primary"
            :disabled="batchBusy || !batchReason.trim()"
            @click="confirmBatchAction"
          >
            Confirm
            {{
              batchAction === "respond"
                ? "Batch Answer"
                : batchAction === "close"
                  ? "Batch Close"
                  : "Batch Cancel"
            }}
          </button>
          <button
            type="button"
            class="btn btn-secondary"
            @click="dismissBatchAction"
          >
            Cancel
          </button>
        </div>
      </div>

      <ul class="query-list">
        <li
          v-for="query in queries"
          :id="`query-${query.id}`"
          :key="query.id"
          class="query"
        >
          <div class="query-head">
            <input
              v-if="isQuerySelectable(query)"
              :id="`select-query-${query.id}`"
              v-model="selectedQueryIds"
              type="checkbox"
              :value="query.id"
              :aria-label="`Select query ${query.id}`"
              class="query-checkbox"
            >
            <span
              class="query-status"
              :data-status="query.status"
            >{{
              queryStatusLabel(query.status)
            }}</span>
            <span class="query-target">{{ targetLabel(query) }}</span>
          </div>
          <p class="query-text">
            {{ query.explanation }}
          </p>
          <p
            v-if="query.response"
            class="query-response"
          >
            Answer: {{ query.response }}
          </p>
          <p
            v-if="query.cancellation_reason"
            class="query-response"
          >
            Cancelled: {{ query.cancellation_reason }}
          </p>

          <div
            v-if="openAction?.queryId === query.id"
            class="query-form"
            role="group"
            :aria-label="ACTION_TEXT[openAction.action].button"
          >
            <label :for="`query-text-${query.id}`">{{
              ACTION_TEXT[openAction.action].label
            }}</label>
            <textarea
              :id="`query-text-${query.id}`"
              v-model="actionText"
              rows="2"
            />
            <div class="query-buttons">
              <button
                :id="`btn-confirm-${openAction.action}-${query.id}`"
                type="button"
                class="btn btn-primary"
                :disabled="busy || !actionText.trim()"
                @click="confirm(query)"
              >
                {{ ACTION_TEXT[openAction.action].button }}
              </button>
              <button
                type="button"
                class="btn btn-secondary"
                @click="dismiss"
              >
                Back
              </button>
            </div>
          </div>
          <div
            v-else
            class="query-buttons"
          >
            <button
              v-for="action in actionsFor(query)"
              :id="`btn-${action}-${query.id}`"
              :key="action"
              type="button"
              class="btn btn-secondary"
              @click="begin(query, action)"
            >
              {{ ACTION_TEXT[action].start }}
            </button>
          </div>
        </li>
      </ul>
    </template>
    <p
      v-if="!canManage"
      id="record-queries-note"
      class="status-line"
    >
      A CRA or data manager closes, reopens and cancels queries.
    </p>
  </section>
</template>

<script setup lang="ts">
import { computed, ref } from "vue";
import type { ClinicalQuery, Observation } from "../../api/executionApi";
import {
  useStudyCanvasStore,
  type QueryAction,
} from "../../stores/studyCanvas";

/**
 * A participant's queries from Execution, with the transitions Execution allows from each state: anyone may
 * answer an open or reopened query; a CRA or data manager closes an answered one, reopens an answered or
 * closed one, and cancels any that is still live. Every transition carries the text Execution records.
 */
const props = defineProps<{
  subjectCode: string;
  queries: ClinicalQuery[];
  observations: Observation[];
  canManage: boolean;
}>();

const canvas = useStudyCanvasStore();

const STATUS_LABELS: Record<string, string> = {
  OPEN: "Open",
  ANSWERED: "Answered",
  CLOSED: "Closed",
  REOPENED: "Reopened",
  CANCELLED: "Cancelled",
  RESOLVED: "Resolved",
  CANDIDATE: "Candidate",
  DRAFT_AI: "AI draft",
};

const ACTION_TEXT: Record<
  QueryAction,
  { start: string; label: string; button: string }
> = {
  respond: { start: "Answer", label: "Answer", button: "Send answer" },
  close: {
    start: "Close",
    label: "Reason for closing",
    button: "Close query",
  },
  reopen: {
    start: "Reopen",
    label: "Reason for reopening",
    button: "Reopen query",
  },
  cancel: {
    start: "Cancel query",
    label: "Reason for cancelling",
    button: "Cancel query",
  },
};

const openAction = ref<{ queryId: string; action: QueryAction } | null>(null);
const actionText = ref("");
const busy = ref(false);

const selectedQueryIds = ref<string[]>([]);
const batchAction = ref<"respond" | "close" | "cancel" | null>(null);
const batchReason = ref("");
const batchBusy = ref(false);

function isQuerySelectable(query: ClinicalQuery): boolean {
  if (props.canManage) {
    return query.status !== "CLOSED" && query.status !== "CANCELLED";
  }
  return query.status === "OPEN" || query.status === "REOPENED";
}

const selectableQueries = computed(() => {
  return props.queries.filter(isQuerySelectable);
});

const selectedRespondableQueries = computed(() => {
  return props.queries.filter(
    (q) =>
      selectedQueryIds.value.includes(q.id) &&
      (q.status === "OPEN" || q.status === "REOPENED")
  );
});

const selectedCloseableQueries = computed(() => {
  return props.queries.filter(
    (q) => selectedQueryIds.value.includes(q.id) && q.status === "ANSWERED"
  );
});

const selectedCancellableQueries = computed(() => {
  return props.queries.filter(
    (q) => selectedQueryIds.value.includes(q.id) && isQuerySelectable(q)
  );
});

const allSelected = computed(() => {
  return (
    selectableQueries.value.length > 0 &&
    selectedQueryIds.value.length === selectableQueries.value.length
  );
});

function toggleSelectAll(event: Event) {
  const checked = (event.target as HTMLInputElement).checked;
  if (checked) {
    selectedQueryIds.value = selectableQueries.value.map((q) => q.id);
  } else {
    selectedQueryIds.value = [];
  }
}

function startBatchAction(action: "respond" | "close" | "cancel") {
  batchAction.value = action;
  batchReason.value = "";
}

function dismissBatchAction() {
  batchAction.value = null;
  batchReason.value = "";
}

async function confirmBatchAction() {
  const action = batchAction.value;
  const reason = batchReason.value.trim();
  if (!action || !reason) return;

  const targetQueries =
    action === "respond"
      ? selectedRespondableQueries.value
      : action === "close"
        ? selectedCloseableQueries.value
        : selectedCancellableQueries.value;

  const targetIds = targetQueries.map((q) => q.id);
  if (targetIds.length === 0) return;

  batchBusy.value = true;
  try {
    const success =
      action === "respond"
        ? await canvas.batchRespondToQueries(targetIds, reason)
        : await canvas.batchActOnQueries(targetIds, action, reason);
    if (success) {
      selectedQueryIds.value = selectedQueryIds.value.filter(
        (id) => !targetIds.includes(id)
      );
      dismissBatchAction();
    }
  } finally {
    batchBusy.value = false;
  }
}

function queryStatusLabel(status: string): string {
  return STATUS_LABELS[status] ?? status;
}

function targetLabel(query: ClinicalQuery): string {
  const observation = props.observations.find(
    (o) => o.id === query.observation_id
  );
  const test = observation?.test_name ?? query.test_code;
  return query.visit_id ? `${test} · ${query.visit_id}` : test;
}

function actionsFor(query: ClinicalQuery): QueryAction[] {
  const actions: QueryAction[] = [];
  const status = query.status;
  if (status === "OPEN" || status === "REOPENED") actions.push("respond");
  if (!props.canManage) return actions;
  if (status === "ANSWERED") actions.push("close");
  if (status === "ANSWERED" || status === "CLOSED") actions.push("reopen");
  if (status !== "CLOSED" && status !== "CANCELLED") actions.push("cancel");
  return actions;
}

function begin(query: ClinicalQuery, action: QueryAction) {
  openAction.value = { queryId: query.id, action };
  actionText.value = "";
}

function dismiss() {
  openAction.value = null;
  actionText.value = "";
}

async function confirm(query: ClinicalQuery) {
  const current = openAction.value;
  const text = actionText.value.trim();
  if (!current || !text) return;
  busy.value = true;
  try {
    if (await canvas.actOnQuery(query, current.action, text)) dismiss();
  } finally {
    busy.value = false;
  }
}
</script>

<style scoped>
.queries h3 {
  margin: 0 0 8px;
  font-size: 1rem;
}

.status-line {
  margin: 0;
  color: var(--neutral-dark);
  font-size: 0.85rem;
}

.batch-controls {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  margin-bottom: 12px;
  padding: 8px 12px;
  background: var(--neutral-light);
  border: 1px solid var(--border);
  border-radius: 6px;
}

.select-all-label {
  display: flex;
  align-items: center;
  gap: 6px;
  font-size: 0.85rem;
  font-weight: 600;
  cursor: pointer;
  min-height: 44px;
}

.batch-buttons {
  display: flex;
  gap: 6px;
}

.batch-form {
  display: flex;
  flex-direction: column;
  gap: 6px;
  margin-bottom: 12px;
  padding: 10px 12px;
  border: 1px solid var(--border);
  border-radius: 8px;
}

.batch-form textarea {
  padding: 8px;
  border: 1px solid var(--border);
  border-radius: 4px;
}

.query-checkbox {
  width: 18px;
  height: 18px;
  cursor: pointer;
}

.query-list {
  display: flex;
  flex-direction: column;
  gap: 8px;
  margin: 0;
  padding: 0;
  list-style: none;
}

.query {
  display: flex;
  flex-direction: column;
  gap: 6px;
  padding: 10px 12px;
  border: 1px solid var(--border);
  border-radius: 8px;
}

.query-head {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
}

.query-status {
  padding: 0 8px;
  border-radius: 999px;
  background: var(--neutral-light);
  font-size: 0.75rem;
  font-weight: 600;
}

.query-status[data-status="OPEN"],
.query-status[data-status="REOPENED"] {
  background: var(--warning-bg);
  color: var(--warning);
}

.query-target {
  color: var(--neutral-dark);
  font-size: 0.85rem;
}

.query-text,
.query-response {
  margin: 0;
}

.query-response {
  color: var(--neutral-dark);
}

.query-form {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.query-form textarea {
  padding: 8px;
  border: 1px solid var(--border);
  border-radius: 4px;
}

.query-buttons {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}
</style>

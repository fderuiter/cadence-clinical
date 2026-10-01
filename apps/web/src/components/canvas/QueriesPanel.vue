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
    <ul
      v-else
      class="query-list"
    >
      <li
        v-for="query in queries"
        :id="`query-${query.id}`"
        :key="query.id"
        class="query"
      >
        <div class="query-head">
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
import { ref } from "vue";
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

<template>
  <ul class="form-query-list" data-testid="form-query-list">
    <li
      v-for="query in queries"
      :id="`query-${query.query_id}`"
      :key="query.query_id"
      class="form-query"
      :data-status="query.status"
    >
      <div class="form-query-head">
        <span class="form-query-status" :data-status="query.status">{{
          STATUS_LABELS[query.status] ?? query.status
        }}</span>
        <span class="form-query-target">
          {{ query.item_id }}, revision {{ query.revision }}
          <template v-if="showForm"> · form {{ query.form_id }}</template>
        </span>
        <span
          v-if="query.value_changed"
          class="form-query-changed"
          data-testid="query-value-changed"
        >
          Value changed (now revision {{ query.current_revision }})
        </span>
      </div>
      <ol class="form-query-messages">
        <li v-for="(message, i) in query.messages" :key="i">
          <strong>{{ MESSAGE_LABELS[message.kind] ?? message.kind }}</strong>
          by {{ message.by }} at {{ message.at }}: {{ message.text }}
        </li>
      </ol>

      <form
        v-if="pendingAction?.queryId === query.query_id"
        class="form-query-form"
        :aria-label="ACTION_TEXT[pendingAction.action].button"
        @submit.prevent="confirm(query)"
      >
        <label :for="`query-text-${query.query_id}`">{{
          ACTION_TEXT[pendingAction.action].label
        }}</label>
        <textarea
          :id="`query-text-${query.query_id}`"
          v-model="actionText"
          rows="2"
        />
        <div class="form-query-buttons">
          <button
            :id="`btn-confirm-${pendingAction.action}-${query.query_id}`"
            type="submit"
            class="btn btn-primary"
            :disabled="busy || !actionText.trim()"
          >
            {{ ACTION_TEXT[pendingAction.action].button }}
          </button>
          <button type="button" class="btn btn-secondary" @click="dismiss">
            Back
          </button>
        </div>
      </form>
      <div v-else class="form-query-buttons">
        <button
          v-for="action in queryActions(query, canManage)"
          :id="`btn-${action}-${query.query_id}`"
          :key="action"
          type="button"
          class="btn btn-secondary"
          :disabled="busy"
          @click="begin(query, action)"
        >
          {{ ACTION_TEXT[action].start }}
        </button>
      </div>
    </li>
  </ul>
</template>

<script setup lang="ts">
import { ref } from "vue";
import type {
  FormQueryAction,
  FormQueryResponse,
} from "../../api/ecrfQueriesApi";
import { queryActions } from "../../composables/useFormQueries";

/**
 * A list of eCRF queries (#5461) with the transitions the Execution module allows from each state. Each transition
 * asks for its text first: the answer, the reopening note, or the reason for closing or cancelling.
 */
const props = defineProps<{
  queries: FormQueryResponse[];
  canManage: boolean;
  busy: boolean;
  /** Show each query's form, for a list that spans an enrollment's forms. */
  showForm?: boolean;
  act: (
    query: FormQueryResponse,
    action: FormQueryAction,
    text: string
  ) => Promise<boolean>;
}>();

const STATUS_LABELS: Record<string, string> = {
  open: "Open",
  answered: "Answered",
  closed: "Closed",
  cancelled: "Cancelled",
};

const MESSAGE_LABELS: Record<string, string> = {
  raised: "Raised",
  answered: "Answered",
  reopened: "Reopened",
  closed: "Closed",
  cancelled: "Cancelled",
};

const ACTION_TEXT: Record<
  FormQueryAction,
  { start: string; label: string; button: string }
> = {
  answer: { start: "Answer", label: "Answer", button: "Send answer" },
  reopen: {
    start: "Reopen",
    label: "Why the query is reopened",
    button: "Reopen query",
  },
  close: {
    start: "Close",
    label: "Reason for closing",
    button: "Close query",
  },
  cancel: {
    start: "Cancel query",
    label: "Reason for cancelling",
    button: "Cancel query",
  },
};

const pendingAction = ref<{ queryId: string; action: FormQueryAction } | null>(
  null
);
const actionText = ref("");

function begin(query: FormQueryResponse, action: FormQueryAction) {
  pendingAction.value = { queryId: query.query_id, action };
  actionText.value = "";
}

function dismiss() {
  pendingAction.value = null;
  actionText.value = "";
}

async function confirm(query: FormQueryResponse) {
  const current = pendingAction.value;
  const text = actionText.value.trim();
  if (!current || !text) return;
  if (await props.act(query, current.action, text)) dismiss();
}
</script>

<style scoped>
.form-query-list {
  display: flex;
  flex-direction: column;
  gap: 8px;
  margin: 0;
  padding: 0;
  list-style: none;
}
.form-query {
  display: flex;
  flex-direction: column;
  gap: 6px;
  padding: 10px 12px;
  border: 1px solid var(--border);
  border-radius: 8px;
  font-size: 0.85rem;
}
.form-query-head {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
}
.form-query-status {
  padding: 0 8px;
  border-radius: 999px;
  background: var(--neutral-light);
  font-size: 0.75rem;
  font-weight: 600;
}
.form-query-status[data-status="open"] {
  background: var(--warning-bg);
  color: var(--warning);
}
.form-query-target {
  color: var(--neutral-dark);
}
.form-query-changed {
  font-size: 0.75rem;
  font-weight: 600;
  color: var(--semantic-color-interactive-primary);
}
.form-query-messages {
  margin: 0;
  padding-left: 18px;
}
.form-query-form {
  display: flex;
  flex-direction: column;
  gap: 6px;
}
.form-query-form textarea {
  padding: 8px;
  border: 1px solid var(--border);
  border-radius: 4px;
}
.form-query-buttons {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}
</style>

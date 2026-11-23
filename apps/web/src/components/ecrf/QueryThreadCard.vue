<template>
  <li
    :id="`query-${query.query_id}`"
    class="form-query"
    :data-status="query.status"
  >
    <div class="form-query-head">
      <input
        v-if="showCheckbox"
        :id="`select-query-${query.query_id}`"
        type="checkbox"
        :checked="selected"
        :value="query.query_id"
        :disabled="busy || assertionsLocked || !eligibleForBulk"
        :aria-label="`Select query on ${query.item_id}`"
        @change="
          emit('toggle-select', ($event.target as HTMLInputElement).checked)
        "
      />
      <span class="form-query-status" :data-status="query.status">{{
        STATUS_LABELS[query.status] ?? query.status
      }}</span>
      <QueryContextLinks
        :query="query"
        :show-form="showForm"
        :show-context="showContext"
        :study-code="studyCode"
        :subject-code="subjectCode"
        :form-titles="formTitles"
        :form-to="formTo"
        :observation-to="observationTo"
      />
    </div>

    <ol class="form-query-messages">
      <li v-for="(message, i) in query.messages" :key="i">
        <strong>{{ MESSAGE_LABELS[message.kind] ?? message.kind }}</strong>
        by {{ message.by }} at {{ message.at }}: {{ message.text }}
      </li>
    </ol>

    <form
      v-if="activeAction"
      class="form-query-form"
      :aria-label="QUERY_ACTION_TEXT[activeAction].button"
      @submit.prevent="emit('confirm')"
    >
      <label :for="`query-text-${query.query_id}`">{{
        QUERY_ACTION_TEXT[activeAction].label
      }}</label>
      <textarea
        :id="`query-text-${query.query_id}`"
        :value="actionText"
        rows="2"
        @input="
          emit(
            'update:actionText',
            ($event.target as HTMLTextAreaElement).value
          )
        "
      />
      <div class="form-query-buttons">
        <button
          :id="`btn-confirm-${activeAction}-${query.query_id}`"
          type="submit"
          class="btn btn-primary"
          :disabled="busy || assertionsLocked || !actionText.trim()"
        >
          {{ QUERY_ACTION_TEXT[activeAction].button }}
        </button>
        <button
          type="button"
          class="btn btn-secondary"
          @click="emit('dismiss')"
        >
          Back
        </button>
      </div>
    </form>
    <div v-else class="form-query-buttons">
      <button
        v-for="action in actions"
        :id="`btn-${action}-${query.query_id}`"
        :key="action"
        type="button"
        class="btn btn-secondary"
        :disabled="busy || assertionsLocked"
        @click="emit('begin', action)"
      >
        {{ QUERY_ACTION_TEXT[action].start }}
      </button>
    </div>
  </li>
</template>

<script setup lang="ts">
import type { RouteLocationRaw } from "vue-router";
import type {
  FormQueryAction,
  FormQueryResponse,
} from "../../api/ecrfQueriesApi";
import { QUERY_ACTION_TEXT } from "./QueryBulkToolbar.vue";
import QueryContextLinks from "./QueryContextLinks.vue";

defineProps<{
  query: FormQueryResponse;
  actions: FormQueryAction[];
  activeAction: FormQueryAction | null;
  actionText: string;
  showCheckbox: boolean;
  selected: boolean;
  eligibleForBulk: boolean;
  busy: boolean;
  assertionsLocked?: boolean;
  showForm?: boolean;
  showContext?: boolean;
  studyCode?: string;
  subjectCode?: string;
  formTitles?: Record<string, string>;
  formTo?: (query: FormQueryResponse) => RouteLocationRaw | null;
  observationTo?: (query: FormQueryResponse) => RouteLocationRaw | null;
}>();

const emit = defineEmits<{
  (e: "toggle-select", checked: boolean): void;
  (e: "begin", action: FormQueryAction): void;
  (e: "update:actionText", text: string): void;
  (e: "confirm"): void;
  (e: "dismiss"): void;
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
</script>

<style scoped>
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
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 4px;
}
.form-query-buttons {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}
</style>

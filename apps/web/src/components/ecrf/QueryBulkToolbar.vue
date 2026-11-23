<template>
  <div
    v-if="availableActions.length > 0"
    class="form-query-bulk"
    data-testid="query-bulk-bar"
  >
    <div class="form-query-bulk-controls">
      <label for="query-bulk-action">Selected action</label>
      <select
        id="query-bulk-action"
        :value="bulkAction"
        :disabled="busy || assertionsLocked"
        @change="
          emit(
            'update:bulkAction',
            ($event.target as HTMLSelectElement).value as FormQueryAction
          )
        "
      >
        <option
          v-for="action in availableActions"
          :key="action"
          :value="action"
        >
          {{ QUERY_ACTION_TEXT[action].start }}
        </option>
      </select>
      <button
        id="btn-select-eligible-queries"
        type="button"
        class="btn btn-secondary"
        :disabled="busy || assertionsLocked || eligibleCount === 0"
        @click="emit('select-eligible')"
      >
        Select eligible ({{ eligibleCount }})
      </button>
      <button
        v-if="hasSelection"
        id="btn-clear-selected-queries"
        type="button"
        class="btn btn-secondary"
        :disabled="busy"
        @click="emit('clear-selection')"
      >
        Clear selection
      </button>
    </div>

    <form
      v-if="selectedEligibleCount > 0"
      class="form-query-form"
      @submit.prevent="emit('confirm-bulk')"
    >
      <label for="query-bulk-note">
        {{ QUERY_ACTION_TEXT[bulkAction].label }} ({{ selectedEligibleCount }}
        selected)
      </label>
      <textarea
        id="query-bulk-note"
        :value="bulkNote"
        rows="2"
        :disabled="busy || assertionsLocked"
        @input="
          emit('update:bulkNote', ($event.target as HTMLTextAreaElement).value)
        "
      />
      <label for="query-bulk-reason">
        Reason for change ({{ selectedEligibleCount }} selected)
      </label>
      <input
        id="query-bulk-reason"
        :value="bulkReason"
        type="text"
        autocomplete="off"
        :disabled="busy || assertionsLocked"
        @input="
          emit('update:bulkReason', ($event.target as HTMLInputElement).value)
        "
      />
      <div class="form-query-buttons">
        <button
          id="btn-apply-bulk-queries"
          type="submit"
          class="btn btn-primary"
          :disabled="
            busy ||
            assertionsLocked ||
            !bulkReason.trim() ||
            (actionTakesText(bulkAction) && !bulkNote.trim())
          "
        >
          {{ QUERY_ACTION_TEXT[bulkAction].button }} ({{
            selectedEligibleCount
          }})
        </button>
      </div>
    </form>

    <ul
      v-if="bulkResults.length > 0"
      class="form-query-bulk-results"
      data-testid="query-bulk-results"
      aria-live="polite"
    >
      <li
        v-for="res in bulkResults"
        :key="res.queryId"
        :data-query-result="res.queryId"
        :data-ok="String(res.ok)"
        :class="
          res.ok
            ? 'form-query-bulk-result--ok'
            : 'form-query-bulk-result--failed'
        "
      >
        <strong>
          <template v-if="res.subjectCode">{{ res.subjectCode }} · </template>
          {{ res.itemId }}:
        </strong>
        {{ res.message }}
      </li>
    </ul>
  </div>
</template>

<script lang="ts">
import {
  actionTakesText,
  type FormQueryAction,
} from "../../api/ecrfQueriesApi";

export const QUERY_ACTION_TEXT: Record<
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
    label: "Closing note (optional)",
    button: "Close query",
  },
  cancel: {
    start: "Cancel query",
    label: "Cancellation note (optional)",
    button: "Cancel query",
  },
};
</script>

<script setup lang="ts">
import type { FormQueryBatchItemResult } from "../../composables/useFormQueries";

defineProps<{
  availableActions: FormQueryAction[];
  bulkAction: FormQueryAction;
  bulkNote: string;
  bulkReason: string;
  bulkResults: FormQueryBatchItemResult[];
  eligibleCount: number;
  selectedEligibleCount: number;
  hasSelection: boolean;
  busy: boolean;
  assertionsLocked?: boolean;
}>();

const emit = defineEmits<{
  (e: "update:bulkAction", action: FormQueryAction): void;
  (e: "update:bulkNote", note: string): void;
  (e: "update:bulkReason", reason: string): void;
  (e: "select-eligible"): void;
  (e: "clear-selection"): void;
  (e: "confirm-bulk"): void;
}>();
</script>

<style scoped>
.form-query-bulk {
  display: flex;
  flex-direction: column;
  gap: 8px;
  padding: 10px 12px;
  border: 1px solid var(--border);
  border-radius: 8px;
  background: var(--semantic-color-surface-muted);
  font-size: 0.85rem;
}
.form-query-bulk-controls {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
}
.form-query-bulk-results {
  margin: 0;
  padding-left: 18px;
  display: flex;
  flex-direction: column;
  gap: 4px;
}
.form-query-bulk-result--ok {
  color: var(--semantic-color-success-dark);
}
.form-query-bulk-result--failed {
  color: var(--semantic-color-error-text-dark);
}
.form-query-form {
  display: flex;
  flex-direction: column;
  gap: 6px;
}
.form-query-form textarea,
.form-query-form input {
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

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
      <span
        v-if="query.origin || query.raised_by?.startsWith('svc:')"
        class="form-query-badge"
        :data-testid="`query-origin-${query.query_id}`"
      >
        {{ isSystemQuery(query) ? "System" : "Manual" }}
      </span>
      <span
        v-if="query.owner_role"
        class="form-query-badge"
        :data-testid="`query-owner-${query.query_id}`"
      >
        Owner: {{ OWNER_LABELS[query.owner_role] ?? query.owner_role }}
      </span>
      <span
        v-if="query.assignee && query.assignee !== 'masked'"
        class="form-query-badge form-query-badge--assignee"
        :data-testid="`query-assignee-${query.query_id}`"
      >
        Assignee: <span class="form-query-code">{{ query.assignee }}</span>
      </span>
      <span
        v-if="query.priority"
        class="form-query-badge"
        :data-priority="query.priority"
        :data-testid="`query-priority-${query.query_id}`"
      >
        Priority: {{ PRIORITY_LABELS[query.priority] ?? query.priority }}
      </span>
      <span
        v-if="query.escalated_at"
        class="form-query-badge form-query-badge--escalated"
        :data-testid="`query-escalated-${query.query_id}`"
      >
        Escalated
      </span>
      <span
        v-if="query.status === 'open' && query.due_at"
        class="form-query-due"
        :data-testid="`query-due-${query.query_id}`"
      >
        Due {{ formatDueDate(query.due_at) }}
      </span>
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
        <span
          v-if="
            message.reason_for_change &&
            message.reason_for_change !== message.text
          "
          class="form-query-message-reason"
        >
          (reason: {{ message.reason_for_change }})
        </span>
      </li>
    </ol>
    <p
      v-if="
        canManage &&
        isBlockedBySeparationOfDuties(query, effectiveCurrentUserId)
      "
      class="form-query-sod-hint"
      :data-testid="`query-sod-hint-${query.query_id}`"
    >
      Another monitor or data manager must close a query you answered.
    </p>

    <form
      v-if="activeReassign"
      class="form-query-form"
      aria-label="Reassign query"
      @submit.prevent="emit('confirm-reassign')"
    >
      <label :for="`reassign-assignee-${query.query_id}`">
        Assignee (leave blank to unassign)
      </label>
      <input
        :id="`reassign-assignee-${query.query_id}`"
        :value="reassignAssignee"
        type="text"
        autocomplete="off"
        placeholder="Actor ID or leave blank to unassign"
        :disabled="busy || assertionsLocked"
        @input="
          emit(
            'update:reassignAssignee',
            ($event.target as HTMLInputElement).value
          )
        "
      />
      <label :for="`reassign-owner-${query.query_id}`">Responsible role</label>
      <select
        :id="`reassign-owner-${query.query_id}`"
        :value="reassignOwnerRole"
        :disabled="busy || assertionsLocked"
        @change="
          emit(
            'update:reassignOwnerRole',
            ($event.target as HTMLSelectElement).value as FormQueryOwnerRole
          )
        "
      >
        <option value="site">Site</option>
        <option value="monitor">Monitor</option>
        <option value="data_manager">Data manager</option>
      </select>
      <label :for="`reassign-note-${query.query_id}`">
        Reassignment note (optional)
      </label>
      <input
        :id="`reassign-note-${query.query_id}`"
        :value="reassignNote"
        type="text"
        autocomplete="off"
        :disabled="busy || assertionsLocked"
        @input="
          emit('update:reassignNote', ($event.target as HTMLInputElement).value)
        "
      />
      <label :for="`reassign-reason-${query.query_id}`"
        >Reason for change</label
      >
      <input
        :id="`reassign-reason-${query.query_id}`"
        :value="reassignReason"
        type="text"
        autocomplete="off"
        :disabled="busy || assertionsLocked"
        @input="
          emit(
            'update:reassignReason',
            ($event.target as HTMLInputElement).value
          )
        "
      />
      <div class="form-query-buttons">
        <button
          :id="`btn-confirm-reassign-${query.query_id}`"
          type="submit"
          class="btn btn-primary"
          :disabled="
            busy ||
            assertionsLocked ||
            !reassignReason?.trim() ||
            !hasReassignChange
          "
        >
          Confirm reassignment<span class="sr-only">
            for {{ query.item_id }}</span
          >
        </button>
        <button
          :id="`btn-cancel-reassign-${query.query_id}`"
          type="button"
          class="btn btn-secondary"
          @click="emit('dismiss-reassign')"
        >
          Back<span class="sr-only"> from query on {{ query.item_id }}</span>
        </button>
      </div>
    </form>
    <form
      v-else-if="activeAction"
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
        :disabled="busy || assertionsLocked"
        @input="
          emit(
            'update:actionText',
            ($event.target as HTMLTextAreaElement).value
          )
        "
      />
      <template v-if="activeAction === 'answer' || activeAction === 'reopen'">
        <label :for="`query-assignee-${query.query_id}`">
          Replacement assignee (optional)
        </label>
        <input
          :id="`query-assignee-${query.query_id}`"
          :value="actionAssignee"
          type="text"
          autocomplete="off"
          placeholder="Leave blank to clear previous role assignee"
          :disabled="busy || assertionsLocked"
          @input="
            emit(
              'update:actionAssignee',
              ($event.target as HTMLInputElement).value
            )
          "
        />
      </template>
      <label :for="`query-reason-${query.query_id}`">Reason for change</label>
      <input
        :id="`query-reason-${query.query_id}`"
        :value="actionReason"
        type="text"
        autocomplete="off"
        :disabled="busy || assertionsLocked"
        @input="
          emit('update:actionReason', ($event.target as HTMLInputElement).value)
        "
      />
      <div class="form-query-buttons">
        <button
          :id="`btn-confirm-${activeAction}-${query.query_id}`"
          type="submit"
          class="btn btn-primary"
          :disabled="
            busy ||
            assertionsLocked ||
            !actionReason.trim() ||
            (actionTakesText(activeAction) && !actionText.trim())
          "
        >
          {{ QUERY_ACTION_TEXT[activeAction].button
          }}<span class="sr-only"> for {{ query.item_id }}</span>
        </button>
        <button
          type="button"
          class="btn btn-secondary"
          @click="emit('dismiss')"
        >
          Back<span class="sr-only"> from query on {{ query.item_id }}</span>
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
        {{ QUERY_ACTION_TEXT[action].start
        }}<span class="sr-only"> query on {{ query.item_id }}</span>
      </button>
      <button
        v-if="canReassign"
        :id="`btn-reassign-${query.query_id}`"
        type="button"
        class="btn btn-secondary"
        :disabled="busy || assertionsLocked"
        @click="emit('begin-reassign')"
      >
        Reassign<span class="sr-only"> query on {{ query.item_id }}</span>
      </button>
    </div>
  </li>
</template>

<script setup lang="ts">
import { computed } from "vue";
import type { RouteLocationRaw } from "vue-router";
import {
  actionTakesText,
  type FormQueryAction,
  type FormQueryOwnerRole,
  type FormQueryResponse,
} from "../../api/ecrfQueriesApi";
import {
  isBlockedBySeparationOfDuties,
  isLiveQuery,
  isSystemQuery,
} from "../../composables/useFormQueries";
import { QUERY_ACTION_TEXT } from "./QueryBulkToolbar.vue";
import QueryContextLinks from "./QueryContextLinks.vue";

const props = defineProps<{
  query: FormQueryResponse;
  canManage: boolean;
  canReassign?: boolean;
  effectiveCurrentUserId?: string | readonly string[] | null;
  actions: FormQueryAction[];
  activeAction: FormQueryAction | null;
  actionText: string;
  actionAssignee?: string;
  actionReason: string;
  activeReassign?: boolean;
  reassignAssignee?: string;
  reassignOwnerRole?: FormQueryOwnerRole;
  reassignNote?: string;
  reassignReason?: string;
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
  (e: "update:actionAssignee", assignee: string): void;
  (e: "update:actionReason", reason: string): void;
  (e: "confirm"): void;
  (e: "dismiss"): void;
  (e: "begin-reassign"): void;
  (e: "update:reassignAssignee", assignee: string): void;
  (e: "update:reassignOwnerRole", ownerRole: FormQueryOwnerRole): void;
  (e: "update:reassignNote", note: string): void;
  (e: "update:reassignReason", reason: string): void;
  (e: "confirm-reassign"): void;
  (e: "dismiss-reassign"): void;
}>();

const canReassign = computed(() =>
  props.canReassign !== undefined
    ? props.canReassign
    : props.canManage &&
      isLiveQuery(props.query) &&
      props.query.raised_by !== "masked"
);

const hasReassignChange = computed(() => {
  const nextAssignee = (props.reassignAssignee ?? "").trim();
  const currentAssignee = (props.query.assignee ?? "").trim();
  const nextRole =
    props.reassignOwnerRole ?? (props.query.owner_role || "site");
  const currentRole = props.query.owner_role || "site";
  return nextAssignee !== currentAssignee || nextRole !== currentRole;
});

function formatDueDate(iso: string): string {
  return iso.length >= 10 ? iso.slice(0, 10) : iso;
}

const STATUS_LABELS: Record<string, string> = {
  open: "Open",
  answered: "Answered",
  closed: "Closed",
  cancelled: "Cancelled",
};

const OWNER_LABELS: Record<string, string> = {
  site: "Site",
  monitor: "Monitor",
  data_manager: "Data manager",
};

const PRIORITY_LABELS: Record<string, string> = {
  low: "Low",
  normal: "Normal",
  high: "High",
};

const MESSAGE_LABELS: Record<string, string> = {
  raised: "Raised",
  answered: "Answered",
  reopened: "Reopened",
  reassigned: "Reassigned",
  closed: "Closed",
  cancelled: "Cancelled",
  escalated: "Escalated",
};
</script>

<style scoped>
.form-query {
  position: relative;
  display: flex;
  flex-direction: column;
  gap: 6px;
  padding: 10px 12px;
  border: 1px solid var(--border);
  border-radius: 8px;
  font-size: var(--font-size-sm);
  min-width: 0;
  max-width: 100%;
  box-sizing: border-box;
  overflow-wrap: anywhere;
}
.form-query-head {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
}
.form-query-status,
.form-query-badge {
  padding: 0 8px;
  border-radius: 999px;
  background: var(--neutral-light);
  color: var(--semantic-color-text-dark);
  font-size: var(--font-size-2xs);
  font-weight: 600;
}
.form-query-code {
  font-family: var(--semantic-font-mono);
}
.form-query-status[data-status="open"],
.form-query-badge[data-priority="high"] {
  background: var(--warning-bg);
  color: var(--warning);
}
.form-query-badge--escalated {
  border: 1px solid var(--semantic-color-error-text-dark);
  color: var(--semantic-color-error-text-dark);
}
.form-query-due {
  font-size: var(--font-size-2xs);
  color: var(--neutral-dark);
  font-variant-numeric: tabular-nums;
}
.form-query-messages {
  margin: 0;
  padding-left: 18px;
}
.form-query-message-reason {
  color: var(--neutral-dark);
  font-style: italic;
}
.form-query-sod-hint {
  margin: 0;
  color: var(--neutral-dark);
  font-size: var(--font-size-xs);
}
.form-query-form {
  display: flex;
  flex-direction: column;
  gap: 6px;
}
.form-query-form textarea,
.form-query-form input,
.form-query-form select {
  padding: 8px;
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 4px;
  background: var(--semantic-color-surface, inherit);
}
@media (pointer: coarse) {
  .form-query-form textarea,
  .form-query-form input,
  .form-query-form select {
    font-size: var(--font-size-md);
  }
}
.form-query-buttons {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}
</style>

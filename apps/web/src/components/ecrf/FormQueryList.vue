<template>
  <div class="form-query-container">
    <div
      v-if="actMany && availableBulkActions.length > 0"
      class="form-query-bulk"
      data-testid="query-bulk-bar"
    >
      <div class="form-query-bulk-controls">
        <label for="query-bulk-action">Selected action</label>
        <select
          id="query-bulk-action"
          v-model="bulkAction"
          :disabled="busy || assertionsLocked"
        >
          <option
            v-for="action in availableBulkActions"
            :key="action"
            :value="action"
          >
            {{ ACTION_TEXT[action].start }}
          </option>
        </select>
        <button
          id="btn-select-eligible-queries"
          type="button"
          class="btn btn-secondary"
          :disabled="busy || assertionsLocked || eligibleQueries.length === 0"
          @click="selectAllEligible"
        >
          Select eligible ({{ eligibleQueries.length }})
        </button>
        <button
          v-if="selectedQueryIds.length > 0"
          id="btn-clear-selected-queries"
          type="button"
          class="btn btn-secondary"
          :disabled="busy"
          @click="selectedQueryIds = []"
        >
          Clear selection
        </button>
      </div>

      <form
        v-if="selectedEligibleQueries.length > 0"
        class="form-query-form"
        @submit.prevent="confirmBulk"
      >
        <label for="query-bulk-note">
          {{ ACTION_TEXT[bulkAction].label }} ({{
            selectedEligibleQueries.length
          }}
          selected)
        </label>
        <textarea
          id="query-bulk-note"
          v-model="bulkNote"
          rows="2"
          :disabled="busy || assertionsLocked"
        />
        <label for="query-bulk-reason">
          Reason for change ({{ selectedEligibleQueries.length }} selected)
        </label>
        <input
          id="query-bulk-reason"
          v-model="bulkReason"
          type="text"
          autocomplete="off"
          :disabled="busy || assertionsLocked"
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
            {{ ACTION_TEXT[bulkAction].button }} ({{
              selectedEligibleQueries.length
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

    <ul class="form-query-list" data-testid="form-query-list">
      <li
        v-for="query in queries"
        :id="`query-${query.query_id}`"
        :key="query.query_id"
        class="form-query"
        :data-status="query.status"
      >
        <div class="form-query-head">
          <input
            v-if="actMany && availableBulkActions.length > 0"
            :id="`select-query-${query.query_id}`"
            v-model="selectedQueryIds"
            type="checkbox"
            :value="query.query_id"
            :disabled="busy || assertionsLocked || !isEligibleForBulk(query)"
            :aria-label="`Select query on ${query.item_id}`"
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
          <span
            v-if="showContext && (query.site_code || query.subject_code)"
            class="form-query-context"
            :data-testid="`query-context-${query.query_id}`"
          >
            <template v-if="query.site_code"
              >Site {{ query.site_code }}</template
            >
            <template v-if="query.site_code && query.subject_code">
              ·
            </template>
            <template v-if="query.subject_code">{{
              query.subject_code
            }}</template>
          </span>
          <span class="form-query-target">
            <component
              :is="routerLinkTag"
              v-if="observationLink(query)"
              :id="`query-observation-${query.query_id}`"
              class="form-query-link"
              :to="observationLink(query)!"
              >{{ query.item_id }}, revision {{ query.revision }}</component
            >
            <template v-else
              >{{ query.item_id }}, revision {{ query.revision }}</template
            >
            <template v-if="showForm">
              ·
              <component
                :is="routerLinkTag"
                v-if="formLink(query)"
                :id="`query-open-form-${query.query_id}`"
                :data-testid="`query-form-link-${query.query_id}`"
                class="form-query-link"
                :to="formLink(query)!"
                >{{ formDisplayLabel(query) }}</component
              >
              <template v-else>{{ formDisplayLabel(query) }}</template>
            </template>
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
            :disabled="busy || assertionsLocked"
          />
          <label :for="`query-reason-${query.query_id}`"
            >Reason for change</label
          >
          <input
            :id="`query-reason-${query.query_id}`"
            v-model="actionReason"
            type="text"
            autocomplete="off"
            :disabled="busy || assertionsLocked"
          />
          <div class="form-query-buttons">
            <button
              :id="`btn-confirm-${pendingAction.action}-${query.query_id}`"
              type="submit"
              class="btn btn-primary"
              :disabled="
                busy ||
                assertionsLocked ||
                !actionReason.trim() ||
                (actionTakesText(pendingAction.action) && !actionText.trim())
              "
            >
              {{ ACTION_TEXT[pendingAction.action].button
              }}<span class="sr-only"> for {{ query.item_id }}</span>
            </button>
            <button type="button" class="btn btn-secondary" @click="dismiss">
              Back<span class="sr-only">
                from query on {{ query.item_id }}</span
              >
            </button>
          </div>
        </form>
        <div v-else class="form-query-buttons">
          <button
            v-for="action in queryActions(
              query,
              canManage,
              effectiveCanAnswer,
              effectiveCurrentUserId
            )"
            :id="`btn-${action}-${query.query_id}`"
            :key="action"
            type="button"
            class="btn btn-secondary"
            :disabled="busy || assertionsLocked"
            @click="begin(query, action)"
          >
            {{ ACTION_TEXT[action].start
            }}<span class="sr-only"> query on {{ query.item_id }}</span>
          </button>
        </div>
      </li>
    </ul>
  </div>
</template>

<script setup lang="ts">
import { computed, getCurrentInstance, ref, watch } from "vue";
import { getActivePinia } from "pinia";
import type { RouteLocationRaw } from "vue-router";
import {
  actionTakesText,
  type FormQueryAction,
  type FormQueryResponse,
} from "../../api/ecrfQueriesApi";
import {
  isBlockedBySeparationOfDuties,
  isSystemQuery,
  queryActions,
  type FormQueryBatchItemResult,
} from "../../composables/useFormQueries";
import { getNodeDisplay } from "../../canvas/nodeResolver";
import { useAuthStore } from "../../stores/auth";

/**
 * A list of eCRF queries (#5461, ADR-2625) with provenance/ownership/SLA badges, separation-of-duties guards,
 * dedicated `X-Change-Reason` input separate from conversational notes, links to open the target form with the
 * questioned item highlighted, and a multi-query workflow that dispatches one transition per selected query.
 */
const props = withDefaults(
  defineProps<{
    queries: FormQueryResponse[];
    canManage: boolean;
    canAnswer?: boolean;
    currentUserId?: string | null;
    busy: boolean;
    /** Show each query's form link, for a list that spans an enrollment's or study's forms. */
    showForm?: boolean;
    /** Show each query's site and participant codes, for a study-wide worklist. */
    showContext?: boolean;
    studyCode?: string;
    subjectCode?: string;
    formTitles?: Record<string, string>;
    assertionsLocked?: boolean;
    formTo?: (query: FormQueryResponse) => RouteLocationRaw | null;
    observationTo?: (query: FormQueryResponse) => RouteLocationRaw | null;
    act: (
      query: FormQueryResponse,
      action: FormQueryAction,
      text: string,
      reason: string
    ) => Promise<boolean>;
    actMany?: (
      targets: FormQueryResponse[],
      action: FormQueryAction,
      text: string,
      reason: string
    ) => Promise<FormQueryBatchItemResult[]>;
  }>(),
  {
    canAnswer: undefined,
    currentUserId: undefined,
  }
);

const instance = getCurrentInstance();
const hasRouter = Boolean(instance?.appContext.config.globalProperties.$router);
const routerLinkTag = instance?.appContext.components.RouterLink ?? "a";

const effectiveCurrentUserId = computed<string | readonly string[] | null>(
  () => {
    if (props.currentUserId !== undefined) return props.currentUserId;
    const pinia = getActivePinia();
    if (!pinia) return null;
    const authStore = useAuthStore(pinia);
    const candidates = [
      authStore.identity?.id,
      authStore.identity?.username,
      authStore.identity?.email,
    ].filter((v): v is string => Boolean(v?.trim()));
    return candidates.length > 0 ? candidates : null;
  }
);

function observationLink(query: FormQueryResponse): RouteLocationRaw | null {
  if (!hasRouter || !props.observationTo) return null;
  return props.observationTo(query);
}

function formLink(query: FormQueryResponse): RouteLocationRaw | null {
  if (!hasRouter) return null;
  if (props.formTo) return props.formTo(query);
  return defaultFormLocation(query);
}

function formDisplayLabel(query: FormQueryResponse): string {
  const fromProp = props.formTitles?.[query.form_id]?.trim();
  if (fromProp) return fromProp;
  if (query.collection_spec_id) {
    return query.planned_activity_id
      ? `${query.collection_spec_id} · ${query.planned_activity_id}`
      : query.collection_spec_id;
  }
  const resolved = getNodeDisplay(`collection_instance:${query.form_id}`);
  if (resolved.resolved && resolved.title) return resolved.title;
  return "Case report form";
}

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
  closed: "Closed",
  cancelled: "Cancelled",
  escalated: "Escalated",
};

const ACTION_ORDER: readonly FormQueryAction[] = [
  "close",
  "answer",
  "reopen",
  "cancel",
];

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
    label: "Closing note (optional)",
    button: "Close query",
  },
  cancel: {
    start: "Cancel query",
    label: "Cancellation note (optional)",
    button: "Cancel query",
  },
};

const effectiveCanAnswer = computed(() => props.canAnswer ?? false);

const pendingAction = ref<{ queryId: string; action: FormQueryAction } | null>(
  null
);
const actionText = ref("");
const actionReason = ref("");

const bulkAction = ref<FormQueryAction>("close");
const selectedQueryIds = ref<string[]>([]);
const bulkNote = ref("");
const bulkReason = ref("");
const bulkResults = ref<FormQueryBatchItemResult[]>([]);

const availableBulkActions = computed<FormQueryAction[]>(() => {
  const present = new Set<FormQueryAction>();
  for (const q of props.queries) {
    for (const action of queryActions(
      q,
      props.canManage,
      effectiveCanAnswer.value,
      effectiveCurrentUserId.value
    )) {
      present.add(action);
    }
  }
  return ACTION_ORDER.filter((a) => present.has(a));
});

watch(
  availableBulkActions,
  (actions) => {
    if (actions.length > 0 && !actions.includes(bulkAction.value)) {
      bulkAction.value = actions[0];
    }
  },
  { immediate: true }
);

function isEligibleForBulk(query: FormQueryResponse): boolean {
  return queryActions(
    query,
    props.canManage,
    effectiveCanAnswer.value,
    effectiveCurrentUserId.value
  ).includes(bulkAction.value);
}

const eligibleQueries = computed(() =>
  props.queries.filter((q) => isEligibleForBulk(q))
);

const selectedEligibleQueries = computed(() => {
  const selected = new Set(selectedQueryIds.value);
  return eligibleQueries.value.filter((q) => selected.has(q.query_id));
});

watch(bulkAction, () => {
  const allowed = new Set(eligibleQueries.value.map((q) => q.query_id));
  selectedQueryIds.value = selectedQueryIds.value.filter((id) =>
    allowed.has(id)
  );
});

watch(
  () =>
    [
      props.queries,
      props.canManage,
      effectiveCanAnswer.value,
      effectiveCurrentUserId.value,
    ] as const,
  ([nextQueries, canManage, allowAnswer, callerId]) => {
    const actions = availableBulkActions.value;
    if (actions.length > 0 && !actions.includes(bulkAction.value)) {
      bulkAction.value = actions[0];
    }
    const allowedIds = new Set(
      nextQueries
        .filter((q) =>
          queryActions(q, canManage, allowAnswer, callerId).includes(
            bulkAction.value
          )
        )
        .map((q) => q.query_id)
    );
    const hadSelected = selectedQueryIds.value.length > 0;
    const retainedIds = selectedQueryIds.value.filter((id) =>
      allowedIds.has(id)
    );
    if (retainedIds.length !== selectedQueryIds.value.length) {
      selectedQueryIds.value = retainedIds;
    }
    if (hadSelected && retainedIds.length === 0) {
      bulkNote.value = "";
      bulkReason.value = "";
    }

    if (pendingAction.value) {
      const activeTarget = nextQueries.find(
        (q) => q.query_id === pendingAction.value?.queryId
      );
      if (
        !activeTarget ||
        !queryActions(activeTarget, canManage, allowAnswer, callerId).includes(
          pendingAction.value.action
        )
      ) {
        pendingAction.value = null;
        actionText.value = "";
        actionReason.value = "";
      }
    }
  }
);

function selectAllEligible() {
  selectedQueryIds.value = eligibleQueries.value.map((q) => q.query_id);
}

function defaultFormLocation(query: FormQueryResponse): RouteLocationRaw {
  const routeQuery: Record<string, string> = {};
  if (props.studyCode) routeQuery.studyId = props.studyCode;
  const participantCode = query.subject_code || props.subjectCode;
  if (participantCode) routeQuery.subjectId = participantCode;
  if (query.enrollment_id) routeQuery.enrollmentId = query.enrollment_id;
  routeQuery.formId = query.form_id;
  routeQuery.itemId = query.item_id;
  return { path: "/ecrf", query: routeQuery };
}

function begin(query: FormQueryResponse, action: FormQueryAction) {
  pendingAction.value = { queryId: query.query_id, action };
  actionText.value = "";
  actionReason.value = "";
}

function dismiss() {
  pendingAction.value = null;
  actionText.value = "";
  actionReason.value = "";
}

async function confirm(query: FormQueryResponse) {
  const current = pendingAction.value;
  const text = actionText.value.trim();
  const reason = actionReason.value.trim();
  if (!current || !reason) return;
  if (actionTakesText(current.action) && !text) return;
  if (await props.act(query, current.action, text, reason)) dismiss();
}

async function confirmBulk() {
  if (!props.actMany) return;
  const text = bulkNote.value.trim();
  const reason = bulkReason.value.trim();
  const targets = selectedEligibleQueries.value;
  if (!reason || targets.length === 0) return;
  if (actionTakesText(bulkAction.value) && !text) return;
  const results = await props.actMany(targets, bulkAction.value, text, reason);
  bulkResults.value = results;
  const failedIds = new Set(results.filter((r) => !r.ok).map((r) => r.queryId));
  selectedQueryIds.value = selectedQueryIds.value.filter((id) =>
    failedIds.has(id)
  );
  if (failedIds.size === 0) {
    bulkNote.value = "";
    bulkReason.value = "";
  }
}
</script>

<style scoped>
.form-query-container {
  display: flex;
  flex-direction: column;
  gap: 10px;
}
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
.form-query-status,
.form-query-badge {
  padding: 0 8px;
  border-radius: 999px;
  background: var(--neutral-light);
  color: var(--semantic-color-text-dark);
  font-size: 0.75rem;
  font-weight: 600;
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
  font-size: 0.75rem;
  color: var(--neutral-dark);
}
.form-query-context {
  font-weight: 600;
  color: var(--primary);
}
.form-query-target {
  color: var(--neutral-dark);
}
.form-query-link {
  color: var(--semantic-color-interactive-primary);
  text-decoration: underline;
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
.form-query-message-reason {
  color: var(--neutral-dark);
  font-style: italic;
}
.form-query-sod-hint {
  margin: 0;
  color: var(--neutral-dark);
  font-size: 0.8rem;
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

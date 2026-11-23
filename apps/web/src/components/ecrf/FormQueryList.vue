<template>
  <div class="form-query-container">
    <QueryBulkToolbar
      v-if="actMany && availableBulkActions.length > 0"
      :available-actions="availableBulkActions"
      :bulk-action="bulkAction"
      :bulk-note="bulkNote"
      :bulk-reason="bulkReason"
      :bulk-results="bulkResults"
      :eligible-count="eligibleQueries.length"
      :selected-eligible-count="selectedEligibleQueries.length"
      :has-selection="selectedQueryIds.length > 0"
      :busy="busy"
      :assertions-locked="assertionsLocked"
      @update:bulk-action="bulkAction = $event"
      @update:bulk-note="bulkNote = $event"
      @update:bulk-reason="bulkReason = $event"
      @select-eligible="selectAllEligible"
      @clear-selection="clearSelectedQueries"
      @confirm-bulk="confirmBulk"
    />

    <ul class="form-query-list" data-testid="form-query-list">
      <QueryThreadCard
        v-for="query in queries"
        :key="query.query_id"
        :query="query"
        :can-manage="canManage"
        :effective-current-user-id="effectiveCurrentUserId"
        :actions="
          queryActions(
            query,
            canManage,
            effectiveCanAnswer,
            effectiveCurrentUserId
          )
        "
        :active-action="
          pendingAction?.queryId === query.query_id
            ? pendingAction.action
            : null
        "
        :action-text="actionText"
        :action-reason="actionReason"
        :show-checkbox="Boolean(actMany && availableBulkActions.length > 0)"
        :selected="selectedQueryIds.includes(query.query_id)"
        :eligible-for-bulk="isEligibleForBulk(query)"
        :busy="busy"
        :assertions-locked="assertionsLocked"
        :show-form="showForm"
        :show-context="showContext"
        :study-code="studyCode"
        :subject-code="subjectCode"
        :form-titles="formTitles"
        :form-to="formTo"
        :observation-to="observationTo"
        @toggle-select="toggleQuerySelected(query.query_id, $event)"
        @begin="begin(query, $event)"
        @update:action-text="actionText = $event"
        @update:action-reason="actionReason = $event"
        @confirm="confirm(query)"
        @dismiss="dismiss"
      />
    </ul>
  </div>
</template>

<script setup lang="ts">
import { computed, ref, watch } from "vue";
import { getActivePinia } from "pinia";
import { handoffFocus } from "ui";
import type { RouteLocationRaw } from "vue-router";
import {
  actionTakesText,
  type FormQueryAction,
  type FormQueryResponse,
} from "../../api/ecrfQueriesApi";
import {
  queryActions,
  type FormQueryBatchItemResult,
} from "../../composables/useFormQueries";
import { useAuthStore } from "../../stores/auth";
import QueryBulkToolbar from "./QueryBulkToolbar.vue";
import QueryThreadCard from "./QueryThreadCard.vue";

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

const ACTION_ORDER: readonly FormQueryAction[] = [
  "close",
  "answer",
  "reopen",
  "cancel",
];

const effectiveCanAnswer = computed(() => props.canAnswer ?? !props.canManage);

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

function toggleQuerySelected(queryId: string, checked: boolean) {
  if (checked) {
    if (!selectedQueryIds.value.includes(queryId)) {
      selectedQueryIds.value = [...selectedQueryIds.value, queryId];
    }
  } else {
    selectedQueryIds.value = selectedQueryIds.value.filter(
      (id) => id !== queryId
    );
  }
}

function selectAllEligible() {
  selectedQueryIds.value = eligibleQueries.value.map((q) => q.query_id);
}

function clearSelectedQueries() {
  selectedQueryIds.value = [];
  void handoffFocus(["#btn-select-eligible-queries", "#query-bulk-action"]);
}

function restoreQueryActionFocus(
  queryId: string | undefined,
  preferredAction?: FormQueryAction
) {
  void handoffFocus([
    queryId && preferredAction ? `#btn-${preferredAction}-${queryId}` : null,
    queryId
      ? () => {
          const row = document.getElementById(`query-${queryId}`);
          return (
            row?.querySelector<HTMLElement>(
              ".form-query-buttons button:not([disabled])"
            ) ?? null
          );
        }
      : null,
    queryId ? `#query-open-form-${queryId}` : null,
    "#btn-select-eligible-queries",
    "#query-bulk-action",
    () =>
      document.querySelector<HTMLElement>(
        '[data-testid="form-query-list"] button:not([disabled])'
      ),
    "#ecrf-form-queries-title",
    "#study-queries-title",
  ]);
}

function begin(query: FormQueryResponse, action: FormQueryAction) {
  pendingAction.value = { queryId: query.query_id, action };
  actionText.value = "";
  actionReason.value = "";
  void handoffFocus(
    [`#query-text-${query.query_id}`, `#query-reason-${query.query_id}`],
    {
      scrollIfOffscreen: true,
    }
  );
}

function dismiss() {
  const previous = pendingAction.value;
  pendingAction.value = null;
  actionText.value = "";
  actionReason.value = "";
  restoreQueryActionFocus(previous?.queryId, previous?.action);
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
  if (selectedEligibleQueries.value.length === 0) {
    void handoffFocus([
      "#btn-select-eligible-queries",
      "#query-bulk-action",
      () =>
        document.querySelector<HTMLElement>(
          '[data-testid="form-query-list"] button:not([disabled])'
        ),
      "#ecrf-form-queries-title",
      "#study-queries-title",
    ]);
  }
}
</script>

<style scoped>
.form-query-container {
  display: flex;
  flex-direction: column;
  gap: 10px;
}
.form-query-list {
  display: flex;
  flex-direction: column;
  gap: 8px;
  margin: 0;
  padding: 0;
  list-style: none;
}
</style>

<template>
  <div class="form-query-container">
    <QueryBulkToolbar
      v-if="actMany && availableBulkActions.length > 0"
      :available-actions="availableBulkActions"
      :bulk-action="bulkAction"
      :bulk-reason="bulkReason"
      :bulk-results="bulkResults"
      :eligible-count="eligibleQueries.length"
      :selected-eligible-count="selectedEligibleQueries.length"
      :has-selection="selectedQueryIds.length > 0"
      :busy="busy"
      :assertions-locked="assertionsLocked"
      @update:bulk-action="bulkAction = $event"
      @update:bulk-reason="bulkReason = $event"
      @select-eligible="selectAllEligible"
      @clear-selection="selectedQueryIds = []"
      @confirm-bulk="confirmBulk"
    />

    <ul class="form-query-list" data-testid="form-query-list">
      <QueryThreadCard
        v-for="query in queries"
        :key="query.query_id"
        :query="query"
        :actions="queryActions(query, canManage, effectiveCanAnswer)"
        :active-action="
          pendingAction?.queryId === query.query_id
            ? pendingAction.action
            : null
        "
        :action-text="actionText"
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
        @confirm="confirm(query)"
        @dismiss="dismiss"
      />
    </ul>
  </div>
</template>

<script setup lang="ts">
import { computed, ref, watch } from "vue";
import type { RouteLocationRaw } from "vue-router";
import type {
  FormQueryAction,
  FormQueryResponse,
} from "../../api/ecrfQueriesApi";
import {
  queryActions,
  type FormQueryBatchItemResult,
} from "../../composables/useFormQueries";
import QueryBulkToolbar from "./QueryBulkToolbar.vue";
import QueryThreadCard from "./QueryThreadCard.vue";

/**
 * A list of eCRF queries (#5461) with Execution transitions, deep links to the
 * target form/item, and multi-query batch execution with per-query feedback.
 */
const props = withDefaults(
  defineProps<{
    queries: FormQueryResponse[];
    canManage: boolean;
    canAnswer?: boolean;
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
      text: string
    ) => Promise<boolean>;
    actMany?: (
      targets: FormQueryResponse[],
      action: FormQueryAction,
      text: string
    ) => Promise<FormQueryBatchItemResult[]>;
  }>(),
  {
    canAnswer: undefined,
  }
);

const ACTION_ORDER: readonly FormQueryAction[] = [
  "close",
  "answer",
  "reopen",
  "cancel",
];

const effectiveCanAnswer = computed(() => props.canAnswer ?? !props.canManage);

const pendingAction = ref<{ queryId: string; action: FormQueryAction } | null>(
  null
);
const actionText = ref("");

const bulkAction = ref<FormQueryAction>("close");
const selectedQueryIds = ref<string[]>([]);
const bulkReason = ref("");
const bulkResults = ref<FormQueryBatchItemResult[]>([]);

const availableBulkActions = computed<FormQueryAction[]>(() => {
  const present = new Set<FormQueryAction>();
  for (const q of props.queries) {
    for (const action of queryActions(
      q,
      props.canManage,
      effectiveCanAnswer.value
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
    effectiveCanAnswer.value
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
  () => [props.queries, props.canManage, effectiveCanAnswer.value] as const,
  ([nextQueries, canManage, allowAnswer]) => {
    const actions = availableBulkActions.value;
    if (actions.length > 0 && !actions.includes(bulkAction.value)) {
      bulkAction.value = actions[0];
    }
    const allowedIds = new Set(
      nextQueries
        .filter((q) =>
          queryActions(q, canManage, allowAnswer).includes(bulkAction.value)
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
      bulkReason.value = "";
    }

    if (pendingAction.value) {
      const activeTarget = nextQueries.find(
        (q) => q.query_id === pendingAction.value?.queryId
      );
      if (
        !activeTarget ||
        !queryActions(activeTarget, canManage, allowAnswer).includes(
          pendingAction.value.action
        )
      ) {
        pendingAction.value = null;
        actionText.value = "";
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

async function confirmBulk() {
  if (!props.actMany) return;
  const text = bulkReason.value.trim();
  const targets = selectedEligibleQueries.value;
  if (!text || targets.length === 0) return;
  const results = await props.actMany(targets, bulkAction.value, text);
  bulkResults.value = results;
  const failedIds = new Set(results.filter((r) => !r.ok).map((r) => r.queryId));
  selectedQueryIds.value = selectedQueryIds.value.filter((id) =>
    failedIds.has(id)
  );
  if (failedIds.size === 0) {
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
.form-query-list {
  display: flex;
  flex-direction: column;
  gap: 8px;
  margin: 0;
  padding: 0;
  list-style: none;
}
</style>

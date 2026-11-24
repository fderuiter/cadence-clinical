<template>
  <section
    id="ecrf-form-queries"
    aria-labelledby="ecrf-form-queries-title"
    :aria-busy="queriesLoading"
  >
    <h3 id="ecrf-form-queries-title" tabindex="-1">Queries</h3>
    <p v-if="queriesLoading" role="status">Loading queries…</p>
    <p v-else-if="queriesLoadError" class="ecrf-form-error" role="alert">
      The queries could not be loaded: {{ queriesLoadError }}
      <button
        id="ecrf-queries-retry"
        class="btn btn-secondary"
        type="button"
        :disabled="queriesLoading"
        @click="emit('retry-queries')"
      >
        Try again
      </button>
    </p>
    <p v-else-if="queries.length === 0" data-testid="ecrf-no-queries">
      No queries on this form.
    </p>
    <FormQueryList
      v-else
      :queries="queries"
      :can-manage="!!canManageQueries"
      :can-answer="canAnswerQueries"
      :busy="queriesBusy"
      :assertions-locked="assertionsLocked"
      :act="actQuery"
      :reassign="reassignQuery"
      :act-many="actManyQueries"
      :form-titles="currentFormTitles"
      :form-to="queryFormLink"
      :observation-to="queryObservationLink"
      :show-form="Boolean(queryFormLink)"
      @click="emit('query-list-click', $event)"
    />
    <div
      data-testid="ecrf-query-outcome"
      :class="[
        'ecrf-form-outcome',
        queryOutcome
          ? `ecrf-form-outcome--${queryOutcome.kind === 'done' ? 'saved' : 'failed'}`
          : '',
      ]"
      aria-live="polite"
      role="status"
    >
      <template v-if="queryOutcome">
        {{ queryOutcome.message }}
        <button
          v-if="queryOutcome.kind === 'stale'"
          id="ecrf-query-reload"
          class="btn btn-secondary"
          type="button"
          @click="emit('reload-stale')"
        >
          Reload form
        </button>
      </template>
    </div>
  </section>

  <h3>Verifications, queries and answers</h3>
  <p v-if="assertions.length === 0" data-testid="ecrf-no-assertions">
    None recorded.
  </p>
  <ul v-else data-testid="ecrf-form-assertions">
    <li v-for="assertion in assertions" :key="assertion.assertion_id">
      {{ assertion.kind }} on
      <component
        :is="routerLinkTag"
        v-if="assertionLink(assertion.target)"
        class="ecrf-observation-link"
        :to="assertionLink(assertion.target)!"
      >
        {{ assertion.target }}
      </component>
      <template v-else>
        {{ assertion.target }}
      </template>
      <template v-if="assertion.revision !== null">
        (revision {{ assertion.revision }})
      </template>
      by {{ assertion.asserted_by }} at {{ assertion.asserted_at }}
      <template v-if="assertion.text"> : “{{ assertion.text }}” </template>
      <template v-if="assertion.reason">
        (reason: {{ assertion.reason }})
      </template>
      <em v-if="assertion.retracted_at">
        (retracted {{ assertion.retracted_at }})</em
      >
    </li>
  </ul>
</template>

<script setup lang="ts">
import { getCurrentInstance } from "vue";
import type { RouteLocationRaw } from "vue-router";
import type { EcrfFormAssertion, EcrfFormItem } from "../../api/ecrfFormsApi";
import type {
  FormQueryAction,
  FormQueryResponse,
} from "../../api/ecrfQueriesApi";
import type {
  FormQueryBatchItemResult,
  FormQueryOutcome,
  ReassignQueryOptions,
  TransitionQueryOptions,
} from "../../composables/useFormQueries";
import { observationNodeRef } from "../../canvas/nodeRef";
import FormQueryList from "./FormQueryList.vue";

const props = defineProps<{
  queries: FormQueryResponse[];
  queriesLoading: boolean;
  queriesLoadError: string | null;
  queriesBusy: boolean;
  queryOutcome: FormQueryOutcome | null;
  canManageQueries?: boolean;
  canAnswerQueries: boolean;
  assertionsLocked: boolean;
  actQuery: (
    query: FormQueryResponse,
    action: FormQueryAction,
    text: string,
    reason: string,
    options?: TransitionQueryOptions
  ) => Promise<boolean>;
  reassignQuery?: (
    query: FormQueryResponse,
    assignee: string | null,
    reason: string,
    options?: ReassignQueryOptions
  ) => Promise<boolean>;
  actManyQueries: (
    targets: FormQueryResponse[],
    action: FormQueryAction,
    text: string,
    reason: string
  ) => Promise<FormQueryBatchItemResult[]>;
  currentFormTitles?: Record<string, string>;
  queryFormLink?: (query: FormQueryResponse) => RouteLocationRaw | null;
  queryObservationLink: (query: FormQueryResponse) => RouteLocationRaw | null;
  assertions: EcrfFormAssertion[];
  items: EcrfFormItem[];
  itemObservationLink: (
    observationId: string | null | undefined,
    itemId?: string | null
  ) => RouteLocationRaw | null;
}>();

const emit = defineEmits<{
  (e: "retry-queries"): void;
  (e: "reload-stale"): void;
  (e: "query-list-click", event: MouseEvent): void;
}>();

const instance = getCurrentInstance();
const routerLinkTag = instance?.appContext.components.RouterLink ?? "a";

function assertionLink(target: string): RouteLocationRaw | null {
  if (!target.startsWith("observation:")) return null;
  const ref = observationNodeRef(target);
  if (!ref) return null;
  const id = ref.slice("observation:".length);
  const matchedItemId =
    props.items.find((it) => it.observation_id === id)?.item_id ?? null;
  return props.itemObservationLink(id, matchedItemId);
}
</script>

<style scoped>
[data-testid="ecrf-form-assertions"] {
  min-width: 0;
  max-width: 100%;
  overflow-wrap: anywhere;
}
</style>

<style scoped src="./ecrfFormBanners.css"></style>

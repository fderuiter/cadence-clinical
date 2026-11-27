<template>
  <section
    id="observation-queries"
    class="observation-queries-section"
    aria-labelledby="observation-queries-title"
  >
    <h3 id="observation-queries-title">Open queries</h3>
    <p
      v-if="openQueryThreads.length === 0"
      data-testid="observation-no-open-queries"
    >
      No open queries on this value.
    </p>
    <ul v-else class="observation-query-list" data-testid="observation-queries">
      <li
        v-for="thread in openQueryThreads"
        :key="String(thread.query.assertion_id)"
        class="observation-query-item"
        :data-testid="`observation-query-${thread.query.assertion_id}`"
      >
        <div class="observation-query-header tabular-nums">
          <span
            :class="[
              'clinical-badge',
              thread.hasActiveAnswer
                ? 'clinical-badge--warning'
                : 'clinical-badge--queried',
            ]"
          >
            {{ thread.hasActiveAnswer ? "Answered" : "Open" }}
          </span>
          <span v-if="thread.query.revision !== null">
            Revision {{ thread.query.revision }}
          </span>
          <span v-if="thread.valueChanged" class="observation-query-changed">
            Value changed (now rev {{ currentRevisionNumber }})
          </span>
          <span class="observation-query-meta">
            by {{ thread.query.asserted_by }} at
            {{ thread.query.asserted_at }}
          </span>
        </div>
        <p class="observation-query-text">
          {{
            thread.query.text ||
            assertionReason(thread.query) ||
            "Query raised on this value."
          }}
        </p>
        <ul
          v-if="thread.answers.length > 0"
          class="observation-query-answers tabular-nums"
        >
          <li v-for="ans in thread.answers" :key="String(ans.assertion_id)">
            Answered by {{ ans.asserted_by }} at {{ ans.asserted_at
            }}<template v-if="ans.text">: “{{ ans.text }}”</template
            ><template v-else-if="assertionReason(ans)"
              >: “{{ assertionReason(ans) }}”</template
            >
            <em v-if="ans.retracted_at"> (retracted {{ ans.retracted_at }})</em>
          </li>
        </ul>
        <div
          v-if="hasRouter && (owningFormLocation || studyQueriesLocation)"
          class="observation-query-links"
        >
          <component
            :is="routerLinkTag"
            v-if="owningFormLocation"
            class="observation-context-link"
            :to="owningFormLocation"
          >
            Manage query on form
          </component>
          <component
            :is="routerLinkTag"
            v-if="studyQueriesLocation"
            class="observation-context-link"
            :to="studyQueriesLocation"
          >
            View in study queries
          </component>
        </div>
      </li>
    </ul>
  </section>

  <h3>Verifications, queries and answers</h3>
  <p
    v-if="observationAssertions.length === 0"
    data-testid="observation-no-assertions"
  >
    None recorded.
  </p>
  <ul v-else class="tabular-nums" data-testid="observation-assertions">
    <li
      v-for="assertion in observationAssertions"
      :key="String(assertion.assertion_id)"
    >
      {{ assertion.kind }}
      <template v-if="assertion.revision !== null">
        (revision {{ assertion.revision }})
      </template>
      by {{ assertion.asserted_by }} at {{ assertion.asserted_at }}
      <template v-if="assertion.text"> : “{{ assertion.text }}” </template>
      <template v-if="assertionReason(assertion)">
        (reason: {{ assertionReason(assertion) }})
      </template>
      <em v-if="assertion.retracted_at">
        (retracted {{ assertion.retracted_at }})</em
      >
    </li>
  </ul>
</template>

<script setup lang="ts">
import type { Component } from "vue";
import type { RouteLocationRaw } from "vue-router";
import {
  assertionReason,
  type ObservationAssertionItem,
  type ObservationQueryThread,
} from "../../composables/useObservationViewModel";

defineProps<{
  openQueryThreads: ObservationQueryThread[];
  observationAssertions: ObservationAssertionItem[];
  currentRevisionNumber: number;
  hasRouter: boolean;
  routerLinkTag: string | Component;
  owningFormLocation: RouteLocationRaw | null;
  studyQueriesLocation: RouteLocationRaw | null;
}>();
</script>

<style scoped>
.observation-context-link {
  color: var(--semantic-color-interactive-primary);
  font-size: 0.875rem;
  font-weight: 500;
}

.observation-queries-section {
  margin-bottom: 16px;
}

.observation-query-list {
  display: flex;
  flex-direction: column;
  gap: 8px;
  margin: 0;
  padding: 0;
  list-style: none;
}

.observation-query-item {
  padding: 10px 12px;
  border: 1px solid var(--semantic-color-border);
  border-radius: 6px;
  background: var(--semantic-color-surface);
}

.observation-query-header {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
  font-size: 0.85rem;
}

.observation-query-meta {
  color: var(--semantic-color-text-secondary);
}

.observation-query-changed {
  font-weight: 600;
  color: var(--semantic-color-warning-text-dark);
}

.observation-query-text {
  margin: 6px 0 0;
}

.observation-query-answers {
  margin: 8px 0 0;
  padding-left: 18px;
  font-size: 0.875rem;
}

.observation-query-links {
  display: flex;
  flex-wrap: wrap;
  gap: 12px;
  margin-top: 8px;
}
</style>

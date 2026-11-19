<template>
  <section
    id="record-queries"
    class="queries"
    aria-labelledby="record-queries-title"
    :aria-busy="queries.loading"
  >
    <h3 id="record-queries-title">Queries</h3>
    <p
      v-if="!enrollmentId"
      id="record-queries-no-enrollment"
      class="status-line"
    >
      The kernel has no enrollment for {{ subjectCode }} that you may see, so
      its queries can't be listed here.
    </p>
    <p v-else-if="queries.loading" class="status-line" role="status">
      Loading {{ subjectCode }}'s queries…
    </p>
    <p
      v-else-if="queries.loadError"
      id="record-queries-error"
      class="status-line"
      role="alert"
    >
      {{ subjectCode }}'s queries couldn't be loaded: {{ queries.loadError }}
    </p>
    <p
      v-else-if="!queries.queries.length"
      id="record-queries-empty"
      class="status-line"
    >
      No queries on {{ subjectCode }}.
    </p>
    <FormQueryList
      v-else
      :queries="queries.queries"
      :can-manage="canManage"
      :busy="queries.busy"
      :act="queries.act"
      :form-to="formTo"
      :observation-to="observationTo"
      show-form
    />
    <p
      id="record-queries-outcome"
      class="status-line"
      aria-live="polite"
      role="status"
    >
      {{ queries.outcome?.message ?? "" }}
    </p>
    <p v-if="!canManage" id="record-queries-note" class="status-line">
      A CRA or data manager closes, reopens and cancels queries.
    </p>
  </section>
</template>

<script setup lang="ts">
import { computed, reactive, watch } from "vue";
import type { RouteLocationRaw } from "vue-router";
import type { FormQueryResponse } from "../../api/ecrfQueriesApi";
import { useFormQueries } from "../../composables/useFormQueries";
import FormQueryList from "../ecrf/FormQueryList.vue";

/**
 * A participant's queries across their forms, read from the Execution module by kernel enrollment (#5461), with
 * the transitions the module allows from each state. Each query links directly to its target form and observation
 * inside Clinical Canvas.
 */
const props = defineProps<{
  subjectCode: string;
  /** The participant's kernel enrollment id, or null when the kernel has none the reader may see. */
  enrollmentId: string | null;
  canManage: boolean;
  formLocation?: (formId: string) => RouteLocationRaw | null;
  observationLocation?: (
    observationId: string,
    formId: string
  ) => RouteLocationRaw | null;
}>();

const formTo = computed(() =>
  props.formLocation
    ? (q: FormQueryResponse) => props.formLocation!(q.form_id)
    : undefined
);
const observationTo = computed(() =>
  props.observationLocation
    ? (q: FormQueryResponse) =>
        props.observationLocation!(q.observation_id, q.form_id)
    : undefined
);

// reactive() unwraps the composable's refs for the template.
const queries = reactive(useFormQueries());

watch(
  () => props.enrollmentId,
  (enrollmentId) => {
    if (enrollmentId) void queries.load({ enrollmentId });
    else queries.clear();
  },
  { immediate: true }
);
</script>

<style scoped>
.queries {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.queries h3 {
  margin: 0;
  font-size: 1rem;
}

.status-line {
  margin: 0;
  color: var(--neutral-dark);
  font-size: 0.85rem;
}

.status-line:empty {
  display: none;
}
</style>

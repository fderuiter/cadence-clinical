<template>
  <section
    id="record-queries"
    class="queries"
    aria-labelledby="record-queries-title"
    :aria-busy="queries.loading"
  >
    <h3 id="record-queries-title">Queries</h3>
    <p
      v-if="queries.loading || enrollmentLoading"
      class="status-line"
      role="status"
    >
      Loading {{ subjectCode }}'s queries…
    </p>
    <p
      v-else-if="!enrollmentId"
      id="record-queries-no-enrollment"
      class="status-line"
    >
      No enrollment record is visible for {{ subjectCode }}, so queries aren't
      available here.
    </p>
    <CanvasErrorAlert
      v-else-if="queries.loadError"
      id="record-queries-error"
      :message="`${subjectCode}'s queries couldn't be loaded right now.`"
      :detail="queries.loadError"
      @retry="queries.reload()"
      @explain="explainLoadError"
    />
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
      :study-code="studyCode"
      :subject-code="subjectCode"
      :act="queries.act"
      :act-many="queries.actMany"
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
import { useStudyCanvasStore } from "../../stores/studyCanvas";
import FormQueryList from "../ecrf/FormQueryList.vue";
import CanvasErrorAlert from "./CanvasErrorAlert.vue";

/**
 * A participant's queries across their forms, read by enrollment, with links directly to target forms and observations.
 */
const props = defineProps<{
  studyCode?: string;
  subjectCode: string;
  /** The participant's enrollment id, or null when none is visible. */
  enrollmentId: string | null;
  enrollmentLoading?: boolean;
  canManage: boolean;
  formTitles?: Record<string, string>;
  formLocation?: (
    formId: string,
    itemId?: string | null
  ) => RouteLocationRaw | null;
  observationLocation?: (
    observationId: string,
    formId: string,
    itemId?: string | null
  ) => RouteLocationRaw | null;
}>();

const formTo = computed(() =>
  props.formLocation
    ? (q: FormQueryResponse) => props.formLocation!(q.form_id, q.item_id)
    : undefined
);
const observationTo = computed(() =>
  props.observationLocation
    ? (q: FormQueryResponse) =>
        props.observationLocation!(q.observation_id, q.form_id, q.item_id)
    : undefined
);

const emit = defineEmits<{ explain: [] }>();
const canvasStore = useStudyCanvasStore();

// reactive() unwraps the composable's refs for the template.
const queries = reactive(useFormQueries());

function explainLoadError() {
  emit("explain");
  canvasStore.requestExplain({
    targetNodeRef: props.enrollmentId
      ? `enrollment:${props.enrollmentId}`
      : null,
  });
}

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
  min-width: 0;
  max-width: 100%;
  box-sizing: border-box;
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

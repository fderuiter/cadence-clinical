<template>
  <span
    v-if="showContext && (query.site_code || query.subject_code)"
    class="form-query-context"
    :data-testid="`query-context-${query.query_id}`"
  >
    <template v-if="query.site_code">Site {{ query.site_code }}</template>
    <template v-if="query.site_code && query.subject_code"> · </template>
    <template v-if="query.subject_code">{{ query.subject_code }}</template>
  </span>
  <span class="form-query-target">
    <component
      :is="routerLinkTag"
      v-if="resolvedObservationLink"
      :id="`query-observation-${query.query_id}`"
      class="form-query-link"
      :to="resolvedObservationLink"
      >{{ query.item_id }}, revision {{ query.revision }}</component
    >
    <template v-else
      >{{ query.item_id }}, revision {{ query.revision }}</template
    >
    <template v-if="showForm">
      ·
      <component
        :is="routerLinkTag"
        v-if="resolvedFormLink"
        :id="`query-open-form-${query.query_id}`"
        :data-testid="`query-form-link-${query.query_id}`"
        class="form-query-link"
        :to="resolvedFormLink"
        >{{ formDisplayLabel }}</component
      >
      <template v-else>{{ formDisplayLabel }}</template>
    </template>
  </span>
  <span
    v-if="query.value_changed"
    class="form-query-changed"
    data-testid="query-value-changed"
  >
    Value changed (now revision {{ query.current_revision }})
  </span>
</template>

<script setup lang="ts">
import { computed, getCurrentInstance } from "vue";
import type { RouteLocationRaw } from "vue-router";
import type { FormQueryResponse } from "../../api/ecrfQueriesApi";
import { getNodeDisplay } from "../../canvas/nodeResolver";

const props = defineProps<{
  query: FormQueryResponse;
  showForm?: boolean;
  showContext?: boolean;
  studyCode?: string;
  subjectCode?: string;
  formTitles?: Record<string, string>;
  formTo?: (query: FormQueryResponse) => RouteLocationRaw | null;
  observationTo?: (query: FormQueryResponse) => RouteLocationRaw | null;
}>();

const instance = getCurrentInstance();
const hasRouter = Boolean(instance?.appContext.config.globalProperties.$router);
const routerLinkTag = instance?.appContext.components.RouterLink ?? "a";

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

const resolvedObservationLink = computed<RouteLocationRaw | null>(() => {
  if (!hasRouter || !props.observationTo) return null;
  return props.observationTo(props.query);
});

const resolvedFormLink = computed<RouteLocationRaw | null>(() => {
  if (!hasRouter) return null;
  if (props.formTo) return props.formTo(props.query);
  return defaultFormLocation(props.query);
});

const formDisplayLabel = computed<string>(() => {
  const fromProp = props.formTitles?.[props.query.form_id]?.trim();
  if (fromProp) return fromProp;
  if (props.query.collection_spec_id) {
    return props.query.planned_activity_id
      ? `${props.query.collection_spec_id} · ${props.query.planned_activity_id}`
      : props.query.collection_spec_id;
  }
  const resolved = getNodeDisplay(`collection_instance:${props.query.form_id}`);
  if (resolved.resolved && resolved.title) return resolved.title;
  return "Case report form";
});
</script>

<style scoped>
.form-query-context {
  font-weight: 600;
  color: var(--text-main);
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
</style>

<template>
  <span
    v-if="showContext && (siteDisplayLabel || participantDisplayLabel)"
    class="form-query-context"
    :data-testid="`query-context-${query.query_id}`"
  >
    <EntityLink
      v-if="siteDisplayLabel"
      :data-testid="`query-site-link-${query.query_id}`"
      :node-ref="siteRef"
      :label="siteDisplayLabel"
      :to="siteLocation"
      :study-code="studyCode"
      :site="effectiveSite"
      :participant="effectiveParticipant"
      :resolve-remotely="false"
    />
    <template v-if="siteDisplayLabel && participantDisplayLabel"> · </template>
    <EntityLink
      v-if="participantDisplayLabel"
      :data-testid="`query-participant-link-${query.query_id}`"
      :node-ref="participantRef"
      :label="participantDisplayLabel"
      :to="participantLocation"
      :study-code="studyCode"
      :site="effectiveSite"
      :participant="effectiveParticipant"
      :resolve-remotely="false"
    />
    <template
      v-if="(siteDisplayLabel || participantDisplayLabel) && visitDisplayLabel"
    >
      ·
    </template>
    <EntityLink
      v-if="visitDisplayLabel"
      :data-testid="`query-visit-link-${query.query_id}`"
      :label="visitDisplayLabel"
      :to="visitLocation"
      :study-code="studyCode"
      :site="effectiveSite"
      :participant="effectiveParticipant"
      :resolve-remotely="false"
    />
  </span>
  <span class="form-query-target">
    <span
      v-if="isObservationMasked"
      :id="`query-observation-${query.query_id}`"
      class="entity-link-masked"
      data-masked="true"
      >masked</span
    >
    <component
      :is="routerLinkTag"
      v-else-if="resolvedObservationLink"
      :id="`query-observation-${query.query_id}`"
      class="form-query-link"
      :to="resolvedObservationLink"
      >{{ observationDisplayText }}</component
    >
    <template v-else>{{ observationDisplayText }}</template>
    <template v-if="showForm">
      ·
      <span
        v-if="isFormMasked"
        :id="`query-open-form-${query.query_id}`"
        :data-testid="`query-form-link-${query.query_id}`"
        class="entity-link-masked"
        data-masked="true"
        >masked</span
      >
      <component
        :is="routerLinkTag"
        v-else-if="resolvedFormLink"
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
    v-if="hasFindingLink || hasRulePackLink"
    class="form-query-provenance"
    :data-testid="`query-provenance-${query.query_id}`"
  >
    <EntityLink
      v-if="hasFindingLink"
      :data-testid="`query-finding-link-${query.query_id}`"
      :node-ref="findingRef"
      :label="findingDisplayLabel"
      kind-label="Finding"
      :to="findingLocation"
      :study-code="studyCode"
      :site="effectiveSite"
      :participant="effectiveParticipant"
      :resolve-remotely="false"
    />
    <template v-if="hasFindingLink && hasRulePackLink"> · </template>
    <EntityLink
      v-if="hasRulePackLink"
      :data-testid="`query-rule-pack-link-${query.query_id}`"
      :node-ref="rulePackRef"
      :label="rulePackDisplayLabel"
      kind-label="Rule pack"
      :to="rulePackLocation"
      :study-code="studyCode"
      :site="effectiveSite"
      :participant="effectiveParticipant"
      :resolve-remotely="false"
    />
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
import {
  enrollmentNodeRef,
  libraryItemRef,
  studySiteRef,
  validationFindingRef,
} from "../../canvas/nodeRef";
import { getNodeDisplay } from "../../canvas/nodeResolver";
import EntityLink from "../canvas/EntityLink.vue";

const props = defineProps<{
  query: FormQueryResponse;
  showForm?: boolean;
  showContext?: boolean;
  studyCode?: string;
  siteScope?: string;
  participantScope?: string;
  subjectCode?: string;
  formTitles?: Record<string, string>;
  formTo?: (query: FormQueryResponse) => RouteLocationRaw | null;
  observationTo?: (query: FormQueryResponse) => RouteLocationRaw | null;
}>();

const instance = getCurrentInstance();
const hasRouter = Boolean(instance?.appContext.config.globalProperties.$router);
const routerLinkTag = instance?.appContext.components.RouterLink ?? "a";

const effectiveSite = computed(() => {
  if (props.query.site_label === "masked") return undefined;
  return props.query.site_code || props.siteScope || undefined;
});

const effectiveParticipant = computed(() => {
  if (props.query.participant_label === "masked") return undefined;
  return (
    props.participantScope ||
    props.query.subject_code ||
    props.subjectCode ||
    undefined
  );
});

const siteRef = computed(
  () => props.query.site_ref ?? studySiteRef(props.query.study_site_id)
);

const siteDisplayLabel = computed<string | null>(() => {
  if (props.query.site_label === "masked") return "masked";
  if (props.query.site_code) return `Site ${props.query.site_code}`;
  if (props.query.site_label) return props.query.site_label;
  return null;
});

const siteLocation = computed<RouteLocationRaw | null>(() => {
  if (!hasRouter || !props.studyCode || props.query.site_label === "masked") {
    return null;
  }
  const site = effectiveSite.value;
  if (!site && !siteRef.value) return null;
  return {
    name: "study",
    params: { studyCode: props.studyCode },
    query: {
      stage: "start",
      section: "sites",
      ...(site ? { site } : {}),
      ...(effectiveParticipant.value
        ? { participant: effectiveParticipant.value }
        : {}),
      ...(siteRef.value ? { node: siteRef.value } : {}),
    },
  };
});

const participantRef = computed(
  () =>
    props.query.participant_ref ?? enrollmentNodeRef(props.query.enrollment_id)
);

const participantDisplayLabel = computed<string | null>(() => {
  if (props.query.participant_label === "masked") return "masked";
  if (props.query.subject_code) return props.query.subject_code;
  if (props.query.participant_label) return props.query.participant_label;
  return null;
});

const participantLocation = computed<RouteLocationRaw | null>(() => {
  if (
    !hasRouter ||
    !props.studyCode ||
    props.query.participant_label === "masked"
  ) {
    return null;
  }
  const participant = effectiveParticipant.value;
  if (!participant && !participantRef.value) return null;
  return {
    name: "study",
    params: { studyCode: props.studyCode },
    query: {
      stage: "conduct",
      ...(participant ? {} : { section: "participants" }),
      ...(effectiveSite.value ? { site: effectiveSite.value } : {}),
      ...(participant ? { participant } : {}),
      ...(participantRef.value ? { node: participantRef.value } : {}),
    },
  };
});

const visitDisplayLabel = computed<string | null>(() => {
  if (props.query.visit_label === "masked") return "masked";
  if (props.query.visit_label) return props.query.visit_label;
  return null;
});

const visitLocation = computed<RouteLocationRaw | null>(() => {
  if (!hasRouter || !props.studyCode || props.query.visit_label === "masked") {
    return null;
  }
  if (resolvedFormLink.value) return resolvedFormLink.value;
  return {
    name: "study",
    params: { studyCode: props.studyCode },
    query: {
      stage: "design",
      section: "protocol-versions",
      ...(effectiveSite.value ? { site: effectiveSite.value } : {}),
      ...(effectiveParticipant.value
        ? { participant: effectiveParticipant.value }
        : {}),
    },
  };
});

const isObservationMasked = computed(
  () =>
    props.query.observation_label === "masked" ||
    props.query.item_label === "masked"
);

const observationDisplayText = computed(() => {
  const itemText =
    props.query.item_label &&
    props.query.item_label !== "masked" &&
    props.query.item_label !== props.query.item_id
      ? `${props.query.item_label} (${props.query.item_id})`
      : props.query.item_id;
  return `${itemText}, revision ${props.query.revision}`;
});

const isFormMasked = computed(() => props.query.form_label === "masked");

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
  if (!hasRouter || !props.observationTo || isObservationMasked.value) {
    return null;
  }
  return props.observationTo(props.query);
});

const resolvedFormLink = computed<RouteLocationRaw | null>(() => {
  if (!hasRouter || isFormMasked.value) return null;
  if (props.formTo) return props.formTo(props.query);
  return defaultFormLocation(props.query);
});

const formDisplayLabel = computed<string>(() => {
  const fromProp = props.formTitles?.[props.query.form_id]?.trim();
  if (fromProp) return fromProp;
  if (props.query.form_label && props.query.form_label !== "masked") {
    return props.query.form_label;
  }
  if (props.query.collection_spec_id) {
    return props.query.planned_activity_id
      ? `${props.query.collection_spec_id} · ${props.query.planned_activity_id}`
      : props.query.collection_spec_id;
  }
  const resolved = getNodeDisplay(`collection_instance:${props.query.form_id}`);
  if (resolved.resolved && resolved.title) return resolved.title;
  return "Case report form";
});

const findingRef = computed(
  () =>
    props.query.validation_finding_ref ??
    validationFindingRef(props.query.validation_finding_id)
);

const findingDisplayLabel = computed<string | null>(() => {
  if (props.query.validation_finding_label === "masked") return "masked";
  if (props.query.validation_finding_label) {
    return props.query.validation_finding_label;
  }
  if (props.query.rule_id) {
    return props.query.rule_version
      ? `${props.query.rule_id} v${props.query.rule_version}`
      : props.query.rule_id;
  }
  return findingRef.value ? "Validation finding" : null;
});

const hasFindingLink = computed(() =>
  Boolean(findingRef.value || props.query.validation_finding_label)
);

const findingLocation = computed<RouteLocationRaw | null>(() => {
  if (
    !hasRouter ||
    !props.studyCode ||
    props.query.validation_finding_label === "masked"
  ) {
    return null;
  }
  return {
    name: "study",
    params: { studyCode: props.studyCode },
    query: {
      stage: "data",
      section: "validation",
      ...(effectiveSite.value ? { site: effectiveSite.value } : {}),
      ...(effectiveParticipant.value
        ? { participant: effectiveParticipant.value }
        : {}),
      ...(props.query.pack_key ? { pack: props.query.pack_key } : {}),
      ...(findingRef.value ? { node: findingRef.value } : {}),
    },
  };
});

const rulePackRef = computed(
  () => props.query.rule_pack_ref ?? libraryItemRef(props.query.rule_pack_id)
);

const rulePackDisplayLabel = computed<string | null>(() => {
  if (props.query.rule_pack_label === "masked") return "masked";
  if (props.query.rule_pack_label) return props.query.rule_pack_label;
  if (props.query.pack_key) return props.query.pack_key;
  return rulePackRef.value ? "Rule pack" : null;
});

const hasRulePackLink = computed(() =>
  Boolean(
    rulePackRef.value || props.query.rule_pack_label || props.query.pack_key
  )
);

const rulePackLocation = computed<RouteLocationRaw | null>(() => {
  if (
    !hasRouter ||
    !props.studyCode ||
    props.query.rule_pack_label === "masked"
  ) {
    return null;
  }
  return {
    name: "study",
    params: { studyCode: props.studyCode },
    query: {
      stage: "data",
      section: "validation",
      ...(effectiveSite.value ? { site: effectiveSite.value } : {}),
      ...(effectiveParticipant.value
        ? { participant: effectiveParticipant.value }
        : {}),
      ...(props.query.pack_key ? { pack: props.query.pack_key } : {}),
      ...(rulePackRef.value ? { node: rulePackRef.value } : {}),
    },
  };
});
</script>

<style scoped>
.form-query-context {
  display: inline-flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 4px;
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
.form-query-provenance {
  display: inline-flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 4px;
  font-size: 0.8rem;
}
.form-query-changed {
  font-size: 0.75rem;
  font-weight: 600;
  color: var(--semantic-color-interactive-primary);
}
.entity-link-masked {
  color: var(--semantic-color-text-muted);
  font-style: italic;
}
</style>

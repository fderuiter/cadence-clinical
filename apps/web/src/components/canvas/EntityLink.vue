<template>
  <span
    v-if="isMasked"
    v-bind="$attrs"
    class="entity-link-masked"
    data-masked="true"
  >
    <template v-if="kindPrefix">{{ kindPrefix }}: </template>masked
  </span>
  <span v-else-if="canonicalRef" v-bind="$attrs" class="entity-link-wrap">
    <span v-if="kindPrefix" class="entity-link-prefix">{{ kindPrefix }}: </span>
    <NodeRefChip
      :node-ref="canonicalRef"
      :fallback-title="cleanLabel"
      :to="resolvedLocation"
      :auto-link="Boolean(resolvedLocation || autoLink)"
      :resolve-remotely="resolveRemotely"
    />
  </span>
  <component
    :is="linkTag"
    v-else-if="resolvedLocation && displayFallback"
    v-bind="$attrs"
    :to="resolvedLocation"
    class="entity-link"
  >
    <template v-if="kindPrefix">{{ kindPrefix }}: </template
    >{{ displayFallback }}
  </component>
  <span v-else-if="displayFallback" v-bind="$attrs" class="entity-link-text">
    <template v-if="kindPrefix">{{ kindPrefix }}: </template
    >{{ displayFallback }}
  </span>
</template>

<script setup lang="ts">
import { computed, getCurrentInstance } from "vue";
import type { LocationQueryRaw, RouteLocationRaw } from "vue-router";
import {
  getNodeDisplay,
  normalizeLookupNodeRef,
} from "../../canvas/nodeResolver";
import NodeRefChip from "./NodeRefChip.vue";

const UUID_ONLY_PATTERN =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

const props = withDefaults(
  defineProps<{
    nodeRef?: string | null;
    label?: string | null;
    kindLabel?: string | null;
    to?: RouteLocationRaw | null;
    studyCode?: string | null;
    stage?: string | null;
    section?: string | null;
    site?: string | null;
    participant?: string | null;
    autoLink?: boolean;
    resolveRemotely?: boolean;
  }>(),
  {
    nodeRef: null,
    label: null,
    kindLabel: null,
    to: null,
    studyCode: null,
    stage: null,
    section: null,
    site: null,
    participant: null,
    autoLink: true,
    resolveRemotely: true,
  }
);

const instance = getCurrentInstance();
const hasRouter = Boolean(instance?.appContext.config.globalProperties.$router);
const routerLinkComponent = instance?.appContext.components.RouterLink ?? null;
const linkTag = computed(() => routerLinkComponent ?? "a");

const currentRoute = computed(
  () =>
    (
      instance?.appContext.config.globalProperties as {
        $route?: {
          name?: string | symbol | null;
          params?: Record<string, unknown>;
          query?: Record<string, unknown>;
        };
      }
    )?.$route ?? null
);

const canonicalRef = computed(() => normalizeLookupNodeRef(props.nodeRef));

const cleanLabel = computed(() => {
  const raw = props.label?.trim() ?? "";
  if (!raw || UUID_ONLY_PATTERN.test(raw)) return null;
  return raw;
});

const kindPrefix = computed(() => {
  const prefix = props.kindLabel?.trim() ?? "";
  if (!prefix) return "";
  if (
    cleanLabel.value &&
    cleanLabel.value.toLowerCase().startsWith(prefix.toLowerCase())
  ) {
    return "";
  }
  return prefix;
});

const isMasked = computed(() => {
  if (props.label?.trim().toLowerCase() === "masked") return true;
  if (canonicalRef.value) {
    return getNodeDisplay(canonicalRef.value, cleanLabel.value).masked;
  }
  return false;
});

const displayFallback = computed(() => cleanLabel.value);

const resolvedLocation = computed<RouteLocationRaw | null>(() => {
  if (!hasRouter || !routerLinkComponent || isMasked.value) return null;
  if (props.to) return props.to;
  if (!props.autoLink || !canonicalRef.value) return null;

  const route = currentRoute.value;
  const routeStudy =
    typeof route?.params?.studyCode === "string"
      ? route.params.studyCode.trim()
      : "";
  const effectiveStudy = props.studyCode?.trim() || routeStudy;
  if (!effectiveStudy) return null;

  const baseQuery: LocationQueryRaw =
    route?.name === "study" && routeStudy === effectiveStudy
      ? { ...(route.query as LocationQueryRaw) }
      : {};

  if (props.stage?.trim()) {
    baseQuery.stage = props.stage.trim();
  }
  if (props.section?.trim()) {
    baseQuery.section = props.section.trim();
  }
  if (props.site?.trim()) {
    baseQuery.site = props.site.trim();
  }
  if (props.participant?.trim()) {
    baseQuery.participant = props.participant.trim();
  }
  baseQuery.node = canonicalRef.value;

  return {
    name: "study",
    params: { studyCode: effectiveStudy },
    query: baseQuery,
  };
});
</script>

<style scoped>
.entity-link-wrap {
  display: inline-flex;
  align-items: center;
  gap: 3px;
}

.entity-link-prefix {
  color: var(--semantic-color-text-secondary);
  font-size: 0.8rem;
}

.entity-link {
  color: var(--semantic-color-primary);
  font-weight: 500;
  text-decoration: underline;
  text-underline-offset: 2px;
}

.entity-link:hover {
  color: var(--semantic-color-primary-dark);
}

.entity-link:focus-visible {
  outline: 2px solid var(--semantic-color-accent);
  outline-offset: 2px;
  border-radius: 3px;
}

.entity-link-masked {
  color: var(--semantic-color-text-muted);
  font-style: italic;
}
</style>

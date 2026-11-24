<template>
  <EntityLink
    :node-ref="summary.ref"
    :label="summary.masked ? '' : summary.label"
    :kind="summary.kind"
    :code="summary.code || ''"
    :status="summary.status || ''"
    :include-kind="includeKind"
    :masked="summary.masked ? summary.maskReason || true : false"
    :masked-label="summary.masked ? summary.label : ''"
    :to="fullViewLocation ?? undefined"
    :inspector-to="inspectorLocation ?? undefined"
    :preview-mode="effectivePreviewMode"
    :subtitle="previewSubtitle"
    :resolve-remotely="false"
    @select="emit('select', summary.ref)"
    @inspect="requestCanvasFocus"
    @navigate="requestCanvasFocus"
  />
</template>

<script setup lang="ts">
import { computed, getCurrentInstance, nextTick, watch } from "vue";
import { getActivePinia } from "pinia";
import type { RouteLocationRaw } from "vue-router";
import { EntityLink } from "ui";
import { resolveEntityFullViewLocation } from "../../canvas/entityDestination";
import { ensureNodesResolved, getNodeDisplay } from "../../canvas/nodeResolver";
import { useStudyCanvasStore } from "../../stores/studyCanvas";

const props = withDefaults(
  defineProps<{
    nodeRef: string;
    fallbackTitle?: string | null;
    includeKind?: boolean;
    to?: RouteLocationRaw | null;
    autoLink?: boolean;
    resolveRemotely?: boolean;
    studyCode?: string | null;
    previewMode?: "drawer" | "inspector" | "navigate" | null;
  }>(),
  {
    fallbackTitle: null,
    includeKind: false,
    to: null,
    autoLink: true,
    resolveRemotely: true,
    studyCode: null,
    previewMode: null,
  }
);

const emit = defineEmits<{
  (e: "select", nodeRef: string): void;
}>();

const instance = getCurrentInstance();
const hasRouter = Boolean(instance?.appContext.config.globalProperties.$router);
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

watch(
  () => [props.nodeRef, props.resolveRemotely] as const,
  ([nextRef, remote]) => {
    if (nextRef && remote) ensureNodesResolved([nextRef]);
  },
  { immediate: true }
);

const summary = computed(() =>
  getNodeDisplay(props.nodeRef, props.fallbackTitle)
);

const activeStudyCode = computed(() => {
  if (props.studyCode && props.studyCode.trim()) {
    return props.studyCode.trim();
  }
  const route = currentRoute.value;
  return typeof route?.params?.studyCode === "string"
    ? route.params.studyCode.trim()
    : "";
});

const activeSiteCode = computed(() => {
  const route = currentRoute.value;
  const site = route?.query?.site;
  return typeof site === "string" ? site.trim() : "";
});

const inspectorLocation = computed<RouteLocationRaw | null>(() => {
  if (!hasRouter || props.autoLink === false) return null;
  const route = currentRoute.value;
  const routeStudy =
    typeof route?.params?.studyCode === "string"
      ? route.params.studyCode.trim()
      : "";
  if (route?.name === "study" && routeStudy && summary.value.ref) {
    return {
      name: "study",
      params: { studyCode: routeStudy },
      query: { ...(route.query ?? {}), node: summary.value.ref },
    };
  }
  return null;
});

const fullViewLocation = computed<RouteLocationRaw | null>(() => {
  if (!hasRouter) return null;
  if (props.to) return props.to;
  if (props.autoLink === false) return null;
  return resolveEntityFullViewLocation(summary.value.ref, {
    studyCode: activeStudyCode.value,
    site: activeSiteCode.value,
    entityCode: summary.value.code,
  });
});

const effectivePreviewMode = computed<"drawer" | "inspector" | "navigate">(
  () => {
    if (props.previewMode) return props.previewMode;
    if (props.to) return "navigate";
    if (props.autoLink === false) return "inspector";
    if (inspectorLocation.value) return "inspector";
    return "drawer";
  }
);

const previewSubtitle = computed(() => {
  if (!activeStudyCode.value) return "";
  return `Study ${activeStudyCode.value}`;
});

function requestCanvasFocus() {
  if (currentRoute.value?.name === "study" && summary.value.ref) {
    const pinia = getActivePinia();
    if (pinia) {
      const canvasStore = useStudyCanvasStore(pinia);
      const targetRef = summary.value.ref;
      void Promise.resolve()
        .then(() => nextTick())
        .then(() =>
          canvasStore.requestInspectorFocus({
            nodeRef: targetRef,
            scrollIntoView: true,
          })
        );
    }
  }
}
</script>

<template>
  <span v-if="summary.masked" class="noderef-masked">{{ summary.label }}</span>
  <component
    :is="linkTag"
    v-else-if="toLocation"
    :to="toLocation"
    class="noderef-chip node-ref-chip"
    :title="tooltipText"
    @click="onSelectClick"
  >
    <span class="noderef-chip-label">{{ displayText }}</span>
  </component>
  <button
    v-else
    type="button"
    class="noderef-chip node-ref-chip noderef-chip-button"
    :title="tooltipText"
    @click="emit('select', summary.ref)"
  >
    <span class="noderef-chip-label">{{ displayText }}</span>
  </button>
</template>

<script setup lang="ts">
import { computed, getCurrentInstance, watch } from "vue";
import type { RouteLocationRaw } from "vue-router";
import { ensureNodesResolved, getNodeDisplay } from "../../canvas/nodeResolver";

const props = withDefaults(
  defineProps<{
    nodeRef: string;
    fallbackTitle?: string | null;
    includeKind?: boolean;
    to?: RouteLocationRaw | null;
    autoLink?: boolean;
    resolveRemotely?: boolean;
  }>(),
  {
    fallbackTitle: null,
    includeKind: false,
    to: null,
    autoLink: true,
    resolveRemotely: true,
  }
);

const emit = defineEmits<{
  (e: "select", nodeRef: string): void;
}>();

const instance = getCurrentInstance();
const hasRouter = Boolean(instance?.appContext.config.globalProperties.$router);
const routerLinkComponent = instance?.appContext.components.RouterLink ?? null;
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

const displayText = computed(() => {
  const s = summary.value;
  if (!props.includeKind) return s.label;
  if (s.label.toLowerCase().startsWith(s.kindLabel.toLowerCase())) {
    return s.label;
  }
  return `${s.kindLabel}: ${s.label}`;
});

const tooltipText = computed(() => {
  const s = summary.value;
  return `Inspect ${s.kindLabel.toLowerCase()} (${s.label})`;
});

const toLocation = computed<RouteLocationRaw | null>(() => {
  if (!hasRouter || !routerLinkComponent) return null;
  if (props.to) return props.to;
  if (props.autoLink === false) return null;
  const route = currentRoute.value;
  const studyCode =
    typeof route?.params?.studyCode === "string"
      ? route.params.studyCode.trim()
      : "";
  if (route?.name === "study" && studyCode && summary.value.ref) {
    return {
      name: "study",
      params: { studyCode },
      query: { ...(route.query ?? {}), node: summary.value.ref },
    };
  }
  return null;
});

const linkTag = computed(() => routerLinkComponent ?? "a");

function onSelectClick() {
  emit("select", summary.value.ref);
}
</script>

<style scoped>
.noderef-chip {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  color: var(--semantic-color-primary);
  font-weight: 500;
  text-decoration: underline;
  text-underline-offset: 2px;
  background: none;
  border: 0;
  padding: 0;
  font-family: inherit;
  font-size: inherit;
  cursor: pointer;
  text-align: left;
}

.noderef-chip:hover {
  color: var(--semantic-color-primary-dark);
}

.noderef-chip:focus-visible {
  outline: 2px solid var(--semantic-color-accent);
  outline-offset: 2px;
  border-radius: 3px;
}

.noderef-masked {
  color: var(--semantic-color-text-muted);
  font-style: italic;
}
</style>

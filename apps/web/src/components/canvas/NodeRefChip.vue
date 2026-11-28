<template>
  <span v-if="summary.masked" class="noderef-masked">{{ summary.label }}</span>
  <component
    :is="linkTag"
    v-else-if="toLocation"
    :to="toLocation"
    class="noderef-chip node-ref-chip"
    :title="tooltipText"
    @click.capture="onSelectClick"
  >
    <span class="noderef-chip-label">{{ displayText }}</span>
  </component>
  <button
    v-else
    type="button"
    class="noderef-chip node-ref-chip noderef-chip-button"
    :title="tooltipText"
    @click="onButtonClick"
  >
    <span class="noderef-chip-label">{{ displayText }}</span>
  </button>
</template>

<script setup lang="ts">
import { computed, getCurrentInstance, nextTick, watch } from "vue";
import { getActivePinia } from "pinia";
import type { RouteLocationRaw } from "vue-router";
import { getTopActiveModalContainer, hasActiveModalFocusTrap } from "ui";
import { ensureNodesResolved, getNodeDisplay } from "../../canvas/nodeResolver";
import { openEntityPeekDrawer } from "../../composables/useEntityPeekDrawer";
import { useStudyCanvasStore } from "../../stores/studyCanvas";

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

function isActivatedInsideModalTrap(event?: MouseEvent): boolean {
  if (!hasActiveModalFocusTrap()) return false;
  const topModal = getTopActiveModalContainer();
  const targetEl =
    event?.currentTarget instanceof Element ? event.currentTarget : null;
  return !topModal || !targetEl || topModal.contains(targetEl);
}

function onButtonClick() {
  emit("select", summary.value.ref);
  if (summary.value.ref) {
    openEntityPeekDrawer({
      nodeRef: summary.value.ref,
      label: displayText.value,
    });
  }
}

function onSelectClick(event?: MouseEvent) {
  emit("select", summary.value.ref);
  if (
    event &&
    (event.defaultPrevented ||
      (typeof event.button === "number" && event.button !== 0) ||
      event.metaKey ||
      event.ctrlKey ||
      event.shiftKey ||
      event.altKey)
  ) {
    return;
  }
  if (summary.value.ref && isActivatedInsideModalTrap(event)) {
    event?.preventDefault();
    event?.stopPropagation();
    openEntityPeekDrawer({
      nodeRef: summary.value.ref,
      label: displayText.value,
    });
    return;
  }
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

<style scoped>
.noderef-chip {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  min-height: var(--semantic-control-target-min, 24px);
  min-width: var(--semantic-control-target-min, 24px);
  max-width: 100%;
  box-sizing: border-box;
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

.noderef-chip-label {
  min-width: 0;
  max-width: 100%;
  overflow-wrap: anywhere;
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
  overflow-wrap: anywhere;
}
</style>

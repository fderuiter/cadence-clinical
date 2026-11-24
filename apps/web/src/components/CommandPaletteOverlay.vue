<template>
  <CanvasPalette
    v-if="isOpen"
    :study-code="activeStudyCode"
    :participants="activeCanvasStore?.paletteParticipants ?? []"
    :workspace-items="workspaceItems"
    @close="emit('close')"
    @go="handleGo"
  />
</template>

<script setup lang="ts">
import { computed, nextTick, watch } from "vue";
import { getActivePinia } from "pinia";
import { useRoute, useRouter } from "vue-router";
import CanvasPalette from "./canvas/CanvasPalette.vue";
import {
  buildWorkspacePaletteItems,
  executePaletteTarget,
  resolveActiveWorkspaceScope,
  type PaletteTarget,
  type PaletteWorkspaceAction,
} from "../canvas/palette";
import { useAuthStore } from "../stores/auth";
import { useKnowledgeStore } from "../stores/knowledge";
import { useOnboardingStore } from "../stores/onboarding";
import {
  getLoadedStudyCanvasStore,
  loadStudyCanvasStore,
} from "../stores/studyScopeBridge";
import { useActiveStudyScope } from "../composables/useActiveStudyScope";

const props = defineProps<{
  isOpen?: boolean;
}>();

const emit = defineEmits<{
  close: [];
}>();

const pinia = getActivePinia();
const route = useRoute();
const router = useRouter();
const authStore = useAuthStore();
const knowledgeStore = useKnowledgeStore();
const onboardingStore = useOnboardingStore();
const activeCanvasStore = computed(() => getLoadedStudyCanvasStore(pinia));
const { activeStudyCode: syncedStudyCode, setActiveStudyCode } =
  useActiveStudyScope();
const workspaceScope = computed(() =>
  resolveActiveWorkspaceScope(route, activeCanvasStore.value)
);
const activeStudyCode = computed(
  () => workspaceScope.value.studyCode || syncedStudyCode.value
);

watch(
  [() => props.isOpen, activeStudyCode],
  async ([open, code]) => {
    if (!open) return;
    const store =
      getLoadedStudyCanvasStore(pinia) ?? (await loadStudyCanvasStore(pinia));
    if (code) {
      store?.ensurePaletteParticipants(code);
    }
  },
  { immediate: true }
);

const workspaceItems = computed(() =>
  buildWorkspacePaletteItems({
    isAuthenticated: Boolean(authStore.isAuthenticated),
    normalizedRoles: authStore.normalizedRoles || [],
    availablePersonas: authStore.availablePersonas || [],
    activeStudyCode: activeStudyCode.value,
    activeSiteId: workspaceScope.value.siteId,
    activeExplicitSiteId: workspaceScope.value.explicitSiteId,
    activeSubjectCode: workspaceScope.value.subjectCode,
    activeParticipantRef: workspaceScope.value.participantRef,
    activeEnrollmentId: workspaceScope.value.enrollmentId,
  })
);

function handleAction(action: PaletteWorkspaceAction) {
  if (action.kind === "open-help") {
    knowledgeStore.openDrawer();
  } else if (action.kind === "open-checklist") {
    onboardingStore.openDrawer();
  } else if (action.kind === "start-tour") {
    onboardingStore.startTour();
  } else if (action.kind === "switch-persona") {
    authStore.switchPersona(action.personaKey);
  }
}

async function handleGo(target: PaletteTarget) {
  const priorStudyCode = activeStudyCode.value;
  const priorScope = {
    ...workspaceScope.value,
    studyCode: priorStudyCode,
  };
  if (target.studyCode) {
    setActiveStudyCode(target.studyCode);
  }
  let canvasStore = getLoadedStudyCanvasStore(pinia);
  if (target.participant) {
    canvasStore ??= await loadStudyCanvasStore(pinia);
    if (canvasStore) {
      if (target.studyCode && target.studyCode !== canvasStore.studyCode) {
        canvasStore.openStudy(target.studyCode);
      }
      canvasStore.rememberParticipants([target.participant]);
    }
  }
  emit("close");
  await nextTick();
  if (
    typeof document !== "undefined" &&
    (!document.activeElement || document.activeElement === document.body)
  ) {
    const fallback =
      document.getElementById("btn-open-palette") ||
      document.getElementById("btn-mobile-menu-toggle") ||
      document.getElementById("main-content") ||
      document.querySelector<HTMLElement>("main, [role='main']");
    fallback?.focus?.();
  }
  await executePaletteTarget(target, {
    router,
    currentRouteName: route?.name,
    currentQuery: route?.query,
    activeStudyCode: priorStudyCode,
    workspaceScope: priorScope,
    onAction: handleAction,
    onFocusInspector: async (nodeRef) => {
      const store =
        getLoadedStudyCanvasStore(pinia) ?? (await loadStudyCanvasStore(pinia));
      return store?.requestInspectorFocus({
        nodeRef,
        scrollIntoView: true,
      });
    },
  });
}
</script>

<template>
  <CanvasPalette
    v-if="isOpen"
    :study-code="activeStudyCode"
    :participants="canvasStore.paletteParticipants"
    :workspace-items="workspaceItems"
    @close="emit('close')"
    @go="handleGo"
  />
</template>

<script setup lang="ts">
import { computed, nextTick, watch } from "vue";
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
import { useStudyCanvasStore } from "../stores/studyCanvas";
import { useActiveStudyScope } from "../composables/useActiveStudyScope";

const props = defineProps<{
  isOpen?: boolean;
}>();

const emit = defineEmits<{
  close: [];
}>();

const route = useRoute();
const router = useRouter();
const authStore = useAuthStore();
const knowledgeStore = useKnowledgeStore();
const onboardingStore = useOnboardingStore();
const canvasStore = useStudyCanvasStore();
const { activeStudyCode: syncedStudyCode, setActiveStudyCode } =
  useActiveStudyScope();
const workspaceScope = computed(() =>
  resolveActiveWorkspaceScope(route, canvasStore)
);
const activeStudyCode = computed(
  () => workspaceScope.value.studyCode || syncedStudyCode.value
);

watch(
  [() => props.isOpen, activeStudyCode],
  ([open, code]) => {
    if (open && code) {
      canvasStore.ensurePaletteParticipants(code);
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
  if (target.participant) {
    if (target.studyCode && target.studyCode !== canvasStore.studyCode) {
      canvasStore.openStudy(target.studyCode);
    }
    canvasStore.rememberParticipants([target.participant]);
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
  executePaletteTarget(target, {
    router,
    currentRouteName: route?.name,
    currentQuery: route?.query,
    activeStudyCode: priorStudyCode,
    workspaceScope: priorScope,
    onAction: handleAction,
  });
}
</script>

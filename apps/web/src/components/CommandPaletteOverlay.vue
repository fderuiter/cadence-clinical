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
import { computed, watch } from "vue";
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
import { useClinicalStore } from "../stores/clinical";
import { useKnowledgeStore } from "../stores/knowledge";
import { useOnboardingStore } from "../stores/onboarding";
import { useStudyCanvasStore } from "../stores/studyCanvas";

const props = defineProps<{
  isOpen?: boolean;
}>();

const emit = defineEmits<{
  close: [];
}>();

const route = useRoute();
const router = useRouter();
const authStore = useAuthStore();
const clinicalStore = useClinicalStore();
const knowledgeStore = useKnowledgeStore();
const onboardingStore = useOnboardingStore();
const canvasStore = useStudyCanvasStore();

const workspaceScope = computed(() =>
  resolveActiveWorkspaceScope(route, canvasStore, clinicalStore)
);
const activeStudyCode = computed(() => workspaceScope.value.studyCode);

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
    activeStudyCode: workspaceScope.value.studyCode,
    activeSiteId: workspaceScope.value.siteId,
    activeExplicitSiteId: workspaceScope.value.explicitSiteId,
    activeSubjectCode: workspaceScope.value.subjectCode,
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

function handleGo(target: PaletteTarget) {
  if (target.participant) {
    if (target.studyCode && target.studyCode !== canvasStore.studyCode) {
      canvasStore.openStudy(target.studyCode);
    }
    canvasStore.rememberParticipants([target.participant]);
  }
  emit("close");
  executePaletteTarget(target, {
    router,
    currentRouteName: route?.name,
    currentQuery: route?.query,
    activeStudyCode: activeStudyCode.value,
    onAction: handleAction,
  });
}
</script>

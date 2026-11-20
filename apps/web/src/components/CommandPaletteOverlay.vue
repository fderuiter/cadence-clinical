<template>
  <CanvasPalette
    v-if="isOpen"
    :study-code="activeStudyCode"
    :participants="knownStudyParticipants"
    :workspace-items="workspaceItems"
    @close="emit('close')"
    @go="handleGo"
  />
</template>

<script setup lang="ts">
import { computed } from "vue";
import { useRoute, useRouter } from "vue-router";
import CanvasPalette from "./canvas/CanvasPalette.vue";
import {
  buildWorkspacePaletteItems,
  executePaletteTarget,
  type PaletteTarget,
  type PaletteWorkspaceAction,
} from "../canvas/palette";
import { useAuthStore } from "../stores/auth";
import { useKnowledgeStore } from "../stores/knowledge";
import { useOnboardingStore } from "../stores/onboarding";
import { useStudyCanvasStore } from "../stores/studyCanvas";
import type { SubjectSummary } from "../api/executionApi";

defineProps<{
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

const activeStudyCode = computed(() => {
  const fromRoute = route?.params?.studyCode;
  if (typeof fromRoute === "string" && fromRoute.trim()) {
    return fromRoute.trim();
  }
  return canvasStore.studyCode || "";
});

const knownStudyParticipants = computed<SubjectSummary[]>(() => {
  const code = activeStudyCode.value;
  if (!code || (canvasStore.studyCode && canvasStore.studyCode !== code)) {
    return [];
  }
  const byId = new Map<string, SubjectSummary>();
  for (const participant of Object.values(canvasStore.knownParticipants)) {
    if (!participant.study_id || participant.study_id === code) {
      byId.set(participant.id, participant);
    }
  }
  for (const participant of canvasStore.participants) {
    if (!participant.study_id || participant.study_id === code) {
      byId.set(participant.id, participant);
    }
  }
  return [...byId.values()];
});

const workspaceItems = computed(() =>
  buildWorkspacePaletteItems({
    isAuthenticated: Boolean(authStore.isAuthenticated),
    normalizedRoles: authStore.normalizedRoles || [],
    availablePersonas: authStore.availablePersonas || [],
    activeStudyCode: activeStudyCode.value,
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
  emit("close");
  if (target.participant) {
    const studyCode = target.studyCode || activeStudyCode.value;
    if (studyCode) {
      canvasStore.openStudy(studyCode);
      canvasStore.knownParticipants[target.participant.id] = target.participant;
    }
  }
  executePaletteTarget(target, {
    router,
    currentRouteName: route?.name,
    currentQuery: route?.query,
    activeStudyCode: activeStudyCode.value,
    onAction: handleAction,
  });
}
</script>

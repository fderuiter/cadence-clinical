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
  type PaletteTarget,
  type PaletteWorkspaceAction,
} from "../canvas/palette";
import { useAuthStore } from "../stores/auth";
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
  if (target.participant && target.studyCode === canvasStore.studyCode) {
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

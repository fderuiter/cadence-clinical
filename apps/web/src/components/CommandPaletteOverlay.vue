<template>
  <CanvasPalette
    v-if="isOpen"
    :study-code="activeStudyCode"
    :participants="paletteParticipants"
    :workspace-items="workspaceItems"
    @close="emit('close')"
    @go="handleGo"
  />
</template>

<script setup lang="ts">
import { computed, nextTick, shallowRef, watch } from "vue";
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
import { resolveEffectivePersonaRoles, useAuthStore } from "../stores/auth";
import { useKnowledgeStore } from "../stores/knowledge";
import { useOnboardingStore } from "../stores/onboarding";
import type { useStudyCanvasStore } from "../stores/studyCanvas";
import { useActiveStudyScope } from "../composables/useActiveStudyScope";

type StudyCanvasStore = ReturnType<typeof useStudyCanvasStore>;

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
const loadedCanvasStore = shallowRef<StudyCanvasStore | null>(null);

function resolveInstantiatedCanvasStore(): StudyCanvasStore | null {
  if (loadedCanvasStore.value) return loadedCanvasStore.value;
  const pinia = getActivePinia() as unknown as {
    _s?: Map<string, StudyCanvasStore>;
  } | null;
  return pinia?._s?.get("studyCanvas") ?? null;
}

async function ensureCanvasStore(): Promise<StudyCanvasStore> {
  const existing = resolveInstantiatedCanvasStore();
  if (existing) {
    loadedCanvasStore.value = existing;
    return existing;
  }
  const mod = await import("../stores/studyCanvas");
  const store = mod.useStudyCanvasStore();
  loadedCanvasStore.value = store;
  return store;
}

const { activeStudyCode: syncedStudyCode, setActiveStudyCode } =
  useActiveStudyScope();
const workspaceScope = computed(() =>
  resolveActiveWorkspaceScope(route, resolveInstantiatedCanvasStore())
);
const activeStudyCode = computed(
  () => workspaceScope.value.studyCode || syncedStudyCode.value
);
const paletteParticipants = computed(
  () => resolveInstantiatedCanvasStore()?.paletteParticipants ?? []
);

watch(
  [() => props.isOpen, activeStudyCode],
  ([open, code]) => {
    if (open && code) {
      const existing = resolveInstantiatedCanvasStore();
      if (existing) {
        existing.ensurePaletteParticipants(code);
      } else {
        void ensureCanvasStore().then((store) => {
          if (props.isOpen) {
            store.ensurePaletteParticipants(code);
          }
        });
      }
    }
  },
  { immediate: true }
);

const effectiveRoles = computed(() => {
  const sourceRoles =
    Array.isArray(authStore.rawRoles) && authStore.rawRoles.length > 0
      ? authStore.rawRoles
      : authStore.normalizedRoles || [];
  return resolveEffectivePersonaRoles(sourceRoles, authStore.currentPersona, {
    isAuthenticated: Boolean(authStore.isAuthenticated),
  });
});

const workspaceItems = computed(() =>
  buildWorkspacePaletteItems({
    isAuthenticated: Boolean(authStore.isAuthenticated),
    normalizedRoles: effectiveRoles.value,
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
    const existing = resolveInstantiatedCanvasStore();
    if (existing) {
      if (target.studyCode && target.studyCode !== existing.studyCode) {
        existing.openStudy(target.studyCode);
      }
      existing.rememberParticipants([target.participant]);
    } else {
      const participant = target.participant;
      const studyCode = target.studyCode;
      void ensureCanvasStore().then((store) => {
        if (studyCode && studyCode !== store.studyCode) {
          store.openStudy(studyCode);
        }
        store.rememberParticipants([participant]);
      });
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
    onFocusInspector: (nodeRef) => {
      const existing = resolveInstantiatedCanvasStore();
      if (existing) {
        existing.requestInspectorFocus({
          nodeRef,
          scrollIntoView: true,
        });
      } else {
        void ensureCanvasStore().then((store) => {
          store.requestInspectorFocus({
            nodeRef,
            scrollIntoView: true,
          });
        });
      }
    },
  });
}
</script>

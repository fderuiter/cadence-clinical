import { computed, ref, watch } from "vue";
import * as VueRouter from "vue-router";
import { useAuthStore } from "../stores/auth";
import { useClinicalStore } from "../stores/clinical";
import { useStudyCanvasStore } from "../stores/studyCanvas";
import { useToastStore } from "../stores/toasts.js";
import { syncWorkspaceScope } from "../stores/workspaceScope";
import {
  formatScopeForbiddenGuidance,
  isForbiddenScopeError,
} from "../utils/callerScope";

export interface ScopedWorkspaceSectionProps {
  studyCode?: string;
  siteId?: string;
  lockStudy?: boolean;
}

export function useScopedWorkspaceSection(
  props: ScopedWorkspaceSectionProps,
  options: {
    onStudyPropChange?: () => void;
    onSitePropChange?: (site: string) => void;
    fallbackErrorTitle: string;
    fallbackErrorMessage: string;
  }
) {
  const authStore = useAuthStore();
  const clinicalStore = useClinicalStore();
  const canvasStore = useStudyCanvasStore();
  const toasts = useToastStore();
  const route =
    typeof VueRouter.useRoute === "function" ? VueRouter.useRoute() : null;
  const router =
    "useRouter" in VueRouter && typeof VueRouter.useRouter === "function"
      ? VueRouter.useRouter()
      : null;

  const isStudyLocked = computed(() =>
    props.lockStudy === undefined ? Boolean(props.studyCode) : props.lockStudy
  );
  const assignedStudies = computed(() => authStore.assignedStudies || []);
  const assignedSites = computed(() => authStore.assignedSites || []);

  const studyInput = ref(
    props.studyCode ||
      clinicalStore.activeStudyId ||
      canvasStore.studyCode ||
      (assignedStudies.value.length ? assignedStudies.value[0] : "")
  );
  const siteInput = ref(
    props.siteId ||
      clinicalStore.activeSiteId ||
      (assignedSites.value.length ? assignedSites.value[0] : "")
  );

  const loading = ref(false);
  const loaded = ref(false);
  const saving = ref(false);
  const loadError = ref("");
  const scopeGuidance = ref("");

  watch(
    () => props.studyCode,
    (val) => {
      if (!val) return;
      studyInput.value = val;
      options.onStudyPropChange?.();
    }
  );

  watch(
    () => props.siteId,
    (val) => {
      if (val === undefined) return;
      siteInput.value = val;
      options.onSitePropChange?.(val);
    }
  );

  function syncActiveScope(study?: string, site?: string) {
    syncWorkspaceScope({
      ...(study ? { studyId: study } : {}),
      ...(site !== undefined && (site || study) ? { siteId: site } : {}),
      router,
      route,
      syncRouteQuery: !isStudyLocked.value,
    });
  }

  function recordLoadFailure(err: unknown, study?: string, site?: string) {
    loaded.value = false;
    if (isForbiddenScopeError(err)) {
      scopeGuidance.value = formatScopeForbiddenGuidance(err, {
        studyId: study || undefined,
        siteId: site || undefined,
        assignedStudies: assignedStudies.value,
        assignedSites: assignedSites.value,
        hasAllStudiesScope: authStore.hasAllStudiesScope,
      });
      return;
    }
    loadError.value =
      err instanceof Error ? err.message : options.fallbackErrorMessage;
    toasts.apiError(options.fallbackErrorTitle, err);
  }

  return {
    authStore,
    clinicalStore,
    toasts,
    isStudyLocked,
    assignedStudies,
    assignedSites,
    studyInput,
    siteInput,
    loading,
    loaded,
    saving,
    loadError,
    scopeGuidance,
    syncActiveScope,
    recordLoadFailure,
  };
}

export function formatIsoDateShort(iso?: string | null): string {
  if (!iso) return "";
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? iso : d.toISOString().slice(0, 10);
}

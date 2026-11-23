import { computed, ref, watch } from "vue";
import { useAuthStore } from "../stores/auth";
import { useClinicalStore } from "../stores/clinical";
import { syncActiveStudyScope } from "../stores/studyScopeBridge";
import { useToastStore } from "../stores/toasts.js";
import {
  formatScopeForbiddenGuidance,
  isForbiddenScopeError,
} from "../utils/callerScope";

export interface ScopedWorkspaceSectionProps {
  studyCode?: string;
  siteId?: string;
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
  const toasts = useToastStore();

  const assignedStudies = computed(() => authStore.assignedStudies || []);
  const assignedSites = computed(() => authStore.assignedSites || []);

  const studyInput = ref(
    props.studyCode ||
      clinicalStore.activeStudyId ||
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
    if (study) {
      clinicalStore.activeStudyId = study;
      syncActiveStudyScope(study);
    }
    if (site) clinicalStore.activeSiteId = site;
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

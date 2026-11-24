import {
  computed,
  getCurrentInstance,
  toValue,
  type ComputedRef,
  type MaybeRefOrGetter,
} from "vue";
import { getActivePinia } from "pinia";
import {
  useRoute,
  useRouter,
  type LocationQueryRaw,
  type RouteLocationNormalizedLoaded,
  type RouteLocationRaw,
  type Router,
} from "vue-router";
import type { SubjectSummary } from "../api/executionApi";
import {
  buildDataCollectionPath,
  buildScopedStudyLocation,
  buildScopedWorkflowLocation,
  buildScopedWorkflowPath,
  buildStudiesWorkspacePath,
  resolveActiveWorkspaceScope,
  resolveWorkspaceNavigationScope,
  type ScopedStudyLocationOptions,
  type WorkspaceScopeSnapshot,
} from "../canvas/palette";
import { useActiveStudyScope } from "./useActiveStudyScope";

interface CanvasStateSlice {
  studyCode?: string;
  knownParticipants?: Record<string, SubjectSummary>;
  knownParticipantsByEnrollmentId?: Record<string, SubjectSummary>;
  record?: { subjectCode?: string; enrollmentId?: string | null };
  participantsFilter?: { siteId?: string };
}

export interface ScopedWorkflowPathOptions {
  studyCode?: string;
  siteId?: string;
  subjectCode?: string;
}

export interface UseScopedStudyLocationOptions extends ScopedStudyLocationOptions {
  status?: string;
  q?: string;
  spec?: string;
  visit?: string;
  preserveStageSection?: boolean;
}

export interface UseScopedNavigationOptions {
  useNavigationFallback?: boolean;
  siteId?: MaybeRefOrGetter<string | null | undefined>;
  subjectCode?: MaybeRefOrGetter<string | null | undefined>;
}

const cleanString = (value: unknown): string => {
  const raw = Array.isArray(value) ? value[0] : value;
  return typeof raw === "string" ? raw.trim() : "";
};

const sanitizeSubject = (value: unknown): string => {
  const clean = cleanString(value);
  return clean && !clean.includes(":") ? clean : "";
};

function resolveOptionalRouteAndRouter(): {
  route: ComputedRef<RouteLocationNormalizedLoaded | null>;
  router: Router | null;
} {
  const inst = getCurrentInstance();
  const gp = inst?.appContext?.config?.globalProperties as
    { $route?: RouteLocationNormalizedLoaded; $router?: Router } | undefined;
  let injRoute: RouteLocationNormalizedLoaded | null = null;
  let injRouter: Router | null = null;
  if (inst) {
    try {
      injRoute = useRoute();
    } catch {
      injRoute = null;
    }
    try {
      injRouter = useRouter();
    } catch {
      injRouter = null;
    }
  }
  const router = injRouter ?? gp?.$router ?? null;
  const route = computed<RouteLocationNormalizedLoaded | null>(
    () => injRoute ?? router?.currentRoute?.value ?? gp?.$route ?? null
  );
  return { route, router };
}

/**
 * Shared composable over `src/canvas/palette.ts` scope resolvers and route builders (ADR-2651, UX-2).
 */
export function useScopedNavigation(
  studyCodeSource?: MaybeRefOrGetter<string | null | undefined>,
  options: UseScopedNavigationOptions = {}
) {
  const { route, router } = resolveOptionalRouteAndRouter();
  const { activeStudyCode: syncedActiveStudyCode } = useActiveStudyScope();

  const workspaceScope = computed<WorkspaceScopeSnapshot>(() => {
    const currentRoute = route.value ?? undefined;
    const canvasState =
      (
        getActivePinia()?.state.value as
          { studyCanvas?: CanvasStateSlice } | undefined
      )?.studyCanvas ?? null;
    const snap = options.useNavigationFallback
      ? resolveWorkspaceNavigationScope(
          currentRoute,
          canvasState,
          undefined,
          syncedActiveStudyCode.value
        )
      : resolveActiveWorkspaceScope(currentRoute, canvasState);
    const explicitStudy = cleanString(toValue(studyCodeSource));
    const explicitSite = cleanString(toValue(options.siteId));
    const explicitSubject = sanitizeSubject(toValue(options.subjectCode));
    const studyCode = explicitStudy || snap.studyCode;
    if (explicitStudy && snap.studyCode && explicitStudy !== snap.studyCode) {
      return {
        studyCode,
        siteId: explicitSite,
        explicitSiteId: explicitSite,
        subjectCode: explicitSubject,
        participantRef: "",
        enrollmentId: "",
        recordParticipantParam: "",
        inspectorNodeParam: "",
      };
    }
    return {
      ...snap,
      studyCode,
      siteId: explicitSite || snap.siteId,
      explicitSiteId: explicitSite || snap.explicitSiteId,
      subjectCode: explicitSubject || sanitizeSubject(snap.subjectCode),
    };
  });

  function mergeOverrides(overrides?: {
    studyCode?: string;
    siteId?: string;
    subjectCode?: string;
    participantRef?: string;
    enrollmentId?: string;
    preferExplicitSite?: boolean;
  }) {
    const scope = workspaceScope.value;
    const studyCode =
      overrides?.studyCode !== undefined
        ? cleanString(overrides.studyCode)
        : scope.studyCode;
    const cross = Boolean(
      studyCode && scope.studyCode && studyCode !== scope.studyCode
    );
    const defaultSite = overrides?.preferExplicitSite
      ? scope.explicitSiteId || scope.siteId
      : scope.siteId;
    return {
      studyCode,
      siteId:
        overrides?.siteId !== undefined
          ? cleanString(overrides.siteId)
          : cross
            ? ""
            : defaultSite,
      subjectCode:
        overrides?.subjectCode !== undefined
          ? sanitizeSubject(overrides.subjectCode)
          : cross
            ? ""
            : sanitizeSubject(scope.subjectCode),
      participantRef:
        overrides?.participantRef !== undefined
          ? cleanString(overrides.participantRef)
          : cross
            ? ""
            : scope.participantRef,
      enrollmentId:
        overrides?.enrollmentId !== undefined
          ? cleanString(overrides.enrollmentId)
          : cross
            ? ""
            : scope.enrollmentId,
    };
  }

  function scopedStudyLocation(
    locationOptions: UseScopedStudyLocationOptions = {},
    targetStudyOverride?: string
  ): {
    name: "study";
    params: { studyCode?: string };
    query: LocationQueryRaw;
  } {
    const scope = workspaceScope.value;
    const targetStudy = cleanString(targetStudyOverride ?? scope.studyCode);
    const currentQuery = route.value?.query ?? {};
    const onStudyRoute =
      route.value?.name === "study" ||
      Boolean(cleanString(route.value?.params?.studyCode));
    const inheritStageSection =
      locationOptions.preserveStageSection ??
      (onStudyRoute &&
        locationOptions.stage === undefined &&
        locationOptions.section === undefined);
    const built = buildScopedStudyLocation(
      targetStudy,
      {
        ...locationOptions,
        stage:
          locationOptions.stage !== undefined
            ? cleanString(locationOptions.stage) || undefined
            : inheritStageSection
              ? cleanString(currentQuery.stage) || undefined
              : undefined,
        section:
          locationOptions.section !== undefined
            ? cleanString(locationOptions.section) || undefined
            : inheritStageSection
              ? cleanString(currentQuery.section) || undefined
              : undefined,
      },
      scope
    );
    const query: LocationQueryRaw = { ...built.query };
    for (const key of ["status", "q", "spec", "visit"] as const) {
      if (locationOptions[key]) query[key] = locationOptions[key];
    }
    return {
      name: "study",
      params: targetStudy ? { studyCode: built.params.studyCode } : {},
      query,
    };
  }

  const scopedWorkflowPath = (
    basePath: string,
    overrides?: ScopedWorkflowPathOptions
  ): string => buildScopedWorkflowPath(basePath, mergeOverrides(overrides));

  const scopedWorkflowLocation = (
    basePath: string,
    overrides?: ScopedWorkflowPathOptions
  ): RouteLocationRaw =>
    buildScopedWorkflowLocation(basePath, mergeOverrides(overrides));

  const scopedStudiesWorkspacePath = (overrides?: {
    studyCode?: string;
    siteId?: string;
    subjectCode?: string;
    participantRef?: string;
  }): string => buildStudiesWorkspacePath(mergeOverrides(overrides));

  const scopedDataCollectionPath = (overrides?: {
    studyCode?: string;
    siteId?: string;
    subjectCode?: string;
    participantRef?: string;
    enrollmentId?: string;
  }): string =>
    buildDataCollectionPath(
      mergeOverrides({ ...overrides, preferExplicitSite: true })
    );

  return {
    route,
    router,
    workspaceScope,
    scopedStudyLocation,
    scopedWorkflowPath,
    scopedWorkflowLocation,
    scopedStudiesWorkspacePath,
    scopedDataCollectionPath,
  };
}

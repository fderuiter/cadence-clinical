import {
  isNavigationFailure,
  type LocationQuery,
  type RouteLocationRaw,
  type Router,
} from "vue-router";
import type { SubjectSummary } from "../api/executionApi";
import { formatNodeRef, parseNodeRef, tryNodeRef } from "./nodeRef";
import {
  canonicalParticipantNodeFromSummary,
  cleanQueryString,
  readInstantiatedClinicalScope,
  resolveActiveWorkspaceScope,
  resolveCanvasParticipantContext,
  syncInstantiatedClinicalScope,
  type WorkspaceScopeSnapshot,
} from "./workspaceScopeResolver";
import {
  isScreenAvailable,
  listDemoCatalogModules,
} from "../router/availability.js";
import { hasRequiredRole } from "../router/helpers.js";
import {
  DATA_COLLECTION_HUB_DESCRIPTOR,
  getRouteDescriptor,
  getRouteRequiredRoles,
  isRouteHubAuthorized,
  isRouteRoleAuthorized,
  PALETTE_DESCRIPTORS,
  SITE_READINESS_DESCRIPTORS,
} from "../router/registry.js";
import { getSyncedStudyScope } from "../stores/studyScopeBridge";
import { isDemoBuild } from "../utils/demoMode.js";

export {
  readInstantiatedClinicalScope,
  resolveActiveWorkspaceScope,
  resolveCanvasParticipantContext,
  syncInstantiatedClinicalScope,
  type WorkspaceScopeSnapshot,
};

export type PaletteWorkspaceAction =
  | { kind: "open-help" }
  | { kind: "open-checklist" }
  | { kind: "start-tour" }
  | { kind: "switch-persona"; personaKey: string };

/** Where a ⌘K palette choice takes the unified Clinical Canvas shell. */
export interface PaletteTarget {
  studyCode?: string;
  stage?: string;
  section?: string;
  participant?: SubjectSummary;
  /** A question for the inspector's Assist pane (ADR-2635). */
  ask?: string;
  /** Authorized workflow or orientation route path. */
  routePath?: string;
  /** Workspace shell action (help drawer, setup checklist, guided tour, or persona view). */
  action?: PaletteWorkspaceAction;
}

export interface WorkspacePaletteOption {
  key: string;
  group: string;
  label: string;
  detail?: string;
  target: PaletteTarget;
}

export const SITE_READINESS_SPONSOR_ROLES: string[] =
  getRouteDescriptor("ctms")?.siteReadinessRoles ||
  getRouteRequiredRoles("ctms");
export const SITE_READINESS_SITE_ROLES: string[] =
  getRouteDescriptor("ssu")?.siteReadinessRoles || [];
export const DATA_COLLECTION_ROLES: string[] =
  getRouteDescriptor("ecrf")?.dataCollectionRoles ||
  getRouteRequiredRoles("ecrf");
export const ETMF_ROLES: string[] =
  getRouteDescriptor("etmf")?.etmfRoles || getRouteRequiredRoles("etmf");
export function buildStudyScopedPath(
  basePath: string,
  studyCode?: string
): string {
  const code = studyCode?.trim() || "";
  return code ? `${basePath}?studyId=${encodeURIComponent(code)}` : basePath;
}

export function buildScopedRoutePath(
  basePath: string,
  scope?: { studyCode?: string; siteId?: string }
): string {
  const studyCode = scope?.studyCode?.trim() || "";
  const siteId = scope?.siteId?.trim() || "";
  if (!studyCode && !siteId) return basePath;
  const params = new URLSearchParams();
  if (studyCode) params.set("studyId", studyCode);
  if (siteId) params.set("siteId", siteId);
  return `${basePath}?${params.toString()}`;
}

export function resolveSiteReadinessPath(
  normalizedRoles: string[],
  scopeOrStudyCode?:
    string | { studyCode?: string; siteId?: string; subjectCode?: string }
): string | null {
  const scope =
    typeof scopeOrStudyCode === "string"
      ? { studyCode: scopeOrStudyCode }
      : scopeOrStudyCode;
  let basePath: string | null = null;
  for (const descriptor of SITE_READINESS_DESCRIPTORS) {
    const routeName = descriptor.route.name;
    const roles =
      descriptor.siteReadinessRoles || getRouteRequiredRoles(routeName);
    if (
      isScreenAvailable(routeName) &&
      hasRequiredRole(normalizedRoles, roles)
    ) {
      basePath = descriptor.route.path;
      break;
    }
  }
  if (!basePath) return null;
  return buildScopedWorkflowPath(basePath, scope);
}

export function buildScopedWorkflowPath(
  basePath: string,
  scope?: { studyCode?: string; siteId?: string; subjectCode?: string }
): string {
  const fallbackClinical =
    scope === undefined ? readInstantiatedClinicalScope() : null;
  const fallbackStudy =
    scope === undefined
      ? cleanQueryString(fallbackClinical?.activeStudyId) ||
        cleanQueryString(getSyncedStudyScope())
      : "";
  const studyCode = scope?.studyCode?.trim() || fallbackStudy;
  const siteId =
    scope?.siteId?.trim() ||
    (scope === undefined &&
    studyCode &&
    cleanQueryString(fallbackClinical?.activeStudyId) === studyCode
      ? cleanQueryString(fallbackClinical?.activeSiteId)
      : "");
  const subjectCode =
    scope?.subjectCode?.trim() ||
    (scope === undefined &&
    studyCode &&
    cleanQueryString(fallbackClinical?.activeStudyId) === studyCode
      ? cleanQueryString(fallbackClinical?.activeSubjectId)
      : "");
  if (!studyCode && !siteId && !subjectCode) return basePath;
  const params = new URLSearchParams();
  if (studyCode) params.set("studyId", studyCode);
  if (siteId) params.set("siteId", siteId);
  if (subjectCode) params.set("subjectId", subjectCode);
  return `${basePath}?${params.toString()}`;
}

export function buildScopedWorkflowLocation(
  basePath: string,
  scope?: { studyCode?: string; siteId?: string; subjectCode?: string }
): RouteLocationRaw {
  return buildScopedWorkflowPath(basePath, scope);
}

export interface ScopedStudyLocationOptions {
  stage?: string;
  section?: string;
  site?: string;
  participant?: string;
  node?: string;
  form?: string;
  item?: string;
  observation?: string;
  proposal?: string;
  ask?: string;
  targetParticipant?: SubjectSummary | null;
}

export function buildScopedStudyLocation(
  targetStudyCode: string,
  options: ScopedStudyLocationOptions = {},
  currentScope?: Partial<WorkspaceScopeSnapshot> | null
): {
  name: "study";
  params: { studyCode: string };
  query: Record<string, string>;
} {
  const studyCode = targetStudyCode.trim();
  const priorStudy = cleanQueryString(currentScope?.studyCode);
  const isCrossStudy = Boolean(
    priorStudy && studyCode && priorStudy !== studyCode
  );
  const query: Record<string, string> = {};

  if (options.stage) query.stage = options.stage;
  if (options.section) query.section = options.section;

  if (options.targetParticipant) {
    if (!isCrossStudy) {
      const activeSite =
        cleanQueryString(options.site) ||
        cleanQueryString(currentScope?.explicitSiteId) ||
        cleanQueryString(currentScope?.siteId);
      const participantSite = cleanQueryString(
        options.targetParticipant.site_id
      );
      if (activeSite && (!participantSite || participantSite === activeSite)) {
        query.site = activeSite;
      }
    }
    const participantNode = canonicalParticipantNodeFromSummary(
      options.targetParticipant
    );
    if (participantNode) {
      query.node = formatNodeRef(participantNode);
    }
  } else if (!isCrossStudy) {
    const site =
      options.site !== undefined
        ? cleanQueryString(options.site)
        : cleanQueryString(currentScope?.siteId);
    const recordParticipant = cleanQueryString(
      currentScope?.recordParticipantParam
    );
    const inspectorNode = cleanQueryString(currentScope?.inspectorNodeParam);
    const fallbackParticipant =
      !recordParticipant && !inspectorNode
        ? cleanQueryString(currentScope?.participantRef) ||
          cleanQueryString(currentScope?.subjectCode)
        : "";
    const participant =
      options.participant !== undefined
        ? cleanQueryString(options.participant)
        : recordParticipant || fallbackParticipant;
    const node =
      options.node !== undefined
        ? cleanQueryString(options.node)
        : inspectorNode;

    if (site) query.site = site;
    if (participant) query.participant = participant;
    if (node) query.node = node;
  } else {
    if (options.site) query.site = cleanQueryString(options.site);
    if (options.participant) {
      query.participant = cleanQueryString(options.participant);
    }
    if (options.node) query.node = cleanQueryString(options.node);
  }

  if (options.form) query.form = options.form;
  if (options.item) query.item = options.item;
  if (options.observation) query.observation = options.observation;
  if (options.proposal) query.proposal = options.proposal;
  if (options.ask) query.ask = options.ask;

  return {
    name: "study",
    params: { studyCode },
    query,
  };
}

export function buildStudiesWorkspacePath(scope?: {
  studyCode?: string;
  siteId?: string;
  subjectCode?: string;
  participantRef?: string;
}): string {
  const studyCode = scope?.studyCode?.trim() || "";
  if (!studyCode) return "/studies";
  const basePath = `/studies/${encodeURIComponent(studyCode)}`;
  const siteId = scope?.siteId?.trim() || "";
  const participant =
    scope?.participantRef?.trim() || scope?.subjectCode?.trim() || "";
  if (!siteId && !participant) return basePath;
  const params = new URLSearchParams();
  if (siteId) params.set("site", siteId);
  if (participant) params.set("participant", participant);
  return `${basePath}?${params.toString()}`;
}

export function canAccessDataCollection(normalizedRoles: string[]): boolean {
  const hubName = DATA_COLLECTION_HUB_DESCRIPTOR?.route.name || "ecrf";
  const screenAvailable = isDemoBuild()
    ? isScreenAvailable(hubName)
    : isScreenAvailable("study") || isScreenAvailable(hubName);
  return screenAvailable && isRouteRoleAuthorized(hubName, normalizedRoles);
}

export function canAccessSupportHub(normalizedRoles: string[]): boolean {
  return (
    isScreenAvailable("tickets") &&
    isRouteHubAuthorized("tickets", normalizedRoles)
  );
}

export function canAccessKnowledgeHub(normalizedRoles: string[]): boolean {
  return (
    isScreenAvailable("knowledge") &&
    isRouteHubAuthorized("knowledge", normalizedRoles)
  );
}

export function resolveWorkspaceNavigationScope(
  route?: {
    params?: Record<string, unknown>;
    query?: LocationQuery | Record<string, unknown>;
  } | null,
  canvasStore?: {
    studyCode?: string;
    knownParticipants?: Record<string, SubjectSummary>;
    knownParticipantsByEnrollmentId?: Record<string, SubjectSummary>;
    record?: { subjectCode?: string; enrollmentId?: string | null };
    participantsFilter?: { siteId?: string };
  } | null,
  clinicalStore?: {
    activeStudyId?: string;
    activeSiteId?: string;
    activeSubjectId?: string;
  } | null,
  fallbackStudyCode?: string
): WorkspaceScopeSnapshot {
  const resolved = resolveActiveWorkspaceScope(
    route,
    canvasStore,
    clinicalStore
  );
  if (!resolved.studyCode && fallbackStudyCode?.trim()) {
    return {
      ...resolved,
      studyCode: fallbackStudyCode.trim(),
    };
  }
  return resolved;
}

/**
 * Builds the Data collection workspace path: targets Clinical Canvas Conduct (`/studies` or
 * `/studies/:studyCode?stage=conduct...`) outside demo mode, and `/ecrf` in static demo builds.
 */
export function buildDataCollectionPath(context: {
  studyCode?: string;
  siteId?: string;
  participantRef?: string;
  subjectCode?: string;
  enrollmentId?: string | null;
}): string {
  const studyCode = context.studyCode?.trim() || "";
  const siteId = context.siteId?.trim() || "";
  if (isDemoBuild()) {
    const subjectCode = context.subjectCode?.trim() || "";
    const enrollmentId = context.enrollmentId?.trim() || "";
    if (!studyCode && !siteId && !subjectCode && !enrollmentId) return "/ecrf";
    const params = new URLSearchParams();
    if (studyCode) {
      params.set("studyId", studyCode);
    }
    if (siteId) {
      params.set("siteId", siteId);
    }
    if (subjectCode) {
      params.set("subjectId", subjectCode);
    }
    if (enrollmentId) {
      params.set("enrollmentId", enrollmentId);
    }
    return `/ecrf?${params.toString()}`;
  }

  if (!studyCode) {
    return "/studies?stage=conduct&section=participants";
  }

  const params = new URLSearchParams();
  params.set("stage", "conduct");
  if (siteId) {
    params.set("site", siteId);
  }
  const participantRef = context.participantRef?.trim() || "";
  const subjectCode = context.subjectCode?.trim() || "";
  const parsedParticipant =
    parseNodeRef(participantRef) ?? tryNodeRef("participant", participantRef);
  if (
    parsedParticipant?.kind === "enrollment" ||
    parsedParticipant?.kind === "participant"
  ) {
    params.set(
      "participant",
      formatNodeRef({ kind: parsedParticipant.kind, id: parsedParticipant.id })
    );
  } else if (subjectCode) {
    params.set("subject", subjectCode);
  } else {
    params.set("section", "participants");
  }
  return `/studies/${encodeURIComponent(studyCode)}?${params.toString()}`;
}

/**
 * Builds UX-1 compliant, backend-available, role-authorized workflow destinations
 * and workspace controls for the unified global command palette.
 */
export function buildWorkspacePaletteItems(options: {
  isAuthenticated: boolean;
  normalizedRoles: string[];
  availablePersonas?: Array<{ key: string; label: string }>;
  activeStudyCode?: string;
  activeSiteId?: string;
  activeExplicitSiteId?: string;
  activeSubjectCode?: string;
  activeParticipantRef?: string;
  activeEnrollmentId?: string | null;
}): WorkspacePaletteOption[] {
  if (!options.isAuthenticated) return [];
  const roles = options.normalizedRoles || [];
  const items: WorkspacePaletteOption[] = [];
  const includedRoutes = new Set<string>();

  for (const descriptor of PALETTE_DESCRIPTORS) {
    const routeName = descriptor.route.name;
    const paletteMeta = descriptor.palette;
    let routePath: string | null = null;

    if (paletteMeta.scopeMode === "studies-workspace") {
      if (
        isScreenAvailable(routeName) &&
        isRouteRoleAuthorized(routeName, roles)
      ) {
        routePath = buildStudiesWorkspacePath({
          studyCode: options.activeStudyCode,
          siteId: options.activeSiteId,
          subjectCode: options.activeSubjectCode,
          participantRef: options.activeParticipantRef,
        });
      }
    } else if (paletteMeta.scopeMode === "data-collection") {
      if (canAccessDataCollection(roles)) {
        routePath = buildDataCollectionPath({
          studyCode: options.activeStudyCode,
          siteId:
            options.activeExplicitSiteId !== undefined
              ? options.activeExplicitSiteId
              : options.activeSiteId,
          participantRef: options.activeParticipantRef,
          subjectCode: options.activeSubjectCode,
          enrollmentId: options.activeEnrollmentId,
        });
      }
    } else if (paletteMeta.scopeMode === "site-readiness") {
      routePath = resolveSiteReadinessPath(roles, {
        studyCode: options.activeStudyCode,
        siteId: options.activeSiteId,
        subjectCode: options.activeSubjectCode,
      });
    } else if (paletteMeta.scopeMode === "workflow") {
      if (
        isScreenAvailable(routeName) &&
        isRouteHubAuthorized(routeName, roles)
      ) {
        routePath = buildScopedWorkflowPath(descriptor.route.path, {
          studyCode: options.activeStudyCode,
          siteId: options.activeSiteId,
          subjectCode: options.activeSubjectCode,
        });
      }
    } else if (paletteMeta.scopeMode === "study") {
      if (
        isScreenAvailable(routeName) &&
        isRouteRoleAuthorized(routeName, roles)
      ) {
        routePath = buildStudyScopedPath(
          descriptor.route.path,
          options.activeStudyCode
        );
      }
    }

    if (routePath) {
      if (paletteMeta.scopeMode === "site-readiness") {
        if (routePath.startsWith("/ctms")) {
          includedRoutes.add("ctms");
        } else if (routePath.startsWith("/ssu")) {
          includedRoutes.add("ssu");
        }
      } else {
        includedRoutes.add(routeName);
      }
      items.push({
        key: paletteMeta.key,
        group: "Workspace",
        label: paletteMeta.label,
        detail: paletteMeta.detail,
        target: { routePath },
      });
    }
  }

  if (isDemoBuild()) {
    for (const mod of listDemoCatalogModules()) {
      if (includedRoutes.has(mod.routeName)) continue;
      if (!isScreenAvailable(mod.routeName)) continue;
      if (
        mod.requiresRole.length > 0 &&
        !hasRequiredRole(roles, mod.requiresRole)
      ) {
        continue;
      }
      const routePath =
        mod.routeName === "observation"
          ? mod.defaultPath
          : buildScopedWorkflowPath(mod.defaultPath, {
              studyCode: options.activeStudyCode,
              siteId: options.activeSiteId,
              subjectCode: options.activeSubjectCode,
            });
      items.push({
        key: `preview:${mod.routeName}`,
        group: "Preview",
        label: mod.title,
        detail: `${mod.family} · ${mod.summary}`,
        target: { routePath },
      });
    }
  }

  items.push(
    {
      key: "action:open-help",
      group: "Action",
      label: "Open contextual help",
      detail: "View regulatory and standard operating procedure guidance",
      target: { action: { kind: "open-help" } },
    },
    {
      key: "action:open-checklist",
      group: "Action",
      label: "Open setup checklist",
      detail: "Review opt-in workspace setup tasks",
      target: { action: { kind: "open-checklist" } },
    },
    {
      key: "action:start-tour",
      group: "Action",
      label: "Start guided tour",
      detail: "Launch opt-in interactive workspace tour",
      target: { action: { kind: "start-tour" } },
    }
  );

  for (const persona of options.availablePersonas || []) {
    const cleanLabel = persona.label.replace(/^[^\w\s]+\s*/, "").trim();
    items.push({
      key: `action:persona:${persona.key}`,
      group: "Action",
      label: `Switch view: ${cleanLabel}`,
      detail: "Adjust workspace orientation and help emphasis",
      target: {
        action: { kind: "switch-persona", personaKey: persona.key },
      },
    });
  }

  return items;
}

export async function executePaletteTarget(
  target: PaletteTarget,
  context: {
    router: Router;
    currentRouteName?: string | symbol | null;
    currentQuery?: LocationQuery;
    activeStudyCode?: string;
    workspaceScope?: Partial<WorkspaceScopeSnapshot> | null;
    onAction?: (action: PaletteWorkspaceAction) => void;
    onFocusInspector?: (nodeRef?: string | null) => void | Promise<void>;
  }
): Promise<void> {
  if (target.action) {
    context.onAction?.(target.action);
    return;
  }
  if (target.routePath) {
    await context.router.push(target.routePath);
    return;
  }
  const priorStudyCode = cleanQueryString(
    context.workspaceScope?.studyCode ?? context.activeStudyCode
  );
  const studyCode = cleanQueryString(target.studyCode) || priorStudyCode;
  if (!studyCode) {
    await context.router.push({ name: "study" });
    return;
  }
  const isCrossStudy = Boolean(priorStudyCode && studyCode !== priorStudyCode);
  const effectiveScope: Partial<WorkspaceScopeSnapshot> =
    context.workspaceScope ??
    resolveActiveWorkspaceScope(
      {
        params:
          context.currentRouteName === "study" && priorStudyCode
            ? { studyCode: priorStudyCode }
            : {},
        query: context.currentQuery,
      },
      null
    );
  const previousScope = isCrossStudy ? readInstantiatedClinicalScope() : null;
  if (isCrossStudy) {
    syncInstantiatedClinicalScope({
      activeStudyId: studyCode,
      activeSiteId: "",
      activeSubjectId: "",
    });
  }
  const restorePreviousScope = () => {
    if (previousScope) {
      syncInstantiatedClinicalScope({
        activeStudyId: previousScope.activeStudyId ?? "",
        activeSiteId: previousScope.activeSiteId ?? "",
        activeSubjectId: previousScope.activeSubjectId ?? "",
      });
    }
  };
  if (target.ask) {
    const baseQuery =
      !isCrossStudy &&
      context.currentRouteName === "study" &&
      context.currentQuery
        ? { ...context.currentQuery }
        : {};
    const failure = await context.router.push({
      name: "study",
      params: { studyCode },
      query: { ...baseQuery, ask: target.ask },
    });
    if (isNavigationFailure(failure)) {
      restorePreviousScope();
      return;
    }
    await context.onFocusInspector?.(null);
    return;
  }
  const scopedWithStudy: Partial<WorkspaceScopeSnapshot> = {
    ...effectiveScope,
    studyCode: priorStudyCode || effectiveScope.studyCode || "",
  };
  const location = buildScopedStudyLocation(
    studyCode,
    {
      stage: target.stage,
      section: target.section,
      targetParticipant: target.participant ?? null,
    },
    scopedWithStudy
  );
  const failure = await context.router.push(location);
  if (isNavigationFailure(failure)) {
    restorePreviousScope();
    return;
  }
  if (target.participant) {
    await context.onFocusInspector?.(location.query.node ?? null);
  }
}

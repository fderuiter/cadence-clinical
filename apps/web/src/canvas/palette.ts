import { getActivePinia } from "pinia";
import type { LocationQuery, Router } from "vue-router";
import type { SubjectSummary } from "../api/executionApi";
import { formatNodeRef, parseNodeRef, tryNodeRef } from "./nodeRef";
import { isScreenAvailable } from "../router/availability.js";
import { hasRequiredRole } from "../router/index.js";
import { isDemoBuild } from "../utils/demoMode.js";

export function readInstantiatedClinicalScope(): {
  activeStudyId?: string;
  activeSiteId?: string;
  activeSubjectId?: string;
} | null {
  const state = getActivePinia()?.state.value?.clinical as
    | {
        activeStudyId?: string;
        activeSiteId?: string;
        activeSubjectId?: string;
      }
    | undefined;
  return state ?? null;
}

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

export interface WorkspaceScopeSnapshot {
  studyCode: string;
  siteId: string;
  explicitSiteId: string;
  subjectCode: string;
  enrollmentId: string;
}

const DATA_COLLECTION_ROLES = [
  "site_investigator",
  "crc",
  "data_manager",
  "sponsor_admin",
  "cra",
  "monitor",
];

const SITE_READINESS_SPONSOR_ROLES = [
  "cra",
  "monitor",
  "data_manager",
  "sponsor_admin",
];
const SITE_READINESS_SITE_ROLES = ["site_investigator", "crc"];
const STAFF_HUB_ROLES = [
  "sponsor_admin",
  "sponsor_designer",
  "data_manager",
  "safety_reviewer",
  "safety_officer",
  "safety_physician",
  "sponsor_mm",
  "cra",
  "monitor",
  "principal_investigator",
  "site_investigator",
  "crc",
  "auditor",
  "tmf_auditor",
];

function cleanQueryString(value: unknown): string {
  return typeof value === "string" ? value.trim() : "";
}

export function resolveSiteReadinessPath(
  normalizedRoles: string[],
  scope?: { studyCode?: string; siteId?: string }
): string | null {
  let basePath: string | null = null;
  if (
    isScreenAvailable("ctms") &&
    hasRequiredRole(normalizedRoles, SITE_READINESS_SPONSOR_ROLES)
  ) {
    basePath = "/ctms";
  } else if (
    isScreenAvailable("ssu") &&
    hasRequiredRole(normalizedRoles, SITE_READINESS_SITE_ROLES)
  ) {
    basePath = "/ssu";
  }
  if (!basePath) return null;
  const studyCode = scope?.studyCode?.trim() || "";
  const siteId = scope?.siteId?.trim() || "";
  if (!studyCode && !siteId) return basePath;
  const params = new URLSearchParams();
  if (studyCode) params.set("studyId", studyCode);
  if (siteId) params.set("siteId", siteId);
  return `${basePath}?${params.toString()}`;
}

export function canAccessDataCollection(normalizedRoles: string[]): boolean {
  return (
    isScreenAvailable("ecrf") &&
    hasRequiredRole(normalizedRoles, DATA_COLLECTION_ROLES)
  );
}

export function canAccessSupportHub(normalizedRoles: string[]): boolean {
  return (
    isScreenAvailable("tickets") &&
    hasRequiredRole(normalizedRoles, STAFF_HUB_ROLES)
  );
}

export function canAccessKnowledgeHub(normalizedRoles: string[]): boolean {
  return (
    isScreenAvailable("knowledge") &&
    hasRequiredRole(normalizedRoles, STAFF_HUB_ROLES)
  );
}

/**
 * Resolves the active participant and kernel enrollment ID from Clinical Canvas state
 * when the current study matches the canvas store's loaded study.
 */
export function resolveCanvasParticipantContext(
  activeStudyCode: string,
  routeQuery: LocationQuery | Record<string, unknown> | undefined,
  canvasStore: {
    studyCode?: string;
    knownParticipants?: Record<string, SubjectSummary>;
    record?: { subjectCode?: string; enrollmentId?: string | null };
  }
): { subjectCode: string; enrollmentId: string } {
  const study = activeStudyCode.trim();
  if (!study || canvasStore.studyCode !== study) {
    return { subjectCode: "", enrollmentId: "" };
  }
  const participantRef =
    parseNodeRef(routeQuery?.participant) ?? parseNodeRef(routeQuery?.node);
  const participantNode =
    participantRef?.kind === "participant" ? participantRef : null;
  const known = participantNode
    ? canvasStore.knownParticipants?.[participantNode.id]
    : undefined;
  const subjectCode =
    known?.subject_id || canvasStore.record?.subjectCode || "";
  const enrollmentId =
    subjectCode &&
    canvasStore.record?.subjectCode === subjectCode &&
    canvasStore.record?.enrollmentId
      ? canvasStore.record.enrollmentId
      : "";
  return { subjectCode, enrollmentId };
}

/**
 * Resolves URL-first study, site, and participant scope with read-only fallback
 * to in-memory `studyCanvas` and `clinical` stores (never fabricating IDs outside demo mode).
 */
export function resolveActiveWorkspaceScope(
  route:
    | {
        params?: Record<string, unknown>;
        query?: LocationQuery | Record<string, unknown>;
      }
    | null
    | undefined,
  canvasStore?: {
    studyCode?: string;
    knownParticipants?: Record<string, SubjectSummary>;
    record?: { subjectCode?: string; enrollmentId?: string | null };
    participantsFilter?: { siteId?: string };
  } | null,
  clinicalStore?: {
    activeStudyId?: string;
    activeSiteId?: string;
    activeSubjectId?: string;
  } | null
): WorkspaceScopeSnapshot {
  const effectiveClinical =
    clinicalStore !== undefined
      ? clinicalStore
      : readInstantiatedClinicalScope();
  const demo = isDemoBuild();
  const routeParamStudy = cleanQueryString(route?.params?.studyCode);
  const routeQueryStudy =
    cleanQueryString(route?.query?.studyId) ||
    cleanQueryString(route?.query?.study);
  const routeStudy = routeParamStudy || routeQueryStudy;

  const canvasStudy = cleanQueryString(canvasStore?.studyCode);
  const clinicalStudy = !demo
    ? cleanQueryString(effectiveClinical?.activeStudyId)
    : "";
  const studyCode = routeStudy || canvasStudy || clinicalStudy;

  const routeSite =
    cleanQueryString(route?.query?.site) ||
    cleanQueryString(route?.query?.siteId);

  let participantSite = "";
  if (!routeSite && studyCode && canvasStore?.studyCode === studyCode) {
    const participantRef =
      parseNodeRef(route?.query?.participant) ??
      parseNodeRef(route?.query?.node);
    if (participantRef?.kind === "participant") {
      participantSite = cleanQueryString(
        canvasStore.knownParticipants?.[participantRef.id]?.site_id
      );
    }
  }

  let storeSite = "";
  if (!routeSite && !routeStudy && studyCode) {
    if (canvasStore?.studyCode === studyCode) {
      storeSite = cleanQueryString(canvasStore.participantsFilter?.siteId);
    }
    if (
      !storeSite &&
      !demo &&
      cleanQueryString(effectiveClinical?.activeStudyId) === studyCode
    ) {
      storeSite = cleanQueryString(effectiveClinical?.activeSiteId);
    }
  }

  const explicitSiteId = routeSite || storeSite;
  const siteId = routeSite || participantSite || storeSite;

  const routeSubject = cleanQueryString(route?.query?.subjectId);
  const routeEnrollment = cleanQueryString(route?.query?.enrollmentId);
  const canvasParticipant = canvasStore
    ? resolveCanvasParticipantContext(studyCode, route?.query, canvasStore)
    : { subjectCode: "", enrollmentId: "" };

  const subjectCode =
    routeSubject ||
    canvasParticipant.subjectCode ||
    (!routeStudy &&
    !demo &&
    studyCode &&
    cleanQueryString(effectiveClinical?.activeStudyId) === studyCode
      ? cleanQueryString(effectiveClinical?.activeSubjectId)
      : "");
  const enrollmentId =
    (routeSubject
      ? routeEnrollment
      : routeEnrollment || canvasParticipant.enrollmentId) || "";

  return {
    studyCode,
    siteId,
    explicitSiteId,
    subjectCode,
    enrollmentId,
  };
}

/**
 * Builds the `/ecrf` path preserving active study, optional site, participant, and resolved kernel enrollment context.
 */
export function buildDataCollectionPath(context: {
  studyCode?: string;
  siteId?: string;
  subjectCode?: string;
  enrollmentId?: string | null;
}): string {
  const studyCode = context.studyCode?.trim() || "";
  if (!studyCode) return "/ecrf";
  const params = new URLSearchParams();
  params.set("studyId", studyCode);
  const siteId = context.siteId?.trim() || "";
  if (siteId) {
    params.set("siteId", siteId);
  }
  const subjectCode = context.subjectCode?.trim() || "";
  if (subjectCode) {
    params.set("subjectId", subjectCode);
  }
  const enrollmentId = context.enrollmentId?.trim() || "";
  if (enrollmentId) {
    params.set("enrollmentId", enrollmentId);
  }
  return `/ecrf?${params.toString()}`;
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
  activeEnrollmentId?: string | null;
}): WorkspacePaletteOption[] {
  if (!options.isAuthenticated) return [];
  const roles = options.normalizedRoles || [];
  const items: WorkspacePaletteOption[] = [];

  if (isScreenAvailable("study")) {
    const studyPath = options.activeStudyCode
      ? `/studies/${encodeURIComponent(options.activeStudyCode)}`
      : "/studies";
    items.push({
      key: "workspace:studies",
      group: "Workspace",
      label: "Studies",
      detail: "Open a study and navigate its lifecycle",
      target: { routePath: studyPath },
    });
  }

  if (canAccessDataCollection(roles)) {
    items.push({
      key: "workspace:data-collection",
      group: "Workspace",
      label: "Data collection",
      detail: "Clinical case report forms and discrepancy review",
      target: {
        routePath: buildDataCollectionPath({
          studyCode: options.activeStudyCode,
          siteId: options.activeExplicitSiteId,
          subjectCode: options.activeSubjectCode,
          enrollmentId: options.activeEnrollmentId,
        }),
      },
    });
  }

  const siteReadinessPath = resolveSiteReadinessPath(roles, {
    studyCode: options.activeStudyCode,
    siteId: options.activeSiteId,
  });
  if (siteReadinessPath) {
    items.push({
      key: "workspace:site-readiness",
      group: "Workspace",
      label: "Site readiness",
      detail: "Site activation, milestones, and readiness workflows",
      target: { routePath: siteReadinessPath },
    });
  }

  if (canAccessSupportHub(roles)) {
    items.push({
      key: "workspace:support",
      group: "Workspace",
      label: "Support tickets",
      detail: "Operational issues, deviations, and resolution sign-off",
      target: { routePath: "/tickets" },
    });
  }

  if (canAccessKnowledgeHub(roles)) {
    items.push({
      key: "workspace:knowledge",
      group: "Workspace",
      label: "Knowledge & procedures",
      detail: "Published standard operating procedures and version history",
      target: { routePath: "/knowledge" },
    });
  }

  if (isScreenAvailable("overview")) {
    items.push({
      key: "workspace:orientation",
      group: "Workspace",
      label: "Orientation",
      detail: "Role-tailored workspace orientation and setup",
      target: { routePath: "/overview" },
    });
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

export function executePaletteTarget(
  target: PaletteTarget,
  context: {
    router: Router;
    currentRouteName?: string | symbol | null;
    currentQuery?: LocationQuery;
    activeStudyCode?: string;
    onAction?: (action: PaletteWorkspaceAction) => void;
  }
): void {
  if (target.action) {
    context.onAction?.(target.action);
    return;
  }
  if (target.routePath) {
    context.router.push(target.routePath);
    return;
  }
  const studyCode = target.studyCode || context.activeStudyCode || "";
  if (!studyCode) {
    context.router.push({ name: "study" });
    return;
  }
  if (target.ask) {
    const baseQuery =
      context.currentRouteName === "study" && context.currentQuery
        ? { ...context.currentQuery }
        : {};
    context.router.push({
      name: "study",
      params: { studyCode },
      query: { ...baseQuery, ask: target.ask },
    });
    return;
  }
  const query: Record<string, string> = {};
  if (target.stage) query.stage = target.stage;
  if (target.section) query.section = target.section;
  if (target.participant) {
    const node = tryNodeRef("participant", target.participant.id);
    if (node) query.node = formatNodeRef(node);
  }
  context.router.push({
    name: "study",
    params: { studyCode },
    query,
  });
}

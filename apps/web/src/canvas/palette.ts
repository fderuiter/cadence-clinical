import { getActivePinia } from "pinia";
import type { LocationQuery, Router } from "vue-router";
import type { SubjectSummary } from "../api/executionApi";
import { formatNodeRef, parseNodeRef, tryNodeRef } from "./nodeRef";
import { isScreenAvailable } from "../router/availability.js";
import { hasRequiredRole } from "../router/index.js";
import { getSyncedStudyScope } from "../stores/studyScopeBridge";
import { isDemoBuild } from "../utils/demoMode.js";

export function readInstantiatedClinicalScope(): {
  activeStudyId?: string;
  activeSiteId?: string;
  activeSubjectId?: string;
} | null {
  const stateRoot = getActivePinia()?.state.value as
    | {
        clinicalScope?: {
          activeStudyId?: string;
          activeSiteId?: string;
          activeSubjectId?: string;
        };
        clinical?: {
          activeStudyId?: string;
          activeSiteId?: string;
          activeSubjectId?: string;
        };
      }
    | undefined;
  const state = stateRoot?.clinicalScope ?? stateRoot?.clinical;
  if (!state) return null;
  return {
    activeStudyId: state.activeStudyId,
    activeSiteId: state.activeSiteId,
    activeSubjectId: state.activeSubjectId,
  };
}

export function syncInstantiatedClinicalScope(next: {
  activeStudyId: string;
  activeSiteId: string;
  activeSubjectId: string;
}): void {
  const stateRoot = getActivePinia()?.state.value as
    | {
        clinicalScope?: {
          activeStudyId?: string;
          activeSiteId?: string;
          activeSubjectId?: string;
        };
      }
    | undefined;
  const scopeState = stateRoot?.clinicalScope;
  if (!scopeState) return;
  scopeState.activeStudyId = next.activeStudyId;
  scopeState.activeSiteId = next.activeSiteId;
  scopeState.activeSubjectId = next.activeSubjectId;
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
  participantRef: string;
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
const ETMF_ROLES = [
  "cra",
  "monitor",
  "data_manager",
  "site_investigator",
  "crc",
  "auditor",
  "tmf_auditor",
  "sponsor_admin",
];

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

export function buildStudyScopedPath(
  basePath: string,
  studyCode?: string
): string {
  const code = studyCode?.trim() || "";
  return code ? `${basePath}?studyId=${encodeURIComponent(code)}` : basePath;
}

function cleanQueryString(value: unknown): string {
  return typeof value === "string" ? value.trim() : "";
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
  return buildScopedWorkflowPath(basePath, scope);
}

export function buildScopedWorkflowPath(
  basePath: string,
  scope?: { studyCode?: string; siteId?: string; subjectCode?: string }
): string {
  const studyCode = scope?.studyCode?.trim() || "";
  const siteId = scope?.siteId?.trim() || "";
  const subjectCode = scope?.subjectCode?.trim() || "";
  if (!studyCode && !siteId && !subjectCode) return basePath;
  const params = new URLSearchParams();
  if (studyCode) params.set("studyId", studyCode);
  if (siteId) params.set("siteId", siteId);
  if (subjectCode) params.set("subjectId", subjectCode);
  return `${basePath}?${params.toString()}`;
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
  const screenAvailable = isDemoBuild()
    ? isScreenAvailable("ecrf")
    : isScreenAvailable("study") || isScreenAvailable("ecrf");
  return (
    screenAvailable && hasRequiredRole(normalizedRoles, DATA_COLLECTION_ROLES)
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

function extractParticipantIdFromQueryValue(value: unknown): string {
  const parsed = parseNodeRef(value);
  if (parsed?.kind === "participant") return parsed.id;
  if (typeof value === "string" && value.startsWith("participant:")) {
    const rawId =
      value.slice("participant:".length).split("@")[0]?.trim() || "";
    return rawId;
  }
  return "";
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
): { subjectCode: string; participantRef: string; enrollmentId: string } {
  const study = activeStudyCode.trim();
  if (!study || canvasStore.studyCode !== study) {
    return { subjectCode: "", participantRef: "", enrollmentId: "" };
  }
  const parsedRef =
    parseNodeRef(routeQuery?.participant) ?? parseNodeRef(routeQuery?.node);
  const participantNode = parsedRef?.kind === "participant" ? parsedRef : null;
  const participantRefId =
    participantNode?.id ||
    extractParticipantIdFromQueryValue(routeQuery?.participant) ||
    extractParticipantIdFromQueryValue(routeQuery?.node);
  const rawParticipantParam = cleanQueryString(routeQuery?.participant);
  const rawParticipantCode =
    !participantRefId && !rawParticipantParam.includes(":")
      ? rawParticipantParam
      : "";
  const knownById = participantRefId
    ? canvasStore.knownParticipants?.[participantRefId]
    : undefined;
  const knownByCode =
    !knownById && rawParticipantCode
      ? Object.values(canvasStore.knownParticipants || {}).find(
          (item) => item?.subject_id === rawParticipantCode
        )
      : undefined;
  const known = knownById ?? knownByCode;
  const subjectCode =
    known?.subject_id ||
    rawParticipantCode ||
    canvasStore.record?.subjectCode ||
    "";
  let resolvedRefNode = participantNode
    ? { kind: "participant" as const, id: participantNode.id }
    : known
      ? tryNodeRef("participant", known.id)
      : null;
  if (!resolvedRefNode && subjectCode && canvasStore.knownParticipants) {
    const matched = Object.values(canvasStore.knownParticipants).find(
      (item) => item?.subject_id === subjectCode
    );
    resolvedRefNode = matched ? tryNodeRef("participant", matched.id) : null;
  }
  const participantRef = resolvedRefNode ? formatNodeRef(resolvedRefNode) : "";
  const enrollmentId =
    subjectCode &&
    canvasStore.record?.subjectCode === subjectCode &&
    canvasStore.record?.enrollmentId
      ? canvasStore.record.enrollmentId
      : "";
  return { subjectCode, participantRef, enrollmentId };
}

/**
 * Resolves URL-first study, site, and participant scope with read-only fallback
 * to in-memory `studyCanvas` and `clinicalScope` stores (never fabricating IDs outside demo mode).
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
  const allowClinicalFallback = !demo || clinicalStore !== undefined;
  const routeParamStudy = cleanQueryString(route?.params?.studyCode);
  const routeQueryStudy =
    cleanQueryString(route?.query?.studyId) ||
    cleanQueryString(route?.query?.study);
  const routeStudy = routeParamStudy || routeQueryStudy;

  const canvasStudy = cleanQueryString(canvasStore?.studyCode);
  const clinicalStudy = allowClinicalFallback
    ? cleanQueryString(effectiveClinical?.activeStudyId)
    : "";
  const bridgedStudy = !demo ? cleanQueryString(getSyncedStudyScope()) : "";
  const studyCode = routeStudy || canvasStudy || clinicalStudy || bridgedStudy;

  const routeSite =
    cleanQueryString(route?.query?.site) ||
    cleanQueryString(route?.query?.siteId);

  let participantSite = "";
  if (!routeSite && studyCode && canvasStore?.studyCode === studyCode) {
    const participantId =
      extractParticipantIdFromQueryValue(route?.query?.participant) ||
      extractParticipantIdFromQueryValue(route?.query?.node);
    if (participantId) {
      participantSite = cleanQueryString(
        canvasStore.knownParticipants?.[participantId]?.site_id
      );
    }
  }

  let storeSite = "";
  if (!routeSite && studyCode) {
    if (canvasStore?.studyCode === studyCode) {
      storeSite = cleanQueryString(canvasStore.participantsFilter?.siteId);
    }
    if (
      !storeSite &&
      allowClinicalFallback &&
      cleanQueryString(effectiveClinical?.activeStudyId) === studyCode
    ) {
      storeSite = cleanQueryString(effectiveClinical?.activeSiteId);
    }
  }

  const explicitSiteId = routeSite || storeSite;
  const siteId = routeSite || participantSite || storeSite;

  const rawParticipantParam = cleanQueryString(route?.query?.participant);
  const parsedParticipantParam = parseNodeRef(rawParticipantParam);
  const routeSubject =
    cleanQueryString(route?.query?.subjectId) ||
    cleanQueryString(route?.query?.subject) ||
    (!parsedParticipantParam && !rawParticipantParam.includes(":")
      ? rawParticipantParam
      : "");
  const routeEnrollment = cleanQueryString(route?.query?.enrollmentId);
  const canvasParticipant = canvasStore
    ? resolveCanvasParticipantContext(studyCode, route?.query, canvasStore)
    : { subjectCode: "", participantRef: "", enrollmentId: "" };

  const subjectCode =
    routeSubject ||
    canvasParticipant.subjectCode ||
    (allowClinicalFallback &&
    studyCode &&
    cleanQueryString(effectiveClinical?.activeStudyId) === studyCode
      ? cleanQueryString(effectiveClinical?.activeSubjectId)
      : "");

  let participantRef =
    routeSubject && routeSubject !== canvasParticipant.subjectCode
      ? ""
      : canvasParticipant.participantRef;
  if (!participantRef && parsedParticipantParam?.kind === "participant") {
    participantRef = formatNodeRef({
      kind: "participant",
      id: parsedParticipantParam.id,
    });
  }
  if (
    !participantRef &&
    subjectCode &&
    studyCode &&
    canvasStore?.studyCode === studyCode &&
    canvasStore.knownParticipants
  ) {
    const matched = Object.values(canvasStore.knownParticipants).find(
      (item) => item?.subject_id === subjectCode
    );
    const refNode = matched ? tryNodeRef("participant", matched.id) : null;
    if (refNode) {
      participantRef = formatNodeRef(refNode);
    }
  }

  const enrollmentId =
    (routeSubject
      ? routeEnrollment
      : routeEnrollment || canvasParticipant.enrollmentId) || "";

  return {
    studyCode,
    siteId,
    explicitSiteId,
    subjectCode,
    participantRef,
    enrollmentId,
  };
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
    if (!studyCode && !siteId) return "/ecrf";
    const params = new URLSearchParams();
    if (studyCode) {
      params.set("studyId", studyCode);
    }
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
  if (parsedParticipant?.kind === "participant") {
    params.set(
      "participant",
      formatNodeRef({ kind: "participant", id: parsedParticipant.id })
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

  if (isScreenAvailable("study")) {
    const studyPath = buildStudiesWorkspacePath({
      studyCode: options.activeStudyCode,
      siteId: options.activeSiteId,
      subjectCode: options.activeSubjectCode,
      participantRef: options.activeParticipantRef,
    });
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
      detail: "Clinical casebooks, forms, and discrepancy review",
      target: {
        routePath: buildDataCollectionPath({
          studyCode: options.activeStudyCode,
          siteId:
            options.activeExplicitSiteId !== undefined
              ? options.activeExplicitSiteId
              : options.activeSiteId,
          participantRef: options.activeParticipantRef,
          subjectCode: options.activeSubjectCode,
          enrollmentId: options.activeEnrollmentId,
        }),
      },
    });
  }

  const siteReadinessPath = resolveSiteReadinessPath(roles, {
    studyCode: options.activeStudyCode,
    siteId: options.activeSiteId,
    subjectCode: options.activeSubjectCode,
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

  if (isScreenAvailable("etmf") && hasRequiredRole(roles, ETMF_ROLES)) {
    items.push({
      key: "workspace:trial-documents",
      group: "Workspace",
      label: "Trial documents",
      detail: "Study binder, document classification, and clinical review",
      target: {
        routePath: buildStudyScopedPath("/etmf", options.activeStudyCode),
      },
    });
  }

  if (isScreenAvailable("training")) {
    items.push({
      key: "workspace:training",
      group: "Workspace",
      label: "Study training",
      detail: "Protocol curricula, staff assignments, and delegation sign-off",
      target: {
        routePath: buildStudyScopedPath("/training", options.activeStudyCode),
      },
    });
  }

  if (canAccessSupportHub(roles)) {
    items.push({
      key: "workspace:support",
      group: "Workspace",
      label: "Support tickets",
      detail: "Operational issues, deviations, and resolution sign-off",
      target: {
        routePath: buildScopedWorkflowPath("/tickets", {
          studyCode: options.activeStudyCode,
          siteId: options.activeSiteId,
          subjectCode: options.activeSubjectCode,
        }),
      },
    });
  }

  if (canAccessKnowledgeHub(roles)) {
    items.push({
      key: "workspace:knowledge",
      group: "Workspace",
      label: "Knowledge & procedures",
      detail: "Published standard operating procedures and version history",
      target: {
        routePath: buildScopedWorkflowPath("/knowledge", {
          studyCode: options.activeStudyCode,
          siteId: options.activeSiteId,
          subjectCode: options.activeSubjectCode,
        }),
      },
    });
  }

  if (isScreenAvailable("overview")) {
    items.push({
      key: "workspace:orientation",
      group: "Workspace",
      label: "Orientation",
      detail: "Role-tailored workspace orientation and setup",
      target: {
        routePath: buildStudyScopedPath("/overview", options.activeStudyCode),
      },
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

export async function executePaletteTarget(
  target: PaletteTarget,
  context: {
    router: Router;
    currentRouteName?: string | symbol | null;
    currentQuery?: LocationQuery;
    activeStudyCode?: string;
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
  const studyCode = target.studyCode || context.activeStudyCode || "";
  if (!studyCode) {
    await context.router.push({ name: "study" });
    return;
  }
  if (target.ask) {
    const baseQuery =
      context.currentRouteName === "study" && context.currentQuery
        ? { ...context.currentQuery }
        : {};
    await context.router.push({
      name: "study",
      params: { studyCode },
      query: { ...baseQuery, ask: target.ask },
    });
    await context.onFocusInspector?.(null);
    return;
  }
  const query: Record<string, string> = {};
  if (target.stage) query.stage = target.stage;
  if (target.section) query.section = target.section;
  let participantNodeRef: string | null = null;
  if (target.participant) {
    const node = tryNodeRef("participant", target.participant.id);
    if (node) {
      participantNodeRef = formatNodeRef(node);
      query.node = participantNodeRef;
    }
  }
  await context.router.push({
    name: "study",
    params: { studyCode },
    query,
  });
  if (target.participant) {
    await context.onFocusInspector?.(participantNodeRef);
  }
}

import { DEMO_PARTICIPANTS, DEMO_STUDY } from "demo-fixtures";
import { getActivePinia } from "pinia";
import {
  isNavigationFailure,
  type LocationQuery,
  type RouteLocationRaw,
  type Router,
} from "vue-router";
import type { SubjectSummary } from "../api/executionApi";
import type { KernelZoomStep } from "../api/kernelApi";
import { formatNodeRef, parseNodeRef, tryNodeRef } from "./nodeRef";
import {
  isScreenAvailable,
  listDemoCatalogModules,
} from "../router/availability.js";
import { ROLE_ALIASES } from "../stores/auth.js";
import {
  clearActiveStudyScope,
  getSyncedStudyScope,
  syncActiveStudyScope,
} from "../stores/studyScopeBridge";
import { isDemoBuild } from "../utils/demoMode.js";

function hasRequiredRole(
  userRoles: readonly string[],
  requiredRoles: readonly string[]
): boolean {
  const aliasesMap = ROLE_ALIASES as Record<string, readonly string[]>;
  return userRoles.some((uRole) => {
    if (requiredRoles.includes(uRole)) return true;
    const aliases = aliasesMap[uRole] || [];
    return aliases.some((alias) => requiredRoles.includes(alias));
  });
}

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
  if (scopeState) {
    scopeState.activeStudyId = next.activeStudyId;
    scopeState.activeSiteId = next.activeSiteId;
    scopeState.activeSubjectId = next.activeSubjectId;
  }
  const pinia = getActivePinia() as
    | (ReturnType<typeof getActivePinia> & {
        _s?: Map<
          string,
          {
            activeStudyId?: string;
            activeSiteId?: string;
            activeSubjectId?: string;
            currentStudyId?: string;
            currentSiteId?: string;
          }
        >;
      })
    | undefined;
  const clinicalInstance = pinia?._s?.get("clinical");
  if (clinicalInstance) {
    clinicalInstance.activeStudyId = next.activeStudyId;
    clinicalInstance.activeSiteId = next.activeSiteId;
    clinicalInstance.activeSubjectId = next.activeSubjectId;
  }
  const etmfInstance = pinia?._s?.get("etmf");
  if (etmfInstance) {
    etmfInstance.currentStudyId = next.activeStudyId;
    etmfInstance.currentSiteId = next.activeSiteId;
  }
  if (next.activeStudyId) {
    syncActiveStudyScope(next.activeStudyId, pinia);
  } else {
    clearActiveStudyScope();
  }
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
  site?: string;
  participant?: SubjectSummary;
  participantRef?: string;
  subject?: string;
  form?: string;
  observation?: string;
  proposal?: string;
  /** Canonical NodeRef when opening an indexed kernel or module entity hit. */
  nodeRef?: string;
  /** NodeKind of `nodeRef` (e.g. `study_site`, `enrollment`, `collection_instance`, `form_query`). */
  nodeKind?: string;
  /** Optional natural code of the hit. */
  nodeCode?: string | null;
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
  recordParticipantParam?: string;
  inspectorNodeParam?: string;
}

const DATA_COLLECTION_SPONSOR_ROLES = [
  "cra",
  "monitor",
  "data_manager",
  "sponsor_admin",
];
const DATA_COLLECTION_SITE_ROLES = [
  "site_investigator",
  "crc",
  "trial_coordinator",
];
export const SITE_READINESS_SPONSOR_ROLES = [
  ...DATA_COLLECTION_SPONSOR_ROLES,
  "unblinded_statistician",
];
export const SITE_READINESS_SITE_ROLES = [
  ...DATA_COLLECTION_SITE_ROLES,
  "pharmacist",
];
export const DATA_COLLECTION_ROLES = [
  ...DATA_COLLECTION_SITE_ROLES,
  ...DATA_COLLECTION_SPONSOR_ROLES,
];
export const ETMF_ROLES = [
  "cra",
  "monitor",
  "data_manager",
  "site_investigator",
  "crc",
  "trial_coordinator",
  "auditor",
  "tmf_auditor",
  "sponsor_admin",
];

const STAFF_HUB_ROLES = [
  "sponsor_admin",
  "sponsor_designer",
  "data_manager",
  "unblinded_statistician",
  "pharmacist",
  "safety_reviewer",
  "safety_officer",
  "safety_physician",
  "sponsor_mm",
  "cra",
  "monitor",
  "principal_investigator",
  "site_investigator",
  "crc",
  "trial_coordinator",
  "auditor",
  "tmf_auditor",
];

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
  subject?: string;
  node?: string;
  form?: string;
  item?: string;
  observation?: string;
  proposal?: string;
  ask?: string;
  targetParticipant?: SubjectSummary | null;
}

export function promoteParticipantInspectorNode(
  inspectorNode: string | undefined,
  nextExplicitNode: string | undefined
): string {
  const cleanInspector = cleanQueryString(inspectorNode);
  const cleanNext =
    nextExplicitNode !== undefined
      ? cleanQueryString(nextExplicitNode)
      : undefined;
  if (!cleanInspector || !cleanNext) return "";
  const parsedInspector = parseNodeRef(cleanInspector);
  if (
    parsedInspector?.kind !== "enrollment" &&
    parsedInspector?.kind !== "participant"
  ) {
    return "";
  }
  const parsedNext = parseNodeRef(cleanNext);
  if (parsedNext?.kind === "enrollment" || parsedNext?.kind === "participant") {
    return "";
  }
  return formatNodeRef({
    kind: parsedInspector.kind,
    id: parsedInspector.id,
  });
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
    const participantSite = cleanQueryString(options.targetParticipant.site_id);
    const explicitSite = cleanQueryString(options.site);
    const fallbackActiveSite = !isCrossStudy
      ? cleanQueryString(currentScope?.explicitSiteId) ||
        cleanQueryString(currentScope?.siteId)
      : "";
    const effectiveSite = explicitSite || participantSite || fallbackActiveSite;
    if (effectiveSite) {
      query.site = effectiveSite;
    }
    const participantNode = canonicalParticipantNodeFromSummary(
      options.targetParticipant
    );
    if (options.section) {
      if (participantNode) {
        query.node = formatNodeRef(participantNode);
      }
    } else {
      if (!query.stage) {
        query.stage = "conduct";
      }
      if (participantNode) {
        query.participant = formatNodeRef(participantNode);
      }
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
    const nextExplicitNode =
      options.node !== undefined ? cleanQueryString(options.node) : undefined;
    const promotedInspectorParticipant = promoteParticipantInspectorNode(
      inspectorNode,
      nextExplicitNode
    );
    const fallbackParticipant =
      !recordParticipant && (!inspectorNode || promotedInspectorParticipant)
        ? promotedInspectorParticipant ||
          cleanQueryString(currentScope?.participantRef) ||
          cleanQueryString(currentScope?.subjectCode)
        : "";
    const participant =
      options.participant !== undefined
        ? cleanQueryString(options.participant)
        : recordParticipant || fallbackParticipant;
    const node =
      nextExplicitNode !== undefined ? nextExplicitNode : inspectorNode;

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

  if (options.subject) query.subject = options.subject;
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

function extractParticipantNodeFromQueryValue(
  value: unknown
): { kind: "enrollment" | "participant"; id: string } | null {
  const parsed = parseNodeRef(value);
  if (parsed?.kind === "enrollment" || parsed?.kind === "participant") {
    return { kind: parsed.kind, id: parsed.id };
  }
  if (typeof value === "string") {
    for (const kind of ["enrollment", "participant"] as const) {
      const prefix = `${kind}:`;
      if (value.startsWith(prefix)) {
        const rawId = value.slice(prefix.length).split("@")[0]?.trim() || "";
        if (rawId) return { kind, id: rawId };
      }
    }
  }
  return null;
}

function canonicalParticipantNodeFromSummary(
  item: SubjectSummary | undefined
): { kind: "enrollment" | "participant"; id: string } | null {
  if (!item) return null;
  const enrollment = parseNodeRef(item.enrollment_ref);
  if (enrollment?.kind === "enrollment") {
    return { kind: "enrollment", id: enrollment.id };
  }
  const fallback = tryNodeRef("participant", item.id);
  return fallback ? { kind: "participant", id: fallback.id } : null;
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
    knownParticipantsByEnrollmentId?: Record<string, SubjectSummary>;
    record?: { subjectCode?: string; enrollmentId?: string | null };
  }
): { subjectCode: string; participantRef: string; enrollmentId: string } {
  const study = activeStudyCode.trim();
  if (!study || canvasStore.studyCode !== study) {
    return { subjectCode: "", participantRef: "", enrollmentId: "" };
  }
  const parsedRef =
    parseNodeRef(routeQuery?.participant) ?? parseNodeRef(routeQuery?.node);
  const participantNode =
    parsedRef?.kind === "enrollment" || parsedRef?.kind === "participant"
      ? { kind: parsedRef.kind, id: parsedRef.id }
      : null;
  const extractedNode =
    participantNode ??
    extractParticipantNodeFromQueryValue(routeQuery?.participant) ??
    extractParticipantNodeFromQueryValue(routeQuery?.node);
  const rawParticipantParam = cleanQueryString(routeQuery?.participant);
  const rawParticipantCode =
    !extractedNode && !rawParticipantParam.includes(":")
      ? rawParticipantParam
      : "";
  const knownById = extractedNode
    ? extractedNode.kind === "enrollment"
      ? (canvasStore.knownParticipantsByEnrollmentId?.[extractedNode.id] ??
        canvasStore.knownParticipants?.[extractedNode.id])
      : canvasStore.knownParticipants?.[extractedNode.id]
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
  const persistedEnrollment = parseNodeRef(known?.enrollment_ref);
  let resolvedRefNode: {
    kind: "enrollment" | "participant";
    id: string;
  } | null =
    persistedEnrollment?.kind === "enrollment"
      ? { kind: "enrollment", id: persistedEnrollment.id }
      : participantNode
        ? participantNode
        : canonicalParticipantNodeFromSummary(known);
  if (!resolvedRefNode && subjectCode && canvasStore.knownParticipants) {
    const matched = Object.values(canvasStore.knownParticipants).find(
      (item) => item?.subject_id === subjectCode
    );
    resolvedRefNode = canonicalParticipantNodeFromSummary(matched);
  }
  const participantRef = resolvedRefNode ? formatNodeRef(resolvedRefNode) : "";
  const enrollmentId =
    (persistedEnrollment?.kind === "enrollment"
      ? persistedEnrollment.id
      : "") ||
    (extractedNode?.kind === "enrollment" ? extractedNode.id : "") ||
    (subjectCode &&
    canvasStore.record?.subjectCode === subjectCode &&
    canvasStore.record?.enrollmentId
      ? canvasStore.record.enrollmentId
      : "");
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
    knownParticipantsByEnrollmentId?: Record<string, SubjectSummary>;
    record?: { subjectCode?: string; enrollmentId?: string | null };
    participantsFilter?: { siteId?: string };
  } | null,
  clinicalStore?: {
    activeStudyId?: string;
    activeSiteId?: string;
    activeSubjectId?: string;
    subjects?: ReadonlyArray<{
      id?: string;
      recordId?: string;
      siteId?: string;
      site_id?: string;
    }>;
  } | null
): WorkspaceScopeSnapshot {
  const effectiveClinical =
    clinicalStore !== undefined
      ? clinicalStore
      : readInstantiatedClinicalScope();
  const piniaRoot = getActivePinia()?.state.value as
    | {
        clinicalScope?: {
          subjects?: ReadonlyArray<{
            id?: string;
            recordId?: string;
            siteId?: string;
            site_id?: string;
          }>;
        };
        clinical?: {
          subjects?: ReadonlyArray<{
            id?: string;
            recordId?: string;
            siteId?: string;
            site_id?: string;
          }>;
        };
      }
    | undefined;
  const clinicalSubjects =
    clinicalStore?.subjects ??
    piniaRoot?.clinicalScope?.subjects ??
    piniaRoot?.clinical?.subjects;
  const allowClinicalFallback = Boolean(effectiveClinical);
  const routeParamStudy = cleanQueryString(route?.params?.studyCode);
  const routeQueryStudy =
    cleanQueryString(route?.query?.studyId) ||
    cleanQueryString(route?.query?.study);
  const routeStudy = routeParamStudy || routeQueryStudy;

  const canvasStudy = cleanQueryString(canvasStore?.studyCode);
  const clinicalStudy = allowClinicalFallback
    ? cleanQueryString(effectiveClinical?.activeStudyId)
    : "";
  const bridgedStudy = cleanQueryString(getSyncedStudyScope());
  const studyCode = routeStudy || canvasStudy || clinicalStudy || bridgedStudy;

  const hasExplicitStandaloneStudyQuery = Boolean(
    !routeParamStudy && routeQueryStudy
  );
  const routeSite =
    cleanQueryString(route?.query?.site) ||
    cleanQueryString(route?.query?.siteId);
  const extractedRouteParticipantNode =
    extractParticipantNodeFromQueryValue(route?.query?.participant) ??
    extractParticipantNodeFromQueryValue(route?.query?.node);
  const matchedClinicalSubject =
    extractedRouteParticipantNode && Array.isArray(clinicalSubjects)
      ? clinicalSubjects.find(
          (item) => item?.recordId === extractedRouteParticipantNode.id
        )
      : undefined;
  const matchedDemoParticipant =
    isDemoBuild() &&
    extractedRouteParticipantNode &&
    studyCode === DEMO_STUDY.code
      ? DEMO_PARTICIPANTS.find(
          (item) =>
            item.enrollmentUuid === extractedRouteParticipantNode.id ||
            item.participantUuid === extractedRouteParticipantNode.id
        )
      : undefined;

  let participantSite = "";
  if (!routeSite && studyCode && canvasStore?.studyCode === studyCode) {
    if (extractedRouteParticipantNode?.kind === "enrollment") {
      const known =
        canvasStore.knownParticipantsByEnrollmentId?.[
          extractedRouteParticipantNode.id
        ] ?? canvasStore.knownParticipants?.[extractedRouteParticipantNode.id];
      participantSite = cleanQueryString(known?.site_id);
    } else if (extractedRouteParticipantNode?.kind === "participant") {
      participantSite = cleanQueryString(
        canvasStore.knownParticipants?.[extractedRouteParticipantNode.id]
          ?.site_id
      );
    }
  }
  if (!routeSite && !participantSite && extractedRouteParticipantNode) {
    participantSite =
      cleanQueryString(
        matchedClinicalSubject?.siteId ?? matchedClinicalSubject?.site_id
      ) || cleanQueryString(matchedDemoParticipant?.siteCode);
  }

  let storeSite = "";
  if (!routeSite && studyCode) {
    if (canvasStore?.studyCode === studyCode) {
      storeSite = cleanQueryString(canvasStore.participantsFilter?.siteId);
    }
    if (
      !storeSite &&
      allowClinicalFallback &&
      !hasExplicitStandaloneStudyQuery &&
      cleanQueryString(effectiveClinical?.activeStudyId) === studyCode
    ) {
      storeSite = cleanQueryString(effectiveClinical?.activeSiteId);
    }
  }

  const explicitSiteId = routeSite || storeSite;
  const siteId = routeSite || participantSite || storeSite;

  const rawParticipantParam = cleanQueryString(route?.query?.participant);
  const rawNodeParam = cleanQueryString(route?.query?.node);
  const parsedParticipantParam = parseNodeRef(rawParticipantParam);
  const parsedNodeParam = parseNodeRef(rawNodeParam);
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

  const rawClinicalSubject = cleanQueryString(
    effectiveClinical?.activeSubjectId
  );
  const verifiedClinicalSubject =
    rawClinicalSubject &&
    (rawClinicalSubject !== "SUBJ-001" ||
      Boolean(clinicalSubjects?.some((s) => s?.id === rawClinicalSubject)))
      ? rawClinicalSubject
      : "";
  const resolvedNodeSubjectCode =
    cleanQueryString(matchedClinicalSubject?.id) ||
    cleanQueryString(matchedDemoParticipant?.subjectCode);
  const clinicalSiteId = cleanQueryString(effectiveClinical?.activeSiteId);
  const siteMatchesClinicalStore =
    !routeSite || !clinicalSiteId || clinicalSiteId === routeSite;

  const subjectCode =
    routeSubject ||
    canvasParticipant.subjectCode ||
    resolvedNodeSubjectCode ||
    (allowClinicalFallback &&
    !hasExplicitStandaloneStudyQuery &&
    !extractedRouteParticipantNode &&
    siteMatchesClinicalStore &&
    studyCode &&
    cleanQueryString(effectiveClinical?.activeStudyId) === studyCode
      ? verifiedClinicalSubject
      : "");

  let participantRef =
    routeSubject && routeSubject !== canvasParticipant.subjectCode
      ? ""
      : canvasParticipant.participantRef;
  if (
    !participantRef &&
    (parsedParticipantParam?.kind === "enrollment" ||
      parsedParticipantParam?.kind === "participant")
  ) {
    participantRef = formatNodeRef({
      kind: parsedParticipantParam.kind,
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
    const refNode = canonicalParticipantNodeFromSummary(matched);
    if (refNode) {
      participantRef = formatNodeRef(refNode);
    }
  }

  const enrollmentId =
    (routeSubject
      ? routeEnrollment
      : routeEnrollment || canvasParticipant.enrollmentId) || "";

  const recordParticipantParam = routeParamStudy
    ? parsedParticipantParam?.kind === "enrollment" ||
      parsedParticipantParam?.kind === "participant"
      ? formatNodeRef({
          kind: parsedParticipantParam.kind,
          id: parsedParticipantParam.id,
        })
      : !rawParticipantParam.includes(":")
        ? rawParticipantParam
        : ""
    : "";
  const inspectorNodeParam =
    routeParamStudy && parsedNodeParam
      ? formatNodeRef(parsedNodeParam)
      : routeParamStudy
        ? rawNodeParam
        : "";

  return {
    studyCode,
    siteId,
    explicitSiteId,
    subjectCode,
    participantRef,
    enrollmentId,
    recordParticipantParam,
    inspectorNodeParam,
  };
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

  if (isScreenAvailable("study")) {
    includedRoutes.add("study");
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
    includedRoutes.add("ecrf");
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
    if (siteReadinessPath.startsWith("/ctms")) {
      includedRoutes.add("ctms");
    } else if (siteReadinessPath.startsWith("/ssu")) {
      includedRoutes.add("ssu");
    }
    items.push({
      key: "workspace:site-readiness",
      group: "Workspace",
      label: "Site readiness",
      detail: "Site activation, milestones, and readiness workflows",
      target: { routePath: siteReadinessPath },
    });
  }

  if (isScreenAvailable("etmf") && hasRequiredRole(roles, ETMF_ROLES)) {
    includedRoutes.add("etmf");
    items.push({
      key: "workspace:trial-documents",
      group: "Workspace",
      label: "Trial documents",
      detail: "Study binder, document classification, and clinical review",
      target: {
        routePath: buildScopedWorkflowPath("/etmf", {
          studyCode: options.activeStudyCode,
          siteId: options.activeSiteId,
          subjectCode: options.activeSubjectCode,
        }),
      },
    });
  }

  if (isScreenAvailable("training")) {
    includedRoutes.add("training");
    items.push({
      key: "workspace:training",
      group: "Workspace",
      label: "Study training",
      detail: "Protocol curricula, staff assignments, and delegation sign-off",
      target: {
        routePath: buildScopedWorkflowPath("/training", {
          studyCode: options.activeStudyCode,
          siteId: options.activeSiteId,
          subjectCode: options.activeSubjectCode,
        }),
      },
    });
  }

  if (canAccessSupportHub(roles)) {
    includedRoutes.add("tickets");
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
    includedRoutes.add("knowledge");
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
    includedRoutes.add("overview");
    items.push({
      key: "workspace:orientation",
      group: "Workspace",
      label: "Orientation",
      detail: "Role-tailored workspace orientation and setup",
      target: {
        routePath: buildScopedWorkflowPath("/overview", {
          studyCode: options.activeStudyCode,
          siteId: options.activeSiteId,
          subjectCode: options.activeSubjectCode,
        }),
      },
    });
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
      const routePath = buildScopedWorkflowPath(mod.defaultPath, {
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

function unpinNodeRefString(raw: string | null | undefined): string {
  const clean = cleanQueryString(raw);
  if (!clean) return "";
  const parsed = parseNodeRef(clean);
  if (parsed) {
    return formatNodeRef({ kind: parsed.kind, id: parsed.id });
  }
  return clean.split("@")[0]?.trim() || "";
}

async function tryFetchZoomPath(
  nodeRef: string | undefined
): Promise<KernelZoomStep[] | null> {
  const clean = cleanQueryString(nodeRef);
  if (!clean) {
    return null;
  }
  try {
    const { kernelApi } = await import("../api/kernelApi");
    if (typeof kernelApi.getZoomPath !== "function") {
      return null;
    }
    const steps = await kernelApi.getZoomPath(clean);
    return Array.isArray(steps) ? steps : null;
  } catch {
    return null;
  }
}

function syncWorkspaceStoresNonBlocking(options: {
  studyId: string;
  siteId: string;
}): void {
  const current = readInstantiatedClinicalScope();
  syncInstantiatedClinicalScope({
    activeStudyId: options.studyId,
    activeSiteId: options.siteId,
    activeSubjectId:
      current?.activeStudyId === options.studyId
        ? (current?.activeSubjectId ?? "")
        : "",
  });
  void import("../stores/workspaceScope").then(({ syncWorkspaceScope }) => {
    syncWorkspaceScope(options);
  });
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

  if (target.nodeRef) {
    const steps = await tryFetchZoomPath(target.nodeRef);
    const stepOf = (level: string) =>
      steps?.find((step) => step.level === level && !step.mask);
    const zoomStudyCode = cleanQueryString(stepOf("study")?.label);
    const zoomSiteCode = cleanQueryString(stepOf("study_site")?.label);
    const zoomDirectorySiteCode = cleanQueryString(stepOf("site")?.label);
    const zoomEnrollmentStep = stepOf("enrollment");
    const zoomEnrollmentRef = unpinNodeRefString(zoomEnrollmentStep?.ref);
    const zoomSubjectCode = cleanQueryString(zoomEnrollmentStep?.label);
    const zoomCollectionRef = unpinNodeRefString(
      stepOf("collection_instance")?.ref
    );
    const zoomObservationRef = unpinNodeRefString(stepOf("observation")?.ref);
    const unpinnedTargetRef = unpinNodeRefString(target.nodeRef);
    const kind =
      cleanQueryString(target.nodeKind) ||
      parseNodeRef(unpinnedTargetRef)?.kind ||
      "";

    const resolvedStudy =
      zoomStudyCode || cleanQueryString(target.studyCode) || priorStudyCode;
    const resolvedSite =
      zoomSiteCode ||
      cleanQueryString(target.site) ||
      (kind === "site" || kind === "study_site"
        ? zoomDirectorySiteCode || cleanQueryString(target.nodeCode)
        : "");

    const pushScopedStudy = async (
      study: string,
      site: string,
      query: Record<string, string>,
      inspectorRef?: string | null
    ) => {
      const failure = await context.router.push({
        name: "study",
        params: { studyCode: study },
        query,
      });
      if (isNavigationFailure(failure)) {
        return;
      }
      syncWorkspaceStoresNonBlocking({ studyId: study, siteId: site });
      if (inspectorRef !== undefined) {
        await context.onFocusInspector?.(inspectorRef);
      }
    };

    if (kind === "study") {
      const studyTarget =
        zoomStudyCode ||
        cleanQueryString(target.studyCode) ||
        cleanQueryString(target.nodeCode);
      if (studyTarget) {
        await pushScopedStudy(studyTarget, "", {});
      } else {
        await context.router.push({ name: "study" });
      }
      return;
    }

    if (kind === "study_site" || kind === "site") {
      if (resolvedStudy) {
        const query: Record<string, string> =
          kind === "study_site"
            ? { stage: "start", section: "site-activation" }
            : { stage: "conduct", section: "sites" };
        if (resolvedSite) query.site = resolvedSite;
        if (unpinnedTargetRef) query.node = unpinnedTargetRef;
        await pushScopedStudy(
          resolvedStudy,
          resolvedSite,
          query,
          unpinnedTargetRef || null
        );
      } else {
        await context.router.push({
          name: "study",
          query: resolvedSite ? { site: resolvedSite } : {},
        });
      }
      return;
    }

    if (kind === "enrollment") {
      if (resolvedStudy) {
        const participantParam = zoomEnrollmentRef || unpinnedTargetRef;
        const query: Record<string, string> = { stage: "conduct" };
        if (resolvedSite) query.site = resolvedSite;
        if (participantParam) query.participant = participantParam;
        await pushScopedStudy(
          resolvedStudy,
          resolvedSite,
          query,
          participantParam || null
        );
      } else {
        await context.router.push({ name: "study" });
      }
      return;
    }

    if (kind === "collection_instance") {
      if (resolvedStudy) {
        const formParam = unpinnedTargetRef || zoomCollectionRef;
        const query: Record<string, string> = { stage: "conduct" };
        if (resolvedSite) query.site = resolvedSite;
        if (zoomEnrollmentRef) query.participant = zoomEnrollmentRef;
        if (formParam) query.form = formParam;
        await pushScopedStudy(resolvedStudy, resolvedSite, query);
      } else {
        await context.router.push({ name: "study" });
      }
      return;
    }

    if (kind === "observation") {
      if (resolvedStudy) {
        const obsParam = unpinnedTargetRef || zoomObservationRef;
        const query: Record<string, string> = { stage: "conduct" };
        if (resolvedSite) query.site = resolvedSite;
        if (zoomEnrollmentRef) query.participant = zoomEnrollmentRef;
        if (zoomCollectionRef) query.form = zoomCollectionRef;
        if (obsParam) query.observation = obsParam;
        await pushScopedStudy(resolvedStudy, resolvedSite, query);
      } else {
        await context.router.push({ name: "study" });
      }
      return;
    }

    if (kind === "form_query") {
      if (resolvedStudy) {
        const query: Record<string, string> = {
          stage: "data",
          section: "queries",
        };
        if (resolvedSite) query.site = resolvedSite;
        if (zoomSubjectCode) query.subject = zoomSubjectCode;
        if (unpinnedTargetRef) query.node = unpinnedTargetRef;
        await pushScopedStudy(
          resolvedStudy,
          resolvedSite,
          query,
          unpinnedTargetRef || null
        );
      } else {
        await context.router.push({ name: "study" });
      }
      return;
    }

    if (kind === "validation_finding") {
      if (resolvedStudy) {
        const query: Record<string, string> = {
          stage: "data",
          section: "validation",
        };
        if (resolvedSite) query.site = resolvedSite;
        if (unpinnedTargetRef) query.node = unpinnedTargetRef;
        await pushScopedStudy(
          resolvedStudy,
          resolvedSite,
          query,
          unpinnedTargetRef || null
        );
      } else {
        await context.router.push({ name: "study" });
      }
      return;
    }

    if (kind === "proposal") {
      if (resolvedStudy) {
        const query: Record<string, string> = {};
        if (resolvedSite) query.site = resolvedSite;
        if (unpinnedTargetRef) query.proposal = unpinnedTargetRef;
        await pushScopedStudy(resolvedStudy, resolvedSite, query);
      } else {
        await context.router.push({ name: "study" });
      }
      return;
    }

    if (kind === "protocol_version") {
      if (resolvedStudy) {
        const query: Record<string, string> = {
          stage: "design",
          section: "protocol-versions",
        };
        if (unpinnedTargetRef) query.node = unpinnedTargetRef;
        await pushScopedStudy(
          resolvedStudy,
          "",
          query,
          unpinnedTargetRef || null
        );
      } else {
        await context.router.push({ name: "study" });
      }
      return;
    }

    if (kind === "artifact") {
      if (resolvedStudy) {
        const query: Record<string, string> = {
          stage: "start",
          section: "documents",
        };
        if (resolvedSite) query.site = resolvedSite;
        if (unpinnedTargetRef) query.node = unpinnedTargetRef;
        await pushScopedStudy(
          resolvedStudy,
          resolvedSite,
          query,
          unpinnedTargetRef || null
        );
      } else if (isScreenAvailable("etmf")) {
        await context.router.push("/etmf");
      } else {
        await context.router.push({ name: "study" });
      }
      return;
    }

    if (kind === "qualification") {
      if (resolvedStudy) {
        const query: Record<string, string> = {
          stage: "start",
          section: "training",
        };
        if (unpinnedTargetRef) query.node = unpinnedTargetRef;
        await pushScopedStudy(
          resolvedStudy,
          "",
          query,
          unpinnedTargetRef || null
        );
      } else if (isScreenAvailable("training")) {
        await context.router.push("/training");
      } else {
        await context.router.push({ name: "study" });
      }
      return;
    }

    if (
      kind === "concept" ||
      kind === "code_system" ||
      kind === "code_system_version" ||
      kind === "standard_version" ||
      kind === "library_item"
    ) {
      if (resolvedStudy && context.currentRouteName === "study") {
        const query: Record<string, string> = {};
        if (unpinnedTargetRef) query.node = unpinnedTargetRef;
        await pushScopedStudy(
          resolvedStudy,
          resolvedSite,
          query,
          unpinnedTargetRef || null
        );
      } else if (isScreenAvailable("knowledge")) {
        await context.router.push(
          buildScopedWorkflowPath(
            "/knowledge",
            resolvedStudy ? { studyCode: resolvedStudy } : {}
          )
        );
      } else if (resolvedStudy) {
        const query: Record<string, string> = {};
        if (unpinnedTargetRef) query.node = unpinnedTargetRef;
        await pushScopedStudy(
          resolvedStudy,
          resolvedSite,
          query,
          unpinnedTargetRef || null
        );
      } else {
        await context.router.push({ name: "study" });
      }
      return;
    }

    if (resolvedStudy) {
      const query: Record<string, string> = {};
      if (resolvedSite) query.site = resolvedSite;
      if (unpinnedTargetRef) query.node = unpinnedTargetRef;
      await pushScopedStudy(
        resolvedStudy,
        resolvedSite,
        query,
        unpinnedTargetRef || null
      );
      return;
    }
  }

  const studyCode = cleanQueryString(target.studyCode) || priorStudyCode;
  if (!studyCode) {
    await context.router.push({ name: "study" });
    return;
  }
  const isCrossStudy = Boolean(priorStudyCode && studyCode !== priorStudyCode);
  const onStudyRoute = context.currentRouteName === "study";
  const previousScope = readInstantiatedClinicalScope();
  if (isCrossStudy && onStudyRoute) {
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
      !isCrossStudy && onStudyRoute && context.currentQuery
        ? { ...context.currentQuery }
        : {};
    const failure = await context.router.push({
      name: "study",
      params: { studyCode },
      query: { ...baseQuery, ask: target.ask },
    });
    if (isNavigationFailure(failure)) {
      if (onStudyRoute) restorePreviousScope();
      return;
    }
    if (isCrossStudy && !onStudyRoute) {
      syncInstantiatedClinicalScope({
        activeStudyId: studyCode,
        activeSiteId: "",
        activeSubjectId: "",
      });
    }
    await context.onFocusInspector?.(null);
    return;
  }

  let resolvedParticipantTarget = target.participant ?? null;
  if (
    resolvedParticipantTarget?.enrollment_ref &&
    !resolvedParticipantTarget.site_id
  ) {
    const steps = await tryFetchZoomPath(
      resolvedParticipantTarget.enrollment_ref
    );
    const zoomSite = cleanQueryString(
      steps?.find((s) => s.level === "study_site" && !s.mask)?.label
    );
    if (zoomSite) {
      resolvedParticipantTarget = {
        ...resolvedParticipantTarget,
        site_id: zoomSite,
      };
    }
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
      site: target.site,
      participant: target.participantRef,
      subject: target.subject,
      form: target.form,
      observation: target.observation,
      proposal: target.proposal,
      targetParticipant: resolvedParticipantTarget,
    },
    scopedWithStudy
  );
  const applyTargetScope = () => {
    if (
      resolvedParticipantTarget ||
      target.site !== undefined ||
      target.participantRef !== undefined
    ) {
      syncWorkspaceStoresNonBlocking({
        studyId: studyCode,
        siteId: location.query.site ?? "",
      });
    } else if (isCrossStudy) {
      syncInstantiatedClinicalScope({
        activeStudyId: studyCode,
        activeSiteId: "",
        activeSubjectId: "",
      });
    } else if (location.query.site !== undefined) {
      syncInstantiatedClinicalScope({
        activeStudyId: studyCode,
        activeSiteId: location.query.site,
        activeSubjectId: previousScope?.activeSubjectId ?? "",
      });
    }
  };
  if (onStudyRoute) {
    applyTargetScope();
  }
  const failure = await context.router.push(location);
  if (isNavigationFailure(failure)) {
    if (onStudyRoute) {
      restorePreviousScope();
    }
    return;
  }
  if (!onStudyRoute) {
    applyTargetScope();
  }
  if (resolvedParticipantTarget || target.participantRef) {
    await context.onFocusInspector?.(
      location.query.participant ?? location.query.node ?? null
    );
  }
}

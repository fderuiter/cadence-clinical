import { getActivePinia } from "pinia";
import type { LocationQuery, RouteLocationRaw, Router } from "vue-router";
import type { SubjectSummary } from "../api/executionApi";
import { formatNodeRef, parseNodeRef, tryNodeRef } from "./nodeRef";
import {
  isScreenAvailable,
  listDemoCatalogModules,
} from "../router/availability.js";
import { ROLE_ALIASES } from "../stores/auth.js";
import { getSyncedStudyScope } from "../stores/studyScopeBridge";
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

export function readInstantiatedStudyCanvas(): {
  studyCode?: string;
  knownParticipants?: Record<string, SubjectSummary>;
  knownParticipantsByEnrollmentId?: Record<string, SubjectSummary>;
  record?: { subjectCode?: string; enrollmentId?: string | null };
  participantsFilter?: { siteId?: string };
} | null {
  const stateRoot = getActivePinia()?.state.value as
    | {
        studyCanvas?: {
          studyCode?: string;
          knownParticipants?: Record<string, SubjectSummary>;
          knownParticipantsByEnrollmentId?: Record<string, SubjectSummary>;
          record?: { subjectCode?: string; enrollmentId?: string | null };
          participantsFilter?: { siteId?: string };
        };
      }
    | undefined;
  return stateRoot?.studyCanvas ?? null;
}

const UUID_REGEX =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

function normalizeWorkflowSubjectCode(
  value: unknown,
  canvasStore?: {
    knownParticipants?: Record<string, SubjectSummary>;
    knownParticipantsByEnrollmentId?: Record<string, SubjectSummary>;
  } | null
): string {
  if (typeof value !== "string") return "";
  const trimmed = value.trim();
  if (!trimmed) return "";

  const parsed = parseNodeRef(trimmed);
  let participantId = "";
  let enrollmentId = "";
  if (parsed) {
    if (parsed.kind === "participant") {
      participantId = parsed.id;
    } else if (parsed.kind === "enrollment") {
      enrollmentId = parsed.id;
    } else {
      return "";
    }
  } else if (trimmed.startsWith("participant:")) {
    participantId =
      trimmed.slice("participant:".length).split("@")[0]?.trim() || "";
  } else if (trimmed.startsWith("enrollment:")) {
    enrollmentId =
      trimmed.slice("enrollment:".length).split("@")[0]?.trim() || "";
  } else if (trimmed.includes(":")) {
    return "";
  } else if (UUID_REGEX.test(trimmed)) {
    participantId = trimmed;
  }

  if (enrollmentId) {
    const instantiated = readInstantiatedStudyCanvas();
    const known =
      canvasStore?.knownParticipantsByEnrollmentId?.[enrollmentId] ??
      canvasStore?.knownParticipants?.[enrollmentId] ??
      instantiated?.knownParticipantsByEnrollmentId?.[enrollmentId] ??
      instantiated?.knownParticipants?.[enrollmentId];
    return typeof known?.subject_id === "string" ? known.subject_id.trim() : "";
  }

  if (participantId) {
    const instantiated = readInstantiatedStudyCanvas();
    const known =
      canvasStore?.knownParticipants?.[participantId] ??
      canvasStore?.knownParticipantsByEnrollmentId?.[participantId] ??
      instantiated?.knownParticipants?.[participantId] ??
      instantiated?.knownParticipantsByEnrollmentId?.[participantId];
    return typeof known?.subject_id === "string" ? known.subject_id.trim() : "";
  }

  return trimmed;
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
        etmf?: {
          currentStudyId?: string;
          currentSiteId?: string;
          documentsList?: unknown[];
          qcQueue?: unknown[];
          activeDocument?: unknown;
          selectedDocumentIds?: unknown[];
          scopeForbiddenGuidance?: string;
          documentsError?: string;
        };
      }
    | undefined;
  const scopeState = stateRoot?.clinicalScope;
  if (scopeState) {
    scopeState.activeStudyId = next.activeStudyId;
    scopeState.activeSiteId = next.activeSiteId;
    scopeState.activeSubjectId = normalizeWorkflowSubjectCode(
      next.activeSubjectId
    );
  }
  const etmfState = stateRoot?.etmf;
  if (etmfState) {
    const prevStudy = (etmfState.currentStudyId || "").trim();
    const prevSite = (etmfState.currentSiteId || "").trim();
    const nextStudy = (next.activeStudyId || "").trim();
    const nextSite = (next.activeSiteId || "").trim();
    if (prevStudy !== nextStudy || prevSite !== nextSite) {
      etmfState.currentStudyId = nextStudy;
      etmfState.currentSiteId = nextSite;
      etmfState.documentsList = [];
      etmfState.qcQueue = [];
      etmfState.activeDocument = null;
      etmfState.selectedDocumentIds = [];
      etmfState.scopeForbiddenGuidance = "";
      etmfState.documentsError = "";
    }
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
  recordParticipantParam?: string;
  inspectorNodeParam?: string;
}

const SITE_READINESS_SPONSOR_ROLES = [
  "cra",
  "monitor",
  "data_manager",
  "sponsor_admin",
];
const SITE_READINESS_SITE_ROLES = ["site_investigator", "crc"];
const DATA_COLLECTION_ROLES = [
  ...SITE_READINESS_SITE_ROLES,
  ...SITE_READINESS_SPONSOR_ROLES,
];
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
  const subjectCode = normalizeWorkflowSubjectCode(
    scope?.subjectCode?.trim() ||
      (scope === undefined &&
      studyCode &&
      cleanQueryString(fallbackClinical?.activeStudyId) === studyCode
        ? cleanQueryString(fallbackClinical?.activeSubjectId)
        : "")
  );
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
    subjects?: Array<{ id?: string }>;
  } | null
): WorkspaceScopeSnapshot {
  const demo = isDemoBuild();
  const effectiveCanvas =
    canvasStore !== undefined
      ? canvasStore
      : !demo
        ? readInstantiatedStudyCanvas()
        : null;
  const effectiveClinical =
    clinicalStore !== undefined
      ? clinicalStore
      : readInstantiatedClinicalScope();
  const allowClinicalFallback = Boolean(effectiveClinical);
  const routeParamStudy = cleanQueryString(route?.params?.studyCode);
  const routeQueryStudy =
    cleanQueryString(route?.query?.studyId) ||
    cleanQueryString(route?.query?.study);
  const routeStudy = routeParamStudy || routeQueryStudy;

  const canvasStudy = cleanQueryString(effectiveCanvas?.studyCode);
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

  let participantSite = "";
  if (!routeSite && studyCode && effectiveCanvas?.studyCode === studyCode) {
    const extractedNode =
      extractParticipantNodeFromQueryValue(route?.query?.participant) ??
      extractParticipantNodeFromQueryValue(route?.query?.node);
    if (extractedNode?.kind === "enrollment") {
      const known =
        effectiveCanvas.knownParticipantsByEnrollmentId?.[extractedNode.id] ??
        effectiveCanvas.knownParticipants?.[extractedNode.id];
      participantSite = cleanQueryString(known?.site_id);
    } else if (extractedNode?.kind === "participant") {
      participantSite = cleanQueryString(
        effectiveCanvas.knownParticipants?.[extractedNode.id]?.site_id
      );
    }
  }

  let storeSite = "";
  if (!routeSite && studyCode) {
    if (effectiveCanvas?.studyCode === studyCode) {
      storeSite = cleanQueryString(effectiveCanvas.participantsFilter?.siteId);
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
  const routeSubject = normalizeWorkflowSubjectCode(
    cleanQueryString(route?.query?.subjectId) ||
      cleanQueryString(route?.query?.subject) ||
      (!parsedParticipantParam && !rawParticipantParam.includes(":")
        ? rawParticipantParam
        : ""),
    effectiveCanvas
  );
  const routeEnrollment = cleanQueryString(route?.query?.enrollmentId);
  const canvasParticipant = effectiveCanvas
    ? resolveCanvasParticipantContext(studyCode, route?.query, effectiveCanvas)
    : { subjectCode: "", participantRef: "", enrollmentId: "" };

  const rawClinicalSubject = cleanQueryString(
    effectiveClinical?.activeSubjectId
  );
  const clinicalSubjects =
    clinicalStore?.subjects ??
    (
      getActivePinia()?.state.value as
        { clinical?: { subjects?: Array<{ id?: string }> } } | undefined
    )?.clinical?.subjects;
  const verifiedClinicalSubject =
    rawClinicalSubject &&
    (rawClinicalSubject !== "SUBJ-001" ||
      Boolean(clinicalSubjects?.some((s) => s?.id === rawClinicalSubject)))
      ? rawClinicalSubject
      : "";

  const subjectCode = normalizeWorkflowSubjectCode(
    routeSubject ||
      canvasParticipant.subjectCode ||
      (allowClinicalFallback &&
      !hasExplicitStandaloneStudyQuery &&
      studyCode &&
      cleanQueryString(effectiveClinical?.activeStudyId) === studyCode
        ? verifiedClinicalSubject
        : ""),
    effectiveCanvas
  );

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
    effectiveCanvas?.studyCode === studyCode &&
    effectiveCanvas.knownParticipants
  ) {
    const matched = Object.values(effectiveCanvas.knownParticipants).find(
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
  if (isCrossStudy) {
    syncInstantiatedClinicalScope({
      activeStudyId: studyCode,
      activeSiteId: "",
      activeSubjectId: "",
    });
  }
  if (target.ask) {
    const baseQuery =
      !isCrossStudy &&
      context.currentRouteName === "study" &&
      context.currentQuery
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
  await context.router.push(location);
  if (target.participant) {
    await context.onFocusInspector?.(location.query.node ?? null);
  }
}

import { getActivePinia } from "pinia";
import type { LocationQuery } from "vue-router";
import type { SubjectSummary } from "../api/executionApi";
import { getSyncedStudyScope } from "../stores/studyScopeBridge";
import { formatNodeRef, parseNodeRef, tryNodeRef } from "./nodeRef";

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

export function cleanQueryString(value: unknown): string {
  return typeof value === "string" ? value.trim() : "";
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
  if (!scopeState) return;
  scopeState.activeStudyId = next.activeStudyId;
  scopeState.activeSiteId = next.activeSiteId;
  scopeState.activeSubjectId = next.activeSubjectId;
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

export function canonicalParticipantNodeFromSummary(
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

  let participantSite = "";
  if (!routeSite && studyCode && canvasStore?.studyCode === studyCode) {
    const extractedNode =
      extractParticipantNodeFromQueryValue(route?.query?.participant) ??
      extractParticipantNodeFromQueryValue(route?.query?.node);
    if (extractedNode?.kind === "enrollment") {
      const known =
        canvasStore.knownParticipantsByEnrollmentId?.[extractedNode.id] ??
        canvasStore.knownParticipants?.[extractedNode.id];
      participantSite = cleanQueryString(known?.site_id);
    } else if (extractedNode?.kind === "participant") {
      participantSite = cleanQueryString(
        canvasStore.knownParticipants?.[extractedNode.id]?.site_id
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

  const subjectCode =
    routeSubject ||
    canvasParticipant.subjectCode ||
    (allowClinicalFallback &&
    !hasExplicitStandaloneStudyQuery &&
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

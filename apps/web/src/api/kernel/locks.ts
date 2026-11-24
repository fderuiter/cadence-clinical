import { unwrapResponse } from "../client";
import {
  ASSERTION_ALREADY_LOCKED,
  ASSERTION_LOCKED,
  ASSERTION_SCOPE_NOT_READY,
  assertionErrorCode,
  describeKernelError,
  extractKernelExplanation,
  KERNEL_AUTHORIZATION_DENIED,
  KERNEL_SUBSCRIPTION_EXPIRED,
  kernelClient,
  signedMutationHeaders,
  streamKernelDownload,
  unversioned,
  type KernelSchemas,
  type KernelStreamedArtifact,
} from "./shared";

export type KernelLock = KernelSchemas["Kernel_KernelLockResponse"];
export type KernelStudyLocks = KernelSchemas["Kernel_KernelStudyLocksResponse"];
export type KernelLockReadiness =
  KernelSchemas["Kernel_KernelLockReadinessResponse"];
export type KernelLockBlocker =
  KernelSchemas["Kernel_KernelLockBlockerResponse"];
export type KernelLockBlockerKind =
  | "OpenQuery"
  | "MissingRequiredItem"
  | "UnverifiedValue"
  | "UnsignedForm"
  | "UnopenedVisit"
  | "ErrorFinding";
export type KernelLockKind = KernelSchemas["Kernel_LockKind"];
export type KernelLockMeaning = "Approved" | "Responsibility";
export type StudyExportFormat =
  | "csv"
  | "odm_xml"
  | "dataset_json"
  | "sas_bundle"
  | "r_bundle"
  | "study_archive";
export type StudyExportLockFilter =
  "all" | "locked_only" | "frozen_or_locked" | "unlocked_only";
export type CasebookExportFormat = "json" | "odm_xml" | "html" | "pdf";

export const LOCK_APPLY_ACTION = "/api/v1/kernel/locks";
export const LOCK_APPLY_SEMANTIC_ACTION = "kernel.lock.apply";
export const LOCK_RELEASE_SEMANTIC_ACTION = "kernel.lock.release";

/** Gateway step-up target URL for `POST /api/v1/kernel/locks/{lockRef}/release`. */
export function lockReleaseAction(lockRef: string): string {
  const clean = unversioned(lockRef);
  return `/api/v1/kernel/locks/${clean}/release`;
}

export interface KernelApplyLock {
  studyRef: string;
  scopeRef: string;
  kind: KernelLockKind;
  meaning?: KernelLockMeaning;
  lockId?: string | null;
  attestationId?: string | null;
  sigToken: string;
  reason: string;
  idempotencyKey: string;
}

export interface KernelReleaseLock {
  lockRef: string;
  studyRef: string;
  meaning?: KernelLockMeaning;
  assertionId?: string | null;
  attestationId?: string | null;
  sigToken: string;
  reason: string;
  idempotencyKey: string;
}

export interface KernelStudyLocksQuery {
  activeOnly?: boolean;
  asOf?: string;
  validAt?: string;
  knownAt?: string;
}

export interface KernelStudyExportQuery {
  studyRef: string;
  format?: StudyExportFormat;
  lockFilter?: StudyExportLockFilter;
  asOf?: string;
  validAt?: string;
  knownAt?: string;
}

export interface KernelCasebookExportQuery {
  enrollmentRef: string;
  format?: CasebookExportFormat;
  asOf?: string;
  validAt?: string;
  knownAt?: string;
}

/** Formats a concise context summary for a visible `KernelLockBlocker` item. */
export function formatLockBlockerDetail(blocker: KernelLockBlocker): string {
  if (blocker.masked) return "";
  if (blocker.detail) return blocker.detail;
  const parts: string[] = [];
  if (blocker.subject_code) parts.push(blocker.subject_code);
  if (blocker.collection_title) parts.push(blocker.collection_title);
  const item = blocker.item_label || blocker.item_id;
  if (item) parts.push(item);
  if (blocker.rule_id) parts.push(`[${blocker.rule_id}]`);
  if (blocker.query_text) parts.push(blocker.query_text);
  return parts.join(" · ");
}

/** Actionable user-facing explanation when a lock apply or release call fails. */
export function describeLockError(error: unknown): string {
  const code = assertionErrorCode(error);
  const status = (error as { status?: number | null } | null)?.status ?? null;
  if (code === KERNEL_SUBSCRIPTION_EXPIRED) {
    return describeKernelError(error);
  }
  if (code === ASSERTION_SCOPE_NOT_READY) {
    const data =
      ((error as { data?: Record<string, unknown> } | null)?.data as
        Record<string, unknown> | undefined) ??
      (error && typeof error === "object"
        ? (error as Record<string, unknown>)
        : undefined);
    const detail =
      typeof data?.detail === "string" && data.detail.trim()
        ? data.detail.trim()
        : "";
    const explanation = extractKernelExplanation(error);
    const conditionDetails = Array.isArray(explanation?.conditions)
      ? explanation.conditions
          .filter((item) => item && item.met === false)
          .map((item) => item.description?.trim())
          .filter((item): item is string => Boolean(item))
      : [];
    const nextActions = Array.isArray(explanation?.next_actions)
      ? explanation.next_actions
          .map((item) => item?.description?.trim())
          .filter((item): item is string => Boolean(item))
      : [];
    const parts: string[] = [];
    if (detail) {
      parts.push(detail);
    } else if (explanation?.claim) {
      parts.push(explanation.claim);
    }
    if (conditionDetails.length > 0) {
      parts.push(conditionDetails.slice(0, 3).join(" · "));
    }
    if (nextActions.length > 0) {
      parts.push(`Next actions: ${nextActions.slice(0, 3).join(" · ")}`);
    }
    if (parts.length > 0) {
      return parts.join(" ");
    }
    return "This scope has open queries, missing required items, unverified values, unsigned forms, unopened scheduled visits, or unresolved error validation findings that must be resolved before locking. Freeze remains available.";
  }
  if (code === ASSERTION_ALREADY_LOCKED || status === 409) {
    return "That lock state already changed; reload and inspect the current locks.";
  }
  if (code === ASSERTION_LOCKED || status === 423) {
    return "A higher or peer lock blocks this change until that lock is released.";
  }
  if (code === KERNEL_AUTHORIZATION_DENIED || status === 403) {
    return "You are not authorised or your step-up signature token could not be verified.";
  }
  return error instanceof Error ? error.message : String(error);
}

function buildExportParams(
  init: Record<string, string>,
  q: { asOf?: string; validAt?: string; knownAt?: string }
): string {
  const params = new URLSearchParams(init);
  if (q.asOf) params.set("as_of", q.asOf);
  if (q.validAt) params.set("valid_at", q.validAt);
  if (q.knownAt) params.set("known_at", q.knownAt);
  return params.toString();
}

const STUDY_EXPORT_EXT: Record<StudyExportFormat, string> = {
  csv: "csv",
  odm_xml: "odm.xml",
  dataset_json: "dataset.json",
  sas_bundle: "sas_bundle.zip",
  r_bundle: "r_bundle.zip",
  study_archive: "study_archive.zip",
};

const CASEBOOK_EXPORT_EXT: Record<CasebookExportFormat, string> = {
  json: "casebook.json",
  odm_xml: "casebook.odm.xml",
  html: "casebook.html",
  pdf: "casebook.pdf",
};

export const kernelLocksApi = {
  /** Lists active and historical Frozen and Locked assertions in a study, masked per caller visibility and blinding (GET /api/v1/kernel/studies/{studyRef}/locks). */
  listStudyLocks(
    studyRef: string,
    query: KernelStudyLocksQuery = {}
  ): Promise<KernelStudyLocks> {
    return unwrapResponse(
      kernelClient.GET("/api/v1/kernel/studies/{studyRef}/locks", {
        params: {
          path: { studyRef: unversioned(studyRef) },
          query: {
            active_only: query.activeOnly,
            as_of: query.asOf,
            valid_at: query.validAt,
            known_at: query.knownAt,
          },
        },
      })
    );
  },

  /** Evaluates all six lock readiness gates (open queries, missing required items, unverified values, unsigned forms, unopened scheduled visits, and unresolved error validation findings) for a study or child scope (`GET /api/v1/kernel/studies/{studyRef}/locks/readiness`). */
  getStudyLockReadiness(
    studyRef: string,
    scopeRef?: string
  ): Promise<KernelLockReadiness> {
    return unwrapResponse(
      kernelClient.GET("/api/v1/kernel/studies/{studyRef}/locks/readiness", {
        params: {
          path: { studyRef: unversioned(studyRef) },
          query: scopeRef ? { scope_ref: unversioned(scopeRef) } : undefined,
        },
      })
    );
  },

  /** Applies a signed Frozen or Locked assertion to a study, site, enrollment, form, or observation (POST /api/v1/kernel/locks). */
  applyLock({
    studyRef,
    scopeRef,
    kind,
    meaning = "Approved",
    lockId,
    attestationId,
    sigToken,
    reason,
    idempotencyKey,
  }: KernelApplyLock): Promise<KernelLock> {
    return unwrapResponse(
      kernelClient.POST("/api/v1/kernel/locks", {
        body: {
          study_ref: unversioned(studyRef),
          scope_ref: unversioned(scopeRef),
          kind,
          meaning,
          ...(lockId ? { lock_id: lockId } : {}),
          ...(attestationId ? { attestation_id: attestationId } : {}),
        },
        headers: signedMutationHeaders(reason, idempotencyKey, sigToken),
      })
    );
  },

  /** Releases an active Frozen or Locked assertion with step-up re-authentication and reason (POST /api/v1/kernel/locks/{lockRef}/release). */
  releaseLock({
    lockRef,
    studyRef,
    meaning = "Responsibility",
    assertionId,
    attestationId,
    sigToken,
    reason,
    idempotencyKey,
  }: KernelReleaseLock): Promise<KernelLock> {
    return unwrapResponse(
      kernelClient.POST("/api/v1/kernel/locks/{lockRef}/release", {
        params: { path: { lockRef: unversioned(lockRef) } },
        body: {
          study_ref: unversioned(studyRef),
          meaning,
          ...(assertionId ? { assertion_id: assertionId } : {}),
          ...(attestationId ? { attestation_id: attestationId } : {}),
        },
        headers: signedMutationHeaders(reason, idempotencyKey, sigToken),
      })
    );
  },

  /** Streams a study clinical snapshot or statistical/regulatory ZIP bundle at optional bitemporal `as_of` (GET /api/v1/kernel/studies/{studyRef}/exports). */
  streamStudyExport({
    studyRef,
    format = "csv",
    lockFilter = "locked_only",
    asOf,
    validAt,
    knownAt,
  }: KernelStudyExportQuery): Promise<KernelStreamedArtifact> {
    const qs = buildExportParams(
      { format, lock_filter: lockFilter },
      { asOf, validAt, knownAt }
    );
    return streamKernelDownload(
      `/api/v1/kernel/studies/${encodeURIComponent(unversioned(studyRef))}/exports?${qs}`,
      `study-export.${STUDY_EXPORT_EXT[format] || "csv"}`
    );
  },

  /** Streams a participant casebook in JSON, CDISC ODM v1.3.2 XML, printable HTML, or certified PDF with `X-Content-Sha256` integrity digest (GET /api/v1/kernel/enrollments/{enrollmentRef}/casebook). */
  streamEnrollmentCasebook({
    enrollmentRef,
    format = "json",
    asOf,
    validAt,
    knownAt,
  }: KernelCasebookExportQuery): Promise<KernelStreamedArtifact> {
    const qs = buildExportParams({ format }, { asOf, validAt, knownAt });
    return streamKernelDownload(
      `/api/v1/kernel/enrollments/${encodeURIComponent(unversioned(enrollmentRef))}/casebook?${qs}`,
      `participant.${CASEBOOK_EXPORT_EXT[format] || "casebook.json"}`
    );
  },
};

import {
  notifySubscriptionExpiredIfNeeded,
  SUBSCRIPTION_EXPIRED_GUIDANCE,
} from "../authorization.js";
import { encodeChangeReason } from "../changeReason";
import { unwrapResponse } from "../client";
import {
  ASSERTION_ALREADY_LOCKED,
  ASSERTION_LOCKED,
  assertionErrorCode,
  KERNEL_AUTHORIZATION_DENIED,
  KERNEL_SUBSCRIPTION_EXPIRED,
  kernelClient,
  streamKernelDownload,
  unversioned,
  type KernelSchemas,
  type KernelStreamedArtifact,
} from "./shared";

export type KernelLock = KernelSchemas["Kernel_KernelLockResponse"];
export type KernelStudyLocks = KernelSchemas["Kernel_KernelStudyLocksResponse"];
export type KernelLockKind = KernelSchemas["Kernel_LockKind"];
export type KernelLockMeaning = "Approved" | "Responsibility";
export type StudyExportFormat = "csv" | "odm_xml" | "dataset_json";
export type StudyExportLockFilter =
  "all" | "locked_only" | "frozen_or_locked" | "unlocked_only";
export type CasebookExportFormat = "json" | "odm_xml" | "html";

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

/** Actionable user-facing explanation when a lock apply or release call fails. */
export function describeLockError(error: unknown): string {
  const code = assertionErrorCode(error);
  const status = (error as { status?: number | null } | null)?.status ?? null;
  if (code === KERNEL_SUBSCRIPTION_EXPIRED) {
    notifySubscriptionExpiredIfNeeded(
      status ?? 403,
      (error as { data?: unknown } | null)?.data
    );
    return SUBSCRIPTION_EXPIRED_GUIDANCE;
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
        headers: {
          "X-Change-Reason": encodeChangeReason(reason),
          "Idempotency-Key": idempotencyKey,
          "X-Sig-Token": sigToken,
        },
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
        headers: {
          "X-Change-Reason": encodeChangeReason(reason),
          "Idempotency-Key": idempotencyKey,
          "X-Sig-Token": sigToken,
        },
      })
    );
  },

  /** Streams a study clinical snapshot in CSV, CDISC ODM v1.3.2 XML, or CDISC Dataset-JSON at optional bitemporal `as_of` (GET /api/v1/kernel/studies/{studyRef}/exports). */
  streamStudyExport({
    studyRef,
    format = "csv",
    lockFilter = "locked_only",
    asOf,
    validAt,
    knownAt,
  }: KernelStudyExportQuery): Promise<KernelStreamedArtifact> {
    const cleanRef = unversioned(studyRef);
    const params = new URLSearchParams({
      format,
      lock_filter: lockFilter,
    });
    if (asOf) params.set("as_of", asOf);
    if (validAt) params.set("valid_at", validAt);
    if (knownAt) params.set("known_at", knownAt);
    const ext =
      format === "odm_xml"
        ? "odm.xml"
        : format === "dataset_json"
          ? "dataset.json"
          : "csv";
    return streamKernelDownload(
      `/api/v1/kernel/studies/${encodeURIComponent(cleanRef)}/exports?${params.toString()}`,
      `study-export.${ext}`
    );
  },

  /** Streams a participant casebook in JSON, CDISC ODM v1.3.2 XML, or printable HTML with `X-Content-Sha256` integrity digest (GET /api/v1/kernel/enrollments/{enrollmentRef}/casebook). */
  streamEnrollmentCasebook({
    enrollmentRef,
    format = "json",
    asOf,
    validAt,
    knownAt,
  }: KernelCasebookExportQuery): Promise<KernelStreamedArtifact> {
    const cleanRef = unversioned(enrollmentRef);
    const params = new URLSearchParams({ format });
    if (asOf) params.set("as_of", asOf);
    if (validAt) params.set("valid_at", validAt);
    if (knownAt) params.set("known_at", knownAt);
    const ext =
      format === "odm_xml"
        ? "casebook.odm.xml"
        : format === "html"
          ? "casebook.html"
          : "casebook.json";
    return streamKernelDownload(
      `/api/v1/kernel/enrollments/${encodeURIComponent(cleanRef)}/casebook?${params.toString()}`,
      `participant.${ext}`
    );
  },
};

import { unwrapResponse } from "../client";
import type {
  CasebookExportFormat,
  KernelApplyLock,
  KernelCasebookExportQuery,
  KernelLock,
  KernelLockMilestone,
  KernelLockReadiness,
  KernelReleaseLock,
  KernelStudyExportQuery,
  KernelStudyLocks,
  KernelStudyLocksQuery,
  StudyExportFormat,
} from "./locks";
import {
  kernelClient,
  signedMutationHeaders,
  streamKernelDownload,
  unversioned,
  type KernelStreamedArtifact,
} from "./shared";

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

export function executeApplyLock({
  studyRef,
  scopeRef,
  kind,
  milestone,
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
        ...(milestone ? { milestone } : {}),
        ...(lockId ? { lock_id: lockId } : {}),
        ...(attestationId ? { attestation_id: attestationId } : {}),
      },
      headers: signedMutationHeaders(reason, idempotencyKey, sigToken),
    })
  );
}

export function executeReleaseLock({
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
}

export function executeStreamStudyExport({
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
}

export function executeStreamEnrollmentCasebook({
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
}

export function executeListStudyLocks(
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
}

export function executeGetStudyLockReadiness(
  studyRef: string,
  scopeRef?: string,
  milestone?: KernelLockMilestone
): Promise<KernelLockReadiness> {
  const query: { scope_ref?: string; milestone?: string } = {};
  if (scopeRef) {
    query.scope_ref = unversioned(scopeRef);
  }
  if (milestone) {
    query.milestone = milestone;
  }
  return unwrapResponse(
    kernelClient.GET("/api/v1/kernel/studies/{studyRef}/locks/readiness", {
      params: {
        path: { studyRef: unversioned(studyRef) },
        query: Object.keys(query).length > 0 ? query : undefined,
      },
    })
  );
}

import { getBearerToken } from "../api/authorization.js";
import { encodeChangeReason } from "../api/changeReason";
import { ApiError, getBaseUrl, unwrapResponse } from "../api/client";
import {
  type CasebookExportFormat,
  describeKernelError,
  type KernelApplyLock,
  type KernelApproveLibraryItemRequest,
  type KernelCasebookExportQuery,
  type KernelDeclareStudyRulePacksInput,
  type KernelEntitlements,
  type KernelLock,
  type KernelMatrixGapFinding,
  type KernelOpenApiClient,
  type KernelProtocolDraftSummary,
  type KernelProtocolImportRequest,
  type KernelReleaseLock,
  type KernelRulePack,
  type KernelSimulationResult,
  type KernelStreamedArtifact,
  type KernelStudyExportQuery,
  type KernelStudyRulePacks,
  type KernelTrialOnboardingRequest,
  type KernelTrialOnboardingResponse,
  type StudyExportFormat,
} from "../api/kernelApi";
import { demoNowMs, nextDemoStep } from "./demoMode.js";

function unversioned(nodeRef: string): string {
  const at = nodeRef.indexOf("@");
  return at < 0 ? nodeRef : nodeRef.slice(0, at);
}

function commandHeaders(reason: string, idempotencyKey: string) {
  return {
    "X-Change-Reason": encodeChangeReason(reason),
    "Idempotency-Key": idempotencyKey,
  };
}

function signedCommandHeaders(
  reason: string,
  idempotencyKey: string,
  sigToken: string
) {
  return {
    "X-Change-Reason": encodeChangeReason(reason),
    "Idempotency-Key": idempotencyKey,
    "X-Sig-Token": sigToken,
  };
}

function parseDispositionFilename(
  disposition: string | null,
  fallback: string
): string {
  if (!disposition) return fallback;
  const match = disposition.match(/filename="?([^";]+)"?/i);
  return match?.[1]?.trim() || fallback;
}

export async function streamKernelDownload(
  pathAndQuery: string,
  fallbackFilename: string
): Promise<KernelStreamedArtifact> {
  const base = getBaseUrl().replace(/\/+$/, "");
  const headers = new Headers();
  const token = await getBearerToken();
  if (token) {
    headers.set("Authorization", `Bearer ${token}`);
  }
  const response = await globalThis.fetch(`${base}${pathAndQuery}`, {
    method: "GET",
    headers,
  });
  if (!response.ok) {
    let errData: unknown;
    try {
      errData = await response.json();
    } catch {
      errData = null;
    }
    const detail =
      (errData as Record<string, unknown> | null)?.detail ||
      (errData as Record<string, unknown> | null)?.message ||
      `Request failed with status ${response.status}`;
    throw new ApiError(
      typeof detail === "string" ? detail : JSON.stringify(detail),
      response.status,
      response.statusText,
      errData
    );
  }
  const blob = await response.blob();
  const contentType =
    response.headers.get("content-type") ||
    blob.type ||
    "application/octet-stream";
  const filename = parseDispositionFilename(
    response.headers.get("content-disposition"),
    fallbackFilename
  );
  const contentSha256 = response.headers.get("x-content-sha256");
  return { blob, filename, contentType, contentSha256 };
}

export async function uploadProtocolDraftFile({
  studyRef,
  file,
  templateIds,
  replaceForms = false,
  reason,
  idempotencyKey,
}: KernelProtocolImportRequest & {
  file: File;
}): Promise<KernelProtocolDraftSummary> {
  const baseUrl = getBaseUrl().replace(/\/+$/, "");
  const url = `${baseUrl}/api/v1/kernel/studies/${encodeURIComponent(studyRef)}/protocol-imports`;
  const formData = new FormData();
  formData.append("file", file);
  if (templateIds && templateIds.length > 0) {
    for (const tplId of templateIds) {
      formData.append("template_ids", tplId);
    }
  }
  if (replaceForms) {
    formData.append("replace_forms", "true");
  }

  const headers: Record<string, string> = {
    "X-Change-Reason": encodeChangeReason(reason),
    "Idempotency-Key": idempotencyKey,
  };
  const token = await getBearerToken();
  if (token) {
    headers["Authorization"] = `Bearer ${token}`;
  }

  const response = await globalThis.fetch(url, {
    method: "POST",
    headers,
    body: formData,
  });
  if (!response.ok) {
    const errData = await response.json().catch(() => null);
    const tempError = new ApiError(
      `Import failed with status ${response.status}`,
      response.status,
      response.statusText,
      errData
    );
    throw new ApiError(
      describeKernelError(tempError),
      response.status,
      response.statusText,
      errData
    );
  }
  return (await response.json()) as KernelProtocolDraftSummary;
}

export function performStudyExportStream({
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
  const extByFormat: Record<StudyExportFormat, string> = {
    csv: "csv",
    odm_xml: "odm.xml",
    dataset_json: "dataset.json",
    sas_bundle: "sas_bundle.zip",
    r_bundle: "r_bundle.zip",
    study_archive: "study_archive.zip",
  };
  const ext = extByFormat[format] || "csv";
  return streamKernelDownload(
    `/api/v1/kernel/studies/${encodeURIComponent(cleanRef)}/exports?${params.toString()}`,
    `study-export.${ext}`
  );
}

export function performEnrollmentCasebookStream({
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
  const extByFormat: Record<CasebookExportFormat, string> = {
    json: "casebook.json",
    odm_xml: "casebook.odm.xml",
    html: "casebook.html",
    pdf: "casebook.pdf",
  };
  const ext = extByFormat[format] || "casebook.json";
  return streamKernelDownload(
    `/api/v1/kernel/enrollments/${encodeURIComponent(cleanRef)}/casebook?${params.toString()}`,
    `participant.${ext}`
  );
}

export function performSimulate(
  client: KernelOpenApiClient,
  request: {
    baseVersionRef?: string;
    storedVersionRef?: string;
    candidateContent?: string;
    validAt?: string;
    knownAt?: string;
  }
): Promise<KernelSimulationResult> {
  return unwrapResponse(
    client.POST("/api/v1/kernel/simulation/simulate", {
      body: {
        base_version_ref: request.baseVersionRef ?? null,
        stored_version_ref: request.storedVersionRef ?? null,
        candidate_content: request.candidateContent ?? null,
        valid_at: request.validAt ?? null,
        known_at: request.knownAt ?? null,
      },
    })
  );
}

export function performCheckMatrixGaps(
  client: KernelOpenApiClient,
  protocolContent: string
): Promise<KernelMatrixGapFinding[]> {
  return unwrapResponse(
    client.POST("/api/v1/kernel/protocols/matrix-gap-check", {
      body: { protocol_content: protocolContent },
    })
  );
}

export function performBootstrapTrial(
  client: KernelOpenApiClient,
  request: KernelTrialOnboardingRequest,
  options?: { idempotencyKey?: string; reason?: string }
): Promise<KernelTrialOnboardingResponse> {
  const idempotencyKey =
    options?.idempotencyKey ||
    `web-trial-bootstrap-${demoNowMs(nextDemoStep("trial-bootstrap"))}`;
  const reason =
    options?.reason ||
    `Bootstrap self-service ${request.tier || "Individual"} trial workspace`;
  return unwrapResponse(
    client.POST("/api/v1/kernel/onboarding/trial", {
      headers: commandHeaders(reason, idempotencyKey),
      body: request,
    })
  );
}

export function performUpgradeSubscriptionTier(
  client: KernelOpenApiClient,
  {
    tier,
    idempotencyKey,
    reason = "Update commercial subscription tier in workspace",
  }: {
    tier: string;
    idempotencyKey: string;
    reason?: string;
  }
): Promise<KernelEntitlements> {
  return unwrapResponse(
    client.POST("/api/v1/kernel/entitlements/tier", {
      headers: commandHeaders(reason, idempotencyKey),
      body: { tier },
    })
  );
}

export function performActivateSubscription(
  client: KernelOpenApiClient,
  {
    tier,
    studyPhase,
    studyDurationMonths,
    countries,
    siteCount,
    participantCount,
    activeStudyCount,
    billingConfirmation,
    idempotencyKey,
    reason = "Activate scoped commercial subscription in workspace",
  }: {
    tier: string;
    studyPhase?: string | null;
    studyDurationMonths?: number | null;
    countries?: string[] | null;
    siteCount?: number | null;
    participantCount?: number | null;
    activeStudyCount?: number | null;
    billingConfirmation?: string | null;
    idempotencyKey: string;
    reason?: string;
  }
): Promise<KernelEntitlements> {
  return unwrapResponse(
    client.POST("/api/v1/kernel/entitlements/activate", {
      headers: commandHeaders(reason, idempotencyKey),
      body: {
        tier,
        study_phase: studyPhase ?? null,
        study_duration_months: studyDurationMonths ?? null,
        countries: countries ?? null,
        site_count: siteCount ?? null,
        participant_count: participantCount ?? null,
        active_study_count: activeStudyCount ?? null,
        billing_confirmation: billingConfirmation ?? null,
      },
    })
  );
}

export function performApplyLock(
  client: KernelOpenApiClient,
  {
    studyRef,
    scopeRef,
    kind,
    meaning = "Approved",
    lockId,
    attestationId,
    sigToken,
    reason,
    idempotencyKey,
  }: KernelApplyLock
): Promise<KernelLock> {
  return unwrapResponse(
    client.POST("/api/v1/kernel/locks", {
      body: {
        study_ref: unversioned(studyRef),
        scope_ref: unversioned(scopeRef),
        kind,
        meaning,
        ...(lockId ? { lock_id: lockId } : {}),
        ...(attestationId ? { attestation_id: attestationId } : {}),
      },
      headers: signedCommandHeaders(reason, idempotencyKey, sigToken),
    })
  );
}

export function performReleaseLock(
  client: KernelOpenApiClient,
  {
    lockRef,
    studyRef,
    meaning = "Responsibility",
    assertionId,
    attestationId,
    sigToken,
    reason,
    idempotencyKey,
  }: KernelReleaseLock
): Promise<KernelLock> {
  return unwrapResponse(
    client.POST("/api/v1/kernel/locks/{lockRef}/release", {
      params: { path: { lockRef: unversioned(lockRef) } },
      body: {
        study_ref: unversioned(studyRef),
        meaning,
        ...(assertionId ? { assertion_id: assertionId } : {}),
        ...(attestationId ? { attestation_id: attestationId } : {}),
      },
      headers: signedCommandHeaders(reason, idempotencyKey, sigToken),
    })
  );
}

export function performApproveLibraryItem(
  client: KernelOpenApiClient,
  {
    itemRef,
    meaning = "Approved",
    attestationId,
    sigToken,
    reason,
    idempotencyKey,
  }: KernelApproveLibraryItemRequest
): Promise<KernelRulePack> {
  return unwrapResponse(
    client.POST("/api/v1/kernel/library-items/{itemRef}/approve", {
      params: { path: { itemRef: unversioned(itemRef) } },
      body: {
        meaning,
        ...(attestationId ? { attestation_id: attestationId } : {}),
      },
      headers: signedCommandHeaders(reason, idempotencyKey, sigToken),
    })
  );
}

export function performDeclareStudyRulePacks(
  client: KernelOpenApiClient,
  {
    studyRef,
    rulePackRefs,
    reason,
    idempotencyKey,
  }: KernelDeclareStudyRulePacksInput
): Promise<KernelStudyRulePacks> {
  const normalizedRefs = rulePackRefs.map((ref) => {
    const clean = unversioned(ref.trim());
    return clean.includes(":") ? clean : `library_item:${clean}`;
  });
  return unwrapResponse(
    client.PUT("/api/v1/kernel/studies/{studyRef}/standards", {
      params: { path: { studyRef } },
      body: { rule_pack_refs: normalizedRefs },
      headers: commandHeaders(reason, idempotencyKey),
    })
  );
}

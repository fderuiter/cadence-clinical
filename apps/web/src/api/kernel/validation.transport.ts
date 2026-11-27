import { unwrapResponse } from "../client";
import {
  kernelClient,
  mutationHeaders,
  signedMutationHeaders,
  unversioned,
} from "./shared";
import type {
  KernelAcceptValidationFindingInput,
  KernelApproveLibraryItemRequest,
  KernelClassifyValidationFindingInput,
  KernelDeclareStudyRulePacksInput,
  KernelExplainValidationFindingInput,
  KernelRulePack,
  KernelRulePackPreview,
  KernelStudyAttention,
  KernelStudyRulePacks,
  KernelStudyValidationFindingsQuery,
  KernelValidationFinding,
  KernelValidationFindingDetail,
  KernelValidationFindingPage,
  KernelValidationFindingReasonInput,
} from "./validation";

function findingPath(findingRef: string) {
  return { path: { findingRef: unversioned(findingRef) } };
}

export function executeListStudyRulePacks(
  studyRef: string
): Promise<KernelStudyRulePacks> {
  return unwrapResponse(
    kernelClient.GET("/api/v1/kernel/studies/{studyRef}/rule-packs", {
      params: { path: { studyRef } },
    })
  );
}

export function executePreviewStudyRulePack(
  studyRef: string,
  itemRef: string
): Promise<KernelRulePackPreview> {
  return unwrapResponse(
    kernelClient.POST(
      "/api/v1/kernel/studies/{studyRef}/rule-packs/{itemRef}/preview",
      {
        params: { path: { studyRef, itemRef: unversioned(itemRef) } },
      }
    )
  );
}

export function executeApproveLibraryItem({
  itemRef,
  meaning = "Approved",
  attestationId,
  sigToken,
  reason,
  idempotencyKey,
}: KernelApproveLibraryItemRequest): Promise<KernelRulePack> {
  return unwrapResponse(
    kernelClient.POST("/api/v1/kernel/library-items/{itemRef}/approve", {
      params: { path: { itemRef: unversioned(itemRef) } },
      body: {
        meaning,
        ...(attestationId ? { attestation_id: attestationId } : {}),
      },
      headers: signedMutationHeaders(reason, idempotencyKey, sigToken),
    })
  );
}

export function executeDeclareStudyRulePacks({
  studyRef,
  rulePackRefs,
  reason,
  idempotencyKey,
}: KernelDeclareStudyRulePacksInput): Promise<KernelStudyRulePacks> {
  const normalizedRefs = rulePackRefs.map((ref) => {
    const clean = unversioned(ref.trim());
    return clean.includes(":") ? clean : `library_item:${clean}`;
  });
  return unwrapResponse(
    kernelClient.PUT("/api/v1/kernel/studies/{studyRef}/standards", {
      params: { path: { studyRef } },
      body: { rule_pack_refs: normalizedRefs },
      headers: mutationHeaders(reason, idempotencyKey),
    })
  );
}

export function executeListStudyValidationFindings(
  studyRef: string,
  query: KernelStudyValidationFindingsQuery = {}
): Promise<KernelValidationFindingPage> {
  const statuses = Array.isArray(query.status)
    ? query.status.filter(Boolean)
    : query.status
      ? [query.status]
      : undefined;
  return unwrapResponse(
    kernelClient.GET("/api/v1/kernel/studies/{studyRef}/validation/findings", {
      params: {
        path: { studyRef },
        query: {
          stage: query.stage || undefined,
          status: statuses && statuses.length > 0 ? statuses : undefined,
          severity: query.severity || undefined,
          dataset: query.dataset || undefined,
          pack_key: query.packKey || undefined,
          limit: query.limit,
        },
      },
    })
  );
}

export function executeGetStudyAttention(
  studyRef: string,
  query: { limit?: number } = {}
): Promise<KernelStudyAttention> {
  return unwrapResponse(
    kernelClient.GET("/api/v1/kernel/studies/{studyRef}/attention", {
      params: {
        path: { studyRef },
        query: { limit: query.limit },
      },
    })
  );
}

export function executeGetValidationFinding(
  findingRef: string
): Promise<KernelValidationFindingDetail> {
  return unwrapResponse(
    kernelClient.GET("/api/v1/kernel/validation/findings/{findingRef}", {
      params: findingPath(findingRef),
    })
  );
}

export function executeInvestigateValidationFinding({
  findingRef,
  reason,
  idempotencyKey,
}: KernelValidationFindingReasonInput): Promise<KernelValidationFinding> {
  return unwrapResponse(
    kernelClient.POST(
      "/api/v1/kernel/validation/findings/{findingRef}/investigate",
      {
        params: findingPath(findingRef),
        headers: mutationHeaders(reason, idempotencyKey),
      }
    )
  );
}

export function executeClassifyValidationFinding({
  findingRef,
  classification,
  reason,
  idempotencyKey,
}: KernelClassifyValidationFindingInput): Promise<KernelValidationFinding> {
  return unwrapResponse(
    kernelClient.POST(
      "/api/v1/kernel/validation/findings/{findingRef}/classify",
      {
        params: findingPath(findingRef),
        body: { classification },
        headers: mutationHeaders(reason, idempotencyKey),
      }
    )
  );
}

export function executeAcceptValidationFinding({
  findingRef,
  rationale,
  attestationId,
  sigToken,
  reason,
  idempotencyKey,
}: KernelAcceptValidationFindingInput): Promise<KernelValidationFinding> {
  return unwrapResponse(
    kernelClient.POST(
      "/api/v1/kernel/validation/findings/{findingRef}/accept",
      {
        params: findingPath(findingRef),
        body: {
          rationale,
          ...(attestationId ? { attestation_id: attestationId } : {}),
        },
        headers: signedMutationHeaders(reason, idempotencyKey, sigToken),
      }
    )
  );
}

export function executeExplainValidationFinding({
  findingRef,
  explanation,
  attestationId,
  sigToken,
  reason,
  idempotencyKey,
}: KernelExplainValidationFindingInput): Promise<KernelValidationFinding> {
  return unwrapResponse(
    kernelClient.POST(
      "/api/v1/kernel/validation/findings/{findingRef}/explain",
      {
        params: findingPath(findingRef),
        body: {
          explanation,
          ...(attestationId ? { attestation_id: attestationId } : {}),
        },
        headers: signedMutationHeaders(reason, idempotencyKey, sigToken),
      }
    )
  );
}

export function executeReopenValidationFinding({
  findingRef,
  reason,
  idempotencyKey,
}: KernelValidationFindingReasonInput): Promise<KernelValidationFinding> {
  return unwrapResponse(
    kernelClient.POST(
      "/api/v1/kernel/validation/findings/{findingRef}/reopen",
      {
        params: findingPath(findingRef),
        headers: mutationHeaders(reason, idempotencyKey),
      }
    )
  );
}

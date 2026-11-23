import { encodeChangeReason } from "../changeReason";
import { unwrapResponse } from "../client";
import type { KernelLockMeaning } from "./locks";
import { kernelClient, unversioned, type KernelSchemas } from "./shared";

export type KernelRulePack = KernelSchemas["Kernel_KernelRulePackResponse"];
export type KernelStudyRulePacks =
  KernelSchemas["Kernel_KernelStudyRulePacksResponse"];
export type KernelRulePackPreview =
  KernelSchemas["Kernel_KernelRulePackPreviewResponse"];
export type KernelRulePackPreviewViolation =
  KernelSchemas["Kernel_KernelRulePackPreviewViolationResponse"];
export type KernelValidationFinding =
  KernelSchemas["Kernel_KernelValidationFindingResponse"];
export type KernelValidationFindingPage =
  KernelSchemas["Kernel_KernelValidationFindingPageResponse"];
export type KernelValidationFindingDetail =
  KernelSchemas["Kernel_KernelValidationFindingDetailResponse"];

/** Semantic step-up action for `POST /api/v1/kernel/library-items/{itemRef}/approve`. */
export const RULE_PACK_APPROVE_SEMANTIC_ACTION = "kernel.library_item.approve";

/** Gateway step-up target URL for `POST /api/v1/kernel/library-items/{itemRef}/approve`. */
export function rulePackApproveAction(itemRef: string): string {
  const clean = unversioned(itemRef);
  return `/api/v1/kernel/library-items/${clean}/approve`;
}

export interface KernelApproveLibraryItemRequest {
  itemRef: string;
  meaning?: KernelLockMeaning;
  attestationId?: string | null;
  sigToken: string;
  reason: string;
  idempotencyKey: string;
}

export interface KernelDeclareStudyRulePacksInput {
  studyRef: string;
  rulePackRefs: string[];
  reason: string;
  idempotencyKey: string;
}

export interface KernelStudyValidationFindingsQuery {
  stage?: string;
  status?: string;
  severity?: string;
  dataset?: string;
  packKey?: string;
  limit?: number;
}

export const kernelValidationApi = {
  /** Lists edit-check rule packs in the tenant alongside whether each is pinned on the study. */
  listStudyRulePacks(studyRef: string): Promise<KernelStudyRulePacks> {
    return unwrapResponse(
      kernelClient.GET("/api/v1/kernel/studies/{studyRef}/rule-packs", {
        params: { path: { studyRef } },
      })
    );
  },

  /** Dry-runs a rule pack in memory against all collection instances of a study without persisting findings. */
  previewStudyRulePack(
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
  },

  /** Approves a draft library item (such as a RulePack) with a step-up signature token. */
  approveLibraryItem({
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
        headers: {
          "X-Change-Reason": encodeChangeReason(reason),
          "Idempotency-Key": idempotencyKey,
          "X-Sig-Token": sigToken,
        },
      })
    );
  },

  /** Pins the active approved rule packs on a study's standards profile. */
  declareStudyRulePacks({
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
        headers: {
          "X-Change-Reason": encodeChangeReason(reason),
          "Idempotency-Key": idempotencyKey,
        },
      })
    );
  },

  /** Lists validation findings recorded for a study, newest first. */
  listStudyValidationFindings(
    studyRef: string,
    query: KernelStudyValidationFindingsQuery = {}
  ): Promise<KernelValidationFindingPage> {
    return unwrapResponse(
      kernelClient.GET(
        "/api/v1/kernel/studies/{studyRef}/validation/findings",
        {
          params: {
            path: { studyRef },
            query: {
              stage: query.stage || undefined,
              status: query.status || undefined,
              severity: query.severity || undefined,
              dataset: query.dataset || undefined,
              pack_key: query.packKey || undefined,
              limit: query.limit,
            },
          },
        }
      )
    );
  },

  /** Reads a validation finding together with its structured explanation. */
  getValidationFinding(
    findingRef: string
  ): Promise<KernelValidationFindingDetail> {
    return unwrapResponse(
      kernelClient.GET("/api/v1/kernel/validation/findings/{findingRef}", {
        params: { path: { findingRef: unversioned(findingRef) } },
      })
    );
  },
};

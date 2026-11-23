import { unwrapResponse } from "../client";
import type { KernelLockMeaning } from "./locks";
import {
  kernelClient,
  mutationHeaders,
  signedMutationHeaders,
  unversioned,
  type KernelSchemas,
} from "./shared";

export type KernelRulePack = KernelSchemas["Kernel_KernelRulePackResponse"];
export type KernelRuleSummary =
  KernelSchemas["Kernel_KernelRuleSummaryResponse"];
export type KernelStudyRulePacks =
  KernelSchemas["Kernel_KernelStudyRulePacksResponse"];
export type KernelRulePackPreview =
  KernelSchemas["Kernel_KernelRulePackPreviewResponse"];
export type KernelRulePackPreviewViolation =
  KernelSchemas["Kernel_KernelRulePackPreviewViolationResponse"];
export type KernelValidationFinding =
  KernelSchemas["Kernel_KernelValidationFindingResponse"];
export type KernelFindingDecision =
  KernelSchemas["Kernel_KernelFindingDecisionResponse"];
export type KernelValidationFindingPage =
  KernelSchemas["Kernel_KernelValidationFindingPageResponse"];
export type KernelValidationFindingDetail =
  KernelSchemas["Kernel_KernelValidationFindingDetailResponse"];
export type KernelFindingClassification =
  "DataIssue" | "MappingIssue" | "MetadataIssue" | "RuleIssue" | "NotAnIssue";

/** Gateway step-up semantic action for `POST /api/v1/kernel/library-items/{itemRef}/approve`. */
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

export const FINDING_ACCEPT_SEMANTIC_ACTION =
  "kernel.validation_finding.accept";
export const FINDING_EXPLAIN_SEMANTIC_ACTION =
  "kernel.validation_finding.explain";

/** Gateway step-up target URL for `POST /api/v1/kernel/validation/findings/{findingRef}/accept`. */
export function findingAcceptAction(findingRef: string): string {
  const clean = unversioned(findingRef);
  return `/api/v1/kernel/validation/findings/${clean}/accept`;
}

/** Gateway step-up target URL for `POST /api/v1/kernel/validation/findings/{findingRef}/explain`. */
export function findingExplainAction(findingRef: string): string {
  const clean = unversioned(findingRef);
  return `/api/v1/kernel/validation/findings/${clean}/explain`;
}

export interface KernelValidationFindingReasonInput {
  findingRef: string;
  reason: string;
  idempotencyKey: string;
}

export interface KernelClassifyValidationFindingInput {
  findingRef: string;
  classification: KernelFindingClassification;
  reason: string;
  idempotencyKey: string;
}

export interface KernelAcceptValidationFindingInput {
  findingRef: string;
  rationale: string;
  lastDetectedRunRef?: string | null;
  attestationId?: string | null;
  sigToken: string;
  reason: string;
  idempotencyKey: string;
}

export interface KernelExplainValidationFindingInput {
  findingRef: string;
  explanation: string;
  lastDetectedRunRef?: string | null;
  attestationId?: string | null;
  sigToken: string;
  reason: string;
  idempotencyKey: string;
}

function findingPath(findingRef: string) {
  return { path: { findingRef: unversioned(findingRef) } };
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
        headers: signedMutationHeaders(reason, idempotencyKey, sigToken),
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
        headers: mutationHeaders(reason, idempotencyKey),
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
        params: findingPath(findingRef),
      })
    );
  },

  /** Marks an Open validation finding as Investigating (`POST /api/v1/kernel/validation/findings/{findingRef}/investigate`). */
  investigateValidationFinding({
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
  },

  /** Classifies a detected validation finding (`POST /api/v1/kernel/validation/findings/{findingRef}/classify`). */
  classifyValidationFinding({
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
  },

  /** Accepts an Open or Investigating validation finding with a Responsibility signature (`POST /api/v1/kernel/validation/findings/{findingRef}/accept`). */
  acceptValidationFinding({
    findingRef,
    rationale,
    lastDetectedRunRef,
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
            ...(lastDetectedRunRef
              ? { last_detected_run_ref: lastDetectedRunRef }
              : {}),
            ...(attestationId ? { attestation_id: attestationId } : {}),
          },
          headers: signedMutationHeaders(reason, idempotencyKey, sigToken),
        }
      )
    );
  },

  /** Explains an Open or Investigating validation finding with an Approved signature (`POST /api/v1/kernel/validation/findings/{findingRef}/explain`). */
  explainValidationFinding({
    findingRef,
    explanation,
    lastDetectedRunRef,
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
            ...(lastDetectedRunRef
              ? { last_detected_run_ref: lastDetectedRunRef }
              : {}),
            ...(attestationId ? { attestation_id: attestationId } : {}),
          },
          headers: signedMutationHeaders(reason, idempotencyKey, sigToken),
        }
      )
    );
  },

  /** Reopens an Investigating, Accepted, or Explained validation finding (`POST /api/v1/kernel/validation/findings/{findingRef}/reopen`). */
  reopenValidationFinding({
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
  },
};

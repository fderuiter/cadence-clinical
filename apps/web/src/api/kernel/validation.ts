import { encodeChangeReason } from "../changeReason";
import { unwrapResponse } from "../client";
import type { KernelLockMeaning } from "./locks";
import { kernelClient, unversioned, type KernelSchemas } from "./shared";

export type KernelRulePack = KernelSchemas["Kernel_KernelRulePackResponse"];
export type KernelRuleSummary =
  KernelSchemas["Kernel_KernelRuleSummaryResponse"];
export type KernelStudyRulePacks =
  KernelSchemas["Kernel_KernelStudyRulePacksResponse"];
export type KernelRulePackPreview =
  KernelSchemas["Kernel_KernelRulePackPreviewResponse"];
export type KernelRulePackPreviewViolation =
  KernelSchemas["Kernel_KernelRulePackPreviewViolationResponse"];
export type KernelStudyValidationRun =
  KernelSchemas["Kernel_KernelStudyValidationRunResponse"];
export type KernelValidationFinding =
  KernelSchemas["Kernel_KernelValidationFindingResponse"];
export type KernelValidationFindingPage =
  KernelSchemas["Kernel_KernelValidationFindingPageResponse"];
export type KernelValidationFindingDetail =
  KernelSchemas["Kernel_KernelValidationFindingDetailResponse"];
export type KernelFindingClassification =
  "DataIssue" | "MappingIssue" | "MetadataIssue" | "RuleIssue" | "NotAnIssue";
export type KernelRulePackAuthority =
  "Study" | "Sponsor" | "CDISC" | "FDA" | "EMA" | "PMDA" | "NMPA";

/** Gateway step-up semantic actions for library items and validation finding decisions. */
export const RULE_PACK_APPROVE_SEMANTIC_ACTION = "kernel.library_item.approve";
export const RULE_PACK_DEPRECATE_SEMANTIC_ACTION =
  "kernel.library_item.deprecate";
export const FINDING_ACCEPT_SEMANTIC_ACTION =
  "kernel.validation_finding.accept";
export const FINDING_EXPLAIN_SEMANTIC_ACTION =
  "kernel.validation_finding.explain";

/** Gateway step-up target URL for `POST /api/v1/kernel/library-items/{itemRef}/approve`. */
export function rulePackApproveAction(itemRef: string): string {
  const clean = unversioned(itemRef);
  return `/api/v1/kernel/library-items/${clean}/approve`;
}

/** Gateway step-up target URL for `POST /api/v1/kernel/library-items/{itemRef}/deprecate`. */
export function rulePackDeprecateAction(itemRef: string): string {
  const clean = unversioned(itemRef);
  return `/api/v1/kernel/library-items/${clean}/deprecate`;
}

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

export interface KernelDraftStudyRulePackInput {
  studyRef: string;
  key: string;
  title: string;
  content: Record<string, unknown>;
  libraryItemId?: string | null;
  standardVersionRefs?: string[] | null;
  reason: string;
  idempotencyKey: string;
}

export interface KernelApproveLibraryItemRequest {
  itemRef: string;
  meaning?: KernelLockMeaning;
  attestationId?: string | null;
  sigToken: string;
  reason: string;
  idempotencyKey: string;
}

export interface KernelDeprecateLibraryItemInput {
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

export interface KernelRunStudyValidationInput {
  studyRef: string;
  asOf?: string | null;
  reason: string;
  idempotencyKey: string;
}

export interface KernelUnsignedFindingDecisionInput {
  findingRef: string;
  reason: string;
  idempotencyKey: string;
}

export interface KernelClassifyFindingInput {
  findingRef: string;
  classification: KernelFindingClassification;
  reason: string;
  idempotencyKey: string;
}

export interface KernelAcceptFindingInput {
  findingRef: string;
  rationale: string;
  lastDetectedRunRef?: string | null;
  attestationId?: string | null;
  sigToken: string;
  reason: string;
  idempotencyKey: string;
}

export interface KernelExplainFindingInput {
  findingRef: string;
  explanation: string;
  lastDetectedRunRef?: string | null;
  attestationId?: string | null;
  sigToken: string;
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

  /** Drafts or revises a governed RulePack library item in the caller's tenant (`POST /api/v1/kernel/studies/{studyRef}/rule-packs`). */
  draftStudyRulePack({
    studyRef,
    key,
    title,
    content,
    libraryItemId,
    standardVersionRefs,
    reason,
    idempotencyKey,
  }: KernelDraftStudyRulePackInput): Promise<KernelRulePack> {
    return unwrapResponse(
      kernelClient.POST("/api/v1/kernel/studies/{studyRef}/rule-packs", {
        params: { path: { studyRef: unversioned(studyRef) } },
        body: {
          key,
          title,
          content: content as KernelSchemas["Kernel_JsonElement"],
          ...(libraryItemId ? { library_item_id: libraryItemId } : {}),
          ...(standardVersionRefs
            ? { standard_version_refs: standardVersionRefs }
            : {}),
        },
        headers: {
          "X-Change-Reason": encodeChangeReason(reason),
          "Idempotency-Key": idempotencyKey,
        },
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

  /** Deprecates an approved library item (such as a RulePack) with a Responsibility step-up signature token. */
  deprecateLibraryItem({
    itemRef,
    meaning = "Responsibility",
    attestationId,
    sigToken,
    reason,
    idempotencyKey,
  }: KernelDeprecateLibraryItemInput): Promise<KernelRulePack> {
    return unwrapResponse(
      kernelClient.POST("/api/v1/kernel/library-items/{itemRef}/deprecate", {
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

  /** Executes a post-commit validation run over all collection instances of a study (`POST /api/v1/kernel/studies/{studyRef}/validation/runs`). */
  runStudyValidation({
    studyRef,
    asOf,
    reason,
    idempotencyKey,
  }: KernelRunStudyValidationInput): Promise<KernelStudyValidationRun> {
    return unwrapResponse(
      kernelClient.POST("/api/v1/kernel/studies/{studyRef}/validation/runs", {
        params: { path: { studyRef: unversioned(studyRef) } },
        body: {
          ...(asOf ? { as_of: asOf } : {}),
        },
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

  /** Marks an Open validation finding as Investigating (`POST /api/v1/kernel/validation/findings/{findingRef}/investigate`). */
  investigateValidationFinding({
    findingRef,
    reason,
    idempotencyKey,
  }: KernelUnsignedFindingDecisionInput): Promise<KernelValidationFinding> {
    return unwrapResponse(
      kernelClient.POST(
        "/api/v1/kernel/validation/findings/{findingRef}/investigate",
        {
          params: { path: { findingRef: unversioned(findingRef) } },
          headers: {
            "X-Change-Reason": encodeChangeReason(reason),
            "Idempotency-Key": idempotencyKey,
          },
        }
      )
    );
  },

  /** Classifies a non-Resolved validation finding (`POST /api/v1/kernel/validation/findings/{findingRef}/classify`). */
  classifyValidationFinding({
    findingRef,
    classification,
    reason,
    idempotencyKey,
  }: KernelClassifyFindingInput): Promise<KernelValidationFinding> {
    return unwrapResponse(
      kernelClient.POST(
        "/api/v1/kernel/validation/findings/{findingRef}/classify",
        {
          params: { path: { findingRef: unversioned(findingRef) } },
          body: {
            classification,
          },
          headers: {
            "X-Change-Reason": encodeChangeReason(reason),
            "Idempotency-Key": idempotencyKey,
          },
        }
      )
    );
  },

  /** Accepts an Open or Investigating validation finding with a step-up signature bound to FindingDecisionContent.Hash. */
  acceptValidationFinding({
    findingRef,
    rationale,
    lastDetectedRunRef,
    attestationId,
    sigToken,
    reason,
    idempotencyKey,
  }: KernelAcceptFindingInput): Promise<KernelValidationFinding> {
    return unwrapResponse(
      kernelClient.POST(
        "/api/v1/kernel/validation/findings/{findingRef}/accept",
        {
          params: { path: { findingRef: unversioned(findingRef) } },
          body: {
            rationale,
            ...(lastDetectedRunRef
              ? { last_detected_run_ref: lastDetectedRunRef }
              : {}),
            ...(attestationId ? { attestation_id: attestationId } : {}),
          },
          headers: {
            "X-Change-Reason": encodeChangeReason(reason),
            "Idempotency-Key": idempotencyKey,
            "X-Sig-Token": sigToken,
          },
        }
      )
    );
  },

  /** Explains an Open or Investigating validation finding for the reviewer guide with a step-up signature bound to FindingDecisionContent.Hash. */
  explainValidationFinding({
    findingRef,
    explanation,
    lastDetectedRunRef,
    attestationId,
    sigToken,
    reason,
    idempotencyKey,
  }: KernelExplainFindingInput): Promise<KernelValidationFinding> {
    return unwrapResponse(
      kernelClient.POST(
        "/api/v1/kernel/validation/findings/{findingRef}/explain",
        {
          params: { path: { findingRef: unversioned(findingRef) } },
          body: {
            explanation,
            ...(lastDetectedRunRef
              ? { last_detected_run_ref: lastDetectedRunRef }
              : {}),
            ...(attestationId ? { attestation_id: attestationId } : {}),
          },
          headers: {
            "X-Change-Reason": encodeChangeReason(reason),
            "Idempotency-Key": idempotencyKey,
            "X-Sig-Token": sigToken,
          },
        }
      )
    );
  },

  /** Reopens an Investigating, Accepted, or Explained validation finding (`POST /api/v1/kernel/validation/findings/{findingRef}/reopen`). */
  reopenValidationFinding({
    findingRef,
    reason,
    idempotencyKey,
  }: KernelUnsignedFindingDecisionInput): Promise<KernelValidationFinding> {
    return unwrapResponse(
      kernelClient.POST(
        "/api/v1/kernel/validation/findings/{findingRef}/reopen",
        {
          params: { path: { findingRef: unversioned(findingRef) } },
          headers: {
            "X-Change-Reason": encodeChangeReason(reason),
            "Idempotency-Key": idempotencyKey,
          },
        }
      )
    );
  },
};

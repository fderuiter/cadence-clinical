import { unwrapResponse } from "./client";
import type { KernelLockMeaning } from "./kernel/locks";
import {
  kernelClient,
  mutationHeaders,
  signedMutationHeaders,
  unversioned,
  type KernelSchemas,
} from "./kernel/shared";
import {
  FINDING_ACCEPT_SEMANTIC_ACTION,
  FINDING_EXPLAIN_SEMANTIC_ACTION,
  findingAcceptAction,
  findingExplainAction,
  type KernelAcceptValidationFindingInput,
  type KernelClassifyValidationFindingInput,
  type KernelExplainValidationFindingInput,
  type KernelFindingClassification,
  type KernelRulePack,
  type KernelValidationFinding,
  type KernelValidationFindingReasonInput,
} from "./kernel/validation";
import { kernelApi } from "./kernelApi";

export {
  FINDING_ACCEPT_SEMANTIC_ACTION,
  FINDING_EXPLAIN_SEMANTIC_ACTION,
  findingAcceptAction,
  findingExplainAction,
  type KernelFindingClassification,
};
export type KernelUnsignedFindingDecisionInput =
  KernelValidationFindingReasonInput;
export type KernelClassifyFindingInput = KernelClassifyValidationFindingInput;
export type KernelAcceptFindingInput = KernelAcceptValidationFindingInput;
export type KernelExplainFindingInput = KernelExplainValidationFindingInput;

export type KernelStudyValidationRun =
  KernelSchemas["Kernel_KernelStudyValidationRunResponse"];
export type KernelRulePackAuthority =
  "Study" | "Sponsor" | "CDISC" | "FDA" | "EMA" | "PMDA" | "NMPA";

/** Gateway step-up semantic action for library item deprecation. */
export const RULE_PACK_DEPRECATE_SEMANTIC_ACTION =
  "kernel.library_item.deprecate";

/** Gateway step-up target URL for `POST /api/v1/kernel/library-items/{itemRef}/deprecate`. */
export function rulePackDeprecateAction(itemRef: string): string {
  const clean = unversioned(itemRef);
  return `/api/v1/kernel/library-items/${clean}/deprecate`;
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

export interface KernelDeprecateLibraryItemInput {
  itemRef: string;
  meaning?: KernelLockMeaning;
  attestationId?: string | null;
  sigToken: string;
  reason: string;
  idempotencyKey: string;
}

export interface KernelRunStudyValidationInput {
  studyRef: string;
  asOf?: string | null;
  reason: string;
  idempotencyKey: string;
}

/**
 * Route-only Clinical Kernel rule-pack authoring, deprecation, validation run, and finding triage
 * mutations used by `ValidationSection.vue` (kept out of the cold-boot `kernelApi` barrel).
 */
export const kernelValidationWriteApi = {
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
        headers: mutationHeaders(reason, idempotencyKey),
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
        headers: signedMutationHeaders(reason, idempotencyKey, sigToken),
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
        body: asOf ? { as_of: asOf } : {},
        headers: mutationHeaders(reason, idempotencyKey),
      })
    );
  },

  /** Marks an Open validation finding as Investigating (`POST /api/v1/kernel/validation/findings/{findingRef}/investigate`). */
  investigateValidationFinding(
    input: KernelUnsignedFindingDecisionInput
  ): Promise<KernelValidationFinding> {
    return kernelApi.investigateValidationFinding(input);
  },

  /** Classifies a non-Resolved validation finding (`POST /api/v1/kernel/validation/findings/{findingRef}/classify`). */
  classifyValidationFinding(
    input: KernelClassifyFindingInput
  ): Promise<KernelValidationFinding> {
    return kernelApi.classifyValidationFinding(input);
  },

  /** Accepts an Open or Investigating validation finding with a step-up signature bound to FindingDecisionContent.Hash. */
  acceptValidationFinding(
    input: KernelAcceptFindingInput
  ): Promise<KernelValidationFinding> {
    return kernelApi.acceptValidationFinding(input);
  },

  /** Explains an Open or Investigating validation finding for the reviewer guide with a step-up signature bound to FindingDecisionContent.Hash. */
  explainValidationFinding(
    input: KernelExplainFindingInput
  ): Promise<KernelValidationFinding> {
    return kernelApi.explainValidationFinding(input);
  },

  /** Reopens an Investigating, Accepted, or Explained validation finding (`POST /api/v1/kernel/validation/findings/{findingRef}/reopen`). */
  reopenValidationFinding(
    input: KernelUnsignedFindingDecisionInput
  ): Promise<KernelValidationFinding> {
    return kernelApi.reopenValidationFinding(input);
  },
};

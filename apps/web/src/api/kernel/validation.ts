import type { KernelLockMeaning } from "./locks";
import { unversioned, type KernelSchemas } from "./shared";

const loadRouteTransport = () => import("./validation.transport");

export type KernelRulePack = KernelSchemas["Kernel_KernelRulePackResponse"];
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
export type KernelStudyAttention =
  KernelSchemas["Kernel_KernelStudyAttentionResponse"];
export type KernelStudySdvAlert =
  KernelSchemas["Kernel_KernelStudySdvAlertResponse"];
export type KernelFindingClassification =
  "DataIssue" | "MappingIssue" | "MetadataIssue" | "RuleIssue" | "NotAnIssue";

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
  status?: string | string[];
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
  attestationId?: string | null;
  sigToken: string;
  reason: string;
  idempotencyKey: string;
}

export interface KernelExplainValidationFindingInput {
  findingRef: string;
  explanation: string;
  attestationId?: string | null;
  sigToken: string;
  reason: string;
  idempotencyKey: string;
}

export const kernelValidationApi = {
  /** Lists edit-check rule packs in the tenant alongside whether each is pinned on the study. */
  async listStudyRulePacks(studyRef: string): Promise<KernelStudyRulePacks> {
    const { executeListStudyRulePacks } = await loadRouteTransport();
    return executeListStudyRulePacks(studyRef);
  },

  /** Dry-runs a rule pack in memory against all collection instances of a study without persisting findings. */
  async previewStudyRulePack(
    studyRef: string,
    itemRef: string
  ): Promise<KernelRulePackPreview> {
    const { executePreviewStudyRulePack } = await loadRouteTransport();
    return executePreviewStudyRulePack(studyRef, itemRef);
  },

  /** Approves a draft library item (such as a RulePack) with a step-up signature token. */
  async approveLibraryItem(
    input: KernelApproveLibraryItemRequest
  ): Promise<KernelRulePack> {
    const { executeApproveLibraryItem } = await loadRouteTransport();
    return executeApproveLibraryItem(input);
  },

  /** Pins the active approved rule packs on a study's standards profile. */
  async declareStudyRulePacks(
    input: KernelDeclareStudyRulePacksInput
  ): Promise<KernelStudyRulePacks> {
    const { executeDeclareStudyRulePacks } = await loadRouteTransport();
    return executeDeclareStudyRulePacks(input);
  },

  /** Lists validation findings recorded for a study, newest first. */
  async listStudyValidationFindings(
    studyRef: string,
    query: KernelStudyValidationFindingsQuery = {}
  ): Promise<KernelValidationFindingPage> {
    const { executeListStudyValidationFindings } = await loadRouteTransport();
    return executeListStudyValidationFindings(studyRef, query);
  },

  /** Unified study attention summary: open proposals, Open and Investigating validation findings, and active CRA SDV modification alerts. */
  async getStudyAttention(
    studyRef: string,
    query: { limit?: number } = {}
  ): Promise<KernelStudyAttention> {
    const { executeGetStudyAttention } = await loadRouteTransport();
    return executeGetStudyAttention(studyRef, query);
  },

  /** Reads a validation finding together with its structured explanation. */
  async getValidationFinding(
    findingRef: string
  ): Promise<KernelValidationFindingDetail> {
    const { executeGetValidationFinding } = await loadRouteTransport();
    return executeGetValidationFinding(findingRef);
  },

  /** Marks an Open validation finding as Investigating (`POST /api/v1/kernel/validation/findings/{findingRef}/investigate`). */
  async investigateValidationFinding(
    input: KernelValidationFindingReasonInput
  ): Promise<KernelValidationFinding> {
    const { executeInvestigateValidationFinding } = await loadRouteTransport();
    return executeInvestigateValidationFinding(input);
  },

  /** Classifies a detected validation finding (`POST /api/v1/kernel/validation/findings/{findingRef}/classify`). */
  async classifyValidationFinding(
    input: KernelClassifyValidationFindingInput
  ): Promise<KernelValidationFinding> {
    const { executeClassifyValidationFinding } = await loadRouteTransport();
    return executeClassifyValidationFinding(input);
  },

  /** Accepts an Open or Investigating validation finding with a Responsibility signature (`POST /api/v1/kernel/validation/findings/{findingRef}/accept`). */
  async acceptValidationFinding(
    input: KernelAcceptValidationFindingInput
  ): Promise<KernelValidationFinding> {
    const { executeAcceptValidationFinding } = await loadRouteTransport();
    return executeAcceptValidationFinding(input);
  },

  /** Explains an Open or Investigating validation finding with an Approved signature (`POST /api/v1/kernel/validation/findings/{findingRef}/explain`). */
  async explainValidationFinding(
    input: KernelExplainValidationFindingInput
  ): Promise<KernelValidationFinding> {
    const { executeExplainValidationFinding } = await loadRouteTransport();
    return executeExplainValidationFinding(input);
  },

  /** Reopens an Investigating, Accepted, or Explained validation finding (`POST /api/v1/kernel/validation/findings/{findingRef}/reopen`). */
  async reopenValidationFinding(
    input: KernelValidationFindingReasonInput
  ): Promise<KernelValidationFinding> {
    const { executeReopenValidationFinding } = await loadRouteTransport();
    return executeReopenValidationFinding(input);
  },
};

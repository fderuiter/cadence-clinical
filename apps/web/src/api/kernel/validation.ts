import type { KernelLockMeaning } from "./locks";
import { lazyKernelAction, unversioned, type KernelSchemas } from "./shared";

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
  listStudyRulePacks: lazyKernelAction(
    loadRouteTransport,
    "executeListStudyRulePacks"
  ),

  /** Dry-runs a rule pack in memory against all collection instances of a study without persisting findings. */
  previewStudyRulePack: lazyKernelAction(
    loadRouteTransport,
    "executePreviewStudyRulePack"
  ),

  /** Approves a draft library item (such as a RulePack) with a step-up signature token. */
  approveLibraryItem: lazyKernelAction(
    loadRouteTransport,
    "executeApproveLibraryItem"
  ),

  /** Pins the active approved rule packs on a study's standards profile. */
  declareStudyRulePacks: lazyKernelAction(
    loadRouteTransport,
    "executeDeclareStudyRulePacks"
  ),

  /** Lists validation findings recorded for a study, newest first. */
  listStudyValidationFindings: lazyKernelAction(
    loadRouteTransport,
    "executeListStudyValidationFindings"
  ),

  /** Unified study attention summary: open proposals, Open and Investigating validation findings, and active CRA SDV modification alerts. */
  getStudyAttention: lazyKernelAction(
    loadRouteTransport,
    "executeGetStudyAttention"
  ),

  /** Reads a validation finding together with its structured explanation. */
  getValidationFinding: lazyKernelAction(
    loadRouteTransport,
    "executeGetValidationFinding"
  ),

  /** Marks an Open validation finding as Investigating (`POST /api/v1/kernel/validation/findings/{findingRef}/investigate`). */
  investigateValidationFinding: lazyKernelAction(
    loadRouteTransport,
    "executeInvestigateValidationFinding"
  ),

  /** Classifies a detected validation finding (`POST /api/v1/kernel/validation/findings/{findingRef}/classify`). */
  classifyValidationFinding: lazyKernelAction(
    loadRouteTransport,
    "executeClassifyValidationFinding"
  ),

  /** Accepts an Open or Investigating validation finding with a Responsibility signature (`POST /api/v1/kernel/validation/findings/{findingRef}/accept`). */
  acceptValidationFinding: lazyKernelAction(
    loadRouteTransport,
    "executeAcceptValidationFinding"
  ),

  /** Explains an Open or Investigating validation finding with an Approved signature (`POST /api/v1/kernel/validation/findings/{findingRef}/explain`). */
  explainValidationFinding: lazyKernelAction(
    loadRouteTransport,
    "executeExplainValidationFinding"
  ),

  /** Reopens an Investigating, Accepted, or Explained validation finding (`POST /api/v1/kernel/validation/findings/{findingRef}/reopen`). */
  reopenValidationFinding: lazyKernelAction(
    loadRouteTransport,
    "executeReopenValidationFinding"
  ),
};

import {
  notifySubscriptionExpiredIfNeeded,
  SUBSCRIPTION_EXPIRED_GUIDANCE,
} from "./authorization.js";
import { encodeChangeReason } from "./changeReason";
import { createOpenApiClient, unwrapResponse } from "./client";
import type { components, paths } from "./generated/kernel";
import type { KernelObservationValue } from "./observationValue";

export type KernelOpenApiClient = ReturnType<typeof createOpenApiClient<paths>>;

/**
 * Typed client for the kernel API host, generated from docs/openapi/kernel_openapi.json (ADR-2540).
 *
 * Most calls read. `correctObservation` (ADR-2620) and `rejectProposal` (ADR-2635) send kernel commands with the
 * reason as `X-Change-Reason` and an `Idempotency-Key`, so a retried request is answered once. The host acts in the kernel tenant and as the
 * person named by the gateway-signed identity, so nothing here names a tenant or an actor. NodeRefs travel as their canonical strings
 * (`kind:id@version`). An object the reader may not see comes back masked (ADR-2471): only its `kind` and `mask`,
 * everything else null; search leaves it out. Clients render the kernel's explanations and never compose one
 * (K-EXP-1).
 */
type Schemas = components["schemas"];

export type KernelNode = Schemas["Kernel_KernelNodeResponse"];
export type KernelZoomStep = Schemas["Kernel_KernelZoomStepResponse"];
export type KernelChild = Schemas["Kernel_KernelChildResponse"];
export type KernelDecision = Schemas["Kernel_KernelDecisionResponse"];
export type KernelExplanation = Schemas["Kernel_KernelExplanationResponse"];
export type KernelCommand = Schemas["Kernel_KernelCommandResponse"];
export type KernelSearchHit = Schemas["Kernel_KernelSearchHitResponse"];
export type KernelProtocolVersion =
  Schemas["Kernel_KernelProtocolVersionResponse"];
export type KernelScheduleOfActivities =
  Schemas["Kernel_KernelScheduleOfActivitiesResponse"];
export type KernelEnrollmentSchedule =
  Schemas["Kernel_KernelEnrollmentScheduleResponse"];
export type KernelPendingReconsent =
  Schemas["Kernel_KernelPendingReconsentResponse"];
export type KernelEffectivityState =
  Schemas["Kernel_KernelEffectivityStateResponse"];
export type KernelStudySiteState =
  Schemas["Kernel_KernelStudySiteStateResponse"];
export type KernelConsentStatusState =
  Schemas["Kernel_KernelConsentStatusStateResponse"];
export type KernelSoaEpoch = Schemas["Kernel_KernelSoaEpochResponse"];
export type KernelSoaColumn = Schemas["Kernel_KernelSoaColumnResponse"];
export type KernelSoaRow = Schemas["Kernel_KernelSoaRowResponse"];
export type KernelSoaFootnote = Schemas["Kernel_KernelSoaFootnoteResponse"];
export type KernelScreenEnrollmentResult =
  Schemas["Kernel_KernelEnrollmentStateResponse"];
export type KernelLineageNode = Schemas["Kernel_KernelLineageNodeResponse"];
export type KernelLineageEdge = Schemas["Kernel_KernelLineageEdgeResponse"];
export type KernelLineage = Schemas["Kernel_KernelLineageResponse"];
export type KernelDependentGroup =
  Schemas["Kernel_KernelDependentGroupResponse"];
export type KernelDependents = Schemas["Kernel_KernelDependentsResponse"];
export type KernelAmendmentImpact =
  Schemas["Kernel_KernelAmendmentImpactResponse"];
export type KernelActivityEntry = Schemas["Kernel_KernelActivityEntryResponse"];
export type KernelActivityPage = Schemas["Kernel_KernelActivityPageResponse"];
export type KernelObservation = Schemas["Kernel_KernelObservationResponse"];
export type KernelObservationState =
  Schemas["Kernel_KernelObservationStateResponse"];
export type KernelObservationRevision =
  Schemas["Kernel_ObservationRevisionView"];
export type KernelObservationAuditEntry =
  Schemas["Kernel_ObservationAuditView"];
export type KernelProposal = Schemas["Kernel_KernelProposalResponse"];
export type KernelProposalState = Schemas["Kernel_KernelProposalStateResponse"];
export type KernelAssertionState =
  Schemas["Kernel_KernelAssertionStateResponse"];
export type KernelSimulationResult = Schemas["Kernel_SimulationResult"];
export type KernelMatrixGapFinding = Schemas["Kernel_MatrixGapFinding"];
export type KernelLock = Schemas["Kernel_KernelLockResponse"];
export type KernelStudyLocks = Schemas["Kernel_KernelStudyLocksResponse"];
export type KernelLockKind = Schemas["Kernel_LockKind"];
export type KernelLockMeaning = "Approved" | "Responsibility";
export type StudyExportFormat = "csv" | "odm_xml" | "dataset_json";
export type StudyExportLockFilter =
  "all" | "locked_only" | "frozen_or_locked" | "unlocked_only";
export type CasebookExportFormat = "json" | "odm_xml" | "html";
export type KernelStudySummary = Schemas["Kernel_KernelStudySummaryResponse"];
export type KernelOrganizationSummary =
  Schemas["Kernel_KernelOrganizationSummaryResponse"];
export type KernelProtocolTemplate =
  Schemas["Kernel_KernelProtocolTemplateResponse"];
export type KernelProtocolDraftSummary =
  Schemas["Kernel_KernelProtocolDraftSummaryResponse"];
export type KernelProtocolFormSummary =
  Schemas["Kernel_KernelProtocolFormSummaryResponse"];
export type KernelProtocolFormItem =
  Schemas["Kernel_KernelProtocolFormItemResponse"];
export type KernelProtocolImportNote =
  Schemas["Kernel_KernelProtocolImportNoteResponse"];
export type KernelImportedRulePackSummary =
  Schemas["Kernel_KernelImportedRulePackSummaryResponse"];
export type KernelRulePack = Schemas["Kernel_KernelRulePackResponse"];
export type KernelStudyRulePacks =
  Schemas["Kernel_KernelStudyRulePacksResponse"];
export type KernelRulePackPreview =
  Schemas["Kernel_KernelRulePackPreviewResponse"];
export type KernelRulePackPreviewViolation =
  Schemas["Kernel_KernelRulePackPreviewViolationResponse"];
export type KernelValidationFinding =
  Schemas["Kernel_KernelValidationFindingResponse"];
export type KernelValidationFindingPage =
  Schemas["Kernel_KernelValidationFindingPageResponse"];
export type KernelValidationFindingDetail =
  Schemas["Kernel_KernelValidationFindingDetailResponse"];
export type KernelEntitlements = Schemas["Kernel_KernelEntitlementsResponse"];
export type KernelSubscriptionScoping =
  Schemas["Kernel_KernelSubscriptionScopingResponse"];
export type KernelTrialOnboardingRequest =
  Schemas["Kernel_KernelTrialOnboardingRequest"];
export type KernelTrialOnboardingResponse =
  Schemas["Kernel_KernelTrialOnboardingResponse"];
export type KernelUpgradeSubscriptionRequest =
  Schemas["Kernel_KernelUpgradeSubscriptionRequest"];
export type KernelActivateSubscriptionRequest =
  Schemas["Kernel_KernelActivateSubscriptionRequest"];
export type { KernelObservationValue };

export interface KernelCreateStudyRequest {
  code: string;
  title: string;
  phase: string;
  sponsorRef?: string | null;
  sponsorCode?: string | null;
  sponsorName?: string | null;
  reason: string;
  idempotencyKey: string;
}

export interface KernelProtocolImportRequest {
  studyRef: string;
  file?: File | null;
  content?: string | null;
  fileName?: string | null;
  format?: string | null;
  templateIds?: string[];
  replaceForms?: boolean;
  reason: string;
  idempotencyKey: string;
}

export const ASSERTION_LOCKED = "K.Assertion.Locked";
export const ASSERTION_ALREADY_LOCKED = "K.Assertion.AlreadyLocked";
export const ASSERTION_REVISION_STALE = "K.Assertion.RevisionStale";
export const ASSERTION_ALREADY_RETRACTED = "K.Assertion.AlreadyRetracted";
export const KERNEL_AUTHORIZATION_DENIED = "K.Authorization.Denied";
export const KERNEL_SUBSCRIPTION_EXPIRED = "K.Subscription.Expired";
export const KERNEL_COMMERCIAL_SCOPING_INVALID = "K.Commercial.ScopingInvalid";
export const KERNEL_COMMERCIAL_SCOPING_CAP_EXCEEDED =
  "K.Commercial.ScopingCapExceeded";
export const KERNEL_COMMERCIAL_QUOTE_INVALID = "K.Commercial.QuoteInvalid";
export const KERNEL_COMMERCIAL_ONBOARDING_CALLER_INVALID =
  "K.Commercial.OnboardingCallerInvalid";
export const KERNEL_COMMERCIAL_PRODUCT_FAMILY_NOT_ENTITLED =
  "K.Commercial.ProductFamilyNotEntitled";
export { SUBSCRIPTION_EXPIRED_GUIDANCE };

/** True when a thrown error carries `code === "K.Subscription.Expired"`. */
export function isSubscriptionExpiredError(error: unknown): boolean {
  return assertionErrorCode(error) === KERNEL_SUBSCRIPTION_EXPIRED;
}

/** Extracts structured field validation errors (`Record<string, string[]>`) from RFC 9457 ProblemDetails. */
export function extractKernelFieldErrors(
  error: unknown
): Record<string, string[]> {
  if (!error || typeof error !== "object") return {};
  const data = (error as { data?: unknown }).data;
  if (!data || typeof data !== "object") return {};
  const rawErrors = (data as Record<string, unknown>).errors;
  if (!rawErrors || typeof rawErrors !== "object" || Array.isArray(rawErrors)) {
    return {};
  }
  const normalized: Record<string, string[]> = {};
  for (const [field, messages] of Object.entries(
    rawErrors as Record<string, unknown>
  )) {
    if (Array.isArray(messages)) {
      const clean = messages.filter(
        (msg): msg is string => typeof msg === "string" && msg.trim().length > 0
      );
      if (clean.length > 0) {
        normalized[field] = clean;
      }
    } else if (typeof messages === "string" && messages.trim().length > 0) {
      normalized[field] = [messages.trim()];
    }
  }
  return normalized;
}

/** Gateway step-up target URL for `POST /api/v1/kernel/locks`. */
export const RULE_PACK_APPROVE_SEMANTIC_ACTION = "kernel.library_item.approve";

/** Gateway step-up target URL for `POST /api/v1/kernel/library-items/{itemRef}/approve`. */
export function rulePackApproveAction(itemRef: string): string {
  const at = itemRef.indexOf("@");
  const clean = at < 0 ? itemRef : itemRef.slice(0, at);
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

export const LOCK_APPLY_ACTION = "/api/v1/kernel/locks";
export const LOCK_APPLY_SEMANTIC_ACTION = "kernel.lock.apply";
export const LOCK_RELEASE_SEMANTIC_ACTION = "kernel.lock.release";

/** Gateway step-up target URL for `POST /api/v1/kernel/locks/{lockRef}/release`. */
export function lockReleaseAction(lockRef: string): string {
  const at = lockRef.indexOf("@");
  const clean = at < 0 ? lockRef : lockRef.slice(0, at);
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

export interface KernelStreamedArtifact {
  blob: Blob;
  filename: string;
  contentType: string;
  contentSha256: string | null;
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

/** Triggers a browser file download from an in-memory streamed Blob. */
export function triggerBrowserDownload(blob: Blob, filename: string): void {
  if (
    typeof document === "undefined" ||
    typeof URL.createObjectURL !== "function"
  ) {
    return;
  }
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.download = filename;
  document.body.appendChild(link);
  link.click();
  document.body.removeChild(link);
  URL.revokeObjectURL(url);
}

/** Source data verification of one observation's current revision (`VerifyObservation`). */
export interface KernelObservationVerification {
  /** The observation's NodeRef; a version suffix is ignored. */
  observationRef: string;
  /** The enrollment the observation belongs to: the scope the verification is authorised against. */
  enrollmentRef: string;
  /** The revision the monitor verified; the kernel answers 409 (`K.Assertion.RevisionStale`) if it moved on. */
  revision: number;
  assertionId?: string | null;
  reason: string;
  /** One key per verification the user confirms, reused if that request is retried. */
  idempotencyKey: string;
}

/** Retraction (withdrawal) of an active assertion (`RetractAssertion`). */
export interface KernelAssertionRetraction {
  /** The assertion's NodeRef (`assertion:<uuid>`). */
  assertionRef: string;
  /** The enrollment the target belongs to: the scope the withdrawal is authorised against. */
  enrollmentRef: string;
  assertionId?: string | null;
  reason: string;
  /** One key per withdrawal the user confirms, reused if that request is retried. */
  idempotencyKey: string;
}

/** Extracts the kernel error code (`K.Assertion.Locked`, …) from a failed call, or null. */
export function assertionErrorCode(error: unknown): string | null {
  const data = (error as { data?: unknown } | null)?.data;
  if (!data || typeof data !== "object") return null;
  const code = (data as { code?: unknown }).code;
  return typeof code === "string" ? code : null;
}

/** Actionable user-facing explanation when a verification or withdrawal call fails. */
export function describeAssertionError(error: unknown): string {
  const code = assertionErrorCode(error);
  const status = (error as { status?: number | null } | null)?.status ?? null;
  if (code === KERNEL_SUBSCRIPTION_EXPIRED) {
    notifySubscriptionExpiredIfNeeded(
      status ?? 403,
      (error as { data?: unknown } | null)?.data
    );
    return SUBSCRIPTION_EXPIRED_GUIDANCE;
  }
  if (code === ASSERTION_LOCKED || status === 423) {
    return "This scope is locked (Locked), so verifications cannot be recorded or withdrawn until the lock is released.";
  }
  if (code === ASSERTION_ALREADY_RETRACTED) {
    return "That verification was already withdrawn; reload the form.";
  }
  if (code === ASSERTION_REVISION_STALE || status === 409) {
    return "The value changed since you loaded the form; reload the form and verify the current revision.";
  }
  if (code === KERNEL_AUTHORIZATION_DENIED || status === 403) {
    return "You are not authorised to verify or withdraw verifications on this participant.";
  }
  return error instanceof Error ? error.message : String(error);
}

/** A proposal's status as the kernel derives it: an open proposal past its expiry reads as Expired (ADR-2489). */
export type KernelProposalStatus =
  "Open" | "Approved" | "Rejected" | "Withdrawn" | "Expired";

/** A rejection of one proposal; the reason is stored as the decision reason and in the audit trail. */
export interface KernelProposalRejection {
  proposalRef: string;
  reason: string;
  /** One key per rejection the user confirms, reused if that request is retried. */
  idempotencyKey: string;
}

/** A correction of one observation's value (ADR-2620). */
export interface KernelObservationCorrection {
  /** The observation's NodeRef; a version suffix is ignored. */
  observationRef: string;
  /** The enrollment the observation belongs to: the scope the correction is authorised against. */
  enrollmentRef: string;
  /** The corrected value, of the same kind as the current one (ADR-2623). */
  value: KernelObservationValue;
  /** The version the user corrected; the kernel answers 409 if the observation has changed since. */
  expectedVersion: number;
  reason: string;
  /** One key per correction the user confirms, reused if that request is retried. */
  idempotencyKey: string;
}

/** The event families of the kernel's activity stream (spec §5.2). */
export type KernelEventFamily =
  "Knowledge" | "Execution" | "Evidence" | "Trust" | "Quality" | "Intelligence";

/** Which activity to read (ADR-2485). The whole tenant when `scope` is left out; `before` is an earlier page's `next`. */
export interface KernelActivityQuery {
  scope?: string;
  families?: KernelEventFamily[];
  actor?: string;
  from?: string;
  before?: string;
  limit?: number;
}

/** How far and along which edges a lineage read goes, and as of when (ADR-2471). The host's defaults apply to anything left out. */
export interface KernelLineageWindow {
  depth?: number;
  edgeKinds?: string[];
  validAt?: string;
  knownAt?: string;
}

export interface KernelTraceQuery extends KernelLineageWindow {
  /** `Backward` toward inputs (the default) or `Forward` toward dependents. */
  direction?: "Backward" | "Forward";
}

export interface KernelSearchQuery {
  text: string;
  kinds?: string[];
  limit?: number;
}

/** True when the reader may not see the object: show its place and `mask`, never guess at the rest. */
export function isMasked(object: { mask: string | null }): boolean {
  return object.mask !== null;
}

const client = createOpenApiClient<paths>();

function windowQuery({
  depth,
  edgeKinds,
  validAt,
  knownAt,
}: KernelLineageWindow) {
  return {
    depth,
    edge_kind: edgeKinds && edgeKinds.length > 0 ? edgeKinds : undefined,
    valid_at: validAt,
    known_at: knownAt,
  };
}

/** The NodeRef without its `@version`, as the observation routes take it. */
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

/** Extracts an actionable error string from RFC 9457 ProblemDetails (including validation/import `errors[]` or field `errors` maps). */
export function describeKernelError(error: unknown): string {
  if (assertionErrorCode(error) === KERNEL_SUBSCRIPTION_EXPIRED) {
    notifySubscriptionExpiredIfNeeded(
      (error as { status?: number | null } | null)?.status ?? 403,
      (error as { data?: unknown } | null)?.data
    );
    return SUBSCRIPTION_EXPIRED_GUIDANCE;
  }
  if (error && typeof error === "object") {
    const data = (error as { data?: unknown }).data;
    if (data && typeof data === "object") {
      const record = data as Record<string, unknown>;
      const detail =
        typeof record.detail === "string" && record.detail.trim()
          ? record.detail.trim()
          : "";
      const arrayProblems = Array.isArray(record.errors)
        ? record.errors.filter(
            (item): item is string =>
              typeof item === "string" && item.trim().length > 0
          )
        : [];
      const fieldErrors = extractKernelFieldErrors(error);
      const fieldProblems = Object.values(fieldErrors).flat();
      const problems = [...arrayProblems, ...fieldProblems];
      if (detail && problems.length > 0) {
        const extra = problems.filter((p) => p !== detail);
        return extra.length > 0 ? `${detail} (${extra.join("; ")})` : detail;
      }
      if (problems.length > 0) return problems.join("; ");
      if (detail) return detail;
    }
  }
  return error instanceof Error ? error.message : String(error);
}

export const kernelApi = {
  /** One observation with its revisions, the locks covering it, the assertions about it and its kernel audit trail (GET /api/v1/kernel/observations/{observationRef}). */
  getObservation(observationRef: string): Promise<KernelObservation> {
    return unwrapResponse(
      client.GET("/api/v1/kernel/observations/{observationRef}", {
        params: { path: { observationRef: unversioned(observationRef) } },
      })
    );
  },

  /**
   * Corrects an observation's value (POST /api/v1/kernel/observations/{observationRef}/corrections). The promise
   * resolves only when the kernel has committed the value, its revision, the audit entry and the outgoing event
   * together; it rejects with an ApiError of status 409 (stale version), 423 (locked) or 422 (no reason).
   */
  correctObservation({
    observationRef,
    enrollmentRef,
    value,
    expectedVersion,
    reason,
    idempotencyKey,
  }: KernelObservationCorrection): Promise<KernelObservationState> {
    return unwrapResponse(
      client.POST("/api/v1/kernel/observations/{observationRef}/corrections", {
        params: { path: { observationRef: unversioned(observationRef) } },
        body: {
          enrollment_ref: enrollmentRef,
          value,
          expected_version: expectedVersion,
        },
        headers: commandHeaders(reason, idempotencyKey),
      })
    );
  },

  /**
   * Verifies an observation's current revision against source (`POST /api/v1/kernel/observations/{observationRef}/verifications`).
   * Rejects with 409 (`K.Assertion.RevisionStale`), 423 (`K.Assertion.Locked`), 422 (no reason), or 403 (`K.Authorization.Denied`).
   */
  verifyObservation({
    observationRef,
    enrollmentRef,
    revision,
    assertionId,
    reason,
    idempotencyKey,
  }: KernelObservationVerification): Promise<KernelAssertionState> {
    return unwrapResponse(
      client.POST(
        "/api/v1/kernel/observations/{observationRef}/verifications",
        {
          params: { path: { observationRef: unversioned(observationRef) } },
          body: {
            enrollment_ref: enrollmentRef,
            revision,
            ...(assertionId ? { assertion_id: assertionId } : {}),
          },
          headers: commandHeaders(reason, idempotencyKey),
        }
      )
    );
  },

  /**
   * Withdraws an active verification or assertion with a mandatory reason (`POST /api/v1/kernel/assertions/{assertionRef}/retraction`).
   * Rejects with 409 (`K.Assertion.AlreadyRetracted`), 423 (`K.Assertion.Locked`), 422 (no reason), or 403 (`K.Authorization.Denied`).
   */
  retractAssertion({
    assertionRef,
    enrollmentRef,
    assertionId,
    reason,
    idempotencyKey,
  }: KernelAssertionRetraction): Promise<KernelAssertionState> {
    return unwrapResponse(
      client.POST("/api/v1/kernel/assertions/{assertionRef}/retraction", {
        params: { path: { assertionRef: unversioned(assertionRef) } },
        body: {
          enrollment_ref: enrollmentRef,
          ...(assertionId ? { assertion_id: assertionId } : {}),
        },
        headers: commandHeaders(reason, idempotencyKey),
      })
    );
  },

  /** Summary of one object at its current version (GET /api/v1/kernel/nodes/{nodeRef}). */
  getNode(nodeRef: string): Promise<KernelNode> {
    return unwrapResponse(
      client.GET("/api/v1/kernel/nodes/{nodeRef}", {
        params: { path: { nodeRef } },
      })
    );
  },

  /** Zoom path from the tenant down to the object; presentation only, it grants nothing (spec §9.3). */
  getZoomPath(nodeRef: string): Promise<KernelZoomStep[]> {
    return unwrapResponse(
      client.GET("/api/v1/kernel/nodes/{nodeRef}/path", {
        params: { path: { nodeRef } },
      })
    );
  },

  /** Zoom children of a scope node, with their own child counts. */
  getChildren(nodeRef: string): Promise<KernelChild[]> {
    return unwrapResponse(
      client.GET("/api/v1/kernel/nodes/{nodeRef}/children", {
        params: { path: { nodeRef } },
      })
    );
  },

  /** Kernel commands offered on the object, each with the reader's dry-run decision and its explanation (ADR-2483). */
  getCommands(nodeRef: string): Promise<KernelCommand[]> {
    return unwrapResponse(
      client.GET("/api/v1/kernel/nodes/{nodeRef}/commands", {
        params: { path: { nodeRef } },
      })
    );
  },

  /** Whether the reader may exercise a capability on an object now, and why (ADR-2482). Executes nothing. */
  canI(capability: string, resource: string): Promise<KernelDecision> {
    return unwrapResponse(
      client.GET("/api/v1/kernel/authorization/can-i", {
        params: { query: { capability, resource } },
      })
    );
  },

  /** Kernel objects by code or title (ADR-2484); hidden objects are left out. */
  search({
    text,
    kinds,
    limit,
  }: KernelSearchQuery): Promise<KernelSearchHit[]> {
    return unwrapResponse(
      client.GET("/api/v1/kernel/search", {
        params: {
          query: {
            q: text,
            kind: kinds && kinds.length > 0 ? kinds : undefined,
            limit,
          },
        },
      })
    );
  },

  /** Visible studies in the caller's tenant, ordered by study code (GET /api/v1/kernel/studies). */
  listStudies(): Promise<KernelStudySummary[]> {
    return unwrapResponse(client.GET("/api/v1/kernel/studies"));
  },

  /** Registered organizations in the caller's tenant, optionally filtered by kind (GET /api/v1/kernel/organizations). */
  listOrganizations(kind?: string): Promise<KernelOrganizationSummary[]> {
    return unwrapResponse(
      client.GET("/api/v1/kernel/organizations", {
        params: { query: { kind: kind || undefined } },
      })
    );
  },

  /** Registers a new study with a signed reason for change and idempotency key (POST /api/v1/kernel/studies). */
  createStudy({
    code,
    title,
    phase,
    sponsorRef,
    sponsorCode,
    sponsorName,
    reason,
    idempotencyKey,
  }: KernelCreateStudyRequest): Promise<KernelStudySummary> {
    return unwrapResponse(
      client.POST("/api/v1/kernel/studies", {
        body: {
          study_code: code,
          title,
          phase,
          sponsor_ref: sponsorRef ?? null,
          sponsor_code: sponsorCode ?? null,
          sponsor_name: sponsorName ?? null,
        },
        headers: commandHeaders(reason, idempotencyKey),
      })
    );
  },

  /** Bundled CDISC Library eCRF templates available to import into a protocol draft (GET /api/v1/kernel/protocol-templates). */
  listProtocolTemplates(): Promise<KernelProtocolTemplate[]> {
    return unwrapResponse(client.GET("/api/v1/kernel/protocol-templates"));
  },

  /** Viewable forms, ordered items, prompts, mandatory flags, and import notes for a protocol version (GET /api/v1/kernel/protocol-versions/{versionRef}/design-summary). */
  getProtocolDesignSummary(
    versionRef: string
  ): Promise<KernelProtocolDraftSummary> {
    return unwrapResponse(
      client.GET(
        "/api/v1/kernel/protocol-versions/{versionRef}/design-summary",
        {
          params: { path: { versionRef } },
        }
      )
    );
  },

  /**
   * Synchronously imports an ODM v1.3.2 XML file, USDM v4 JSON file, or bundled CDISC Library eCRF templates
   * to create or revise a study's protocol draft (POST /api/v1/kernel/studies/{studyRef}/protocol-imports).
   */
  async importProtocolDraft({
    studyRef,
    file,
    content,
    fileName,
    format,
    templateIds,
    replaceForms = false,
    reason,
    idempotencyKey,
  }: KernelProtocolImportRequest): Promise<KernelProtocolDraftSummary> {
    if (file) {
      const { uploadProtocolDraftFile } =
        await import("../utils/kernelTransfer");
      return uploadProtocolDraftFile({
        studyRef,
        file,
        content,
        fileName,
        format,
        templateIds,
        replaceForms,
        reason,
        idempotencyKey,
      });
    }

    return unwrapResponse(
      client.POST("/api/v1/kernel/studies/{studyRef}/protocol-imports", {
        params: { path: { studyRef } },
        body: {
          content: content ?? null,
          file_name: fileName ?? null,
          format: format ?? null,
          template_ids:
            templateIds && templateIds.length > 0 ? templateIds : null,
          replace_forms: replaceForms,
        },
        headers: commandHeaders(reason, idempotencyKey),
      })
    );
  },

  /** A study's protocol versions, newest first, without content. */
  listProtocolVersions(studyRef: string): Promise<KernelProtocolVersion[]> {
    return unwrapResponse(
      client.GET("/api/v1/kernel/studies/{studyRef}/protocol-versions", {
        params: { path: { studyRef } },
      })
    );
  },

  /** A protocol version's Schedule of Activities: planned activities as columns, collection specifications as rows (ADR-2475). */
  getScheduleOfActivities(
    versionRef: string
  ): Promise<KernelScheduleOfActivities> {
    return unwrapResponse(
      client.GET(
        "/api/v1/kernel/protocol-versions/{versionRef}/schedule-of-activities",
        {
          params: { path: { versionRef } },
        }
      )
    );
  },

  /**
   * An enrollment's governing protocol version, Schedule of Activities, and pending re-consent lock state at `validAt`
   * (GET /api/v1/kernel/enrollments/{enrollmentRef}/schedule-of-activities).
   */
  getEnrollmentScheduleOfActivities(
    enrollmentRef: string,
    validAt?: string
  ): Promise<KernelEnrollmentSchedule> {
    return unwrapResponse(
      client.GET(
        "/api/v1/kernel/enrollments/{enrollmentRef}/schedule-of-activities",
        {
          params: {
            path: { enrollmentRef: unversioned(enrollmentRef) },
            query: validAt ? { valid_at: validAt } : undefined,
          },
        }
      )
    );
  },

  /**
   * A study's proposals, newest first (GET /api/v1/kernel/studies/{studyRef}/proposals). A proposal the reader may
   * not see comes back masked; an affected object they may not see keeps its place with its mask (ADR-2471).
   */
  listProposals(
    studyRef: string,
    query: { status?: KernelProposalStatus; limit?: number } = {}
  ): Promise<KernelProposal[]> {
    return unwrapResponse(
      client.GET("/api/v1/kernel/studies/{studyRef}/proposals", {
        params: {
          path: { studyRef },
          query: { status: query.status, limit: query.limit },
        },
      })
    );
  },

  /** One proposal with its plan, affected objects, requested capabilities, model, content hash and decision. */
  getProposal(proposalRef: string): Promise<KernelProposal> {
    return unwrapResponse(
      client.GET("/api/v1/kernel/proposals/{proposalRef}", {
        params: { path: { proposalRef: unversioned(proposalRef) } },
      })
    );
  },

  /**
   * Rejects an open proposal with a reason (POST /api/v1/kernel/proposals/{proposalRef}/rejection). Rejects with an
   * ApiError of status 409 when the proposal is already decided or expired, 422 without a reason and 403 without
   * proposal.reject in its study.
   */
  rejectProposal({
    proposalRef,
    reason,
    idempotencyKey,
  }: KernelProposalRejection): Promise<KernelProposalState> {
    return unwrapResponse(
      client.POST("/api/v1/kernel/proposals/{proposalRef}/rejection", {
        params: { path: { proposalRef: unversioned(proposalRef) } },
        headers: commandHeaders(reason, idempotencyKey),
      })
    );
  },

  /** One protocol version, without content. */
  getProtocolVersion(versionRef: string): Promise<KernelProtocolVersion> {
    return unwrapResponse(
      client.GET("/api/v1/kernel/protocol-versions/{versionRef}", {
        params: { path: { versionRef } },
      })
    );
  },

  /** Trace: the object's lineage walk, backward to its inputs by default. Masked objects keep their place (ADR-2471). */
  trace(nodeRef: string, query: KernelTraceQuery = {}): Promise<KernelLineage> {
    return unwrapResponse(
      client.GET("/api/v1/kernel/nodes/{nodeRef}/lineage", {
        params: {
          path: { nodeRef },
          query: { direction: query.direction, ...windowQuery(query) },
        },
      })
    );
  },

  /** Impact: what depends on the object, grouped by kind; masked objects are counted like any other (Canvas D2). */
  dependents(
    nodeRef: string,
    window: KernelLineageWindow = {}
  ): Promise<KernelDependents> {
    return unwrapResponse(
      client.GET("/api/v1/kernel/nodes/{nodeRef}/dependents", {
        params: { path: { nodeRef }, query: windowQuery(window) },
      })
    );
  },

  /**
   * What happened in a scope, newest first (ADR-2485). A scope the reader may not see is refused; an entry about an
   * object the reader may not see keeps its place with its kind and `mask`, and no `subject`.
   */
  activity({
    scope,
    families,
    actor,
    from,
    before,
    limit,
  }: KernelActivityQuery = {}): Promise<KernelActivityPage> {
    return unwrapResponse(
      client.GET("/api/v1/kernel/activity", {
        params: {
          query: {
            scope,
            family: families && families.length > 0 ? families : undefined,
            actor,
            from,
            before,
            limit,
          },
        },
      })
    );
  },

  /** What depends on the version a protocol amendment changes (K-PROT-2). A first version amends nothing and is refused. */
  amendmentImpact(
    versionRef: string,
    window: Omit<KernelLineageWindow, "edgeKinds"> = {}
  ): Promise<KernelAmendmentImpact> {
    const { depth, valid_at, known_at } = windowQuery(window);
    return unwrapResponse(
      client.GET("/api/v1/kernel/protocol-versions/{versionRef}/impact", {
        params: { path: { versionRef }, query: { depth, valid_at, known_at } },
      })
    );
  },

  /** Runs evaluate-only in-memory simulation over candidate protocol version. */
  async simulate(request: {
    baseVersionRef?: string;
    storedVersionRef?: string;
    candidateContent?: string;
    validAt?: string;
    knownAt?: string;
  }): Promise<KernelSimulationResult> {
    const { performSimulate } = await import("../utils/kernelTransfer");
    return performSimulate(client, request);
  },

  /** Performs real-time schedule matrix gap check over a protocol design document. */
  async checkMatrixGaps(
    protocolContent: string
  ): Promise<KernelMatrixGapFinding[]> {
    const { performCheckMatrixGaps } = await import("../utils/kernelTransfer");
    return performCheckMatrixGaps(client, protocolContent);
  },

  /** Reads the caller's resolved kernel tenant commercial subscription tier and enabled product families. */
  entitlements(): Promise<KernelEntitlements> {
    return unwrapResponse(client.GET("/api/v1/kernel/entitlements"));
  },

  /** Provisions or idempotently replays the authenticated OIDC caller's self-service trial workspace. */
  async bootstrapTrial(
    request: KernelTrialOnboardingRequest,
    options?: { idempotencyKey?: string; reason?: string }
  ): Promise<KernelTrialOnboardingResponse> {
    const { performBootstrapTrial } = await import("../utils/kernelTransfer");
    return performBootstrapTrial(client, request, options);
  },

  /** Switches the caller's tenant commercial subscription tier in place while preserving an active trial window (POST /api/v1/kernel/entitlements/tier). */
  async upgradeSubscriptionTier(params: {
    tier: string;
    idempotencyKey: string;
    reason?: string;
  }): Promise<KernelEntitlements> {
    const { performUpgradeSubscriptionTier } =
      await import("../utils/kernelTransfer");
    return performUpgradeSubscriptionTier(client, params);
  },

  /** Activates or upgrades the caller's tenant commercial subscription with validated study scoping and billing confirmation (POST /api/v1/kernel/entitlements/activate). */
  async activateSubscription(params: {
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
  }): Promise<KernelEntitlements> {
    const { performActivateSubscription } =
      await import("../utils/kernelTransfer");
    return performActivateSubscription(client, params);
  },

  /** Lists active and historical Frozen and Locked assertions in a study, masked per caller visibility and blinding (GET /api/v1/kernel/studies/{studyRef}/locks). */
  listStudyLocks(
    studyRef: string,
    query: KernelStudyLocksQuery = {}
  ): Promise<KernelStudyLocks> {
    return unwrapResponse(
      client.GET("/api/v1/kernel/studies/{studyRef}/locks", {
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
  async applyLock(input: KernelApplyLock): Promise<KernelLock> {
    const { performApplyLock } = await import("../utils/kernelTransfer");
    return performApplyLock(client, input);
  },

  /** Releases an active Frozen or Locked assertion with step-up re-authentication and reason (POST /api/v1/kernel/locks/{lockRef}/release). */
  async releaseLock(input: KernelReleaseLock): Promise<KernelLock> {
    const { performReleaseLock } = await import("../utils/kernelTransfer");
    return performReleaseLock(client, input);
  },

  /** Streams a study clinical snapshot in CSV, CDISC ODM v1.3.2 XML, or CDISC Dataset-JSON at optional bitemporal `as_of` (GET /api/v1/kernel/studies/{studyRef}/exports). */
  async streamStudyExport(
    query: KernelStudyExportQuery
  ): Promise<KernelStreamedArtifact> {
    const { performStudyExportStream } =
      await import("../utils/kernelTransfer");
    return performStudyExportStream(query);
  },

  /** Streams a participant casebook in JSON, CDISC ODM v1.3.2 XML, or printable HTML with `X-Content-Sha256` integrity digest (GET /api/v1/kernel/enrollments/{enrollmentRef}/casebook). */
  async streamEnrollmentCasebook(
    query: KernelCasebookExportQuery
  ): Promise<KernelStreamedArtifact> {
    const { performEnrollmentCasebookStream } =
      await import("../utils/kernelTransfer");
    return performEnrollmentCasebookStream(query);
  },

  /** Lists edit-check rule packs in the tenant alongside whether each is pinned on the study. */
  listStudyRulePacks(studyRef: string): Promise<KernelStudyRulePacks> {
    return unwrapResponse(
      client.GET("/api/v1/kernel/studies/{studyRef}/rule-packs", {
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
      client.POST(
        "/api/v1/kernel/studies/{studyRef}/rule-packs/{itemRef}/preview",
        {
          params: { path: { studyRef, itemRef: unversioned(itemRef) } },
        }
      )
    );
  },

  /** Approves a draft library item (such as a RulePack) with a step-up signature token. */
  async approveLibraryItem(
    input: KernelApproveLibraryItemRequest
  ): Promise<KernelRulePack> {
    const { performApproveLibraryItem } =
      await import("../utils/kernelTransfer");
    return performApproveLibraryItem(client, input);
  },

  /** Pins the active approved rule packs on a study's standards profile. */
  async declareStudyRulePacks(
    input: KernelDeclareStudyRulePacksInput
  ): Promise<KernelStudyRulePacks> {
    const { performDeclareStudyRulePacks } =
      await import("../utils/kernelTransfer");
    return performDeclareStudyRulePacks(client, input);
  },

  /** Lists validation findings recorded for a study, newest first. */
  listStudyValidationFindings(
    studyRef: string,
    query: KernelStudyValidationFindingsQuery = {}
  ): Promise<KernelValidationFindingPage> {
    return unwrapResponse(
      client.GET("/api/v1/kernel/studies/{studyRef}/validation/findings", {
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
      })
    );
  },

  /** Reads a validation finding together with its structured explanation. */
  getValidationFinding(
    findingRef: string
  ): Promise<KernelValidationFindingDetail> {
    return unwrapResponse(
      client.GET("/api/v1/kernel/validation/findings/{findingRef}", {
        params: { path: { findingRef: unversioned(findingRef) } },
      })
    );
  },
};

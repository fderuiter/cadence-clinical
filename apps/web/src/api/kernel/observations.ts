import type { KernelObservationValue } from "../observationValue";
import {
  ASSERTION_ALREADY_RETRACTED,
  ASSERTION_ALREADY_REVIEWED,
  ASSERTION_ALREADY_VERIFIED,
  ASSERTION_LOCKED,
  ASSERTION_QUERY_OPEN,
  ASSERTION_REVISION_STALE,
  assertionErrorCode,
  describeKernelError,
  KERNEL_AUTHORIZATION_DENIED,
  KERNEL_SUBSCRIPTION_EXPIRED,
  lazyKernelAction,
  type KernelSchemas,
} from "./shared";

const loadRouteTransport = () => import("./observations.transport");
export type { KernelObservationValue };

type GeneratedKernelObservation =
  KernelSchemas["Kernel_KernelObservationResponse"];
export type KernelObservationLock = Omit<
  KernelSchemas["Kernel_LockView"],
  "masked"
> & {
  masked?: boolean;
};
export type KernelObservation = Omit<
  GeneratedKernelObservation,
  "locks" | "sdv_required" | "medical_review_required"
> & {
  locks: KernelObservationLock[];
  sdv_required?: boolean;
  medical_review_required?: boolean;
};
export type KernelObservationState =
  KernelSchemas["Kernel_KernelObservationStateResponse"];
export type KernelObservationRevision =
  KernelSchemas["Kernel_ObservationRevisionView"];
export type KernelObservationAuditEntry =
  KernelSchemas["Kernel_ObservationAuditView"];
export type KernelAssertionState =
  KernelSchemas["Kernel_KernelAssertionStateResponse"];

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

/** Medical review of one observation's current revision (`ReviewObservation`). */
export interface KernelObservationReview {
  /** The observation's NodeRef; a version suffix is ignored. */
  observationRef: string;
  /** The enrollment the observation belongs to: the scope the review is authorised against. */
  enrollmentRef: string;
  /** The revision the medical monitor reviewed; the kernel answers 409 (`K.Assertion.RevisionStale`) if it moved on. */
  revision: number;
  assertionId?: string | null;
  reason: string;
  /** One key per review the user confirms, reused if that request is retried. */
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

/** Actionable user-facing explanation when a verification, review, or withdrawal call fails. */
export function describeAssertionError(error: unknown): string {
  const code = assertionErrorCode(error);
  const status = (error as { status?: number | null } | null)?.status ?? null;
  if (code === KERNEL_SUBSCRIPTION_EXPIRED) {
    return describeKernelError(error);
  }
  if (code === ASSERTION_LOCKED || status === 423) {
    return "This scope is locked (Locked), so verifications and reviews cannot be recorded or withdrawn until the lock is released.";
  }
  if (code === ASSERTION_ALREADY_RETRACTED) {
    return "That assertion was already withdrawn; reload the form.";
  }
  if (code === ASSERTION_QUERY_OPEN) {
    return "This value has an open query; resolve and close the query before verifying or reviewing.";
  }
  if (code === ASSERTION_ALREADY_VERIFIED) {
    return "This revision is already source data verified; reload to see the current verification.";
  }
  if (code === ASSERTION_ALREADY_REVIEWED) {
    return "This revision is already medically reviewed; reload to see the current review.";
  }
  if (code === ASSERTION_REVISION_STALE || status === 409) {
    return "The value changed since you loaded the form; reload the form and verify or review the current revision.";
  }
  if (code === KERNEL_AUTHORIZATION_DENIED || status === 403) {
    return "You are not authorised to verify, review, or withdraw assertions on this participant.";
  }
  return error instanceof Error ? error.message : String(error);
}

export const kernelObservationsApi = {
  /** One observation with its revisions, the locks covering it, the assertions about it and its kernel audit trail (GET /api/v1/kernel/observations/{observationRef}). */
  getObservation: lazyKernelAction(loadRouteTransport, "executeGetObservation"),

  /**
   * Corrects an observation's value (POST /api/v1/kernel/observations/{observationRef}/corrections). The promise
   * resolves only when the kernel has committed the value, its revision, the audit entry and the outgoing event
   * together; it rejects with an ApiError of status 409 (stale version), 423 (locked) or 422 (no reason).
   */
  correctObservation: lazyKernelAction(
    loadRouteTransport,
    "executeCorrectObservation"
  ),

  /**
   * Verifies an observation's current revision against source (`POST /api/v1/kernel/observations/{observationRef}/verifications`).
   * Rejects with 409 (`K.Assertion.RevisionStale`), 423 (`K.Assertion.Locked`), 422 (no reason), or 403 (`K.Authorization.Denied`).
   */
  verifyObservation: lazyKernelAction(
    loadRouteTransport,
    "executeVerifyObservation"
  ),

  /**
   * Records a medical review assertion on an observation's current revision (`POST /api/v1/kernel/observations/{observationRef}/reviews`).
   * Rejects with 409 (`K.Assertion.RevisionStale`, `K.Assertion.QueryOpen`, `K.Assertion.AlreadyReviewed`), 423 (`K.Assertion.Locked`), 422 (no reason), or 403 (`K.Authorization.Denied`).
   */
  reviewObservation: lazyKernelAction(
    loadRouteTransport,
    "executeReviewObservation"
  ),

  /**
   * Withdraws an active verification, review, or assertion with a mandatory reason (`POST /api/v1/kernel/assertions/{assertionRef}/retraction`).
   * Rejects with 409 (`K.Assertion.AlreadyRetracted`), 423 (`K.Assertion.Locked`), 422 (no reason), or 403 (`K.Authorization.Denied`).
   */
  retractAssertion: lazyKernelAction(
    loadRouteTransport,
    "executeRetractAssertion"
  ),
};

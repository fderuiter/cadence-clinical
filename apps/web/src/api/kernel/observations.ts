import {
  notifySubscriptionExpiredIfNeeded,
  SUBSCRIPTION_EXPIRED_GUIDANCE,
} from "../authorization.js";
import { encodeChangeReason } from "../changeReason";
import { unwrapResponse } from "../client";
import type { KernelObservationValue } from "../observationValue";
import {
  ASSERTION_ALREADY_RETRACTED,
  ASSERTION_LOCKED,
  ASSERTION_REVISION_STALE,
  assertionErrorCode,
  KERNEL_AUTHORIZATION_DENIED,
  KERNEL_SUBSCRIPTION_EXPIRED,
  kernelClient,
  unversioned,
  type KernelSchemas,
} from "./shared";

export type { KernelObservationValue };

export type KernelObservation =
  KernelSchemas["Kernel_KernelObservationResponse"];
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

export const kernelObservationsApi = {
  /** One observation with its revisions, the locks covering it, the assertions about it and its kernel audit trail (GET /api/v1/kernel/observations/{observationRef}). */
  getObservation(observationRef: string): Promise<KernelObservation> {
    return unwrapResponse(
      kernelClient.GET("/api/v1/kernel/observations/{observationRef}", {
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
      kernelClient.POST(
        "/api/v1/kernel/observations/{observationRef}/corrections",
        {
          params: { path: { observationRef: unversioned(observationRef) } },
          body: {
            enrollment_ref: enrollmentRef,
            value,
            expected_version: expectedVersion,
          },
          headers: {
            "X-Change-Reason": encodeChangeReason(reason),
            "Idempotency-Key": idempotencyKey,
          },
        }
      )
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
      kernelClient.POST(
        "/api/v1/kernel/observations/{observationRef}/verifications",
        {
          params: { path: { observationRef: unversioned(observationRef) } },
          body: {
            enrollment_ref: enrollmentRef,
            revision,
            ...(assertionId ? { assertion_id: assertionId } : {}),
          },
          headers: {
            "X-Change-Reason": encodeChangeReason(reason),
            "Idempotency-Key": idempotencyKey,
          },
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
      kernelClient.POST("/api/v1/kernel/assertions/{assertionRef}/retraction", {
        params: { path: { assertionRef: unversioned(assertionRef) } },
        body: {
          enrollment_ref: enrollmentRef,
          ...(assertionId ? { assertion_id: assertionId } : {}),
        },
        headers: {
          "X-Change-Reason": encodeChangeReason(reason),
          "Idempotency-Key": idempotencyKey,
        },
      })
    );
  },
};

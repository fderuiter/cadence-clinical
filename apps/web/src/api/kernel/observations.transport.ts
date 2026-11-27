import { unwrapResponse } from "../client";
import type {
  KernelAssertionRetraction,
  KernelAssertionState,
  KernelObservation,
  KernelObservationCorrection,
  KernelObservationState,
  KernelObservationVerification,
} from "./observations";
import { kernelClient, mutationHeaders, unversioned } from "./shared";

export function executeGetObservation(
  observationRef: string
): Promise<KernelObservation> {
  return unwrapResponse(
    kernelClient.GET("/api/v1/kernel/observations/{observationRef}", {
      params: { path: { observationRef: unversioned(observationRef) } },
    })
  );
}

export function executeCorrectObservation({
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
        headers: mutationHeaders(reason, idempotencyKey),
      }
    )
  );
}

export function executeVerifyObservation({
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
        headers: mutationHeaders(reason, idempotencyKey),
      }
    )
  );
}

export function executeRetractAssertion({
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
      headers: mutationHeaders(reason, idempotencyKey),
    })
  );
}

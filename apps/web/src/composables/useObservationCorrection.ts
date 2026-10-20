import { computed, ref } from "vue";
import { ApiError } from "../api/client";
import {
  kernelApi,
  type KernelObservation,
  type KernelObservationState,
  type KernelObservationValue,
} from "../api/kernelApi";

/** What the screen tells the user after a correction attempt. */
export type CorrectionOutcome =
  | { kind: "saved"; state: KernelObservationState }
  | { kind: "stale"; message: string }
  | { kind: "locked"; message: string }
  | { kind: "failed"; message: string };

function newIdempotencyKey(): string {
  return globalThis.crypto.randomUUID();
}

function messageOf(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}

/**
 * One observation and its correction (ADR-2620).
 *
 * A correction is shown as saved only when the kernel has answered success, which means the value, its revision,
 * the reason in the audit trail and the outgoing event were committed together. Nothing is kept or shown as saved
 * in the browser before that. A stale version (409) reloads the observation so the user corrects the current
 * value; a locked observation (423) is reported and reloaded. The same idempotency key is reused if the same
 * correction is retried after a network failure, and a new key is used for the next correction.
 */
export function useObservationCorrection(observationRef: () => string) {
  const observation = ref<KernelObservation | null>(null);
  const loading = ref(false);
  const saving = ref(false);
  const loadError = ref<string | null>(null);
  const outcome = ref<CorrectionOutcome | null>(null);
  let pendingKey: {
    value: string;
    version: number;
    key: string;
  } | null = null;

  const version = computed(() => Number(observation.value?.version ?? 0));
  const locked = computed(() => observation.value?.locked != null);

  async function load(): Promise<void> {
    loading.value = true;
    loadError.value = null;
    try {
      observation.value = await kernelApi.getObservation(observationRef());
    } catch (error) {
      observation.value = null;
      loadError.value = messageOf(error);
    } finally {
      loading.value = false;
    }
  }

  function keyFor(
    value: KernelObservationValue,
    expectedVersion: number
  ): string {
    const sent = JSON.stringify(value);
    if (pendingKey?.value !== sent || pendingKey.version !== expectedVersion) {
      pendingKey = {
        value: sent,
        version: expectedVersion,
        key: newIdempotencyKey(),
      };
    }
    return pendingKey.key;
  }

  async function correct(
    value: KernelObservationValue,
    reason: string
  ): Promise<void> {
    const current = observation.value;
    if (!current || saving.value) return;
    const expectedVersion = version.value;
    saving.value = true;
    outcome.value = null;
    try {
      const state = await kernelApi.correctObservation({
        observationRef: current.ref,
        enrollmentRef: current.enrollment_ref,
        value,
        expectedVersion,
        reason,
        idempotencyKey: keyFor(value, expectedVersion),
      });
      pendingKey = null;
      outcome.value = { kind: "saved", state };
      await load();
    } catch (error) {
      const status = error instanceof ApiError ? error.status : null;
      if (status === 409) {
        pendingKey = null;
        outcome.value = {
          kind: "stale",
          message:
            "Someone changed this value after you opened it. It has been reloaded: review the current value and correct it again.",
        };
        await load();
      } else if (status === 423) {
        pendingKey = null;
        outcome.value = {
          kind: "locked",
          message: "This value is locked and can no longer be corrected.",
        };
        await load();
      } else {
        // A failed request may be retried with the same key, so the kernel commits it at most once.
        outcome.value = { kind: "failed", message: messageOf(error) };
      }
    } finally {
      saving.value = false;
    }
  }

  return {
    observation,
    loading,
    saving,
    loadError,
    outcome,
    version,
    locked,
    load,
    correct,
  };
}

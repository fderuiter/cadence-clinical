import { computed, ref, type Ref } from "vue";
import { handoffFocus } from "ui";
import { isUnrecordedValue } from "../api/observationValue";
import type {
  EcrfForm,
  EcrfFormAssertion,
  EcrfFormItem,
} from "../api/ecrfFormsApi";
import {
  ASSERTION_ALREADY_RETRACTED,
  ASSERTION_ALREADY_VERIFIED,
  ASSERTION_QUERY_OPEN,
  ASSERTION_REVISION_STALE,
  assertionErrorCode,
  describeAssertionError,
  kernelApi,
} from "../api/kernelApi";

export type PendingSdvTarget =
  | {
      mode: "verify";
      item_id: string;
      observation_id: string;
      revision: number;
    }
  | {
      mode: "retract";
      item_id: string;
      assertion_id: string;
    };

export interface SdvOutcome {
  kind: "done" | "stale" | "failed";
  message: string;
}

export interface BulkSdvItemResult {
  itemId: string;
  ok: boolean;
  message: string;
}

function matchingTarget(
  target: string | null | undefined,
  observationId: string
): boolean {
  if (!target) return false;
  const prefix = `observation:${observationId}`;
  return target === prefix || target.startsWith(`${prefix}@`);
}

export function hasRecordedValue(item: EcrfFormItem): boolean {
  return Boolean(item.observation_id) && !isUnrecordedValue(item.value);
}

/**
 * Append-only kernel source data verification (`VerifyObservation`, `RetractAssertion`)
 * for single items and multi-item batches on an eCRF form instance.
 */
export function useEcrfSdv(options: {
  form: Ref<EcrfForm | null>;
  items: Ref<EcrfFormItem[]>;
  reload: () => Promise<unknown>;
}) {
  const { form, items, reload } = options;

  const pendingSdv = ref<PendingSdvTarget | null>(null);
  const sdvReason = ref("");
  const sdvBusy = ref(false);
  const sdvOutcome = ref<SdvOutcome | null>(null);
  const selectedSdvItems = ref<string[]>([]);
  const bulkSdvReason = ref("");
  const bulkSdvResults = ref<BulkSdvItemResult[]>([]);

  function activeVerification(item: EcrfFormItem): EcrfFormAssertion | null {
    if (!hasRecordedValue(item) || !item.observation_id) return null;
    const assertions = form.value?.assertions ?? [];
    for (let i = assertions.length - 1; i >= 0; i--) {
      const a = assertions[i];
      if (
        a.kind.toLowerCase() === "verified" &&
        matchingTarget(a.target, item.observation_id) &&
        !a.retracted_at &&
        Number(a.revision) === Number(item.revision)
      ) {
        return a;
      }
    }
    return null;
  }

  function needsReverification(item: EcrfFormItem): boolean {
    if (!hasRecordedValue(item) || !item.observation_id) return false;
    if (activeVerification(item)) return false;
    return (form.value?.assertions ?? []).some(
      (a) =>
        a.kind.toLowerCase() === "verified" &&
        matchingTarget(a.target, item.observation_id as string) &&
        !a.retracted_at &&
        a.revision !== null &&
        Number(a.revision) < Number(item.revision)
    );
  }

  const unverifiedItems = computed(() =>
    items.value.filter(
      (item) => hasRecordedValue(item) && !activeVerification(item)
    )
  );

  function selectAllUnverifiedSdv() {
    selectedSdvItems.value = unverifiedItems.value.map((item) => item.item_id);
  }

  function clearSdvSelection() {
    selectedSdvItems.value = [];
    void handoffFocus([
      "#ecrf-sdv-select-all",
      "#canvas-form-workspace-title",
      "#ecrf-form-title",
    ]);
  }

  function toggleSdvItem(itemId: string, checked: boolean) {
    if (checked) {
      if (!selectedSdvItems.value.includes(itemId)) {
        selectedSdvItems.value = [...selectedSdvItems.value, itemId];
      }
    } else {
      selectedSdvItems.value = selectedSdvItems.value.filter(
        (id) => id !== itemId
      );
    }
  }

  function beginVerify(item: EcrfFormItem) {
    if (!item.observation_id || !hasRecordedValue(item)) return;
    pendingSdv.value = {
      mode: "verify",
      item_id: item.item_id,
      observation_id: item.observation_id,
      revision: Number(item.revision),
    };
    sdvReason.value = "";
    sdvOutcome.value = null;
    void handoffFocus("#ecrf-sdv-reason", { scrollIfOffscreen: true });
  }

  function beginRetract(item: EcrfFormItem, assertion: EcrfFormAssertion) {
    pendingSdv.value = {
      mode: "retract",
      item_id: item.item_id,
      assertion_id: assertion.assertion_id,
    };
    sdvReason.value = "";
    sdvOutcome.value = null;
    void handoffFocus("#ecrf-sdv-reason", { scrollIfOffscreen: true });
  }

  function cancelPendingSdv(restoreFocus = false) {
    const itemId = pendingSdv.value?.item_id ?? null;
    pendingSdv.value = null;
    if (restoreFocus && itemId) {
      void handoffFocus([
        `#ecrf-verify-${itemId}`,
        `#ecrf-unverify-${itemId}`,
        `#ecrf-item-${itemId}`,
        "#canvas-form-workspace-title",
        "#ecrf-form-title",
      ]);
    }
  }

  function setSdvReason(next: string) {
    sdvReason.value = next;
  }

  function setBulkSdvReason(next: string) {
    bulkSdvReason.value = next;
  }

  async function confirmSdv() {
    const current = pendingSdv.value;
    const text = sdvReason.value.trim();
    if (!form.value || !current || !text || sdvBusy.value) return;
    sdvBusy.value = true;
    sdvOutcome.value = null;
    bulkSdvResults.value = [];
    const enrollmentRef = `enrollment:${form.value.enrollment_id}`;
    try {
      if (current.mode === "verify") {
        await kernelApi.verifyObservation({
          observationRef: `observation:${current.observation_id}`,
          enrollmentRef,
          revision: current.revision,
          reason: text,
          idempotencyKey: globalThis.crypto.randomUUID(),
        });
        pendingSdv.value = null;
        sdvReason.value = "";
        selectedSdvItems.value = selectedSdvItems.value.filter(
          (id) => id !== current.item_id
        );
        await reload();
        sdvOutcome.value = {
          kind: "done",
          message: `Verified ${current.item_id} (revision ${current.revision}).`,
        };
        void handoffFocus([
          `#ecrf-unverify-${current.item_id}`,
          `#ecrf-verify-${current.item_id}`,
          `#ecrf-item-${current.item_id}`,
          "#canvas-form-workspace-title",
          "#ecrf-form-title",
        ]);
      } else {
        await kernelApi.retractAssertion({
          assertionRef: `assertion:${current.assertion_id}`,
          enrollmentRef,
          reason: text,
          idempotencyKey: globalThis.crypto.randomUUID(),
        });
        pendingSdv.value = null;
        sdvReason.value = "";
        await reload();
        sdvOutcome.value = {
          kind: "done",
          message: `Verification withdrawn on ${current.item_id}.`,
        };
        void handoffFocus([
          `#ecrf-verify-${current.item_id}`,
          `#ecrf-unverify-${current.item_id}`,
          `#ecrf-item-${current.item_id}`,
          "#canvas-form-workspace-title",
          "#ecrf-form-title",
        ]);
      }
    } catch (error) {
      const code = assertionErrorCode(error);
      const status =
        (error as { status?: number | null } | null)?.status ?? null;
      const isStale =
        code === ASSERTION_REVISION_STALE ||
        code === ASSERTION_ALREADY_RETRACTED ||
        code === ASSERTION_ALREADY_VERIFIED ||
        (status === 409 && code !== ASSERTION_QUERY_OPEN);
      sdvOutcome.value = {
        kind: isStale ? "stale" : "failed",
        message: describeAssertionError(error),
      };
    } finally {
      sdvBusy.value = false;
    }
  }

  const BULK_SDV_CONCURRENCY = 4;

  async function submitBulkSdv() {
    const text = bulkSdvReason.value.trim();
    if (
      !form.value ||
      !text ||
      sdvBusy.value ||
      selectedSdvItems.value.length === 0
    ) {
      return;
    }
    const selected = new Set(selectedSdvItems.value);
    const targets = unverifiedItems.value.filter(
      (i) => selected.has(i.item_id) && Boolean(i.observation_id)
    );
    if (targets.length === 0) return;

    sdvBusy.value = true;
    sdvOutcome.value = null;
    const results = new Array<BulkSdvItemResult>(targets.length);
    const enrollmentRef = `enrollment:${form.value.enrollment_id}`;
    try {
      let nextIndex = 0;
      const workerCount = Math.min(BULK_SDV_CONCURRENCY, targets.length);
      await Promise.all(
        Array.from({ length: workerCount }, async () => {
          while (nextIndex < targets.length) {
            const index = nextIndex++;
            const target = targets[index];
            try {
              await kernelApi.verifyObservation({
                observationRef: `observation:${target.observation_id}`,
                enrollmentRef,
                revision: Number(target.revision),
                reason: text,
                idempotencyKey: globalThis.crypto.randomUUID(),
              });
              results[index] = {
                itemId: target.item_id,
                ok: true,
                message: `Verified revision ${target.revision}.`,
              };
            } catch (error) {
              results[index] = {
                itemId: target.item_id,
                ok: false,
                message: describeAssertionError(error),
              };
            }
          }
        })
      );
      bulkSdvResults.value = results;
      const failed = new Set(results.filter((r) => !r.ok).map((r) => r.itemId));
      selectedSdvItems.value = selectedSdvItems.value.filter((id) =>
        failed.has(id)
      );
      if (failed.size === 0) {
        bulkSdvReason.value = "";
      }
      await reload();
      if (selectedSdvItems.value.length === 0) {
        void handoffFocus([
          "#ecrf-sdv-select-all",
          () =>
            document.querySelector<HTMLElement>(
              "#ecrf-form-page input:not([disabled])"
            ),
          () =>
            document.querySelector<HTMLElement>(
              "#ecrf-form-instance input:not([disabled])"
            ),
          "#canvas-form-workspace-title",
          "#ecrf-form-title",
        ]);
      }
    } finally {
      sdvBusy.value = false;
    }
  }

  return {
    pendingSdv,
    sdvReason,
    sdvBusy,
    sdvOutcome,
    selectedSdvItems,
    bulkSdvReason,
    bulkSdvResults,
    hasRecordedValue,
    activeVerification,
    needsReverification,
    unverifiedItems,
    selectAllUnverifiedSdv,
    clearSdvSelection,
    toggleSdvItem,
    beginVerify,
    beginRetract,
    cancelPendingSdv,
    setSdvReason,
    setBulkSdvReason,
    confirmSdv,
    submitBulkSdv,
  };
}

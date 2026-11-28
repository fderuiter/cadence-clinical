import { computed, ref, type Ref } from "vue";
import { handoffFocus } from "ui";
import { isUnrecordedValue } from "../api/observationValue";
import type {
  EcrfForm,
  EcrfFormAssertion,
  EcrfFormItem,
} from "../api/ecrfFormsApi";
import type { FormQueryResponse } from "../api/ecrfQueriesApi";
import {
  ASSERTION_ALREADY_RETRACTED,
  ASSERTION_ALREADY_REVIEWED,
  ASSERTION_ALREADY_VERIFIED,
  ASSERTION_QUERY_OPEN,
  ASSERTION_REVISION_STALE,
  assertionErrorCode,
  describeAssertionError,
  kernelApi,
} from "../api/kernelApi";
import { isLiveQuery } from "./useFormQueries";

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
    }
  | {
      mode: "review";
      item_id: string;
      observation_id: string;
      revision: number;
    }
  | {
      mode: "retract-review";
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

const OBSERVATION_TARGET_PREFIX = "observation:";

interface IndexedObservationAssertions {
  byRevision: Map<number, EcrfFormAssertion>;
  minNonNullRevision: number | null;
}

function observationTargetId(target: string | null | undefined): string | null {
  if (!target || !target.startsWith(OBSERVATION_TARGET_PREFIX)) {
    return null;
  }
  const rest = target.slice(OBSERVATION_TARGET_PREFIX.length);
  const atIndex = rest.indexOf("@");
  const id = atIndex < 0 ? rest : rest.slice(0, atIndex);
  return id.length > 0 ? id : null;
}

export function hasRecordedValue(item: EcrfFormItem): boolean {
  return Boolean(item.observation_id) && !isUnrecordedValue(item.value);
}

export function isSdvRequired(item: EcrfFormItem): boolean {
  return item.sdv_required !== false;
}

export function isMedicalReviewRequired(item: EcrfFormItem): boolean {
  return Boolean(item.medical_review_required);
}

function indexAssertionsByKind(
  assertions: EcrfFormAssertion[] | undefined,
  expectedKind: string
): Map<string, IndexedObservationAssertions> {
  const index = new Map<string, IndexedObservationAssertions>();
  if (!assertions || assertions.length === 0) return index;

  for (const a of assertions) {
    if (a.retracted_at || a.kind.toLowerCase() !== expectedKind) continue;
    const observationId = observationTargetId(a.target);
    if (!observationId) continue;

    const numericRevision = Number(a.revision);
    if (Number.isNaN(numericRevision)) continue;

    let entry = index.get(observationId);
    if (!entry) {
      entry = {
        byRevision: new Map<number, EcrfFormAssertion>(),
        minNonNullRevision: null,
      };
      index.set(observationId, entry);
    }

    entry.byRevision.set(numericRevision, a);
    if (
      a.revision !== null &&
      (entry.minNonNullRevision === null ||
        numericRevision < entry.minNonNullRevision)
    ) {
      entry.minNonNullRevision = numericRevision;
    }
  }
  return index;
}

/**
 * Append-only kernel source data verification (`VerifyObservation`), medical review
 * (`ReviewObservation`), and retraction (`RetractAssertion`) for single items and
 * multi-item SDV batches on an eCRF form instance.
 */
export function useEcrfSdv(options: {
  form: Ref<EcrfForm | null>;
  items: Ref<EcrfFormItem[]>;
  queries?: Ref<FormQueryResponse[]>;
  reload: () => Promise<unknown>;
}) {
  const { form, items, queries, reload } = options;

  const pendingSdv = ref<PendingSdvTarget | null>(null);
  const sdvReason = ref("");
  const sdvBusy = ref(false);
  const sdvOutcome = ref<SdvOutcome | null>(null);
  const selectedSdvItems = ref<string[]>([]);
  const selectedSdvItemSet = computed(() => new Set(selectedSdvItems.value));
  const bulkSdvReason = ref("");
  const bulkSdvResults = ref<BulkSdvItemResult[]>([]);

  const verifiedByObservationId = computed(() =>
    indexAssertionsByKind(form.value?.assertions, "verified")
  );

  const reviewedByObservationId = computed(() =>
    indexAssertionsByKind(form.value?.assertions, "reviewed")
  );

  const queriedTargetIndex = computed(() => {
    const observationIds = new Set<string>();
    const itemIds = new Set<string>();

    const assertions = form.value?.assertions;
    if (assertions) {
      for (const a of assertions) {
        if (a.retracted_at || a.kind.toLowerCase() !== "queried") continue;
        const obsId = observationTargetId(a.target);
        if (obsId) observationIds.add(obsId.toLowerCase());
      }
    }

    const currentFormId = form.value?.form_id;
    const loadedQueries = queries?.value;
    if (loadedQueries && loadedQueries.length > 0) {
      for (const q of loadedQueries) {
        if (currentFormId && q.form_id !== currentFormId) continue;
        if (!isLiveQuery(q)) continue;
        if (q.observation_id) {
          observationIds.add(q.observation_id.toLowerCase());
        }
        if (q.item_id) {
          itemIds.add(q.item_id);
        }
      }
    }

    return { observationIds, itemIds };
  });

  function hasOpenQuery(item: EcrfFormItem): boolean {
    const idx = queriedTargetIndex.value;
    if (
      item.observation_id &&
      idx.observationIds.has(item.observation_id.toLowerCase())
    ) {
      return true;
    }
    return idx.itemIds.has(item.item_id);
  }

  function activeVerification(item: EcrfFormItem): EcrfFormAssertion | null {
    if (!hasRecordedValue(item) || !item.observation_id) return null;
    return (
      verifiedByObservationId.value
        .get(item.observation_id)
        ?.byRevision.get(Number(item.revision)) ?? null
    );
  }

  function needsReverification(item: EcrfFormItem): boolean {
    if (!hasRecordedValue(item) || !item.observation_id) return false;
    const entry = verifiedByObservationId.value.get(item.observation_id);
    if (!entry || entry.minNonNullRevision === null) return false;
    const itemRevision = Number(item.revision);
    if (entry.byRevision.has(itemRevision)) return false;
    return entry.minNonNullRevision < itemRevision;
  }

  function activeReview(item: EcrfFormItem): EcrfFormAssertion | null {
    if (!hasRecordedValue(item) || !item.observation_id) return null;
    return (
      reviewedByObservationId.value
        .get(item.observation_id)
        ?.byRevision.get(Number(item.revision)) ?? null
    );
  }

  function needsRereview(item: EcrfFormItem): boolean {
    if (!hasRecordedValue(item) || !item.observation_id) return false;
    const entry = reviewedByObservationId.value.get(item.observation_id);
    if (!entry || entry.minNonNullRevision === null) return false;
    const itemRevision = Number(item.revision);
    if (entry.byRevision.has(itemRevision)) return false;
    return entry.minNonNullRevision < itemRevision;
  }

  function isSdvSelected(itemId: string): boolean {
    return selectedSdvItemSet.value.has(itemId);
  }

  const unverifiedItems = computed(() =>
    items.value.filter(
      (item) =>
        hasRecordedValue(item) &&
        isSdvRequired(item) &&
        !activeVerification(item) &&
        !hasOpenQuery(item)
    )
  );

  const queriedUnverifiedCount = computed(
    () =>
      items.value.filter(
        (item) =>
          hasRecordedValue(item) &&
          isSdvRequired(item) &&
          !activeVerification(item) &&
          hasOpenQuery(item)
      ).length
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
      const target = items.value.find(
        (candidate) => candidate.item_id === itemId
      );
      if (target && (!isSdvRequired(target) || hasOpenQuery(target))) {
        return;
      }
      if (!selectedSdvItemSet.value.has(itemId)) {
        selectedSdvItems.value = [...selectedSdvItems.value, itemId];
      }
    } else {
      selectedSdvItems.value = selectedSdvItems.value.filter(
        (id) => id !== itemId
      );
    }
  }

  function beginVerify(item: EcrfFormItem) {
    if (
      !item.observation_id ||
      !hasRecordedValue(item) ||
      !isSdvRequired(item) ||
      hasOpenQuery(item)
    ) {
      return;
    }
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

  function beginReview(item: EcrfFormItem) {
    if (!item.observation_id || !hasRecordedValue(item) || hasOpenQuery(item)) {
      return;
    }
    pendingSdv.value = {
      mode: "review",
      item_id: item.item_id,
      observation_id: item.observation_id,
      revision: Number(item.revision),
    };
    sdvReason.value = "";
    sdvOutcome.value = null;
    void handoffFocus("#ecrf-sdv-reason", { scrollIfOffscreen: true });
  }

  function beginRetractReview(
    item: EcrfFormItem,
    assertion: EcrfFormAssertion
  ) {
    pendingSdv.value = {
      mode: "retract-review",
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
        `#ecrf-review-${itemId}`,
        `#ecrf-unreview-${itemId}`,
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
      } else if (current.mode === "review") {
        await kernelApi.reviewObservation({
          observationRef: `observation:${current.observation_id}`,
          enrollmentRef,
          revision: current.revision,
          reason: text,
          idempotencyKey: globalThis.crypto.randomUUID(),
        });
        pendingSdv.value = null;
        sdvReason.value = "";
        await reload();
        sdvOutcome.value = {
          kind: "done",
          message: `Reviewed ${current.item_id} (revision ${current.revision}).`,
        };
        void handoffFocus([
          `#ecrf-unreview-${current.item_id}`,
          `#ecrf-review-${current.item_id}`,
          `#ecrf-item-${current.item_id}`,
          "#canvas-form-workspace-title",
          "#ecrf-form-title",
        ]);
      } else {
        const isReviewRetraction = current.mode === "retract-review";
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
          message: isReviewRetraction
            ? `Medical review withdrawn on ${current.item_id}.`
            : `Verification withdrawn on ${current.item_id}.`,
        };
        void handoffFocus([
          isReviewRetraction
            ? `#ecrf-review-${current.item_id}`
            : `#ecrf-verify-${current.item_id}`,
          isReviewRetraction
            ? `#ecrf-unreview-${current.item_id}`
            : `#ecrf-unverify-${current.item_id}`,
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
        code === ASSERTION_ALREADY_REVIEWED ||
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
    const selected = selectedSdvItemSet.value;
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
    isSdvSelected,
    bulkSdvReason,
    bulkSdvResults,
    hasRecordedValue,
    isSdvRequired,
    isMedicalReviewRequired,
    hasOpenQuery,
    activeVerification,
    needsReverification,
    activeReview,
    needsRereview,
    unverifiedItems,
    queriedUnverifiedCount,
    selectAllUnverifiedSdv,
    clearSdvSelection,
    toggleSdvItem,
    beginVerify,
    beginRetract,
    beginReview,
    beginRetractReview,
    cancelPendingSdv,
    setSdvReason,
    setBulkSdvReason,
    confirmSdv,
    submitBulkSdv,
  };
}

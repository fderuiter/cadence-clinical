import { computed, onUnmounted, ref, watch, type Ref } from "vue";
import type { KernelObservation } from "../api/kernelApi";
import {
  correctionUnit,
  isUnrecordedValue,
  parseCorrection,
  sameObservationValue,
  type ExpectedItemSpec,
} from "../api/observationValue";
import type {
  CoveringScopeLockItem,
  DirectScopeLockItem,
} from "../components/canvas/ScopeLockControls.vue";
import {
  toCoveringScopeLocks,
  toDirectScopeLocks,
} from "../canvas/kernelLocks";
import { observationNodeRef } from "../canvas/nodeRef";
import { normalizeLookupNodeRef } from "../canvas/nodeResolver";
import { useUnsavedNavigationGuard } from "./useUnsavedNavigationGuard";

const SCOPE_LABELS: Record<string, string> = {
  study: "study level",
  study_site: "site level",
  enrollment: "participant level",
  collection_instance: "form level",
  observation: "this value",
};

export type ObservationAssertionItem = KernelObservation["assertions"][number];

export interface ObservationQueryThread {
  query: ObservationAssertionItem;
  answers: ObservationAssertionItem[];
  hasActiveAnswer: boolean;
  valueChanged: boolean;
}

export function assertionReason(assertion: ObservationAssertionItem): string {
  const raw =
    assertion.reason_for_change ??
    (assertion as { reason?: string | null }).reason ??
    "";
  return typeof raw === "string" ? raw.trim() : "";
}

export function lockScopeLabel(scope: unknown): string {
  const kind = String(scope).split(":")[0];
  return SCOPE_LABELS[kind] ?? kind;
}

export function useObservationViewModel(options: {
  observation: Ref<KernelObservation | null>;
  locked: Ref<boolean>;
  canCorrect: Ref<boolean>;
  observationRef: () => string;
  embedded: () => boolean | undefined;
  onDirtyChange: (dirty: boolean) => void;
}) {
  const {
    observation,
    locked,
    canCorrect,
    observationRef,
    embedded,
    onDirtyChange,
  } = options;

  const newValue = ref("");
  const reasonOpen = ref(false);

  const normalizedConceptRef = computed(() =>
    normalizeLookupNodeRef(observation.value?.concept)
  );

  const cleanObservationRef = computed(() => {
    const raw = observation.value?.ref ?? observationRef();
    const at = raw.indexOf("@");
    return at < 0 ? raw : raw.slice(0, at);
  });

  const directObservationLocks = computed<DirectScopeLockItem[]>(() =>
    toDirectScopeLocks(
      observation.value?.locks,
      observation.value ? cleanObservationRef.value : ""
    )
  );

  const ancestorObservationLocks = computed<CoveringScopeLockItem[]>(() =>
    toCoveringScopeLocks(
      observation.value?.locks,
      observation.value ? cleanObservationRef.value : ""
    )
  );

  const observationLocks = computed(() => observation.value?.locks ?? []);
  const observationAssertions = computed<ObservationAssertionItem[]>(
    () => observation.value?.assertions ?? []
  );
  const observationRevisions = computed(
    () => observation.value?.revisions ?? []
  );
  const observationAudit = computed(() => observation.value?.audit ?? []);

  const hasRecordedValue = computed(() =>
    observation.value ? !isUnrecordedValue(observation.value.value) : false
  );

  const sdvRequired = computed(() => observation.value?.sdv_required !== false);

  const medicalReviewRequired = computed(() =>
    Boolean(observation.value?.medical_review_required)
  );

  const assertionsLocked = computed(() =>
    observationLocks.value.some(
      (held) => String(held.kind).toLowerCase() === "locked"
    )
  );

  const currentRevisionNumber = computed(() => {
    const obs = observation.value;
    if (!obs) return 1;
    const revisions = observationRevisions.value;
    const latest = revisions[revisions.length - 1];
    return Number(latest?.number ?? obs.version);
  });

  const activeVerification = computed<ObservationAssertionItem | null>(() => {
    if (!observation.value || !hasRecordedValue.value) return null;
    const assertions = observationAssertions.value;
    for (let i = assertions.length - 1; i >= 0; i--) {
      const item = assertions[i];
      if (
        String(item.kind).toLowerCase() === "verified" &&
        !item.retracted_at &&
        Number(item.revision) === currentRevisionNumber.value
      ) {
        return item;
      }
    }
    return null;
  });

  const supersededVerification = computed<ObservationAssertionItem | null>(
    () => {
      if (
        !observation.value ||
        !hasRecordedValue.value ||
        activeVerification.value
      ) {
        return null;
      }
      const assertions = observationAssertions.value;
      for (let i = assertions.length - 1; i >= 0; i--) {
        const item = assertions[i];
        if (
          String(item.kind).toLowerCase() === "verified" &&
          !item.retracted_at &&
          item.revision !== null &&
          Number(item.revision) < currentRevisionNumber.value
        ) {
          return item;
        }
      }
      return null;
    }
  );

  const activeReview = computed<ObservationAssertionItem | null>(() => {
    if (!observation.value || !hasRecordedValue.value) return null;
    const assertions = observationAssertions.value;
    for (let i = assertions.length - 1; i >= 0; i--) {
      const item = assertions[i];
      if (
        String(item.kind).toLowerCase() === "reviewed" &&
        !item.retracted_at &&
        Number(item.revision) === currentRevisionNumber.value
      ) {
        return item;
      }
    }
    return null;
  });

  const supersededReview = computed<ObservationAssertionItem | null>(() => {
    if (!observation.value || !hasRecordedValue.value || activeReview.value) {
      return null;
    }
    const assertions = observationAssertions.value;
    for (let i = assertions.length - 1; i >= 0; i--) {
      const item = assertions[i];
      if (
        String(item.kind).toLowerCase() === "reviewed" &&
        !item.retracted_at &&
        item.revision !== null &&
        Number(item.revision) < currentRevisionNumber.value
      ) {
        return item;
      }
    }
    return null;
  });

  const openQueryThreads = computed<ObservationQueryThread[]>(() => {
    if (!observation.value) return [];
    const assertions = observationAssertions.value;
    return assertions
      .filter(
        (item) =>
          String(item.kind).toLowerCase() === "queried" && !item.retracted_at
      )
      .map((query) => {
        const targetRef = `assertion:${String(query.assertion_id)}`;
        const answers = assertions.filter(
          (candidate) =>
            String(candidate.kind).toLowerCase() === "queryanswered" &&
            (String(candidate.target) === targetRef ||
              String(candidate.target) === String(query.assertion_id))
        );
        const hasActiveAnswer = answers.some((ans) => !ans.retracted_at);
        const valueChanged =
          query.revision !== null &&
          Number(query.revision) < currentRevisionNumber.value;
        return {
          query,
          answers,
          hasActiveAnswer,
          valueChanged,
        };
      });
  });

  const hasOpenQuery = computed(() => openQueryThreads.value.length > 0);

  const correctionDescribedBy = computed(() =>
    activeVerification.value
      ? "observation-sdv-edit-warning observation-new-value-hint"
      : "observation-new-value-hint"
  );

  const reasonModalDescription = computed(() =>
    activeVerification.value
      ? `This value was source data verified at revision ${currentRevisionNumber.value}. Saving this correction supersedes the verified revision, marks the item as Needs re-verification, and sends a modification alert to the CRA.`
      : undefined
  );

  const expectedSpec = computed<ExpectedItemSpec | null>(() => {
    const obs = observation.value;
    if (!obs) return null;
    return {
      valueKind: obs.expected_value_kind ?? null,
      unit: obs.expected_unit ?? null,
      codeSystemVersionId: obs.expected_code_system_version_id ?? null,
      codelist: obs.expected_codelist ?? null,
      codelistOptions: obs.expected_codelist_options ?? null,
    };
  });

  const unit = computed(() =>
    correctionUnit(observation.value?.value, expectedSpec.value)
  );

  const isTabularObservation = computed(() => {
    const valueKind =
      observation.value?.value?.type ?? observation.value?.expected_value_kind;
    return valueKind === "quantity" || valueKind === "dateTime";
  });

  const parsed = computed(() => {
    const current = observation.value?.value;
    if (!current || newValue.value.trim().length === 0) return null;
    return parseCorrection(newValue.value, current, expectedSpec.value);
  });

  const parseError = computed(() =>
    parsed.value && !parsed.value.ok ? parsed.value.message : null
  );

  const canSave = computed(
    () =>
      parsed.value?.ok === true &&
      !sameObservationValue(parsed.value.value, observation.value?.value)
  );

  const hasChangedText = computed(() => {
    const current = observation.value?.value;
    if (!current || locked.value || !canCorrect.value) return false;
    if (newValue.value.trim().length === 0) return false;
    if (parsed.value?.ok) {
      return !sameObservationValue(parsed.value.value, current);
    }
    return true;
  });

  const isDirty = computed(
    () =>
      Boolean(observation.value?.value) &&
      !locked.value &&
      canCorrect.value &&
      (hasChangedText.value || reasonOpen.value)
  );

  function discardCorrection() {
    reasonOpen.value = false;
    newValue.value = "";
  }

  watch(locked, (isLocked) => {
    if (isLocked) {
      reasonOpen.value = false;
    }
  });

  watch(isDirty, (dirty) => {
    onDirtyChange(dirty);
  });

  onUnmounted(() => {
    if (isDirty.value) {
      onDirtyChange(false);
    }
  });

  useUnsavedNavigationGuard({
    isDirty,
    guardNavigation: () => !embedded(),
    guardSignOut: true,
    dialogCopy: {
      title: "Unsaved Observation Changes",
      message:
        "You have an unsaved observation correction. Leave without saving?",
      confirmLabel: "Leave Without Saving",
      cancelLabel: "Stay on Observation",
    },
    shouldPromptOnUpdate: (to, from) => {
      const nextRaw = String(to.params.observationRef ?? "");
      const prevRaw = String(from.params.observationRef ?? "");
      const nextRef = observationNodeRef(nextRaw) ?? nextRaw;
      const prevRef = observationNodeRef(prevRaw) ?? prevRaw;
      return nextRef !== prevRef;
    },
    onConfirmedDiscard: () => {
      discardCorrection();
    },
  });

  return {
    newValue,
    reasonOpen,
    normalizedConceptRef,
    cleanObservationRef,
    directObservationLocks,
    ancestorObservationLocks,
    observationLocks,
    observationAssertions,
    observationRevisions,
    observationAudit,
    hasRecordedValue,
    sdvRequired,
    medicalReviewRequired,
    assertionsLocked,
    currentRevisionNumber,
    activeVerification,
    supersededVerification,
    activeReview,
    supersededReview,
    openQueryThreads,
    hasOpenQuery,
    correctionDescribedBy,
    reasonModalDescription,
    unit,
    isTabularObservation,
    parsed,
    parseError,
    canSave,
    isDirty,
    discardCorrection,
  };
}

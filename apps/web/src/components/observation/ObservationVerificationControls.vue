<template>
  <section
    v-if="showSection"
    id="observation-verification-controls"
    class="observation-verification-controls"
    data-testid="observation-verification-controls"
    aria-label="Source verification and medical review"
  >
    <div
      v-if="showReviewStatus"
      class="observation-review-status tabular-nums"
      data-testid="observation-review"
    >
      <span class="observation-review-label">Medical review</span>
      <template v-if="activeReview">
        <span
          class="clinical-badge clinical-badge--verified"
          data-testid="observation-review-badge"
        >
          Reviewed (rev {{ currentRevisionNumber }})
        </span>
        <span class="observation-review-meta">
          by {{ activeReview.asserted_by }} at {{ activeReview.asserted_at }}
        </span>
      </template>
      <template v-else-if="supersededReview">
        <span
          class="clinical-badge clinical-badge--warning"
          data-testid="observation-review-badge"
        >
          Needs re-review
        </span>
        <span class="observation-review-meta">
          (reviewed at rev {{ supersededReview.revision }} by
          {{ supersededReview.asserted_by }})
        </span>
      </template>
      <span
        v-else-if="medicalReviewRequired"
        class="clinical-badge clinical-badge--draft"
        data-testid="observation-review-badge"
      >
        Medical review required
      </span>
    </div>

    <div v-if="canShowActions" class="observation-assertion-actions">
      <button
        v-if="canVerify && sdvRequired && !activeVerification"
        id="observation-verify"
        type="button"
        class="btn btn-secondary"
        :disabled="busy || assertionsLocked || hasOpenQuery"
        @click="begin('verify')"
      >
        Verify
      </button>
      <button
        v-else-if="canVerify && activeVerification"
        id="observation-unverify"
        type="button"
        class="btn btn-secondary"
        :disabled="busy || assertionsLocked"
        @click="begin('retract-verify')"
      >
        Withdraw verification
      </button>

      <button
        v-if="canReview && !activeReview"
        id="observation-review"
        type="button"
        class="btn btn-secondary"
        :disabled="busy || assertionsLocked || hasOpenQuery"
        @click="begin('review')"
      >
        Review
      </button>
      <button
        v-else-if="canReview && activeReview"
        id="observation-unreview"
        type="button"
        class="btn btn-secondary"
        :disabled="busy || assertionsLocked"
        @click="begin('retract-review')"
      >
        Withdraw review
      </button>
    </div>

    <p
      v-if="canShowActions && assertionsLocked"
      class="observation-assertion-note"
      data-testid="observation-assertion-locked-note"
    >
      This scope is Locked, so verifications and reviews cannot be recorded or
      withdrawn until the lock is released.
    </p>
    <p
      v-else-if="
        canShowActions &&
        hasOpenQuery &&
        ((canVerify && sdvRequired && !activeVerification) ||
          (canReview && !activeReview))
      "
      class="observation-assertion-note"
      data-testid="observation-assertion-query-note"
    >
      Resolve and close open queries on this value before verifying or
      reviewing.
    </p>

    <form
      v-if="pendingMode"
      class="observation-assertion-form"
      @submit.prevent="confirmPending"
    >
      <label for="observation-assertion-reason">{{ pendingLabel }}</label>
      <input
        id="observation-assertion-reason"
        v-model="reason"
        type="text"
        autocomplete="off"
        :disabled="busy || assertionsLocked"
        :placeholder="pendingPlaceholder"
      />
      <button
        id="observation-assertion-confirm"
        type="submit"
        class="btn btn-primary"
        :disabled="busy || assertionsLocked || !reason.trim()"
      >
        {{ confirmLabel }}
      </button>
      <button
        id="observation-assertion-cancel"
        type="button"
        class="btn btn-secondary"
        :disabled="busy"
        @click="cancelPending(true)"
      >
        Back
      </button>
    </form>

    <div
      v-if="assertionOutcome"
      data-testid="observation-assertion-outcome"
      :class="[
        'observation-assertion-outcome',
        'tabular-nums',
        assertionOutcome.kind === 'done'
          ? 'observation-assertion-outcome--ok'
          : 'observation-assertion-outcome--failed',
      ]"
      :role="assertionOutcome.kind === 'done' ? 'status' : 'alert'"
      aria-live="polite"
    >
      <span>{{ assertionOutcome.message }}</span>
      <button
        v-if="assertionOutcome.kind === 'stale'"
        id="observation-assertion-reload"
        type="button"
        class="btn btn-secondary"
        @click="reloadAfterStale"
      >
        Reload observation
      </button>
    </div>
  </section>
</template>

<script setup lang="ts">
import { computed, ref, watch } from "vue";
import { handoffFocus } from "ui";
import {
  ASSERTION_ALREADY_RETRACTED,
  ASSERTION_ALREADY_REVIEWED,
  ASSERTION_ALREADY_VERIFIED,
  ASSERTION_QUERY_OPEN,
  ASSERTION_REVISION_STALE,
  assertionErrorCode,
  describeAssertionError,
  kernelApi,
} from "../../api/kernelApi";
import type { ObservationAssertionItem } from "../../composables/useObservationViewModel";

type PendingAssertionMode =
  "verify" | "retract-verify" | "review" | "retract-review";

const props = defineProps<{
  observationRef: string;
  enrollmentRef: string;
  currentRevisionNumber: number;
  hasRecordedValue: boolean;
  sdvRequired: boolean;
  medicalReviewRequired: boolean;
  activeVerification: ObservationAssertionItem | null;
  activeReview: ObservationAssertionItem | null;
  supersededReview: ObservationAssertionItem | null;
  hasOpenQuery: boolean;
  assertionsLocked: boolean;
  canVerify: boolean;
  canReview: boolean;
}>();

const emit = defineEmits<{
  (e: "changed"): void;
}>();

const pendingMode = ref<PendingAssertionMode | null>(null);
const reason = ref("");
const busy = ref(false);
const assertionOutcome = ref<{
  kind: "done" | "stale" | "failed";
  message: string;
} | null>(null);

const showReviewStatus = computed(
  () =>
    props.hasRecordedValue &&
    Boolean(
      props.activeReview ||
      props.supersededReview ||
      props.medicalReviewRequired
    )
);

const canShowActions = computed(
  () =>
    props.hasRecordedValue &&
    Boolean(
      (props.canVerify && (props.sdvRequired || props.activeVerification)) ||
      props.canReview
    )
);

const showSection = computed(
  () =>
    showReviewStatus.value ||
    canShowActions.value ||
    assertionOutcome.value !== null
);

const pendingLabel = computed(() => {
  switch (pendingMode.value) {
    case "verify":
      return `Reason for verifying revision ${props.currentRevisionNumber}`;
    case "retract-verify":
      return "Reason for withdrawing source verification";
    case "review":
      return `Reason for medically reviewing revision ${props.currentRevisionNumber}`;
    case "retract-review":
      return "Reason for withdrawing medical review";
    default:
      return "Reason";
  }
});

const pendingPlaceholder = computed(() => {
  switch (pendingMode.value) {
    case "verify":
      return "Verified against source medical record";
    case "review":
      return "Clinically reviewed against participant safety profile";
    default:
      return "Documented clinical justification";
  }
});

const confirmLabel = computed(() => {
  if (busy.value) return "Saving…";
  switch (pendingMode.value) {
    case "verify":
      return "Confirm verification";
    case "retract-verify":
      return "Withdraw verification";
    case "review":
      return "Confirm review";
    case "retract-review":
      return "Withdraw review";
    default:
      return "Confirm";
  }
});

watch(
  () => `${props.observationRef}@${props.currentRevisionNumber}`,
  () => {
    pendingMode.value = null;
    reason.value = "";
  }
);

function begin(mode: PendingAssertionMode) {
  pendingMode.value = mode;
  reason.value = "";
  assertionOutcome.value = null;
  void handoffFocus("#observation-assertion-reason", {
    scrollIfOffscreen: true,
  });
}

function cancelPending(restoreFocus = false) {
  const previous = pendingMode.value;
  pendingMode.value = null;
  reason.value = "";
  if (restoreFocus) {
    void handoffFocus([
      previous === "verify" || previous === "retract-verify"
        ? "#observation-verify"
        : "#observation-review",
      "#observation-unverify",
      "#observation-unreview",
      "#observation-view-title",
    ]);
  }
}

function reloadAfterStale() {
  pendingMode.value = null;
  assertionOutcome.value = null;
  emit("changed");
}

async function confirmPending() {
  const mode = pendingMode.value;
  const text = reason.value.trim();
  if (!mode || !text || busy.value) return;

  busy.value = true;
  assertionOutcome.value = null;
  try {
    if (mode === "verify") {
      await kernelApi.verifyObservation({
        observationRef: props.observationRef,
        enrollmentRef: props.enrollmentRef,
        revision: props.currentRevisionNumber,
        reason: text,
        idempotencyKey: globalThis.crypto.randomUUID(),
      });
      pendingMode.value = null;
      reason.value = "";
      emit("changed");
      assertionOutcome.value = {
        kind: "done",
        message: `Verified revision ${props.currentRevisionNumber}.`,
      };
      void handoffFocus(["#observation-unverify", "#observation-view-title"]);
    } else if (mode === "review") {
      await kernelApi.reviewObservation({
        observationRef: props.observationRef,
        enrollmentRef: props.enrollmentRef,
        revision: props.currentRevisionNumber,
        reason: text,
        idempotencyKey: globalThis.crypto.randomUUID(),
      });
      pendingMode.value = null;
      reason.value = "";
      emit("changed");
      assertionOutcome.value = {
        kind: "done",
        message: `Reviewed revision ${props.currentRevisionNumber}.`,
      };
      void handoffFocus(["#observation-unreview", "#observation-view-title"]);
    } else {
      const targetAssertion =
        mode === "retract-verify"
          ? props.activeVerification
          : props.activeReview;
      if (!targetAssertion?.assertion_id) {
        pendingMode.value = null;
        return;
      }
      await kernelApi.retractAssertion({
        assertionRef: `assertion:${String(targetAssertion.assertion_id)}`,
        enrollmentRef: props.enrollmentRef,
        reason: text,
        idempotencyKey: globalThis.crypto.randomUUID(),
      });
      pendingMode.value = null;
      reason.value = "";
      emit("changed");
      assertionOutcome.value = {
        kind: "done",
        message:
          mode === "retract-verify"
            ? "Source verification withdrawn."
            : "Medical review withdrawn.",
      };
      void handoffFocus([
        mode === "retract-verify"
          ? "#observation-verify"
          : "#observation-review",
        "#observation-view-title",
      ]);
    }
  } catch (error) {
    const code = assertionErrorCode(error);
    const status = (error as { status?: number | null } | null)?.status ?? null;
    const isStale =
      code === ASSERTION_REVISION_STALE ||
      code === ASSERTION_ALREADY_RETRACTED ||
      code === ASSERTION_ALREADY_VERIFIED ||
      code === ASSERTION_ALREADY_REVIEWED ||
      (status === 409 && code !== ASSERTION_QUERY_OPEN);
    assertionOutcome.value = {
      kind: isStale ? "stale" : "failed",
      message: describeAssertionError(error),
    };
  } finally {
    busy.value = false;
  }
}
</script>

<style scoped>
.observation-verification-controls {
  display: flex;
  flex-direction: column;
  gap: 8px;
  margin: 12px 0;
  padding: 10px 12px;
  border: 1px solid var(--semantic-color-border-muted);
  border-radius: 6px;
  background: var(--semantic-color-surface-muted);
  min-width: 0;
}
.observation-review-status,
.observation-assertion-actions,
.observation-assertion-form {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
}
.observation-review-status {
  font-size: 0.85rem;
}
.observation-review-label {
  font-weight: 600;
  color: var(--semantic-color-text-dark);
}
.observation-review-meta {
  color: var(--semantic-color-text-secondary);
}
.observation-assertion-note {
  margin: 0;
  font-size: 0.82rem;
  color: var(--semantic-color-text-secondary);
}
.observation-assertion-form input {
  flex: 1 1 220px;
  min-width: 0;
  padding: 6px 10px;
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 4px;
  background: var(--semantic-color-surface);
  color: var(--semantic-color-text);
}
.observation-assertion-outcome {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
  padding: 6px 10px;
  border-radius: 4px;
  font-size: 0.85rem;
}
.observation-assertion-outcome--ok {
  background: var(--semantic-color-success-bg-soft);
  color: var(--semantic-color-success-dark);
}
.observation-assertion-outcome--failed {
  background: var(--semantic-color-error-bg-soft);
  color: var(--semantic-color-error-text-dark);
}
@media (pointer: coarse), (max-width: 1024px) {
  .observation-assertion-form input {
    font-size: 1rem;
  }
}
</style>

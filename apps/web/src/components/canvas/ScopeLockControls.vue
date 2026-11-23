<template>
  <div
    v-if="showControls"
    :id="`${prefix}-panel`"
    class="scope-lock-controls"
    :data-testid="`${prefix}-controls`"
  >
    <div class="scope-lock-row">
      <label :for="`${prefix}-reason`" class="scope-lock-label">
        Reason for {{ scopeLabel }} lock change
      </label>
      <input
        :id="`${prefix}-reason`"
        v-model="reason"
        type="text"
        class="scope-lock-input"
        autocomplete="off"
        placeholder="Source review complete / addendum reopened"
        :disabled="busy"
      />

      <button
        v-if="canFreeze"
        :id="`${prefix}-freeze`"
        type="button"
        class="btn btn-secondary"
        :disabled="busy || !reason.trim()"
        @click="openApply('Frozen')"
      >
        Freeze {{ scopeLabel }}
      </button>

      <button
        v-if="canLock"
        :id="`${prefix}-lock`"
        type="button"
        class="btn btn-primary"
        :disabled="busy || !reason.trim() || isLockBlockedByReadiness"
        @click="openApply('Locked')"
      >
        Lock {{ scopeLabel }}
      </button>

      <button
        v-for="held in releasableLocks"
        :key="held.lockRef"
        :data-testid="`${prefix}-unlock-${held.kind.toLowerCase()}`"
        type="button"
        class="btn btn-secondary"
        :disabled="busy || !reason.trim()"
        @click="openRelease(held.lockRef, held.kind)"
      >
        Unlock {{ scopeLabel }} ({{ held.kind }})
      </button>
    </div>

    <div
      v-if="canApply && !hasDirectLocked && readiness"
      :data-testid="`${prefix}-readiness`"
      :class="[
        'scope-lock-readiness',
        readiness.ready_for_lock
          ? 'scope-lock-readiness--ready'
          : 'scope-lock-readiness--blocked',
      ]"
    >
      <span :data-testid="`${prefix}-readiness-summary`">
        <template v-if="readiness.ready_for_lock">
          Ready for Locked (0 open queries · 0 missing required items).
        </template>
        <template v-else>
          Locked blocked: {{ readiness.open_query_count }} open
          {{ readiness.open_query_count === 1 ? "query" : "queries" }},
          {{ readiness.missing_required_item_count }} missing required
          {{ readiness.missing_required_item_count === 1 ? "item" : "items"
          }}<template v-if="Number(readiness.open_error_finding_count ?? 0) > 0"
            >, {{ readiness.open_error_finding_count }} error
            {{
              Number(readiness.open_error_finding_count) === 1
                ? "finding"
                : "findings"
            }}</template
          >
          (Freeze {{ scopeLabel }} remains available).
        </template>
      </span>
      <ul
        v-if="!readiness.ready_for_lock && readiness.blockers.length > 0"
        class="scope-lock-blocker-list"
        :data-testid="`${prefix}-readiness-blockers`"
      >
        <li
          v-for="(blocker, idx) in readiness.blockers"
          :key="`${blocker.kind}-${blocker.target_ref ?? idx}`"
        >
          <template v-if="blocker.masked">
            Masked ({{ blocker.mask ?? "Restricted" }})
            <span v-if="blocker.next_action">— {{ blocker.next_action }}</span>
          </template>
          <template v-else>
            <strong v-if="blocker.owner">{{ blocker.owner }}:</strong>
            <NodeRefChip
              v-if="blocker.target_ref"
              :node-ref="blocker.target_ref"
              include-kind
            />
            <span v-if="formatLockBlockerDetail(blocker)">{{
              formatLockBlockerDetail(blocker)
            }}</span>
            <span v-if="blocker.next_action">— {{ blocker.next_action }}</span>
          </template>
        </li>
      </ul>
    </div>

    <p
      v-if="outcome"
      :data-testid="`${prefix}-outcome`"
      :class="[
        'scope-lock-outcome',
        outcome.kind === 'ok'
          ? 'scope-lock-outcome--ok'
          : 'scope-lock-outcome--error',
      ]"
      :role="outcome.kind === 'ok' ? 'status' : 'alert'"
      aria-live="polite"
    >
      {{ outcome.message }}
    </p>

    <SignatureCaptureModal
      v-if="modalOp"
      v-bind="signatureModalProps"
      @cancel="closeModal"
      @success="closeModal"
    />
  </div>
</template>

<script setup lang="ts">
import { computed, ref, watch } from "vue";
import {
  formatLockBlockerDetail,
  type KernelLock,
  type KernelLockKind,
} from "../../api/kernelApi";
import type {
  CoveringScopeLockItem,
  DirectScopeLockItem,
} from "../../canvas/kernelLocks";
import { useSignedKernelLock } from "../../composables/useSignedKernelLock";
import SignatureCaptureModal from "../SignatureCaptureModal.vue";
import NodeRefChip from "./NodeRefChip.vue";

export type {
  CoveringScopeLockItem,
  DirectScopeLockItem,
} from "../../canvas/kernelLocks";

const props = withDefaults(
  defineProps<{
    studyRef: string | null;
    scopeRef: string;
    scopeLabel: string;
    directLocks?: DirectScopeLockItem[];
    coveringLocks?: CoveringScopeLockItem[];
    idPrefix?: string;
  }>(),
  {
    directLocks: () => [],
    coveringLocks: () => [],
    idPrefix: "scope-lock",
  }
);

const emit = defineEmits<{
  changed: [lock: KernelLock];
}>();

const prefix = computed(() => props.idPrefix);
const reason = ref("");

function handleLockChanged(updated: KernelLock) {
  reason.value = "";
  emit("changed", updated);
}

const {
  canApply,
  canRelease,
  readiness,
  isLockBlockedByReadiness,
  busy,
  outcome,
  modalOp,
  signatureModalProps,
  reloadReadiness,
  openApply: openSignedApply,
  openRelease: openSignedRelease,
  closeModal,
} = useSignedKernelLock({
  studyRef: () => props.studyRef,
  scopeRef: () => props.scopeRef,
  onApplied: handleLockChanged,
  onReleased: handleLockChanged,
});

const hasDirectLocked = computed(() =>
  props.directLocks.some((l) => l.kind === "Locked")
);
const hasDirectFrozenOrLocked = computed(() =>
  props.directLocks.some((l) => l.kind === "Frozen" || l.kind === "Locked")
);
const coveredByLocked = computed(() =>
  props.coveringLocks.some((l) => l.kind === "Locked")
);
const coveredByFrozenOrLocked = computed(() =>
  props.coveringLocks.some((l) => l.kind === "Frozen" || l.kind === "Locked")
);
const canFreeze = computed(
  () =>
    canApply.value &&
    !hasDirectFrozenOrLocked.value &&
    !coveredByFrozenOrLocked.value
);
const canLock = computed(
  () => canApply.value && !hasDirectLocked.value && !coveredByLocked.value
);
const releasableLocks = computed(() =>
  canRelease.value ? props.directLocks : []
);

const showControls = computed(
  () =>
    canFreeze.value ||
    canLock.value ||
    releasableLocks.value.length > 0 ||
    outcome.value !== null
);

watch(
  () => props.directLocks,
  () => {
    void reloadReadiness();
  }
);

function openApply(lockKind: KernelLockKind) {
  openSignedApply({
    scopeRef: props.scopeRef,
    kind: lockKind,
    reason: reason.value,
    targetLabel: () => props.scopeLabel,
  });
}

function openRelease(lockRef: string, lockKind: string) {
  openSignedRelease({
    lockRef,
    lockKind,
    reason: reason.value,
    targetLabel: () => props.scopeLabel,
  });
}
</script>

<style scoped>
.scope-lock-controls {
  margin-top: 8px;
  padding: 10px 12px;
  border: 1px solid var(--semantic-color-border-muted);
  border-radius: 6px;
  background: var(--semantic-color-surface-muted);
}

.scope-lock-row {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
}

.scope-lock-label {
  font-size: 0.85rem;
  font-weight: 600;
  color: var(--semantic-color-text-dark);
}

.scope-lock-input {
  flex: 1 1 220px;
  min-width: 180px;
  padding: 6px 10px;
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 4px;
  font-size: 0.85rem;
  color: var(--semantic-color-text);
  background: var(--semantic-color-surface);
}

.scope-lock-readiness {
  margin: 8px 0 0;
  padding: 6px 10px;
  border-radius: 4px;
  font-size: 0.82rem;
}

.scope-lock-readiness--ready {
  background: var(--semantic-color-success-bg);
  color: var(--semantic-color-success-dark);
}

.scope-lock-readiness--blocked {
  background: var(--semantic-color-error-bg);
  color: var(--semantic-color-error-dark);
}

.scope-lock-blocker-list {
  margin: 6px 0 0;
  padding-left: 18px;
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.scope-lock-outcome {
  margin: 8px 0 0;
  font-size: 0.85rem;
  padding: 6px 10px;
  border-radius: 4px;
}

.scope-lock-outcome--ok {
  background: var(--semantic-color-success-bg);
  color: var(--semantic-color-success-dark);
}

.scope-lock-outcome--error {
  background: var(--semantic-color-error-bg);
  color: var(--semantic-color-error-dark);
}
</style>

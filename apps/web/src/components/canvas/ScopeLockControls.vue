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
        v-if="canApply && !hasDirectFrozenOrLocked"
        :id="`${prefix}-freeze`"
        type="button"
        class="btn btn-secondary"
        :disabled="busy || !reason.trim()"
        @click="openApply('Frozen')"
      >
        Freeze {{ scopeLabel }}
      </button>

      <button
        v-if="canApply && !hasDirectLocked"
        :id="`${prefix}-lock`"
        type="button"
        class="btn btn-primary"
        :disabled="busy || !reason.trim()"
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
import { computed, ref } from "vue";
import type { KernelLock, KernelLockKind } from "../../api/kernelApi";
import type { DirectScopeLockItem } from "../../canvas/kernelLocks";
import { useSignedKernelLock } from "../../composables/useSignedKernelLock";
import SignatureCaptureModal from "../SignatureCaptureModal.vue";

export type { DirectScopeLockItem } from "../../canvas/kernelLocks";

const props = withDefaults(
  defineProps<{
    studyRef: string | null;
    scopeRef: string;
    scopeLabel: string;
    directLocks?: DirectScopeLockItem[];
    idPrefix?: string;
  }>(),
  {
    directLocks: () => [],
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
  busy,
  outcome,
  modalOp,
  signatureModalProps,
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
const releasableLocks = computed(() =>
  canRelease.value ? props.directLocks : []
);

const showControls = computed(
  () =>
    canApply.value || releasableLocks.value.length > 0 || outcome.value !== null
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

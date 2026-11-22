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
      :is-open="Boolean(modalOp)"
      :username="signerUsername"
      :signer-name="signerFullName"
      signer-role="Data Manager"
      :reasons="LOCK_SIGNING_REASONS"
      :action-url="modalActionUrl"
      :semantic-action="modalSemanticAction"
      :on-sign="executeSignedLock"
      @cancel="modalOp = null"
      @success="modalOp = null"
    />
  </div>
</template>

<script setup lang="ts">
import { computed, ref, watch } from "vue";
import {
  describeLockError,
  kernelApi,
  LOCK_APPLY_ACTION,
  LOCK_APPLY_SEMANTIC_ACTION,
  LOCK_RELEASE_SEMANTIC_ACTION,
  lockReleaseAction,
  type KernelLock,
  type KernelLockKind,
  type KernelLockMeaning,
} from "../../api/kernelApi";
import { useAuthStore } from "../../stores/auth.js";
import SignatureCaptureModal from "../SignatureCaptureModal.vue";

export interface DirectScopeLockItem {
  lockRef: string;
  kind: string;
}

const LOCK_SIGNING_REASONS = ["Approved", "Responsibility"];

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

const authStore = useAuthStore();
const prefix = computed(() => props.idPrefix);

const canApply = ref(false);
const canRelease = ref(false);
const reason = ref("");
const busy = ref(false);
const outcome = ref<{ kind: "ok" | "error"; message: string } | null>(null);

type ModalOperation =
  | {
      mode: "apply";
      lockKind: KernelLockKind;
      idempotencyKey: string;
      lockId: string;
      attestationId: string;
    }
  | {
      mode: "release";
      lockRef: string;
      lockKind: string;
      idempotencyKey: string;
      assertionId: string;
      attestationId: string;
    };

const modalOp = ref<ModalOperation | null>(null);

const signerUsername = computed(
  () => authStore?.user?.email || authStore?.user?.username || "data-manager"
);
const signerFullName = computed(() => {
  const user = authStore?.user as
    | (typeof authStore.user & { name?: string; full_name?: string })
    | null
    | undefined;
  const joined = [user?.firstName, user?.lastName].filter(Boolean).join(" ");
  return user?.name || user?.full_name || joined || signerUsername.value;
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

const modalActionUrl = computed(() => {
  if (!modalOp.value) return LOCK_APPLY_ACTION;
  return modalOp.value.mode === "apply"
    ? LOCK_APPLY_ACTION
    : lockReleaseAction(modalOp.value.lockRef);
});

const modalSemanticAction = computed(() =>
  modalOp.value?.mode === "release"
    ? LOCK_RELEASE_SEMANTIC_ACTION
    : LOCK_APPLY_SEMANTIC_ACTION
);

let authProbeSeq = 0;

watch(
  [() => props.studyRef, () => props.scopeRef],
  async ([studyRef]) => {
    const seq = ++authProbeSeq;
    if (!studyRef || typeof kernelApi.canI !== "function") {
      canApply.value = false;
      canRelease.value = false;
      return;
    }
    try {
      const [applyDec, releaseDec] = await Promise.all([
        kernelApi.canI("lock.apply", studyRef),
        kernelApi.canI("lock.release", studyRef),
      ]);
      if (seq !== authProbeSeq) return;
      canApply.value = Boolean(applyDec?.permitted);
      canRelease.value = Boolean(releaseDec?.permitted);
    } catch {
      if (seq !== authProbeSeq) return;
      canApply.value = false;
      canRelease.value = false;
    }
  },
  { immediate: true }
);

function openApply(lockKind: KernelLockKind) {
  if (!reason.value.trim() || !props.studyRef) return;
  outcome.value = null;
  modalOp.value = {
    mode: "apply",
    lockKind,
    idempotencyKey: globalThis.crypto.randomUUID(),
    lockId: globalThis.crypto.randomUUID(),
    attestationId: globalThis.crypto.randomUUID(),
  };
}

function openRelease(lockRef: string, lockKind: string) {
  if (!reason.value.trim() || !props.studyRef) return;
  outcome.value = null;
  modalOp.value = {
    mode: "release",
    lockRef,
    lockKind,
    idempotencyKey: globalThis.crypto.randomUUID(),
    assertionId: globalThis.crypto.randomUUID(),
    attestationId: globalThis.crypto.randomUUID(),
  };
}

async function executeSignedLock(sigToken: string, signingReason: string) {
  const op = modalOp.value;
  if (!op || !props.studyRef) return { kind: "signed" as const };
  const meaning: KernelLockMeaning =
    signingReason === "Responsibility" ? "Responsibility" : "Approved";
  busy.value = true;
  try {
    let updated: KernelLock;
    if (op.mode === "apply") {
      updated = await kernelApi.applyLock({
        studyRef: props.studyRef,
        scopeRef: props.scopeRef,
        kind: op.lockKind,
        meaning,
        lockId: op.lockId,
        attestationId: op.attestationId,
        sigToken,
        reason: reason.value.trim(),
        idempotencyKey: op.idempotencyKey,
      });
      outcome.value = {
        kind: "ok",
        message: `${op.lockKind} applied to ${props.scopeLabel}.`,
      };
    } else {
      updated = await kernelApi.releaseLock({
        lockRef: op.lockRef,
        studyRef: props.studyRef,
        meaning,
        assertionId: op.assertionId,
        attestationId: op.attestationId,
        sigToken,
        reason: reason.value.trim(),
        idempotencyKey: op.idempotencyKey,
      });
      outcome.value = {
        kind: "ok",
        message: `${op.lockKind} released on ${props.scopeLabel}.`,
      };
    }
    reason.value = "";
    emit("changed", updated);
    return { kind: "signed" as const };
  } catch (err) {
    const message = describeLockError(err);
    outcome.value = { kind: "error", message };
    throw new Error(message, { cause: err });
  } finally {
    busy.value = false;
  }
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

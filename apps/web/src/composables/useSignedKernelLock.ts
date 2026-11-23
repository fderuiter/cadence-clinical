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
  type KernelLockReadiness,
} from "../api/kernelApi";
import { useAuthStore } from "../stores/auth.js";

export const LOCK_SIGNING_REASONS: string[] = ["Approved", "Responsibility"];

export interface SignedLockOutcome {
  kind: "ok" | "error";
  message: string;
}

export type SignedLockModalOperation =
  | {
      mode: "apply";
      scopeRef: string;
      lockKind: KernelLockKind;
      reason: string;
      targetLabel?: string | (() => string);
      idempotencyKey: string;
      lockId: string;
      attestationId: string;
    }
  | {
      mode: "release";
      lockRef: string;
      lockKind: string;
      reason: string;
      targetLabel?: string | (() => string);
      idempotencyKey: string;
      assertionId: string;
      attestationId: string;
    };

export interface OpenApplyLockParams {
  scopeRef: string;
  kind: KernelLockKind;
  reason: string;
  targetLabel?: string | (() => string);
}

export interface OpenReleaseLockParams {
  lockRef: string;
  lockKind: string;
  reason: string;
  targetLabel?: string | (() => string);
}

export interface UseSignedKernelLockOptions {
  studyRef: () => string | null;
  scopeRef?: () => string;
  watchPermissions?: boolean;
  onApplied?: (lock: KernelLock) => Promise<void> | void;
  onReleased?: (lock: KernelLock) => Promise<void> | void;
}

function resolveLabel(
  label: string | (() => string) | undefined
): string | undefined {
  if (typeof label === "function") return label();
  return label;
}

/**
 * Shared 21 CFR Part 11 signed kernel lock workflow (ADR-2621, ADR-2259): dry-run capability checks (`lock.apply` and
 * `lock.release`), lock readiness evaluation (`getStudyLockReadiness`), signer details, step-up target URLs and
 * semantic actions, per-attempt UUID generation, signed `applyLock` / `releaseLock` execution, and error formatting.
 */
export function useSignedKernelLock(options: UseSignedKernelLockOptions) {
  const authStore = useAuthStore();

  const canApply = ref(false);
  const canRelease = ref(false);
  const readiness = ref<KernelLockReadiness | null>(null);
  const readinessLoading = ref(false);
  const busy = ref(false);
  const outcome = ref<SignedLockOutcome | null>(null);
  const modalOp = ref<SignedLockModalOperation | null>(null);

  const isLockBlockedByReadiness = computed(
    () => readiness.value !== null && !readiness.value.ready_for_lock
  );

  const signerUsername = computed(
    () => authStore?.user?.email || authStore?.user?.username || "data-manager"
  );
  const signerFullName = computed(() => {
    const user = authStore?.user as
      | (typeof authStore.user & { name?: string; full_name?: string })
      | null
      | undefined;
    return user?.name || user?.full_name || signerUsername.value;
  });

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
  let readinessSeq = 0;

  function resetReadiness(): void {
    readinessSeq++;
    readiness.value = null;
    readinessLoading.value = false;
  }

  async function reloadReadiness(): Promise<void> {
    const seq = ++readinessSeq;
    const currentStudy = options.studyRef();
    const targetScope = options.scopeRef?.()?.trim() ?? "";
    if (
      !currentStudy ||
      !targetScope ||
      !canApply.value ||
      typeof kernelApi.getStudyLockReadiness !== "function"
    ) {
      readiness.value = null;
      readinessLoading.value = false;
      return;
    }

    readinessLoading.value = true;
    try {
      const result = await kernelApi.getStudyLockReadiness(
        currentStudy,
        targetScope
      );
      if (seq !== readinessSeq) return;
      readiness.value = result ?? null;
    } catch {
      if (seq === readinessSeq) {
        readiness.value = null;
      }
    } finally {
      if (seq === readinessSeq) {
        readinessLoading.value = false;
      }
    }
  }

  async function probePermissions(
    overrideStudyRef?: string | null
  ): Promise<void> {
    const targetStudyRef =
      overrideStudyRef !== undefined ? overrideStudyRef : options.studyRef();
    const seq = ++authProbeSeq;
    if (!targetStudyRef || typeof kernelApi.canI !== "function") {
      canApply.value = false;
      canRelease.value = false;
      resetReadiness();
      return;
    }
    try {
      const [applyDec, releaseDec] = await Promise.all([
        kernelApi.canI("lock.apply", targetStudyRef),
        kernelApi.canI("lock.release", targetStudyRef),
      ]);
      if (seq !== authProbeSeq) return;
      canApply.value = Boolean(applyDec?.permitted);
      canRelease.value = Boolean(releaseDec?.permitted);
      if (!canApply.value) {
        resetReadiness();
      }
    } catch {
      if (seq !== authProbeSeq) return;
      canApply.value = false;
      canRelease.value = false;
      resetReadiness();
    }
  }

  if (options.watchPermissions !== false) {
    watch(
      [options.studyRef, () => options.scopeRef?.()],
      async ([nextStudyRef]) => {
        const expectedProbeSeq = authProbeSeq + 1;
        resetReadiness();
        await probePermissions(nextStudyRef);
        if (authProbeSeq !== expectedProbeSeq) return;
        if (options.scopeRef && canApply.value) {
          await reloadReadiness();
        }
      },
      { immediate: true }
    );
  }

  function openApply(params: OpenApplyLockParams): boolean {
    const cleanScope = params.scopeRef.trim();
    const cleanReason = params.reason.trim();
    if (!options.studyRef() || !cleanScope || !cleanReason) return false;
    if (params.kind === "Locked" && isLockBlockedByReadiness.value) {
      return false;
    }
    outcome.value = null;
    modalOp.value = {
      mode: "apply",
      scopeRef: cleanScope,
      lockKind: params.kind,
      reason: cleanReason,
      targetLabel: params.targetLabel,
      idempotencyKey: globalThis.crypto.randomUUID(),
      lockId: globalThis.crypto.randomUUID(),
      attestationId: globalThis.crypto.randomUUID(),
    };
    return true;
  }

  function openRelease(params: OpenReleaseLockParams): boolean {
    const cleanLockRef = params.lockRef.trim();
    const cleanReason = params.reason.trim();
    if (!options.studyRef() || !cleanLockRef || !cleanReason) return false;
    outcome.value = null;
    modalOp.value = {
      mode: "release",
      lockRef: cleanLockRef,
      lockKind: params.lockKind,
      reason: cleanReason,
      targetLabel: params.targetLabel,
      idempotencyKey: globalThis.crypto.randomUUID(),
      assertionId: globalThis.crypto.randomUUID(),
      attestationId: globalThis.crypto.randomUUID(),
    };
    return true;
  }

  function closeModal(): void {
    modalOp.value = null;
  }

  async function executeSign(sigToken: string, signingReason: string) {
    const op = modalOp.value;
    const currentStudyRef = options.studyRef();
    if (!op || !currentStudyRef) return { kind: "signed" as const };
    const meaning: KernelLockMeaning =
      signingReason === "Responsibility" ? "Responsibility" : "Approved";
    busy.value = true;
    try {
      if (op.mode === "apply") {
        const updated = await kernelApi.applyLock({
          studyRef: currentStudyRef,
          scopeRef: op.scopeRef,
          kind: op.lockKind,
          meaning,
          lockId: op.lockId,
          attestationId: op.attestationId,
          sigToken,
          reason: op.reason,
          idempotencyKey: op.idempotencyKey,
        });
        const label = resolveLabel(op.targetLabel) || op.scopeRef;
        outcome.value = {
          kind: "ok",
          message: `${op.lockKind} applied to ${label}.`,
        };
        await Promise.all([
          Promise.resolve(options.onApplied?.(updated)),
          options.scopeRef ? reloadReadiness() : Promise.resolve(),
        ]);
      } else {
        const updated = await kernelApi.releaseLock({
          lockRef: op.lockRef,
          studyRef: currentStudyRef,
          meaning,
          assertionId: op.assertionId,
          attestationId: op.attestationId,
          sigToken,
          reason: op.reason,
          idempotencyKey: op.idempotencyKey,
        });
        const label = resolveLabel(op.targetLabel);
        outcome.value = {
          kind: "ok",
          message: label
            ? `${op.lockKind} released on ${label}.`
            : `${op.lockKind} released.`,
        };
        await Promise.all([
          Promise.resolve(options.onReleased?.(updated)),
          options.scopeRef ? reloadReadiness() : Promise.resolve(),
        ]);
      }
      return { kind: "signed" as const };
    } catch (err) {
      const message = describeLockError(err);
      outcome.value = { kind: "error", message };
      throw new Error(message, { cause: err });
    } finally {
      busy.value = false;
    }
  }

  const signatureModalProps = computed(() => ({
    isOpen: Boolean(modalOp.value),
    username: signerUsername.value,
    signerName: signerFullName.value,
    signerRole: "Data Manager",
    reasons: LOCK_SIGNING_REASONS,
    actionUrl: modalActionUrl.value,
    semanticAction: modalSemanticAction.value,
    onSign: executeSign,
  }));

  return {
    LOCK_SIGNING_REASONS,
    canApply,
    canRelease,
    readiness,
    readinessLoading,
    isLockBlockedByReadiness,
    busy,
    outcome,
    modalOp,
    signerUsername,
    signerFullName,
    modalActionUrl,
    modalSemanticAction,
    signatureModalProps,
    probePermissions,
    reloadReadiness,
    resetReadiness,
    openApply,
    openRelease,
    closeModal,
    executeSign,
  };
}

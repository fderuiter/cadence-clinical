import { effectScope, nextTick, ref } from "vue";
import { flushPromises } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { ApiError } from "../../src/api/client";
import {
  ASSERTION_ALREADY_LOCKED,
  kernelApi,
  LOCK_APPLY_ACTION,
  LOCK_APPLY_SEMANTIC_ACTION,
  LOCK_RELEASE_SEMANTIC_ACTION,
  type KernelDecision,
  type KernelLock,
  type KernelLockReadiness,
} from "../../src/api/kernelApi";
import { useSignedKernelLock } from "../../src/composables/useSignedKernelLock";
import { useAuthStore } from "../../src/stores/auth.js";

const mockCanI = vi.fn();
const mockGetStudyLockReadiness = vi.fn();
const mockApplyLock = vi.fn();
const mockReleaseLock = vi.fn();

vi.mock("../../src/api/kernelApi", async () => {
  const actual = await vi.importActual<
    typeof import("../../src/api/kernelApi")
  >("../../src/api/kernelApi");
  return {
    ...actual,
    kernelApi: {
      ...actual.kernelApi,
      canI: (...args: unknown[]) => mockCanI(...args),
      getStudyLockReadiness: (...args: unknown[]) =>
        mockGetStudyLockReadiness(...args),
      applyLock: (...args: unknown[]) => mockApplyLock(...args),
      releaseLock: (...args: unknown[]) => mockReleaseLock(...args),
    },
  };
});

const STUDY_A = "study:01960000-0000-7000-8000-000000000001";
const STUDY_B = "study:01960000-0000-7000-8000-000000000002";
const SCOPE_REF = "enrollment:01960000-0000-7000-8000-000000000010";
const LOCK_REF = "assertion:01960000-0000-7000-8000-000000000020@1";

function decision(permitted: boolean): KernelDecision {
  return {
    capability: "lock.apply",
    verb_class: "Kernel",
    resource: STUDY_A,
    permitted,
    reason: null,
    explanation: {
      subject: STUDY_A,
      claim: permitted ? "Permitted" : "Denied",
      producer: "test",
      steps: [],
      conditions: [],
      next_actions: [],
    },
  };
}

describe("useSignedKernelLock", () => {
  beforeEach(() => {
    setActivePinia(createPinia());
    vi.clearAllMocks();
    mockCanI.mockResolvedValue(decision(true));
  });

  it("probes lock.apply and lock.release permissions and drops stale responses across rapid switches", async () => {
    let resolveFirstApply!: (value: KernelDecision) => void;
    mockCanI
      .mockImplementationOnce(
        () =>
          new Promise<KernelDecision>((resolve) => {
            resolveFirstApply = resolve;
          })
      )
      .mockResolvedValueOnce(decision(true))
      .mockResolvedValueOnce(decision(false))
      .mockResolvedValueOnce(decision(true));

    const studyRef = ref<string | null>(STUDY_A);
    const scope = effectScope();
    const lock = scope.run(() =>
      useSignedKernelLock({ studyRef: () => studyRef.value })
    )!;

    studyRef.value = STUDY_B;
    await nextTick();
    await flushPromises();

    expect(lock.canApply.value).toBe(false);
    expect(lock.canRelease.value).toBe(true);

    // Resolve earlier STUDY_A probe out of order; state must remain STUDY_B's.
    resolveFirstApply(decision(true));
    await flushPromises();

    expect(lock.canApply.value).toBe(false);
    expect(lock.canRelease.value).toBe(true);

    studyRef.value = null;
    await nextTick();
    await flushPromises();
    expect(lock.canApply.value).toBe(false);
    expect(lock.canRelease.value).toBe(false);

    scope.stop();
  });

  it("derives signer username and full name from authStore with safe defaults", () => {
    const auth = useAuthStore();
    const scope = effectScope();
    const lock = scope.run(() =>
      useSignedKernelLock({ studyRef: () => STUDY_A, watchPermissions: false })
    )!;

    expect(lock.signerUsername.value).toBe("data-manager");
    expect(lock.signerFullName.value).toBe("data-manager");

    auth.user = {
      email: "dm@cadence.test",
      full_name: "Dana Manager",
    } as never;

    expect(lock.signerUsername.value).toBe("dm@cadence.test");
    expect(lock.signerFullName.value).toBe("Dana Manager");

    auth.user = {
      username: "dm.lead",
      email: "dm.lead@cadence.clinical",
      firstName: "Dana",
      lastName: "Mercer",
    } as never;

    expect(lock.signerUsername.value).toBe("dm.lead");
    expect(lock.signerFullName.value).toBe("Dana Mercer");
    scope.stop();
  });

  it("generates fresh UUIDs per apply/release attempt, executes signed calls, and formats banners", async () => {
    const applied: KernelLock[] = [];
    const released: KernelLock[] = [];
    const scope = effectScope();
    const lock = scope.run(() =>
      useSignedKernelLock({
        studyRef: () => STUDY_A,
        onApplied: (item) => {
          applied.push(item);
        },
        onReleased: (item) => {
          released.push(item);
        },
      })
    )!;
    await flushPromises();

    // Rejects blank inputs
    expect(
      lock.openApply({ scopeRef: SCOPE_REF, kind: "Frozen", reason: "   " })
    ).toBe(false);
    expect(lock.modalOp.value).toBeNull();

    expect(
      lock.openApply({
        scopeRef: ` ${SCOPE_REF} `,
        kind: "Frozen",
        reason: " Interim cut ",
        targetLabel: "participant",
      })
    ).toBe(true);
    const firstOp = lock.modalOp.value!;
    expect(firstOp.mode).toBe("apply");
    expect(lock.signatureModalProps.value.actionUrl).toBe(LOCK_APPLY_ACTION);
    expect(lock.signatureModalProps.value.semanticAction).toBe(
      LOCK_APPLY_SEMANTIC_ACTION
    );
    expect(lock.LOCK_SIGNING_REASONS).toEqual(["Approved", "Responsibility"]);

    // Second attempt generates fresh keys
    lock.openApply({
      scopeRef: SCOPE_REF,
      kind: "Locked",
      reason: "Database lock",
      targetLabel: "Study CAD-101",
    });
    const secondOp = lock.modalOp.value!;
    expect(secondOp.idempotencyKey).not.toBe(firstOp.idempotencyKey);

    mockApplyLock.mockResolvedValueOnce({
      ref: "assertion:lock-1",
      kind: "Locked",
    });
    await expect(
      lock.executeSign("sig-token-1", "Responsibility")
    ).resolves.toEqual({ kind: "signed" });

    expect(mockApplyLock).toHaveBeenCalledWith({
      studyRef: STUDY_A,
      scopeRef: SCOPE_REF,
      kind: "Locked",
      meaning: "Responsibility",
      lockId: (secondOp as Extract<typeof secondOp, { mode: "apply" }>).lockId,
      attestationId: secondOp.attestationId,
      sigToken: "sig-token-1",
      reason: "Database lock",
      idempotencyKey: secondOp.idempotencyKey,
    });
    expect(lock.outcome.value).toEqual({
      kind: "ok",
      message: "Locked applied to Study CAD-101.",
    });
    expect(applied).toHaveLength(1);

    // Release with and without targetLabel
    expect(
      lock.openRelease({
        lockRef: LOCK_REF,
        lockKind: "Frozen",
        reason: "Reopen participant",
        targetLabel: "participant",
      })
    ).toBe(true);
    expect(lock.signatureModalProps.value.actionUrl).toBe(
      "/api/v1/kernel/locks/assertion:01960000-0000-7000-8000-000000000020/release"
    );
    expect(lock.signatureModalProps.value.semanticAction).toBe(
      LOCK_RELEASE_SEMANTIC_ACTION
    );

    mockReleaseLock.mockResolvedValueOnce({
      ref: "assertion:lock-1",
      kind: "Frozen",
      released: true,
    });
    await lock.executeSign("sig-token-2", "Approved");
    expect(lock.outcome.value).toEqual({
      kind: "ok",
      message: "Frozen released on participant.",
    });
    expect(released).toHaveLength(1);

    lock.openRelease({
      lockRef: LOCK_REF,
      lockKind: "Frozen",
      reason: "Study-level release",
    });
    mockReleaseLock.mockResolvedValueOnce({
      ref: "assertion:lock-1",
      kind: "Frozen",
      released: true,
    });
    await lock.executeSign("sig-token-3", "Approved");
    expect(lock.outcome.value).toEqual({
      kind: "ok",
      message: "Frozen released.",
    });

    lock.closeModal();
    expect(lock.modalOp.value).toBeNull();
    scope.stop();
  });

  it("formats lock API failures with describeLockError, updates outcome, and rethrows with cause", async () => {
    const scope = effectScope();
    const lock = scope.run(() =>
      useSignedKernelLock({ studyRef: () => STUDY_A, watchPermissions: false })
    )!;

    lock.openApply({
      scopeRef: SCOPE_REF,
      kind: "Locked",
      reason: "Lock attempt",
    });

    const conflict = new ApiError("Conflict", 409, null, {
      code: ASSERTION_ALREADY_LOCKED,
    });
    mockApplyLock.mockRejectedValueOnce(conflict);

    await expect(
      lock.executeSign("sig-token-err", "Approved")
    ).rejects.toThrowError(
      "That lock state already changed; reload and inspect the current locks."
    );
    expect(lock.outcome.value).toEqual({
      kind: "error",
      message:
        "That lock state already changed; reload and inspect the current locks.",
    });
    expect(lock.busy.value).toBe(false);

    // Fallback when canI rejects
    mockCanI.mockRejectedValueOnce(new Error("Gateway down"));
    await lock.probePermissions(STUDY_A);
    expect(lock.canApply.value).toBe(false);
    expect(lock.canRelease.value).toBe(false);
    expect(kernelApi.canI).toBeDefined();

    scope.stop();
  });

  it("evaluates scope lock readiness, blocks Locked while allowing Frozen on open blockers, and refreshes after signing", async () => {
    const blockedReadiness: KernelLockReadiness = {
      study_ref: STUDY_A,
      study_code: "CAD-101",
      scope_ref: SCOPE_REF,
      scope_kind: "enrollment",
      ready_for_lock: false,
      can_freeze: true,
      open_query_count: 1,
      missing_required_item_count: 0,
      open_error_finding_count: 0,
      blocker_count: 1,
      blockers: [],
      explanation: {
        subject: SCOPE_REF,
        claim: "Scope has 1 open query blocking hard lock.",
        producer: "Kernel.LockReadinessEvaluator",
        steps: [],
        conditions: [],
        next_actions: [],
      },
    };
    const clearReadiness: KernelLockReadiness = {
      ...blockedReadiness,
      ready_for_lock: true,
      open_query_count: 0,
      blocker_count: 0,
    };
    mockGetStudyLockReadiness
      .mockResolvedValueOnce(blockedReadiness)
      .mockResolvedValueOnce(clearReadiness);

    const scope = effectScope();
    const lock = scope.run(() =>
      useSignedKernelLock({
        studyRef: () => STUDY_A,
        scopeRef: () => SCOPE_REF,
      })
    )!;
    await flushPromises();

    expect(mockGetStudyLockReadiness).toHaveBeenCalledWith(STUDY_A, SCOPE_REF);
    expect(lock.readiness.value).toEqual(blockedReadiness);
    expect(lock.isLockBlockedByReadiness.value).toBe(true);

    // Locked is blocked while Frozen remains available
    expect(
      lock.openApply({
        scopeRef: SCOPE_REF,
        kind: "Locked",
        reason: "Attempt hard lock",
      })
    ).toBe(false);
    expect(
      lock.openApply({
        scopeRef: SCOPE_REF,
        kind: "Frozen",
        reason: "Freeze while query closes",
      })
    ).toBe(true);

    mockApplyLock.mockResolvedValueOnce({
      ref: "assertion:lock-freeze",
      kind: "Frozen",
    });
    await lock.executeSign("sig-tok-freeze", "Approved");

    expect(mockGetStudyLockReadiness).toHaveBeenCalledTimes(2);
    expect(lock.readiness.value).toEqual(clearReadiness);
    expect(lock.isLockBlockedByReadiness.value).toBe(false);

    scope.stop();
  });
});

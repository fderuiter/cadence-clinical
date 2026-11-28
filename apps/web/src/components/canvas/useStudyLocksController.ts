import { computed, ref, watch } from "vue";
import { handoffRetryFocus } from "ui";
import {
  kernelApi,
  type KernelLock,
  type KernelLockKind,
  type KernelLockMilestone,
} from "../../api/kernelApi";
import { fetchStudyLocksResult } from "../../canvas/kernelLocks";
import { getNodeDisplay, seedNodeSummary } from "../../canvas/nodeResolver";
import { useKernelStudyRef } from "../../composables/useKernelStudyRef";
import { useSignedKernelLock } from "../../composables/useSignedKernelLock";
import { useAuthStore } from "../../stores/auth.js";
import {
  unversionedNodeRef,
  useLockHierarchyPickers,
} from "./useLockHierarchyPickers";

export type QuickDialogState =
  | { mode: "apply"; kind: KernelLockKind }
  | { mode: "release"; lockRef: string; lockKind: string };

const LOCKS_RECOVERED_TARGETS = [
  '[data-testid="study-locks-table"] button:not([disabled])',
  '[data-testid="study-locks-table-scroll"]',
  "#lock-apply-scope-kind",
  "#study-locks-title",
];

export function useStudyLocksController(studyCodeGetter: () => string) {
  const authStore = useAuthStore();
  const locks = ref<KernelLock[]>([]);
  const hiddenLocks = ref<{ more_exist: boolean; reasons: string[] }>({
    more_exist: false,
    reasons: [],
  });
  const locksLoading = ref(false);
  const locksError = ref<string | null>(null);
  const activeOnly = ref(false);
  const asOfInput = ref("");

  const applyKind = ref<KernelLockKind>("Frozen");
  const selectedMilestone = ref<KernelLockMilestone>("InterimCut");
  const applyReason = ref("");
  const releaseReason = ref("");

  const quickDialog = ref<QuickDialogState | null>(null);
  const quickDialogReason = ref("");

  const quickDialogTitle = computed(() => {
    if (!quickDialog.value) return "Clinical Lock Justification";
    if (quickDialog.value.mode === "release") {
      return `Release ${quickDialog.value.lockKind} Assertion`;
    }
    return quickDialog.value.kind === "Frozen"
      ? "Soft Freeze Data Scope"
      : "Apply Cryptographic Hard Lock";
  });

  const studyRefBridge = ref<string | null>(null);
  const pickers = useLockHierarchyPickers(studyRefBridge);

  async function reloadLocks() {
    if (
      !studyRefBridge.value ||
      typeof kernelApi.listStudyLocks !== "function"
    ) {
      return;
    }
    locksLoading.value = true;
    locksError.value = null;
    try {
      const result = await fetchStudyLocksResult(studyRefBridge.value, {
        activeOnly: activeOnly.value || undefined,
        asOf: asOfInput.value.trim() || undefined,
      });
      locks.value = result.locks;
      hiddenLocks.value = result.hidden;
    } catch (err) {
      locksError.value = err instanceof Error ? err.message : String(err);
    } finally {
      locksLoading.value = false;
    }
  }

  const {
    canApply,
    canRelease,
    readiness,
    readinessLoading,
    readinessGates,
    canFreezeByReadiness,
    isLockBlockedByReadiness,
    busy,
    outcome,
    modalOp,
    signerUsername,
    verifiedSignerRole,
    signatureModalProps,
    probePermissions,
    reloadReadiness,
    openApply,
    openRelease,
    closeModal,
  } = useSignedKernelLock({
    studyRef: () => studyRefBridge.value,
    scopeRef: () => pickers.applyScopeRef.value,
    milestone: () => selectedMilestone.value,
    watchPermissions: false,
    onApplied: async () => {
      applyReason.value = "";
      await reloadLocks();
    },
    onReleased: async () => {
      releaseReason.value = "";
      await reloadLocks();
    },
  });

  const {
    studyRef,
    studyLoading,
    studyError,
    reload: reloadStudy,
  } = useKernelStudyRef(studyCodeGetter, {
    onReset: () => {
      studyRefBridge.value = null;
      pickers.resetHierarchyPickers();
      locks.value = [];
      hiddenLocks.value = { more_exist: false, reasons: [] };
      locksError.value = null;
      quickDialog.value = null;
      outcome.value = null;
      closeModal();
      void probePermissions(null);
    },
    onResolved: async (resolved, { code, isCurrent }) => {
      studyRefBridge.value = resolved;
      seedNodeSummary(resolved, { kind: "study", code });
      const unversionedStudy = unversionedNodeRef(resolved);
      if (unversionedStudy && unversionedStudy !== resolved) {
        seedNodeSummary(unversionedStudy, { kind: "study", code });
      }
      pickers.applyScopeKind.value = "study";

      const tasks: Promise<unknown>[] = [
        reloadLocks(),
        probePermissions(resolved),
      ];
      if (typeof kernelApi.getChildren === "function") {
        tasks.push(
          kernelApi
            .getChildren(resolved)
            .then((children) => {
              if (!isCurrent()) return;
              pickers.setStudyChildren(children ?? []);
            })
            .catch(() => undefined)
        );
      }
      await Promise.all(tasks);
    },
  });

  watch(
    studyRef,
    (val) => {
      studyRefBridge.value = val;
    },
    { immediate: true }
  );

  watch([studyRef, pickers.applyScopeRef, canApply, selectedMilestone], () => {
    void reloadReadiness();
  });

  function handleRetryStudyLocks() {
    void handoffRetryFocus(() => reloadStudy(), {
      container: () => document.getElementById("study-locks-section"),
      isError: () => Boolean(studyError.value || locksError.value),
      errorTargets: ["#btn-retry-study-locks", "#btn-retry-locks-list"],
      successTargets: LOCKS_RECOVERED_TARGETS,
    });
  }

  function handleRetryLocksList() {
    void handoffRetryFocus(() => reloadLocks(), {
      container: () => document.getElementById("study-locks-section"),
      isError: () => Boolean(locksError.value),
      errorTargets: ["#btn-retry-locks-list"],
      successTargets: LOCKS_RECOVERED_TARGETS,
    });
  }

  const lockColumns = computed(() => [
    { key: "kind", label: "Kind" },
    { key: "scope", label: "Scope" },
    { key: "participant", label: "Participant" },
    { key: "signed_by", label: "Signed by" },
    { key: "locked_at", label: "Locked at" },
    { key: "reason", label: "Reason" },
    { key: "state", label: "State" },
    ...(canRelease.value ? [{ key: "action", label: "Action" }] : []),
  ]);

  const isApplyDisabled = computed(() => {
    const lockedBlocked =
      applyKind.value === "Locked" && isLockBlockedByReadiness.value;
    const freezeBlocked =
      applyKind.value === "Frozen" && !canFreezeByReadiness.value;
    return (
      busy.value ||
      !pickers.applyScopeRef.value.trim() ||
      !applyReason.value.trim() ||
      lockedBlocked ||
      freezeBlocked
    );
  });

  function openQuickApplyDialog(kind: KernelLockKind) {
    if (busy.value || !pickers.applyScopeRef.value.trim()) return;
    if (kind === "Locked" && isLockBlockedByReadiness.value) return;
    applyKind.value = kind;
    quickDialogReason.value = applyReason.value;
    quickDialog.value = { mode: "apply", kind };
  }

  function closeQuickDialog() {
    quickDialog.value = null;
  }

  function openApplyModal() {
    if (isApplyDisabled.value) return;
    const scopeRef = pickers.applyScopeRef.value.trim();
    openApply({
      scopeRef,
      kind: applyKind.value,
      milestone: selectedMilestone.value,
      reason: applyReason.value,
      targetLabel: () => getNodeDisplay(scopeRef).label,
    });
  }

  function isSelfLocked(entry: KernelLock): boolean {
    if (entry.masked || entry.released || !entry.locked_by) return false;
    const lockedByNorm = entry.locked_by.trim().toLowerCase();
    if (!lockedByNorm) return false;
    const storeUser = authStore?.user as
      { username?: string; email?: string; id?: string } | null | undefined;
    const storeIdentity = authStore?.identity as
      { username?: string; email?: string; id?: string } | null | undefined;
    const candidates = [
      storeIdentity?.username,
      storeIdentity?.email,
      storeIdentity?.id,
      storeUser?.username,
      storeUser?.email,
      storeUser?.id,
      signerUsername.value,
    ]
      .filter(
        (val): val is string => typeof val === "string" && val.trim().length > 0
      )
      .map((val) => val.trim().toLowerCase());
    return candidates.some(
      (candidate) =>
        candidate === lockedByNorm || `person:${candidate}` === lockedByNorm
    );
  }

  function coveringAncestorLock(entry: KernelLock): KernelLock | null {
    if (entry.masked || entry.released || !entry.scope_kind) return null;
    const entryKind = entry.scope_kind;
    if (entryKind === "study") return null;

    for (const candidate of locks.value) {
      if (
        candidate.masked ||
        candidate.released ||
        !candidate.scope_kind ||
        candidate.ref === entry.ref
      ) {
        continue;
      }
      if (candidate.scope_kind === "study") {
        return candidate;
      }
      if (
        candidate.scope_kind === "study_site" &&
        entryKind !== "study_site" &&
        candidate.scope_ref &&
        entry.study_site_ref &&
        unversionedNodeRef(candidate.scope_ref) ===
          unversionedNodeRef(entry.study_site_ref)
      ) {
        return candidate;
      }
      if (
        candidate.scope_kind === "enrollment" &&
        (entryKind === "collection_instance" || entryKind === "observation") &&
        candidate.scope_ref &&
        entry.enrollment_ref &&
        unversionedNodeRef(candidate.scope_ref) ===
          unversionedNodeRef(entry.enrollment_ref)
      ) {
        return candidate;
      }
    }
    return null;
  }

  const latestCertificateLock = computed<KernelLock | null>(() => {
    const withCert = locks.value.filter(
      (entry) => !entry.masked && Boolean(entry.certificate)
    );
    if (withCert.length === 0) return null;
    const activeUnrevoked = [...withCert]
      .reverse()
      .find((entry) => !entry.released && !entry.certificate?.revoked);
    return activeUnrevoked ?? withCert[withCert.length - 1] ?? null;
  });

  const activeLockCount = computed(
    () =>
      locks.value.filter((entry) => !entry.masked && entry.released === false)
        .length
  );

  function openReleaseModal(lockRef: string, lockKind: string) {
    if (!releaseReason.value.trim()) {
      quickDialogReason.value = "";
      quickDialog.value = { mode: "release", lockRef, lockKind };
      return;
    }
    openRelease({
      lockRef,
      lockKind,
      reason: releaseReason.value,
    });
  }

  function confirmQuickDialog() {
    const state = quickDialog.value;
    const reason = quickDialogReason.value.trim();
    if (!state || !reason) return;
    quickDialog.value = null;
    if (state.mode === "apply") {
      applyKind.value = state.kind;
      applyReason.value = reason;
      openApplyModal();
    } else {
      releaseReason.value = reason;
      openReleaseModal(state.lockRef, state.lockKind);
    }
  }

  return {
    locks,
    hiddenLocks,
    locksLoading,
    locksError,
    activeOnly,
    asOfInput,
    applyKind,
    selectedMilestone,
    applyReason,
    releaseReason,
    quickDialog,
    quickDialogReason,
    quickDialogTitle,
    studyRef,
    studyLoading,
    studyError,
    canApply,
    canRelease,
    verifiedSignerRole,
    latestCertificateLock,
    activeLockCount,
    readiness,
    readinessLoading,
    readinessGates,
    canFreezeByReadiness,
    isLockBlockedByReadiness,
    busy,
    outcome,
    modalOp,
    signatureModalProps,
    lockColumns,
    isApplyDisabled,
    isSelfLocked,
    coveringAncestorLock,
    ...pickers,
    reloadLocks,
    handleRetryStudyLocks,
    handleRetryLocksList,
    openQuickApplyDialog,
    closeQuickDialog,
    confirmQuickDialog,
    openApplyModal,
    openReleaseModal,
    closeModal,
  };
}

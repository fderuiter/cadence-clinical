import { ref, computed } from "vue";

export function usePiSignoff(store, authStore) {
  const signoffTargetType = ref("FORM");
  const signoffTargetId = ref("");
  const customTargetId = ref("");
  const signoffReason = ref("PI approval and sign-off.");

  const availableSubjects = computed(() => {
    if (store && Array.isArray(store.subjects)) {
      return store.subjects.map((s) => s.id);
    }
    return ["SUBJ-001", "SUBJ-002", "SUBJ-003"];
  });
  const availableVisits = ref(["V-SCR", "V-TRT-A1", "V-TRT-A2", "V-TRT-B1"]);
  const availableFormSubmissions = ref(["FSUB-001", "FSUB-002", "FSUB-003"]);

  const validSigningReasons = [
    "I attest that this data is accurate and complete.",
    "PI approval and sign-off.",
    "Review and confirmation.",
    "DATA_RECORDING",
    "DATA_ENTRY_COMPLETED",
    "PI_REVIEW",
    "PI_SIGN_OFF",
    "COMPLIANCE_ATTESTATION",
  ];

  const showReauthModal = ref(false);
  const reauthUsername = ref(store?.user?.username || "fderuiter");
  const reauthPassword = ref("");
  const reauthTotp = ref("");
  const reauthError = ref("");
  const reauthAction = ref("");
  const pendingCloseQueryFieldId = ref(null);
  const simulateDelay = ref(false);

  function handleSignOffSubmit() {
    const targetId =
      signoffTargetId.value === "custom"
        ? customTargetId.value
        : signoffTargetId.value;
    if (!targetId || !targetId.trim()) {
      alert("Please select or enter a valid Target ID first.");
      return;
    }
    reauthAction.value = "BATCH_SIGN_OFF";
    reauthUsername.value =
      store?.user?.username || authStore?.identity?.username || "fderuiter";
    reauthPassword.value = "";
    reauthTotp.value = "";
    reauthError.value = "";
    showReauthModal.value = true;
  }

  function cancelReauth() {
    showReauthModal.value = false;
    reauthPassword.value = "";
    reauthTotp.value = "";
    reauthError.value = "";
    pendingCloseQueryFieldId.value = null;
    reauthAction.value = "";
  }

  return {
    signoffTargetType,
    signoffTargetId,
    customTargetId,
    signoffReason,
    availableSubjects,
    availableVisits,
    availableFormSubmissions,
    validSigningReasons,
    showReauthModal,
    reauthUsername,
    reauthPassword,
    reauthTotp,
    reauthError,
    reauthAction,
    pendingCloseQueryFieldId,
    simulateDelay,
    handleSignOffSubmit,
    cancelReauth,
  };
}

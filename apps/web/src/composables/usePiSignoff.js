import { ref, computed, watch } from "vue";

export function usePiSignoff(store, authStore) {
  const signoffTargetType = ref("FORM");
  const signoffTargetId = ref("");
  const customTargetId = ref("");
  const signoffReason = ref("PI approval and sign-off.");

  const availableSubjects = computed(() => {
    if (
      store &&
      Array.isArray(store.subjectIds) &&
      store.subjectIds.length > 0
    ) {
      return store.subjectIds;
    }
    if (store && Array.isArray(store.subjects) && store.subjects.length > 0) {
      return store.subjects.map((s) => s.id);
    }
    return ["SUBJ-001", "SUBJ-002", "SUBJ-003"];
  });

  const selectedSubjectIds = ref([...availableSubjects.value]);

  watch(
    availableSubjects,
    (newVal) => {
      if (
        (!selectedSubjectIds.value || selectedSubjectIds.value.length === 0) &&
        newVal &&
        newVal.length > 0
      ) {
        selectedSubjectIds.value = [...newVal];
      }
    },
    { immediate: true }
  );

  const isAllSelected = computed(() => {
    return (
      availableSubjects.value.length > 0 &&
      selectedSubjectIds.value.length === availableSubjects.value.length
    );
  });

  function toggleSelectAll(checked) {
    if (checked) {
      selectedSubjectIds.value = [...availableSubjects.value];
    } else {
      selectedSubjectIds.value = [];
    }
  }

  function toggleSubject(subId) {
    const idx = selectedSubjectIds.value.indexOf(subId);
    if (idx >= 0) {
      selectedSubjectIds.value.splice(idx, 1);
    } else {
      selectedSubjectIds.value.push(subId);
    }
  }

  const lastSignatureResult = ref(null);

  function clearSignatureResult() {
    lastSignatureResult.value = null;
  }

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
  const reauthUsername = ref(store?.user?.username || "");
  const reauthPassword = ref("");
  const reauthTotp = ref("");
  const reauthError = ref("");
  const reauthAction = ref("");
  const pendingCloseQueryFieldId = ref(null);
  const simulateDelay = ref(false);

  function handleSignOffSubmit(openModalCallback, defaultUsername = "") {
    lastSignatureResult.value = null;
    if (signoffTargetType.value === "SUBJECT") {
      if (selectedSubjectIds.value.length === 0) {
        alert("Please select at least one subject to sign off.");
        return;
      }
      if (selectedSubjectIds.value.length > 100) {
        alert("Maximum 100 subjects allowed per signature batch transaction.");
        return;
      }
    } else {
      const targetId =
        signoffTargetId.value === "custom"
          ? customTargetId.value
          : signoffTargetId.value;
      if (!targetId || !targetId.trim()) {
        alert("Please select or enter a valid Target ID first.");
        return;
      }
    }

    if (openModalCallback && typeof openModalCallback === "function") {
      openModalCallback({
        action: "BATCH_SIGN_OFF",
        username:
          defaultUsername ||
          store?.user?.username ||
          authStore?.identity?.username ||
          "",
      });
    } else {
      reauthAction.value = "BATCH_SIGN_OFF";
      reauthUsername.value =
        store?.user?.username || authStore?.identity?.username || "";
      reauthPassword.value = "";
      reauthTotp.value = "";
      reauthError.value = "";
      showReauthModal.value = true;
    }
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
    selectedSubjectIds,
    isAllSelected,
    toggleSelectAll,
    toggleSubject,
    lastSignatureResult,
    clearSignatureResult,
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

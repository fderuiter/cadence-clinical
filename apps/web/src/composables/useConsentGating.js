import { ref, computed } from "vue";

export function useConsentGating(selectedSubjectId) {
  const reconsentGatedSubjects = ref(new Set(["SUBJ-002"]));

  const isReconsentGated = computed(() => {
    return reconsentGatedSubjects.value.has(selectedSubjectId.value);
  });

  const showEconsentModal = ref(false);
  const showPaperIcfModal = ref(false);
  const reconsentSubmitting = ref(false);
  const econsentSignerName = ref("Jane Doe");
  const paperIcfDate = ref(new Date().toISOString().split("T")[0]);
  const paperIcfNote = ref("Verified signed paper ICF in investigator binder.");

  function openEconsentModal() {
    showEconsentModal.value = true;
  }

  function openPaperIcfModal() {
    showPaperIcfModal.value = true;
  }

  async function handleCompleteReconsent(method, store) {
    reconsentSubmitting.value = true;
    try {
      await new Promise((resolve) => setTimeout(resolve, 400));
      reconsentGatedSubjects.value.delete(selectedSubjectId.value);
      showEconsentModal.value = false;
      showPaperIcfModal.value = false;
      if (store && store.addLedgerBlock) {
        await store.addLedgerBlock(
          "RECONSENT_COMPLETED",
          {
            subject_id: selectedSubjectId.value,
            protocol_version: "2.0.0",
            method: method,
          },
          `Subject ${selectedSubjectId.value} re-consent recorded via ${method}. Gating unlocked.`
        );
      }
    } finally {
      reconsentSubmitting.value = false;
    }
  }

  return {
    reconsentGatedSubjects,
    isReconsentGated,
    showEconsentModal,
    showPaperIcfModal,
    reconsentSubmitting,
    econsentSignerName,
    paperIcfDate,
    paperIcfNote,
    openEconsentModal,
    openPaperIcfModal,
    handleCompleteReconsent,
  };
}

import { ref, computed } from "vue";
import { useClinicalStore } from "../stores/clinical";

export function useConsentGating(selectedSubjectId) {
  const store = useClinicalStore();

  const isReconsentGated = computed(() => {
    if (store && typeof store.isSubjectGated === "function") {
      return store.isSubjectGated(selectedSubjectId.value);
    }
    return false;
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

  async function handleCompleteReconsent(method, storeInstance) {
    reconsentSubmitting.value = true;
    try {
      if (store && typeof store.clearReconsentGate === "function") {
        await store.clearReconsentGate(
          selectedSubjectId.value,
          method,
          `Subject ${selectedSubjectId.value} re-consent recorded via ${method}. Gating unlocked.`
        );
      } else if (
        storeInstance &&
        typeof storeInstance.clearReconsentGate === "function"
      ) {
        await storeInstance.clearReconsentGate(
          selectedSubjectId.value,
          method,
          `Subject ${selectedSubjectId.value} re-consent recorded via ${method}. Gating unlocked.`
        );
      }
      if (
        storeInstance &&
        storeInstance !== store &&
        typeof storeInstance.addLedgerBlock === "function"
      ) {
        await storeInstance.addLedgerBlock(
          "RECONSENT_COMPLETED",
          {
            subject_id: selectedSubjectId.value,
            protocol_version: "2.0.0",
            method: method,
          },
          `Subject ${selectedSubjectId.value} re-consent recorded via ${method}. Gating unlocked.`
        );
      }
      showEconsentModal.value = false;
      showPaperIcfModal.value = false;
    } finally {
      reconsentSubmitting.value = false;
    }
  }

  return {
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

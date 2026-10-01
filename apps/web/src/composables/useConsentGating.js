import { ref, computed } from "vue";
import { useClinicalStore } from "../stores/clinical";
import { allowUnbackedAction } from "../utils/unbackedActions.js";

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

  async function handleCompleteReconsent(method, storeInstance, payload = null) {
    // #4827: nothing sends this re-consent to a service yet, so outside demo builds the gate stays locked
    // and nothing is logged.
    if (!allowUnbackedAction("Recording re-consent")) return;
    reconsentSubmitting.value = true;
    try {
      const sigToken = typeof payload === "string" ? payload : payload?.sigToken || null;
      if (store && typeof store.clearReconsentGate === "function") {
        await store.clearReconsentGate(
          selectedSubjectId.value,
          method,
          `Subject ${selectedSubjectId.value} re-consent recorded via ${method}. Gating unlocked.`,
          sigToken
        );
      }
      if (
        storeInstance &&
        storeInstance !== store &&
        typeof storeInstance.clearReconsentGate === "function"
      ) {
        await storeInstance.clearReconsentGate(
          selectedSubjectId.value,
          method,
          `Subject ${selectedSubjectId.value} re-consent recorded via ${method}. Gating unlocked.`,
          sigToken
        );
      }
      const targetLedgerStore =
        storeInstance && typeof storeInstance.addLedgerBlock === "function"
          ? storeInstance
          : store;
      if (
        targetLedgerStore &&
        typeof targetLedgerStore.addLedgerBlock === "function"
      ) {
        await targetLedgerStore.addLedgerBlock(
          "RECONSENT_COMPLETED",
          {
            subject_id: selectedSubjectId.value,
            protocol_version: "2.0.0",
            method: method,
            sig_token: sigToken,
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

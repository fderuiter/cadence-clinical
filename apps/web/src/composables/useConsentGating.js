import { ref, computed } from "vue";
import { executionApi } from "../api/executionApi";
import { useClinicalStore } from "../stores/clinical";
import { useToastStore } from "../stores/toasts.js";
import { isDemoBuild } from "../utils/demoMode.js";

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
  const paperIcfDate = ref(
    isDemoBuild() ? new Date().toISOString().split("T")[0] : ""
  );
  const paperIcfNote = ref("Verified signed paper ICF in investigator binder.");

  function openEconsentModal() {
    showEconsentModal.value = true;
  }

  function openPaperIcfModal() {
    showPaperIcfModal.value = true;
  }

  async function handleCompleteReconsent(
    method,
    storeOrPayload = null,
    maybePayload = null
  ) {
    const subjectId = selectedSubjectId.value;
    if (!subjectId) return false;

    const isStoreLike =
      storeOrPayload &&
      typeof storeOrPayload === "object" &&
      (typeof storeOrPayload.clearReconsentGate === "function" ||
        typeof storeOrPayload.addLedgerBlock === "function");
    const storeInstance = isStoreLike ? storeOrPayload : null;
    const payload = isStoreLike
      ? maybePayload
      : (maybePayload ?? storeOrPayload);

    const sigToken =
      typeof payload === "string" ? payload : payload?.sigToken || null;
    const signingReason =
      (typeof payload === "object" &&
        (payload?.signingReason || payload?.reason)) ||
      (method === "PAPER_UPLOAD" ? paperIcfNote.value : "") ||
      `Subject ${subjectId} re-consent recorded via ${method}.`;

    reconsentSubmitting.value = true;
    try {
      if (!isDemoBuild()) {
        const rawSignedDate =
          (typeof payload === "object" && payload?.signedDate) ||
          (method === "PAPER_UPLOAD" ? paperIcfDate.value : "");
        const trimmedDate = String(rawSignedDate || "").trim();
        if (!trimmedDate) {
          useToastStore().push({
            tone: "error",
            title: "Signed consent date required",
            message:
              "Enter the explicit date the informed consent form was signed.",
          });
          return false;
        }
        const studyId =
          (typeof payload === "object" && payload?.studyId) ||
          storeInstance?.activeStudyId ||
          storeInstance?.currentStudyId ||
          store?.activeStudyId ||
          store?.currentStudyId ||
          "STUDY-USDM-001";
        const versionTag =
          (typeof payload === "object" && payload?.versionTag) || "2.0.0";
        const versionIndex =
          (typeof payload === "object" && Number(payload?.versionIndex)) || 2;
        const isoSignedDate = trimmedDate.includes("T")
          ? trimmedDate
          : `${trimmedDate}T00:00:00Z`;

        try {
          await executionApi.recordSubjectConsent(
            subjectId,
            {
              protocol_version: {
                study_id: studyId,
                version_tag: versionTag,
                version_index: versionIndex,
              },
              icf_signed: true,
              icf_signed_date: isoSignedDate,
              requires_reconsent: false,
              is_paper_override: method === "PAPER_UPLOAD",
            },
            signingReason
          );
        } catch (err) {
          useToastStore().apiError(
            `Consent was not recorded for ${subjectId}`,
            err
          );
          return false;
        }
      }

      if (store && typeof store.clearReconsentGate === "function") {
        await store.clearReconsentGate(
          subjectId,
          method,
          `Subject ${subjectId} re-consent recorded via ${method}. Gating unlocked.`,
          sigToken
        );
      }
      if (
        storeInstance &&
        storeInstance !== store &&
        typeof storeInstance.clearReconsentGate === "function"
      ) {
        await storeInstance.clearReconsentGate(
          subjectId,
          method,
          `Subject ${subjectId} re-consent recorded via ${method}. Gating unlocked.`,
          sigToken
        );
      }
      if (isDemoBuild()) {
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
              subject_id: subjectId,
              protocol_version: "2.0.0",
              method: method,
              sig_token: sigToken,
            },
            `Subject ${subjectId} re-consent recorded via ${method}. Gating unlocked.`
          );
        }
      }
      showEconsentModal.value = false;
      showPaperIcfModal.value = false;
      return true;
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

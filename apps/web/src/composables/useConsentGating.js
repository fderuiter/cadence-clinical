import { ref, computed } from "vue";
import { executionApi } from "../api/executionApi";
import { useClinicalStore } from "../stores/clinical";
import { useStudyCanvasStore } from "../stores/studyCanvas";
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
  const econsentSignerName = ref("Participant");
  const paperIcfDate = ref(new Date().toISOString().split("T")[0]);
  const paperIcfNote = ref("Verified signed paper ICF in investigator binder.");

  function openEconsentModal() {
    showEconsentModal.value = true;
  }

  function openPaperIcfModal() {
    showPaperIcfModal.value = true;
  }

  async function handleCompleteReconsent(
    method,
    storeInstance,
    payload = null
  ) {
    const subjectId = selectedSubjectId?.value;
    if (!subjectId) return;

    reconsentSubmitting.value = true;
    try {
      const sigToken =
        typeof payload === "string" ? payload : payload?.sigToken || null;
      const isPaperOverride = method === "PAPER_OVERRIDE";
      const versionTag =
        (typeof payload === "object" && payload?.versionTag) || "2.0.0";
      const versionIndex =
        typeof payload === "object" && Number(payload?.versionIndex) > 0
          ? Number(payload.versionIndex)
          : 2;
      const reason =
        (isPaperOverride
          ? paperIcfNote.value
          : typeof payload === "object"
            ? payload?.signingReason
            : "") ||
        `Subject ${subjectId} re-consent recorded via ${method}. Gating unlocked.`;

      if (!isDemoBuild()) {
        const canvasStore = useStudyCanvasStore();
        const studyId = String(
          (typeof payload === "object" && payload?.studyId) ||
            storeInstance?.activeStudyId ||
            store?.activeStudyId ||
            canvasStore.studyCode ||
            ""
        ).trim();
        if (!studyId) {
          useToastStore().push({
            tone: "error",
            title: "Re-consent could not be recorded",
            message:
              "Select an active study before recording informed consent.",
          });
          return;
        }
        const signedIso = isPaperOverride
          ? paperIcfDate.value
            ? `${paperIcfDate.value}T00:00:00Z`
            : null
          : (typeof payload === "object" && payload?.icfSignedDate) ||
            new Date().toISOString();
        try {
          await executionApi.recordSubjectConsent(
            subjectId,
            {
              protocol_version: {
                study_id: studyId,
                version_tag: String(versionTag),
                version_index: versionIndex,
              },
              icf_signed: !isPaperOverride || Boolean(signedIso),
              icf_signed_date: signedIso,
              requires_reconsent: false,
              is_paper_override: isPaperOverride,
            },
            reason
          );
        } catch (err) {
          useToastStore().apiError("Re-consent could not be recorded", err);
          return;
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
            protocol_version: String(versionTag),
            method: method,
            sig_token: sigToken,
          },
          `Subject ${subjectId} re-consent recorded via ${method}. Gating unlocked.`
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

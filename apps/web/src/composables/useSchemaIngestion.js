import { ref, computed } from "vue";
import { describeKernelError, kernelApi } from "../api/kernelApi";
import { useToastStore } from "../stores/toasts.js";
import { demoRandomToken, isDemoBuild } from "../utils/demoMode.js";

const MAX_SYNC_UPLOAD_BYTES = 10 * 1024 * 1024;

export function useSchemaIngestion(store) {
  const fileInputRef = ref(null);
  const selectedFileName = ref("");
  const importChangeReason = ref("Import protocol draft into study");
  const selectedTemplateIds = ref([]);
  const availableTemplates = ref([]);
  const importingDraft = ref(false);
  const importSuccessMessage = ref("");
  const importErrorMessage = ref("");
  const importedDraftSummary = ref(null);

  const editingItemId = ref(null);
  const editItemValue = ref("");
  const editItemReason = ref("");
  const rejectingItemId = ref(null);
  const rejectItemReason = ref("");
  const promoteChangeReason = ref("");

  function notify(tone, title, message) {
    useToastStore().push({ tone, title, message });
  }

  const unreviewedCount = computed(() => {
    if (!store.candidateDraft || !store.candidateDraft.items) return 0;
    return Object.values(store.candidateDraft.items).filter(
      (item) => item.review_status === "PENDING"
    ).length;
  });

  function makeIdempotencyKey() {
    if (isDemoBuild()) {
      return `schema-import-${demoRandomToken(12, 36, "schema-import.idempotency")}`;
    }
    if (typeof globalThis.crypto?.randomUUID === "function") {
      return globalThis.crypto.randomUUID();
    }
    return `schema-import-${demoRandomToken(12, 36, "schema-import.idempotency")}`;
  }

  function resolveTargetStudyRef() {
    return (
      store?.activeStudyId ||
      store?.currentStudyId ||
      store?.currentUsdm?.studyId ||
      "CADENCE-101"
    );
  }

  function setFileInputElement(el) {
    fileInputRef.value = el;
  }

  function triggerFileSelect() {
    importErrorMessage.value = "";
    if (fileInputRef.value && typeof fileInputRef.value.click === "function") {
      fileInputRef.value.click();
    }
  }

  async function loadProtocolTemplates() {
    if (typeof kernelApi?.listProtocolTemplates !== "function") return;
    try {
      availableTemplates.value = await kernelApi.listProtocolTemplates();
    } catch {
      // Keep file upload available even if template catalog cannot be loaded.
    }
  }

  async function triggerDocumentUpload(event) {
    const input = event?.target;
    const file = input?.files?.[0];
    if (!file) return;

    selectedFileName.value = file.name;
    importErrorMessage.value = "";
    importSuccessMessage.value = "";

    const lowerName = file.name.toLowerCase();
    const isXmlOrJson =
      lowerName.endsWith(".xml") ||
      lowerName.endsWith(".json") ||
      file.type === "application/xml" ||
      file.type === "text/xml" ||
      file.type === "application/json";

    const isPdf = lowerName.endsWith(".pdf") || file.type === "application/pdf";

    if (!isXmlOrJson) {
      if (
        isPdf &&
        isDemoBuild() &&
        typeof store?.uploadProtocolDocument === "function"
      ) {
        importingDraft.value = true;
        try {
          await store.uploadProtocolDocument(
            file,
            "Uploader triggers protocol ingestion scan."
          );
          const message =
            "Protocol Document ingested successfully. Candidate draft loaded.";
          importSuccessMessage.value = message;
          notify("success", "Protocol ingested", message);
        } catch (err) {
          const message = "Ingestion failed: " + (err?.message || String(err));
          importErrorMessage.value = message;
          notify("error", "Ingestion failed", message);
        } finally {
          importingDraft.value = false;
          if (input) input.value = "";
        }
        return;
      }

      importErrorMessage.value = isPdf
        ? `PDF protocol digitization is not yet available for '${file.name}'. Upload an ODM v1.3.2 XML (.xml) or USDM v4 JSON (.json) file.`
        : `Unsupported file '${file.name}'. Upload an ODM v1.3.2 XML (.xml) or USDM v4 JSON (.json) file.`;
      if (input) input.value = "";
      return;
    }

    if (file.size > MAX_SYNC_UPLOAD_BYTES) {
      importErrorMessage.value = `File '${file.name}' exceeds the 10 MB synchronous upload limit.`;
      if (input) input.value = "";
      return;
    }

    const reason = importChangeReason.value.trim();
    if (!reason) {
      importErrorMessage.value =
        "Provide a reason for change before importing a protocol file.";
      return;
    }

    if (typeof kernelApi?.importProtocolDraft !== "function") {
      importErrorMessage.value = "Protocol import service is unavailable.";
      return;
    }

    importingDraft.value = true;
    try {
      const summary = await kernelApi.importProtocolDraft({
        studyRef: resolveTargetStudyRef(),
        file,
        templateIds: selectedTemplateIds.value,
        reason,
        idempotencyKey: makeIdempotencyKey(),
      });
      importedDraftSummary.value = summary;
      const notesCount = summary.import_notes?.length ?? 0;
      const notesSuffix =
        notesCount > 0
          ? ` (${notesCount} import ${notesCount === 1 ? "note" : "notes"} attached)`
          : "";
      importSuccessMessage.value = `Imported Draft version ${summary.version.number} with ${summary.form_count} ${summary.form_count === 1 ? "form" : "forms"} and ${summary.item_count} ${summary.item_count === 1 ? "item" : "items"}${notesSuffix}.`;
      selectedTemplateIds.value = [];
    } catch (err) {
      importErrorMessage.value = describeKernelError(err);
    } finally {
      importingDraft.value = false;
      if (input) input.value = "";
    }
  }

  async function importSelectedTemplates() {
    importErrorMessage.value = "";
    importSuccessMessage.value = "";

    if (selectedTemplateIds.value.length === 0) {
      importErrorMessage.value =
        "Select at least one CDISC Library eCRF template to import.";
      return;
    }

    const reason = importChangeReason.value.trim();
    if (!reason) {
      importErrorMessage.value =
        "Provide a reason for change before importing templates.";
      return;
    }

    if (typeof kernelApi?.importProtocolDraft !== "function") {
      importErrorMessage.value = "Protocol import service is unavailable.";
      return;
    }

    importingDraft.value = true;
    try {
      const summary = await kernelApi.importProtocolDraft({
        studyRef: resolveTargetStudyRef(),
        templateIds: selectedTemplateIds.value,
        reason,
        idempotencyKey: makeIdempotencyKey(),
      });
      importedDraftSummary.value = summary;
      importSuccessMessage.value = `Added CDISC Library forms to Draft version ${summary.version.number} (${summary.form_count} ${summary.form_count === 1 ? "form" : "forms"}, ${summary.item_count} ${summary.item_count === 1 ? "item" : "items"}).`;
      selectedTemplateIds.value = [];
    } catch (err) {
      importErrorMessage.value = describeKernelError(err);
    } finally {
      importingDraft.value = false;
    }
  }

  function getConfidenceClass(level) {
    if (level === "auto") return "lookup-valid";
    if (level === "needs-review") return "lookup-degraded";
    return "lookup-invalid";
  }

  function getStatusClass(status) {
    if (status === "ACCEPTED") return "lookup-valid";
    if (status === "EDITED") return "lookup-degraded";
    if (status === "REJECTED") return "lookup-invalid";
    return "";
  }

  async function acceptItem(itemId) {
    try {
      await store.transitionCandidateItemState(
        store.candidateDraft.id,
        itemId,
        "ACCEPTED",
        "Accepted by clinical reviewer"
      );
    } catch (err) {
      const message = "Transition failed: " + (err?.message || String(err));
      importErrorMessage.value = message;
      notify("error", "Transition failed", message);
    }
  }

  function startEditItem(item) {
    editingItemId.value = item.id;
    editItemValue.value = item.type === "visit" ? item.name : item.label;
    editItemReason.value = "";
  }

  function cancelEditItem() {
    editingItemId.value = null;
    editItemValue.value = "";
    editItemReason.value = "";
  }

  async function saveEditItem(itemId) {
    if (!editItemReason.value.trim()) {
      const message = "Change reason justification is mandatory for edits!";
      importErrorMessage.value = message;
      notify("error", "Reason required", message);
      return;
    }
    const item = store.candidateDraft.items[itemId];
    const payload =
      item.type === "visit"
        ? { name: editItemValue.value }
        : { label: editItemValue.value };

    try {
      await store.transitionCandidateItemState(
        store.candidateDraft.id,
        itemId,
        "EDITED",
        editItemReason.value,
        payload
      );
      editingItemId.value = null;
      editItemValue.value = "";
      editItemReason.value = "";
    } catch (err) {
      const message = "Transition failed: " + (err?.message || String(err));
      importErrorMessage.value = message;
      notify("error", "Transition failed", message);
    }
  }

  function startRejectItem(itemId) {
    rejectingItemId.value = itemId;
    rejectItemReason.value = "";
  }

  function cancelRejectItem() {
    rejectingItemId.value = null;
    rejectItemReason.value = "";
  }

  async function confirmRejectItem(itemId) {
    if (!rejectItemReason.value.trim()) {
      const message = "Change reason justification is mandatory for rejection!";
      importErrorMessage.value = message;
      notify("error", "Reason required", message);
      return;
    }
    try {
      await store.transitionCandidateItemState(
        store.candidateDraft.id,
        itemId,
        "REJECTED",
        rejectItemReason.value
      );
      rejectingItemId.value = null;
      rejectItemReason.value = "";
    } catch (err) {
      const message = "Transition failed: " + (err?.message || String(err));
      importErrorMessage.value = message;
      notify("error", "Transition failed", message);
    }
  }

  async function promoteCandidate() {
    if (!promoteChangeReason.value.trim()) {
      const message = "Promotion change reason justification is mandatory!";
      importErrorMessage.value = message;
      notify("error", "Reason required", message);
      return;
    }
    try {
      await store.promoteCandidateDraft(
        store.candidateDraft.id,
        promoteChangeReason.value
      );
      const message =
        "Candidate promoted successfully into formal DRAFT version!";
      importSuccessMessage.value = message;
      notify("success", "Candidate promoted", message);
      promoteChangeReason.value = "";
    } catch (err) {
      const message = "Promotion failed: " + (err?.message || String(err));
      importErrorMessage.value = message;
      notify("error", "Promotion failed", message);
    }
  }

  return {
    fileInputRef,
    selectedFileName,
    importChangeReason,
    selectedTemplateIds,
    availableTemplates,
    importingDraft,
    importSuccessMessage,
    importErrorMessage,
    importedDraftSummary,
    editingItemId,
    editItemValue,
    editItemReason,
    rejectingItemId,
    rejectItemReason,
    promoteChangeReason,
    unreviewedCount,
    setFileInputElement,
    loadProtocolTemplates,
    triggerFileSelect,
    triggerDocumentUpload,
    importSelectedTemplates,
    getConfidenceClass,
    getStatusClass,
    acceptItem,
    startEditItem,
    cancelEditItem,
    saveEditItem,
    startRejectItem,
    cancelRejectItem,
    confirmRejectItem,
    promoteCandidate,
  };
}
